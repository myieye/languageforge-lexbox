# Brief: make the sync robust against interrupted runs

**Goal.** A sync that fails part-way must not leave the merge base lying about what has been
applied. Today it does, and the consequences are in [01-findings.md](01-findings.md): thousands of
changes re-applied every run, and CRDT-only state pushed into fwdata where a user's edits get
overwritten.

**Start with a suite of failing tests.** Encode the failure modes before designing the fix; they
are the acceptance criteria and they are the part that will still be valuable if the design
changes. Read [02-hypotheses.md](02-hypotheses.md) for the scenarios to encode.

## The current sequence and where it breaks

`FwHeadless/Services/SyncHostedService.cs` → `ExecuteSync`:

| step | line | action |
|---|---|---|
| 1 | ~167 | `SetupFwData` → hg **Send/Receive** (pull FLEx changes into local fwdata) |
| 2 | 185 | media file sync (fwdata side) |
| 3 | 188–196 | open/create the CRDT project |
| 4 | 202 | `crdtSyncService.SyncHarmonyProject()` — pull CRDT commits from the lexbox server |
| 5 | 213–216 | read `fw_snapshot.json`, then `syncService.Import(...)` or `Sync(...)` — **applies changes to both stores** |
| 6 | 229–233 | hg **Send/Receive** again — push fwdata changes out |
| 7 | 265 | `RegenerateProjectSnapshot(..., keepBackup: false)` |
| 8 | 273 | `SyncHarmonyProject()` again — push CRDT commits to the server |

The merge base is only rewritten at step 7. So **any failure between 5 and 7 leaves the CRDT
advanced and the snapshot describing the pre-sync world** — the exact precondition for the bug
class in [02-hypotheses.md](02-hypotheses.md). The window is wide: step 5 is the multi-hour part,
and step 6 is network I/O against Mercurial.

Note also: step 6 pushes fwdata *before* the snapshot is updated, so a failure in between has
already published fwdata changes; and step 7 passes `keepBackup: false`, discarding the previous
snapshot precisely where it would be most useful for diagnosis.

## What makes this hard

Four stores, no transaction spanning them:

1. local fwdata (a file, under Mercurial)
2. the Mercurial remote (shared with FLEx users — pushes are published, not revocable)
3. the CRDT SQLite database (local)
4. the lexbox CRDT server (Harmony commits are append-only and other clients may already have
   pulled them)
5. …plus `fw_snapshot.json`, whose whole job is to record agreement between 1 and 3

"Atomic" therefore cannot mean "roll everything back". Be explicit about what your design
guarantees and what it merely detects. In particular: once step 6 or 8 has published, undoing is
off the table, so the design has to make the *local* state and the *snapshot* consistent with
what was published, or refuse to publish until it can.

## The two proposals to evaluate (Tim's starting point, not a mandate)

**1. Make the CRDT side of the sync atomic.** Copy the SQLite database, sync into the copy, and
swap it into place only if the sync succeeds. Machinery already exists and is proven: the dry-run
path calls `CrdtProjectsService.OpenTempProjectCopy` (`CrdtProjectsService.cs:290`), which uses
`SqliteConnection.BackupDatabase` and hands back a `TempCrdtProjectCopy` that owns the temp file
and its DI scope. Questions to answer: cost of copying a 268 MB database per sync; how the swap
interacts with the server push at step 8 and with a concurrently-running FW Lite client; whether
Harmony's commit identity survives a swap cleanly; what happens to media files.

**2. Give the snapshot the same treatment.** Options include writing it as you go, writing it
transactionally with the CRDT swap, or keeping backups across runs to make syncs resumable. Tim's
own instinct is that **resumability may be the wrong goal** — a fresh attempt each time is easier
to reason about and reproduces failures better. Weigh that explicitly rather than assuming
resumable is better. Whatever you choose, the invariant to defend is: *the snapshot must never
claim less than what has actually been applied to the CRDT.* Erring toward "snapshot says more was
applied" is also a bug (it hides real fwdata changes), so a wrong snapshot must be detectable, not
just rare.

Also in scope: **detecting** a bad merge base at sync time. A stale or foreign snapshot is
cheaply recognisable (no `MorphTypes` key, entity counts far from the CRDT's, mtime much older
than the newest CRDT commit). Consider refusing to sync, or rebuilding the base, rather than
proceeding with a base you can prove is wrong. Note that rebuilding from the CRDT changes merge
semantics — it makes every fwdata/CRDT difference look like an fwdata-side change — so it is a
design decision, not an optimisation.

## Prior art in this repo

- **The import pipeline** is already resumable and robust; it is a one-way street, so borrow its
  patterns knowingly, not wholesale (`MiniLcmImport`, `ProjectImporter`).
- **`ProjectSnapshotService`** already supports `keepBackup` on save, and
  `RegenerateProjectSnapshotAtCommit` can rebuild a snapshot as of a specific Harmony commit using
  `SnapshotAtCommitService`. There is already an operator endpoint for this:
  `POST /regenerate-snapshot` (`FwHeadless/Routes/MergeRoutes.cs`). A repair path partly exists —
  find out what it can and cannot fix, since it may be the fastest route to healing the affected
  project.
- **`HasSyncedSuccessfully`** (`ProjectSnapshotService.cs:70`) is how the code currently decides
  import-vs-sync. Whatever you change, keep that decision explicit and verified —
  `SyncOrImportInternal` deliberately cross-checks it and throws on mismatch.

## Where the tests go

- **Diff/merge-level scenarios** → `backend/FwLite/FwLiteProjectSync.Tests` (see `SyncTests`,
  `Sena3SyncTests`, `CrdtRepairTests`). This is where the complex-form specificity scenario from
  [02-hypotheses.md](02-hypotheses.md) belongs: apply a component, drop the snapshot update,
  refine the component in fwdata, sync again, assert the CRDT does not end up holding both and
  that nothing flows back to fwdata.
- **Orchestration-level interruption** → `backend/Testing/FwHeadless` (`SyncWorkerTestHarness`
  already mocks the sync service and drives `ExecuteSync`; 19 tests today). This is where "kill the
  sync between step 5 and step 7, then run it again" belongs.
- These need no infrastructure, but FwData-backed tests are slow. Run targeted selections only —
  see the Testing section of the root `AGENTS.md`.

## Out of scope / constraints

- **Do not resurrect `BeginBulkChangeBatch`** (the deferred-write API on the abandoned
  `sync-perf-wip` branch). It was rejected as an unmaintainable API burden.
- Performance work is tracked separately. Expect a large speedup as a *side effect* of not
  re-applying work, but don't let this become the perf project. If you find a cheap correctness-
  preserving win (e.g. not opening a commit for a change that is provably a no-op), note it and
  hand it over rather than bundling it.
- Follow the repo's PR conventions and keep changes reviewable: design first, then a test suite,
  then implementation split into small PRs.
