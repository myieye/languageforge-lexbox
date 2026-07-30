# Hypotheses

The leading explanation, and the failure scenarios it predicts. These are **hypotheses** — the
data in [01-findings.md](01-findings.md) is consistent with them but does not prove them.

## Leading hypothesis: interrupted syncs leave the snapshot lying

`fw_snapshot.json` is the merge base. It is written **once, after a sync completes successfully**
(`ProjectSnapshotService.RegenerateProjectSnapshot`, called after `Sync` in the non-dry path). So
if a sync applies changes to the CRDT and then dies — timeout, crash, cancellation, pod eviction,
an exception in a later phase — the CRDT has moved but the snapshot still describes the old state.

Two consequences, both observed in the data:

1. **The next sync redoes the work.** Everything the interrupted run applied still looks new
   relative to the snapshot, so it is applied again. Each re-application is a full Harmony commit
   even when it changes nothing (finding 7). That is the 6.5 hours, and it is self-perpetuating:
   the slower the sync, the likelier it is interrupted, the more work the next one has.
2. **Real divergence goes invisible, and then flows the wrong way.** Pass 1 only applies fwdata's
   changes *since the snapshot*. If the snapshot doesn't mention an entity, or agrees with fwdata
   about it, pass 1 emits nothing — even when the CRDT holds something different. Pass 2 then
   pushes the CRDT's version into fwdata, because pass 2 is a plain state push, not a merge.

## The scenario that makes complex form components behave this way

Tim's walkthrough, which fits the numbers (7,660 `AddEntryComponentChange` in 2026-07 alone):

1. FLEx has an entry-level component (complex form → component entry, no sense). A sync copies it
   into the CRDT.
2. The sync is interrupted before the snapshot is regenerated. The snapshot still has no record of
   that component.
3. In FLEx the user makes it more specific: removes the entry-level component and replaces it with
   a sense-level one (complex form → that entry's specific sense).
4. Next sync, pass 1: `before` (snapshot) has neither component; `after` (fwdata) has the
   sense-level one. So it creates the sense-level component in the CRDT — and **does not remove
   the entry-level one, because neither side mentions it**. The CRDT now holds both.
5. Pass 2: the CRDT has an entry-level component fwdata lacks → it gets written back into fwdata.
   The user's deliberate refinement is undone. 🐛

The diff key is `(ComplexFormEntryId, ComponentEntryId, ComponentSenseId)`, so an entry-level and
a sense-level link between the same two entries are *different keys* and never match. This is why
the specificity change reads as unrelated add + orphan rather than an update. The data shows 991
snapshot links that agree with fwdata on the entry pair but disagree on the sense id — exactly
this shape.

## Generalising: the bug class

Any state that (a) the CRDT holds, (b) fwdata does not, and (c) the snapshot cannot explain, will
be pushed into fwdata by pass 2. The recipe for producing such state is "sync applied it, then the
sync died before the snapshot was rewritten". So expect this class of problem for **every** field
and relation, not just complex forms. Predicted shapes to look for:

- **Orphaned "old versions" of a refined relation** — the complex-form case above. Same shape
  applies anywhere identity includes an optional discriminator: component sense id, publication
  membership, semantic domain membership, complex form types.
- **Deleted-in-FLEx entities that live on in the CRDT** and get recreated in fwdata. Note pass 1
  deletes only what the snapshot says existed; something deleted in FLEx while absent from the
  snapshot is never deleted from the CRDT.
- **Values that were cleared in FLEx and get refilled from the CRDT.** The 2,010 senses where the
  CRDT has a part of speech and fwdata has none may be exactly this — though 1,972 of them being
  `Noun` (finding 5) points at a default-value bug instead, or as well.
- **Field-level regressions on entities present everywhere**: the fwdata-bound records include 31
  `Remove /Definition/en` and 48 `Replace /Gloss/en`, i.e. English definitions being removed from
  and overwritten in fwdata.
- **Repairs that never converge.** `SetFirstTranslationIdChange` fires in both sync bursts. A
  repair that writes to the CRDT but is judged against a stale snapshot will re-run forever.

## Competing / additional explanations worth keeping alive

Do not collapse these into the interrupted-sync story without evidence:

- **A representation change in the bridge.** If `FwDataMiniLcmApi` ever changed *how* it reports a
  field, the CRDT holds values written under the old reading while fwdata reads back the new one,
  and the two disagree permanently, no interruption required. Candidate: commit 589a34b7a "Only
  use 'main' Parts of speech. Like FLEx. (#2084)". This would explain a *systematic* mismatch
  concentrated in one field better than interruption does.
- **A default applied at import.** 1,972 senses defaulting to `Noun` looks more like an importer
  or mapping default than like drift.
- **The project's fwdata was replaced.** A restore from backup, or a FLEx user sending a different
  copy, would make the CRDT legitimately "ahead" in ways no snapshot could reconcile.
- **Snapshot generation is lossy.** The old snapshot carries a sense id on only 35% of links where
  fwdata has one on 98%. That may be a stale-format artifact, or `TakeProjectSnapshot` may still
  drop them today. **This is the cheapest high-value check available**: regenerate a snapshot from
  the current CRDT and diff its links against `ComplexFormComponents`. If sense ids survive, it's
  legacy; if not, every project thrashes on every sync.

## What would confirm the leading hypothesis

- Evidence of an interrupted sync in the history: CRDT commits from a sync burst whose work is not
  reflected in the snapshot's mtime, FwHeadless logs showing a sync that started and never
  finished, or a `Commits` gap versus snapshot regeneration.
- A test that interrupts a sync between "changes applied" and "snapshot regenerated", runs the
  sync again, and shows the CRDT accumulating both versions of a refined relation. That test is
  the natural first deliverable of
  [04-brief-sync-architecture.md](04-brief-sync-architecture.md).
