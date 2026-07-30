#!/usr/bin/env python3
"""Read-only forensics for a downloaded FwHeadless project (fwdata + crdt.sqlite + fw_snapshot.json).

Usage:  python forensics.py <subcommand> <project-dir>

    links     three-way complex-form-component comparison (snapshot / crdt / fwdata)
    entries   entry + sense set comparison
    pos       sense part-of-speech audit, incl. provenance of crdt-only values
    authors   commit authorship breakdown (who wrote the CRDT history)
    changes   change-type totals, and month histogram for the biggest types

<project-dir> is a folder containing fw/fw.fwdata, crdt.sqlite and fw_snapshot.json
(the layout LcmDebugger's Utils.OpenDownloadedProject expects).

Everything here is read-only. Never point these at a project you are also syncing.
"""
import collections
import json
import sqlite3
import sys
import xml.etree.ElementTree as ET
from pathlib import Path


def paths(root):
    root = Path(root)
    p = {"fwdata": root / "fw" / "fw.fwdata", "crdt": root / "crdt.sqlite", "snapshot": root / "fw_snapshot.json"}
    missing = [str(v) for v in p.values() if not v.exists()]
    if missing:
        sys.exit("missing: " + ", ".join(missing))
    return p


def parse_fwdata(path, want_msa=False):
    """Stream .fwdata. Returns (entries, sense_owner, links, sense_pos).

    Only <rt> elements are cleared: clearing every element destroys children
    before the parent's end event and silently yields empty results.
    """
    entries, sense_owner, refs = set(), {}, []
    sense_msa, msa_fields, msa_class = {}, {}, {}
    ctx = ET.iterparse(str(path), events=("start", "end"))
    _, root = next(ctx)
    seen = 0
    for ev, el in ctx:
        if ev != "end" or el.tag != "rt":
            continue
        cls, guid = el.get("class"), (el.get("guid") or "").upper()
        if cls == "LexEntry":
            entries.add(guid)
        elif cls == "LexSense":
            sense_owner[guid] = (el.get("ownerguid") or "").upper()
            if want_msa:
                o = el.find("./MorphoSyntaxAnalysis/objsur")
                if o is not None:
                    sense_msa[guid] = (o.get("guid") or "").upper()
        elif cls == "LexEntryRef":
            comps = [o.get("guid", "").upper() for o in el.findall("./ComponentLexemes/objsur")]
            if comps:
                refs.append(((el.get("ownerguid") or "").upper(), comps))
        elif want_msa and cls and "Msa" in cls:
            msa_class[guid] = cls
            msa_fields[guid] = {c.tag: (c.find("objsur").get("guid") or "").upper()
                                for c in el if c.find("objsur") is not None}
        el.clear()
        seen += 1
        if seen % 50000 == 0:
            root.clear()

    def owner_entry(g, depth=0):
        while g in sense_owner and depth < 10:
            g, depth = sense_owner[g], depth + 1
        return g

    links = set()
    for owner, comps in refs:
        for c in comps:
            if c in sense_owner:
                links.add((owner, owner_entry(c), c))
            else:
                links.add((owner, c, ""))
    sense_pos = {s: msa_fields.get(m, {}).get("PartOfSpeech", "") for s, m in sense_msa.items()}
    return entries, sense_owner, links, (sense_pos, sense_msa, msa_fields, msa_class)


def snapshot_links(snap):
    return {(str(c["ComplexFormEntryId"]).upper(), str(c["ComponentEntryId"]).upper(),
             str(c["ComponentSenseId"] or "").upper())
            for e in snap["Entries"] for c in (e.get("Components") or [])}


def load_snapshot(p):
    return json.load(open(p, encoding="utf-8-sig"))


def crdt_links(db):
    return {(a.upper(), b.upper(), (c or "").upper()) for a, b, c in db.execute(
        "SELECT ComplexFormEntryId, ComponentEntryId, ComponentSenseId "
        "FROM ComplexFormComponents WHERE DeletedAt IS NULL")}


def cmd_links(p):
    db = sqlite3.connect(p["crdt"])
    snap = load_snapshot(p["snapshot"])
    sn, cr = snapshot_links(snap), crdt_links(db)
    _, _, fw, _ = parse_fwdata(p["fwdata"])
    print(f"links   snapshot={len(sn)}  crdt={len(cr)}  fwdata={len(fw)}")
    print(f"fwdata & crdt     = {len(fw & cr):>6}   fwdata-only={len(fw - cr)}   crdt-only={len(cr - fw)}")
    print(f"fwdata & snapshot = {len(fw & sn):>6}   snapshot-only={len(sn - fw)}")
    print(f"crdt   & snapshot = {len(cr & sn):>6}")
    pf, pc, ps = ({(a, b) for a, b, _ in s} for s in (fw, cr, sn))
    print("\nignoring ComponentSenseId:")
    print(f"fwdata & crdt pairs = {len(pf & pc)}   fwdata & snapshot pairs = {len(pf & ps)}")
    print(f"snapshot links agreeing on the entry pair but NOT the sense id, vs fwdata: "
          f"{len(pf & ps) - len(fw & sn)}")


def cmd_entries(p):
    db = sqlite3.connect(p["crdt"])
    snap = load_snapshot(p["snapshot"])
    sn_e = {str(e["Id"]).upper() for e in snap["Entries"]}
    sn_s = {str(s["Id"]).upper() for e in snap["Entries"] for s in (e.get("Senses") or [])}
    cr_e = {r[0].upper() for r in db.execute("SELECT Id FROM Entry WHERE DeletedAt IS NULL")}
    cr_s = {r[0].upper() for r in db.execute("SELECT Id FROM Sense WHERE DeletedAt IS NULL")}
    fw_e, fw_senses, _, _ = parse_fwdata(p["fwdata"])
    fw_s = set(fw_senses)
    print(f"entries snapshot={len(sn_e)}  crdt={len(cr_e)}  fwdata={len(fw_e)}")
    print(f"senses  snapshot={len(sn_s)}  crdt={len(cr_s)}  fwdata={len(fw_s)}")
    for name, s in (("snapshot", sn_e), ("fwdata", fw_e)):
        print(f"entries in {name} but not crdt: {len(s - cr_e)};  in crdt but not {name}: {len(cr_e - s)}")
    print(f"senses in fwdata but not crdt: {len(fw_s - cr_s)};  in crdt but not fwdata: {len(cr_s - fw_s)}")
    print("NOTE: 'snapshot but not crdt' means the snapshot claims state the CRDT never had -> stale/foreign snapshot.")


def cmd_pos(p):
    db = sqlite3.connect(p["crdt"])
    db.row_factory = sqlite3.Row
    _, _, _, (fw_pos, sense_msa, msa_fields, msa_class) = parse_fwdata(p["fwdata"], want_msa=True)
    cr_pos = {a.upper(): (b or "").upper() for a, b in
              db.execute("SELECT Id, PartOfSpeechId FROM Sense WHERE DeletedAt IS NULL")}
    snap = load_snapshot(p["snapshot"])
    sn_s = {str(s["Id"]).upper() for e in snap["Entries"] for s in (e.get("Senses") or [])}
    both = set(fw_pos) & set(cr_pos)
    bad = [s for s in both if fw_pos[s] != cr_pos[s]]
    print(f"senses in both fwdata and crdt: {len(both)}   POS agrees: {len(both) - len(bad)}   differs: {len(bad)}")
    only_cr = [s for s in bad if cr_pos[s] and not fw_pos[s]]
    only_fw = [s for s in bad if fw_pos[s] and not cr_pos[s]]
    print(f"  crdt has a POS / fwdata blank: {len(only_cr)}   fwdata has one / crdt blank: {len(only_fw)}   "
          f"both set but different: {len(bad) - len(only_cr) - len(only_fw)}")
    print(f"  mismatched senses absent from the snapshot (pass 1 is blind to these): "
          f"{sum(1 for s in bad if s not in sn_s)}")
    if only_cr:
        print("\nMSA classes for 'crdt has a POS / fwdata blank':",
              dict(collections.Counter(msa_class.get(sense_msa.get(s, ''), '<none>') for s in only_cr).most_common(5)))
        alt = collections.Counter()
        for s in only_cr:
            f = msa_fields.get(sense_msa.get(s, ""), {})
            alt[tuple(sorted(k for k, v in f.items() if v == cr_pos[s])) or "(no fwdata field holds the crdt value)"] += 1
        print("does any fwdata MSA field hold the crdt value?", dict(alt.most_common(4)))
        names = {}
        for r in db.execute("SELECT Id, Name FROM PartOfSpeech"):
            try:
                n = json.loads(r["Name"])
                names[r["Id"].upper()] = n.get("en") or next(iter(n.values()), "?")
            except Exception:
                names[r["Id"].upper()] = "?"
        print("crdt POS values involved:",
              dict(collections.Counter(names.get(cr_pos[s], cr_pos[s][:8]) for s in only_cr).most_common(6)))
        want = set(only_cr)
        prov, months = collections.Counter(), collections.Counter()
        for change, dt in db.execute("SELECT ce.Change, c.DateTime FROM ChangeEntities ce "
                                     "JOIN Commits c ON c.Id = ce.CommitId"):
            if "PartOfSpeech" not in change:
                continue
            try:
                j = json.loads(change)
            except Exception:
                continue
            if str(j.get("EntityId") or j.get("SenseId") or "").upper() in want:
                prov[j.get("$type", "?")] += 1
                months[dt[:7]] += 1
        print("provenance of those crdt values:", dict(prov.most_common(5)))
        print("committed in:", dict(sorted(months.items())))


def cmd_authors(p):
    db = sqlite3.connect(p["crdt"])
    db.row_factory = sqlite3.Row
    groups = collections.defaultdict(list)
    order = []
    for r in db.execute("SELECT Id, Metadata, DateTime FROM Commits ORDER BY DateTime, Counter"):
        m = json.loads(r["Metadata"]) if r["Metadata"] else {}
        groups[m.get("AuthorName") or "<no author>"].append((r["Id"], r["DateTime"]))
        order.append(r["Id"])
    pos = {c: i for i, c in enumerate(order)}
    print(f"total commits: {len(order)}")
    for author, g in sorted(groups.items(), key=lambda kv: -len(kv[1])):
        ids = [x[0] for x in g]
        qs = ",".join("?" * len(ids))
        n = db.execute(f"SELECT COUNT(*) FROM ChangeEntities WHERE CommitId IN ({qs})", ids).fetchone()[0] \
            if len(ids) < 900 else None
        idxs = [pos[i] for i in ids]
        extra = f", {n} changes ({n/len(ids):.1f}/commit)" if n is not None else ""
        print(f"  {len(g):>6}  {author:<16} {g[0][1][:10]}..{g[-1][1][:10]}  "
              f"history positions {min(idxs)}-{max(idxs)}{extra}")
    print('\nNOTE: "FieldWorks" is FwHeadless\'s DefaultAuthorForCommits, i.e. the fwdata->CRDT sync,')
    print("      not a person. <no author> in the first commits is the original import.")


def cmd_changes(p):
    db = sqlite3.connect(p["crdt"])
    types, per_type_month = collections.Counter(), collections.defaultdict(collections.Counter)
    for change, dt in db.execute("SELECT ce.Change, c.DateTime FROM ChangeEntities ce "
                                 "JOIN Commits c ON c.Id = ce.CommitId"):
        try:
            t = json.loads(change).get("$type", "?")
        except Exception:
            t = "<unparsed>"
        types[t] += 1
        per_type_month[t][dt[:7]] += 1
    print(f"total changes: {sum(types.values())}")
    for t, n in types.most_common(12):
        print(f"  {n:>7}  {t}")
    print("\nmonth histogram for the top 5 types (a repair or import that recurs is suspicious):")
    for t, _ in types.most_common(5):
        print(f"  {t}: {dict(sorted(per_type_month[t].items()))}")


CMDS = {"links": cmd_links, "entries": cmd_entries, "pos": cmd_pos, "authors": cmd_authors, "changes": cmd_changes}

if __name__ == "__main__":
    if len(sys.argv) != 3 or sys.argv[1] not in CMDS:
        sys.exit(__doc__)
    CMDS[sys.argv[1]](paths(sys.argv[2]))
