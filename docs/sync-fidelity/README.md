# FwData ↔ CRDT sync fidelity investigation

Working notes for a live investigation into the FwHeadless sync. Started 2026-07-29 from a
single symptom (a dry-run sync of one project took 6.5 hours) and turned into a data-integrity
investigation. **Nothing here is a conclusion about a fix yet.**

Read in order:

| file | what it is |
|---|---|
| [01-findings.md](01-findings.md) | measured facts, with the method used to get each one |
| [02-hypotheses.md](02-hypotheses.md) | the interrupted-sync theory and the failure scenarios it predicts |
| [03-brief-data-audit.md](03-brief-data-audit.md) | brief: find strange + lost data in the affected project |
| [04-brief-sync-architecture.md](04-brief-sync-architecture.md) | brief: make the sync atomic so this stops happening |
| [scripts/forensics.py](scripts/forensics.py) | read-only forensics tool that produced the numbers |

## The one-paragraph version

The sync is a 3-way merge between **fwdata** (FieldWorks XML, authoritative for FLEx users),
the **CRDT** (SQLite, authoritative for FW Lite users), and **`fw_snapshot.json`** (the recorded
"last agreed state", the merge base). On the project examined, fwdata and the CRDT agree closely
(9,105 of ~9,500 complex-form links; entry sets differ by ~20) while the snapshot is a year stale
and shares only 503 of those links. A wrong merge base does two things: it makes the sync
re-apply thousands of changes it already applied (the 6.5 hours), and it makes real divergence
invisible, so state that only exists in the CRDT gets pushed *into* fwdata. That second part is a
data-integrity risk, not just a performance problem. Only 3 of the project's 96,003 CRDT commits
were authored by a human, so essentially none of that divergence is user data.

## Vocabulary used throughout

- **pass 1** — `SyncInternal`'s first entry sync: `before` = snapshot, `after` = fwdata, writes to
  the CRDT. Applies *fwdata's changes since the snapshot*.
- **pass 2** — the second: `before` = fwdata, `after` = CRDT, writes to fwdata. Pushes *whatever
  the CRDT holds that fwdata does not*.
- Both live in `CrdtFwdataProjectSyncService.SyncInternal`; the diff engine is
  `MiniLcm/SyncHelpers/` (`EntrySync`, `SenseSync`, `DiffCollection`).

The asymmetry matters: pass 1 is a real 3-way merge and can only see what the snapshot reveals;
pass 2 is a 2-way state push. So any CRDT-side state the snapshot can't explain flows to fwdata.

## Running the forensics

```bash
python docs/sync-fidelity/scripts/forensics.py links   <project-dir>
python docs/sync-fidelity/scripts/forensics.py entries <project-dir>
python docs/sync-fidelity/scripts/forensics.py pos     <project-dir>
python docs/sync-fidelity/scripts/forensics.py authors <project-dir>
python docs/sync-fidelity/scripts/forensics.py changes <project-dir>
```

`<project-dir>` holds `fw/fw.fwdata`, `crdt.sqlite`, `fw_snapshot.json` — the layout
`LcmDebugger`'s `Utils.OpenDownloadedProject` expects, i.e. a folder under
`deployment/_downloads/`. All subcommands are read-only. `pos` and `changes` scan the whole
change table and take a few minutes.

## Gotchas that already cost time

- **`RecordingMiniLcmApi` hides the complex form id.** `ComplexFormComponentName` prints only
  `ComponentHeadword (ComponentEntryId:ComponentSenseId)`, so several identical-looking
  "Create complex form component" lines are usually one component joined to *different* complex
  forms. Do not read them as duplicates (we did, and were wrong; the CRDT has zero duplicate
  component rows).
- **`LcmDebugger` hides most of the work.** EF Core is filtered to Warning, and the fwdata side
  (liblcm, in-process) logs nothing at all. A quiet console is not an idle process. To watch a
  run without disturbing it, attach an EventPipe session to the `Microsoft-Extensions-Logging`
  provider (keyword 4 = `FormattedMessage`) with `Microsoft.Diagnostics.NETCore.Client`; that
  also surfaces the EF commands the console filter suppresses.
- **Streaming `.fwdata` with `iterparse` needs care.** Calling `el.clear()` on *every* element
  destroys children before the parent `<rt>` end event, and you silently get zero results. Clear
  only `rt` elements (see `parse_fwdata`).
- **The `LcmCache` has a 30-minute sliding expiration** that only resets when a new api instance
  first touches the memory cache, so a long sync could have it disposed mid-run.
  `FwDataFactory.PreventEviction` fixes this (sillsdev PR #2500).
- **A dry run still writes to the fwdata copy on dispose.** `LcmCache.Dispose` →
  `WritingSystemManager.Save()` writes writing-system settings regardless of
  `WriteIgnoringMiniLcmApi`; on a temp copy lacking `fw/SharedSettings/` it throws
  `DirectoryNotFoundException`. Harmless to results (it happens after the sync completes) but
  worth knowing.

## Handling the data

The project used here is **real language data belonging to a real team**. Keep it on the machine
it was downloaded to: don't commit project files, don't paste lexical content (headwords, glosses,
definitions) into issues, PRs, or docs, and don't publish it as a test fixture. Counts, GUIDs and
change-type names are fine, and are all this folder contains. For a shareable reproduction,
generate synthetic data instead — see the demo-project work referenced in
[04-brief-sync-architecture.md](04-brief-sync-architecture.md).
