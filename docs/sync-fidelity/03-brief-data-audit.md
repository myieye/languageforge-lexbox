# Brief: audit the affected project for strange and lost data

**Goal.** Establish what is wrong with this project's data and whether the sync has already
destroyed anything a user cares about. The project must end up healthy, whole, and syncable.
Output is a report, not a fix — but propose the repair once you know what needs repairing.

Read [README.md](README.md) and [01-findings.md](01-findings.md) first; they contain the numbers
and the traps. Assume nothing in [02-hypotheses.md](02-hypotheses.md) is settled.

## Part 1 — hunt for lost data in the Mercurial history

This is the highest-value half, and it is bounded: FW Lite has written to fwdata **exactly 5
times** in this project's 351-revision history — revisions **342, 343, 346, 350, 351**
(2026-07-01 … 2026-07-28), all authored `FieldWorks Lite`
(`SendReceiveHelpers.HgUsername`). Everything else came from FLEx users.

```bash
cd <project>/fw
hg log  --config extensions.fixutf8=! -u "FieldWorks Lite" --template "{rev} {date|shortdate} {desc|firstline}\n"
hg diff --config extensions.fixutf8=! -c 342     # and 343, 346, 350, 351
```

`.fwdata` is one big XML file, so a raw diff is large but tractable. For each FW Lite revision,
classify every change as:

- **legitimate** — a FW Lite user's edit propagating to FLEx (there are only 3 human CRDT commits,
  so there should be almost none of these; anything more is suspect by definition)
- **redundant** — writes that changed nothing semantically
- **regression** — data a FLEx user had entered that FW Lite removed, overwrote, or coarsened

Pay specific attention to, because the dry run says the sync wants to do all of these:
- parts of speech written onto senses that had none (expect ~2,000 candidates, ~98% `Noun`)
- English definitions/glosses removed or replaced (`Remove /Definition/en` appeared 31 times)
- complex form components re-added after a user had refined or deleted them
- example sentences deleted, senses moved

Then answer: **did any of the 5 revisions actually lose user data, and if so exactly which
objects?** Cross-check anything you find against the immediately preceding FLEx revision to prove
the value existed and was authored by a person. For anything lost, the previous revision is also
the recovery source — say precisely how to restore it.

## Part 2 — inventory the strange data

Broaden past the two anomalies already found (stale snapshot; 2,010 senses with a phantom part of
speech). Use [scripts/forensics.py](scripts/forensics.py) as a starting point and extend it —
adding subcommands is expected. Sweep every field and relation the sync touches, comparing all
three sources (fwdata / CRDT / snapshot), and look for:

- entities present in one store and not another (start from the small residuals: 20/23 entries,
  42/22 senses, 424/281 links)
- field-level disagreement on entities present in both: lexeme/citation forms, glosses,
  definitions, notes, literal meanings, morph types, homograph numbers, example sentences and
  their translations, pictures, publications, semantic domains, complex form types
- values that look defaulted rather than authored (the `Noun` pattern — 1,972 of 2,010; look for
  the same shape elsewhere)
- duplicates and near-duplicates, especially where identity includes an optional discriminator
  (component sense id) or where names collide: the CRDT has two `Adverb`, two `Location Adverb`
  and two `Time Adverb` parts of speech
- soft-deleted CRDT rows whose fwdata counterpart is alive, or vice versa
- anything a repair keeps rewriting — `SetFirstTranslationIdChange` fires in both sync bursts, so
  find out why `CrdtRepairs.SyncMissingTranslationIds` doesn't converge

For each anomaly report: what it is, how many objects, which store holds the odd value, when the
CRDT acquired it (the change history gives you commit dates and authors — see the `pos`
subcommand for the pattern), and whether a user could notice.

## Part 3 — is this project unique?

Decide whether this is one broken project or a fleet-wide problem, since that changes the urgency
of everything in [04-brief-sync-architecture.md](04-brief-sync-architecture.md). Cheap signals:
snapshot file age versus CRDT commit dates, the presence of a `MorphTypes` key (its absence dates
a snapshot to before morph-type support), and re-created-entity counts in the change history. If
you can reach other projects' data, sample a few; if not, say what you'd need.

## Ground rules

- **Read-only on the real data.** Work on copies. Never run a non-dry sync against this project.
  Never `hg commit`/`push` in the project repo.
- The data is real user data. Report counts, GUIDs and change types; do not paste lexical content
  into anything that leaves the machine (see the README's data-handling note).
- Distinguish measured from inferred in everything you write, and say plainly what you could not
  determine. A confident wrong cause sends the next person down the wrong trail — we already made
  that mistake once (see the README gotcha about the complex-form log format).
- Extend `forensics.py` rather than writing throwaway scripts, so the next agent inherits the
  tooling. Keep it read-only.
