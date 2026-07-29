# Slow-sync demo baseline

Baseline numbers for the slow-sync demo project, measured with the LcmDebugger `sync` harness on
this branch (NuGet SIL.Harmony, `AlwaysValidateCommits` at its default `true`). Optimization PRs
compare against these numbers and must reproduce the same change counts.

## Demo project

Generated with `dotnet run --project backend/FwLite/LcmDebugger -- generate` (seed 42, defaults):

| | |
|---|---|
| Entries in fwdata | 10000 (1–3 senses each, 0–2 examples per sense) |
| Entries in crdt/fw_snapshot | 9000 (every 10th entry lags) |
| CRDT commits | 30605 (9000 entry creates + 18000 net-zero updates + 800 component links + metadata) |
| Complex-form links | 800 synced (in CRDT) + 700 involving lagging entries (fwdata only) |
| Sizes | crdt.sqlite 120MB, fw/ 45MB, fw_snapshot.json 19MB |

Retrieve without regenerating: `backend/FwLite/LcmDebugger/scripts/unpack-demo-project.sh`

## Baseline run (2026-07-29, Linux container, .NET 10.0.302)

`dotnet run --project backend/FwLite/LcmDebugger -- sync slow-sync-demo` (dry run on a temp copy)

| Phase | Duration |
|---|---|
| Open project + load snapshot | 8.1s |
| Metadata phases (WS/pubs/POS/semdoms/CFT/morph types) | 6.3s |
| Syncing fwdata entries into crdt | **17m20s** |
| Syncing crdt entries into fwdata | 1.5s |
| **Total sync** | **17m28s** |

Within the fwdata→crdt phase: the 9000 unchanged-entry diffs take under a second; the rest is
1000 `CreateEntry` (~180ms each early, rising as commits accumulate) and the complex-forms pass
(~13min), which slows measurably toward the end as each new commit grows the history that
`ValidateCommits` rehashes and the snapshot count the `CurrentSnapshots` CTE scans.

Result: `CrdtChanges 2400, FwdataChanges 0`
- crdt records: 1000 `CreateEntry` + 1400 `SubmitCreateComplexFormComponent` (700 lagging links,
  each diffed from both the complex-form and the component side; the second call no-ops)
- average cost: **437ms per change record** over the whole sync

## CurrentSnapshots CTE profile (SQLite, demo scale: 66380 snapshots, 30605 commits)

Harmony's `CurrentSnapshots()` CTE (window over Snapshots⋈Commits, `GROUP BY EntityId`), which
the complex-form cycle check runs once per BFS hop:

| query | time |
|---|---|
| bare CTE (47422 current snapshots) | 679ms |
| CTE + `References.Contains(id)` filter (the cycle-check shape) | 603ms |
| same, with a covering index on Snapshots(EntityId, CommitId, Id) | 564ms |
| same, plus covering index on Commits(Id, DateTime, Counter) | 581ms |
| per-entity `GetCurrentSnapshotByObjectId` shape | <1ms |
| **rewritten reference lookup** (filter by References first, then verify the row is its entity's latest) | **37ms (16x)** |

Indexing is a dead end: the plan already uses `IX_Snapshots_EntityId`, and the cost is the
window sort + GROUP BY over every snapshot row, which no index removes — so no index migration
from this work. The rewrite (or caching current snapshots across a commit's application) is
Harmony-internal.

## Harmony referencing-query fix (branch `sync-perf-snapshot-lookups` on myieye/harmony)

Built with `UseHarmonySource=true` (base commit a14c5bb; note aae2ee0 and later break lexbox with
"ChangeTypeListBuilder is frozen"). Same demo project and gate:

| run | sync time | avg/record |
|---|---|---|
| harmony source, unmodified (validates ≈ NuGet baseline) | 18m02s | 451ms |
| + `CurrentSnapshotsReferencing` fix (validation on) | **12m13s** | **305ms (−32%)** |
| + `--no-validate` as well | 12m20s | 308ms |

The fix replaces the composed CurrentSnapshots-CTE lookup in the cycle check with a query that
filters by References before the latest-per-entity check (603ms → 37ms per lookup). With it in
place, `AlwaysValidateCommits=false` adds nothing at this scale — rehashing 30k small commits is
cheap; the remaining ~300ms/record is snapshot maintenance and per-commit transaction cost.

## Temp-copy SQLite pragmas: no effect here

Branch `claude/sync-perf-temp-copy-pragmas` drops durability (synchronous=OFF,
journal_mode=MEMORY) on dry-run temp copies. Measured 17m22s / 434ms — indistinguishable from
baseline on this hardware (NVMe; fsync isn't the bottleneck). Branch is pushed but not PR'd;
might still matter on slow disks.

## Correctness gate for optimization PRs

A dry-run sync of the demo project must produce identical results before and after the change:
`CrdtChanges 2400, FwdataChanges 0`, same per-method record counts as above.
