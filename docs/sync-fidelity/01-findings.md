# Findings

All numbers measured on one project (code `sbe-flex`, downloaded 2026-07-28) with
[scripts/forensics.py](scripts/forensics.py) and a dry-run sync on 2026-07-29 that took
6h26m. Reproduce with the commands in the [README](README.md).

Each finding is marked **[measured]** (came out of the data) or **[inferred]** (a reading of the
code that fits the data but was not directly observed). Don't promote an inferred item without
checking it.

## 1. The snapshot is the outlier, not the CRDT [measured]

| source | entries | senses | complex-form links |
|---|---|---|---|
| `fw/fw.fwdata` | 9,160 | 11,628 | 9,529 |
| `crdt.sqlite` | 9,163 | 11,608 | 9,386 |
| `fw_snapshot.json` (file mtime **2025-08-13**) | 8,220 | 10,699 | **2,164** |

Link agreement, keyed on `(ComplexFormEntryId, ComponentEntryId, ComponentSenseId)`:

- fwdata ∩ crdt = **9,105**; fwdata-only 424; crdt-only 281
- fwdata ∩ snapshot = **503**; snapshot-only 1,661
- crdt ∩ snapshot = 500

Entity agreement: fwdata vs crdt differ by only 20 / 23 entries and 42 / 22 senses. Snapshot vs
crdt differ by 988 / 1,931 entries. So the two live stores track each other; the merge base does
not track either.

The snapshot also has **no `MorphTypes` key at all** — it predates morph-type support, which is
why `SyncOrImportInternal` has to patch it from the CRDT at runtime.

## 2. The snapshot is a picture of the original import [measured]

The 495 import commits contain exactly **2,164 `AddEntryComponentChange`** — identical to the
snapshot's link count. The snapshot describes the post-import state (mid-2025) and was never
regenerated afterwards, even though syncs ran in 2026-03 and 2026-07.

## 3. Almost no human authorship [measured]

96,003 CRDT commits:

| author | commits | changes/commit | what it is |
|---|---|---|---|
| `FieldWorks` | 95,505 | 2.5 | the fwdata→CRDT sync. `DefaultAuthorForCommits` in `FwHeadless/appsettings.json`, applied at `CrdtMiniLcmApi.cs:50` when no user is signed in. Not a person. |
| *(no author)* | 495 | 61.1 | the original import; 493 of them are the first 500 commits in history |
| `Rainer Oetzel` | 3 | 1.3 | the only human FW Lite editing: 1 entry, 1 sense, 2 part-of-speech sets, 2025-06-09/10 |

So any CRDT state that fwdata lacks is machine-produced. This is the fact that makes pass 2's
1,744 fwdata-bound changes suspicious rather than expected.

## 4. Work recurs on every sync [measured]

Change types over 149,231 changes, by month (only 4 months have any activity — the import, one
human day, then sync bursts in 2026-03 and 2026-07):

| change type | total | 2025-05 (import) | 2026-03 | 2026-07 |
|---|---|---|---|---|
| `AddPublicationChange` | 28,012 | 0 | 18,341 | 9,671 |
| `CreateSenseChange` | 17,294 | 10,704 | 4,095 | 2,494 |
| `CreateEntryChange` | 12,810 | 8,223 | 2,677 | 1,909 |
| `AddEntryComponentChange` | 11,186 | 2,164 | 1,362 | 7,660 |
| `CreateExampleSentenceChange` | 10,849 | 3,954 | 4,624 | 2,271 |
| `SetFirstTranslationIdChange` | 8,949 | 0 | 5,996 | 2,953 |

The project has 9,163 entries and 11,608 senses, so creating 2,677 entries and 4,095 senses in
2026-03 and again in 2026-07 means **entities are being re-created, not created**. Same for
publications: 28,012 additions across ~9,200 entries and 9 publications.

`SetFirstTranslationIdChange` recurring in both bursts means the translation-id repair
(`CrdtRepairs.SyncMissingTranslationIds`) is not converging either.

## 5. ~2,000 senses carry a part of speech fwdata does not have [measured]

Of 11,581 senses present in both stores, **2,584 disagree** on part of speech:

- **2,010** — CRDT has one, fwdata blank. Verified not a parsing artifact: 1,998 are plain
  `MoStemMsa` with no `PartOfSpeech` child; for 2,002 of them no alternate MSA field
  (`FromPartOfSpeech`/`ToPartOfSpeech`) holds the CRDT's value; and the bridge reads exactly that
  field (`sense.MorphoSyntaxAnalysisRA?.GetPartOfSpeech()`, `FwDataMiniLcmApi.cs:794`), so it
  reports null too.
- **564** — both set, different values.
- **10** — fwdata has one, CRDT blank.
- **765** of the 2,584 are senses absent from the snapshot, so pass 1 is structurally unable to
  see the disagreement.

**1,972 of the 2,010 are the same value: `Noun`.** A single category accounting for 98% of the
phantom values looks like a default being applied somewhere, not organic divergence. Provenance
of those CRDT values: 2,495 `CreateSenseChange` + 228 `SetPartOfSpeechChange`, committed
2025-05: 1,743 / 2026-03: 615 / 2026-07: 365 — i.e. partly the import and partly recent syncs.

Consequence: in a real (non-dry) sync, pass 2 pushes these to fwdata. That would write a
part of speech into FLEx for senses that currently have none. **Unconfirmed** whether this has
already happened — see [03-brief-data-audit.md](03-brief-data-audit.md).

## 6. What the 6.5-hour dry run actually did [measured]

Result line: `Crdt changes: 45316, Fwdata changes: 1744` (50,740 and 2,548 recorded API calls).

Top CRDT-bound records: `SubmitCreateComplexFormComponent` 14,591 · `AddPublication` 7,678 ·
`SetSensePartOfSpeech` 4,852 · `SubmitUpdateSense` 3,531 · `DeleteComplexFormComponent` 2,983 ·
`SubmitUpdateEntry` 2,602 · `SubmitUpdateExampleSentence` 2,198 · `AddComplexFormType` 2,127 ·
`CreateEntry` 1,929 · `AddSemanticDomainToSense` 1,731 · `DeleteEntry` 989.

fwdata-bound records (first 1,559 of 2,548 captured; the trailing complex-form block was lost
when the log tap died): `SetSensePartOfSpeech` 629 · `SubmitUpdateEntry` 514 ·
`AddComplexFormType` 133 · `SubmitUpdateSense` 110 · `DeleteExampleSentence` 40 · `MoveSense` 33 ·
`UpdatePicture` 17 · `SubmitUpdateExampleSentence` 15 · `RemoveComplexFormType` 13 ·
`SubmitCreateExampleSentence` 12 · `UpdateTranslation` 10 · `RemoveSemanticDomainFromSense` 9 ·
`SubmitCreateSense` 7 · `AddPublication` 6 · `DeleteSense` 4 · `MoveExampleSentence` 3 ·
`RemoveTranslation` 2 · `AddSemanticDomainToSense` 2.

Note the fwdata-bound `SubmitUpdateSense` records: 52 `Replace /Definition/en`, 48
`Replace /Gloss/en`, **31 `Remove /Definition/en`**, 4 `Remove /Gloss/en`. Removals of English
definitions from fwdata deserve their own look.

## 7. Why each redundant change is expensive [measured + inferred]

Roughly 1 second of wall clock per single-change commit, at 96,003 commits of history. Per commit:

- `DataModel.Add` runs `ValidateCommits` — a full `Commits` scan including `Metadata`, plus
  hash-chain verification — gated by `HarmonyConfig.AlwaysValidateCommits` (default `true`). The
  `SyncWith` paths validate unconditionally, independent of that flag. **[measured in code]**
- Snapshot maintenance runs the `CurrentSnapshots()` CTE (window function over
  Snapshots ⋈ Commits) at ~110 ms per call. **[measured: EF command logs]**
- `AddEntryComponentChange.NewEntity` walks references via `GetObjectsReferencing` (one CTE query
  per hop) and, when it finds the link already exists, sets `DeletedAt` on the new entity — so a
  redundant create still writes, validates and snapshots a commit, and pays the most expensive
  path in the system to produce a dead row. **[measured in code + caught in a stack sample]**
- Each commit is its own SQLite transaction, so its own fsync. **[inferred]**

A stack sample of the running process was in exactly this path:
`SubmitCreateComplexFormComponent` → `DataModel.Add` → `SnapshotWorker.ApplyCommitChanges` →
`AddEntryComponentChange.CreatesReferenceCycleOrDuplicate` → `GetSnapshotsWhere` → SQLite.

## 8. FW Lite has written to fwdata only 5 times [measured]

The downloaded project includes its Mercurial history (351 revisions). FwHeadless commits as
`FieldWorks Lite` (`SendReceiveHelpers.HgUsername`); FLEx users commit under their own names via
FLExBridge. Author breakdown: RainerKarlOetzel 134, raine 119, User 69, janeu 9,
Patrick Sauleah 8, **FieldWorks Lite 5**, Helen 3, www-data 2, ABLEVALUEDCLIENT 2, zook 1.

The 5 FW Lite revisions are **342, 343, 346, 350, 351** (2026-07-01 … 2026-07-28). That is the
complete list of occasions on which this pipeline could have damaged fwdata, which makes the
data-loss question tractable — see [03-brief-data-audit.md](03-brief-data-audit.md).

`hg` here needs `--config extensions.fixutf8=!` outside the container (the extension lives at a
container path).
