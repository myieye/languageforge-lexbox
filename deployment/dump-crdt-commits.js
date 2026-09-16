#!/usr/bin/env node

// Dumps a project's server-side CRDT commits (the CrdtCommits table) into a
// ChangesResult<Commit> JSON that LcmDebugger's FakeSyncSource.FromJsonFile can replay.
// This lets us reproduce a server-only sync failure locally without hitting prod live.

import {execFileSync} from "child_process";
import fs from "fs";
import path from "path";
import zlib from "zlib";

const args = process.argv.slice(2);
if (args.length < 4) {
  console.error("Usage: node dump-crdt-commits.js <projectId> <projectCode> <context> <namespace>");
  process.exit(1);
}
const [projectId, projectCode, context, namespace] = args;
// projectId is interpolated into SQL below; projectCode into the output filename.
if (!/^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i.test(projectId)) {
  console.error(`Not a project id (expected a UUID): ${projectId}`);
  process.exit(1);
}
if (!/^[a-z0-9][a-z0-9-]*$/i.test(projectCode)) {
  console.error(`Not a project code: ${projectCode}`);
  process.exit(1);
}

const timestamp = new Date().toISOString().replace(/[-:T]/g, "").split(".")[0];
const outDir = path.resolve("_downloads");
const outFile = path.join(outDir, `${projectCode}-crdt-commits_${timestamp}.json`);

// One JSON object per row. Field casing matches the CommitBase contract; the client reads
// case-insensitively (Web defaults). ChangeEntities is stored jsonb with each change's $type
// preserved, so it maps straight onto ChangeEntity<IChange> on the client.
const commitsSql = `
SELECT json_build_object(
  'Id', "Id",
  'ClientId', "ClientId",
  'HybridDateTime', json_build_object(
    'DateTime', "HybridDateTime_DateTime",
    'Counter', "HybridDateTime_Counter"
  ),
  'Metadata', "Metadata"::json,
  'ChangeEntities', COALESCE("ChangeEntities", '[]'::jsonb)::json
)
FROM "CrdtCommits"
WHERE "ProjectId" = '${projectId}'
ORDER BY "HybridDateTime_DateTime", "HybridDateTime_Counter", "Id";
`;

// SyncState.ClientHeads: highest commit time (unix ms) per client.
const syncStateSql = `
SELECT COALESCE(json_object_agg("ClientId", max_ms), '{}'::json)
FROM (
  -- floor, not round: must match C#'s ToUnixTimeMilliseconds truncation or heads land 1ms ahead
  SELECT "ClientId", floor(max(extract(epoch from "HybridDateTime_DateTime")) * 1000)::bigint AS max_ms
  FROM "CrdtCommits"
  WHERE "ProjectId" = '${projectId}'
  GROUP BY "ClientId"
) t;
`;

function findPod() {
  const pod = execFileSync("kubectl", [
    "--context", context, "get", "pod", "-n", namespace,
    "-l", "app=db", "-o", "jsonpath={.items[0].metadata.name}"
  ]).toString().trim();
  if (!pod) throw new Error("No db pod found.");
  console.log(`Using db pod: ${pod}`);
  return pod;
}

// Runs psql inside the pod, SQL fed on stdin, output gzipped in-pod to keep the transfer small.
// FETCH_COUNT makes psql stream through a cursor instead of buffering the whole result in the db
// pod's memory. POSIX sh has no pipefail, so an ok-marker file propagates a psql failure past gzip
// (ON_ERROR_STOP makes psql actually report one).
function psql(pod, sql) {
  // Same kubectl transport workaround as download-fw-headless-project.js: websockets are
  // unreliable for large streams.
  const env = {...process.env, KUBECTL_REMOTE_COMMAND_WEBSOCKETS: "false"};
  const gz = execFileSync("kubectl", [
    "exec", "-i", "--context", context, "-n", namespace, "-c", "db", pod, "--",
    "sh", "-c",
    'ok=$(mktemp); trap \'rm -f "$ok"\' EXIT; ' +
    '{ PGPASSWORD="$POSTGRES_PASSWORD" psql -q -v ON_ERROR_STOP=1 -v FETCH_COUNT=1000 -U postgres -d "$POSTGRES_DB" -t -A -f - || rm -f "$ok"; } | gzip -c; ' +
    'test -e "$ok"'
  ], {input: sql, env, maxBuffer: 2 * 1024 * 1024 * 1024});
  return zlib.gunzipSync(gz).toString("utf8");
}

const headsMarker = "===CLIENT_HEADS===";

function main() {
  const pod = findPod();

  console.log("Dumping commits and sync state...");
  // One transaction so the sync state can't claim commits the dump doesn't contain.
  const raw = psql(pod, `
BEGIN ISOLATION LEVEL REPEATABLE READ READ ONLY;
${commitsSql}
SELECT '${headsMarker}';
${syncStateSql}
COMMIT;
`);
  const lines = raw.split("\n").map(l => l.trim()).filter(Boolean);
  const sep = lines.indexOf(headsMarker);
  if (sep < 0) throw new Error("psql output is missing the sync-state marker");
  const commitLines = lines.slice(0, sep);
  console.log(`Got ${commitLines.length} commits.`);

  // Postgres jsonb reorders object keys by length, but the sync wire format (ServerJsonChange)
  // always emits the "$type" discriminator first, and the client's polymorphic reader requires it
  // first. Restore that so the dump deserializes exactly like a real sync response.
  const commits = commitLines.map(line => {
    const c = JSON.parse(line);
    for (const ce of c.ChangeEntities ?? []) {
      // Old rows store the change as a JSON *string*; the server's read converter unwraps it
      // (see ServerJsonChangeConverter in CommitEntityConfiguration.cs), so do the same.
      if (typeof ce.Change === "string") ce.Change = JSON.parse(ce.Change);
      const ch = ce.Change;
      if (ch && typeof ch === "object" && "$type" in ch) {
        const {["$type"]: type, ...rest} = ch;
        ce.Change = {"$type": type, ...rest};
      }
    }
    return JSON.stringify(c);
  });

  const clientHeads = lines[sep + 1] || "{}";

  fs.mkdirSync(outDir, {recursive: true});
  const out = fs.createWriteStream(outFile);
  out.write('{"MissingFromClient":[');
  commits.forEach((line, i) => out.write(i === 0 ? line : "," + line));
  out.write('],"ServerSyncState":{"ClientHeads":' + clientHeads + "}}");
  out.end();
  out.on("finish", () => {
    const {size} = fs.statSync(outFile);
    console.log(`✅ Wrote ${outFile} (${(size / 1024 / 1024).toFixed(1)} MB)`);
    // Compressed copy for sharing.
    const gzFile = outFile + ".gz";
    fs.writeFileSync(gzFile, zlib.gzipSync(fs.readFileSync(outFile)));
    console.log(`✅ Wrote ${gzFile}`);
  });
}

main();
