"""Derive finite settlement footprints from verified TDB335 spawns and original factions."""
from pathlib import Path
import collections
import hashlib
import json
import math
import struct
import sys

import argparse
parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument('--reference', type=Path, required=True)
parser.add_argument('--dbc-dir', type=Path, required=True)
parser.add_argument('--repo', type=Path, default=Path(__file__).resolve().parents[2])
parser.add_argument('--write', action='store_true')
args = parser.parse_args()
REPO = args.repo.resolve()
REF = args.reference.resolve()
DBC = args.dbc_dir.resolve()
sys.path.insert(0, str(REPO / 'Tools/EvidenceAudit'))
from quest_repair_pack_335 import load_verified_tables

tables, sql_receipts = load_verified_tables(REF)
dbc_manifest = json.loads((DBC / "manifest.json").read_text())
raw = (DBC / "FactionTemplate.dbc").read_bytes()
receipt = next(row for row in dbc_manifest["dbc"] if row["table"] == "FactionTemplate")
if hashlib.sha256(raw).hexdigest() != receipt["sha256"]:
    raise ValueError("Original faction table identity changed")
_, count, fields, size, string_size = struct.unpack_from("<4s4I", raw)
if fields != 14 or size != 56:
    raise ValueError("Unexpected build12340 faction template layout")
factions = {row[0]: row for row in struct.iter_unpack("<14I", raw[20:20 + count * size])}
templates = {int(row["entry"]): row for row in tables["creature_template"]}

def team(template):
    row = factions.get(int(template["faction"]))
    if row is None:
        return None
    # Exact original FactionMasks: Alliance=2, Horde=4. A positive
    # corresponding EnemyGroup proves opposing alignment; monsters and
    # reputation-neutral services do not become faction bases.
    own = (row[3] | row[4]) & 6
    enemy = row[5] & 6
    if own == 2 and enemy == 4:
        return "Alliance"
    if own == 4 and enemy == 2:
        return "Horde"
    return None

populations = collections.defaultdict(list)
for spawn in tables["creature"]:
    map_id = int(spawn["map"])
    if map_id not in (530, 571) or not int(spawn["spawnMask"]) & 1 or not int(spawn["phaseMask"]) & 1:
        continue
    entry = int(spawn["id"])
    template = templates.get(entry)
    if template is None:
        continue
    alignment = team(template)
    if alignment is None:
        continue
    x, y, z = (float(spawn["position_" + axis]) for axis in "xyz")
    if not all(math.isfinite(value) for value in (x, y, z)):
        raise ValueError("Nonfinite source spawn")
    populations[map_id, alignment].append({"guid": int(spawn["guid"]), "entry": entry, "name": template["name"],
        "faction": int(template["faction"]), "x": x, "y": y, "z": z,
        "service": bool(int(template["npcflag"]) & (0x2000 | 0x10000 | 0x400000)),
        "guard": bool(int(template["flags_extra"]) & 0x8000), "source_line": spawn["__source_line"]})

def near(a, b, radius):
    return (a["x"] - b["x"]) ** 2 + (a["y"] - b["y"]) ** 2 <= radius ** 2

regions = []
for (map_id, alignment), people in sorted(populations.items()):
    services = [person for person in people if person["service"]]
    remaining = set(range(len(services)))
    while remaining:
        first = remaining.pop()
        component, frontier = {first}, [first]
        while frontier:
            index = frontier.pop()
            linked = {other for other in remaining if near(services[index], services[other], 320)}
            remaining -= linked
            component |= linked
            frontier.extend(linked)
        anchors = [services[index] for index in sorted(component)]
        occupants = [person for person in people if any(near(person, anchor, 240) for anchor in anchors)]
        # Buffer is an explicit route policy, not a claimed server aggro radius.
        # Expand the source footprint so a smoothed aerial route stays outside
        # the settlement, independent of altitude.
        xmin, xmax = min(p["x"] for p in occupants) - 80, max(p["x"] for p in occupants) + 80
        ymin, ymax = min(p["y"] for p in occupants) - 80, max(p["y"] for p in occupants) + 80
        regions.append({"map": map_id, "team": alignment, "bounds": [xmin, ymin, xmax, ymax],
                        "anchors": anchors, "occupants": occupants})

# Overlapping exclusions must not create ambiguous visibility-graph holes.
# Merge only intersecting rectangles from the same map/alignment and retain
# every primary source row in the receipt.
changed = True
while changed:
    changed = False
    for i, left in enumerate(regions):
        for j in range(i + 1, len(regions)):
            right = regions[j]
            a, b = left["bounds"], right["bounds"]
            if (left["map"], left["team"]) != (right["map"], right["team"]) or a[0] > b[2] or b[0] > a[2] or a[1] > b[3] or b[1] > a[3]:
                continue
            left["bounds"] = [min(a[0], b[0]), min(a[1], b[1]), max(a[2], b[2]), max(a[3], b[3])]
            for key in ("anchors", "occupants"):
                left[key] = list({person["guid"]: person for person in left[key] + right[key]}.values())
            regions.pop(j)
            changed = True
            break
        if changed:
            break

regions.sort(key=lambda region: (region["map"], region["team"], min(p["guid"] for p in region["anchors"])))
for region in regions:
    region["id"] = f"{region['map']}-{region['team']}-{min(p['guid'] for p in region['anchors'])}"
    region["label"] = ", ".join(sorted({p["name"] for p in region["anchors"]})[:3])
result = {"schema": 1, "client_build": 12340, "tdb_commit": "95657f54779467effea8a1749a61ff93abc1d707",
          "faction_sha256": receipt["sha256"], "definitions": dbc_manifest["sources"],
          "sql_receipts": [row for row in sql_receipts if row["table"] in ("creature", "creature_template")],
          "policy": {"service_join_yards": 320, "nearby_population_yards": 240, "margin_yards": 80,
                     "altitude_independent": True, "dynamic_server_overrides_proven": False}, "regions": regions}

# Shared/contested footprints require live ownership, not a permanent faction
# assumption. Retain them in the review ledger; only unambiguous bases are
# included in the default flight exclusions.
for left in regions:
    a = left['bounds']
    left['conditional'] = any(right['map'] == left['map'] and right['team'] != left['team']
        and a[0] <= right['bounds'][2] and right['bounds'][0] <= a[2]
        and a[1] <= right['bounds'][3] and right['bounds'][1] <= a[3] for right in regions)
    left['disposition'] = 'shared-or-contested; observed guards required' if left['conditional'] else 'static-opposing-settlement'
lines = ['using System.Collections.Generic;', 'using Tripper.XNAMath;', '',
         'namespace Styx.Logic.Pathing.FlightorNavigation;', '',
         '// Generated by Tools/EvidenceAudit/aerial_settlements_335.py.',
         '// Original client build12340 faction masks plus pinned TDB335.25101.',
         '// Bounds contain an explicit 80-yard planning margin, not a server aggro guarantee.',
         'internal static class AerialSettlementData', '{',
         '    internal readonly record struct Region(uint Map, WoWFactionGroup Owner, float MinX, float MinY, float MaxX, float MaxY, string Id);',
         '    internal static readonly Region[] Regions = new[]', '    {']
for row in regions:
    if row['conditional']:
        continue
    numbers = ', '.join(format(value, '.9g') + 'f' for value in row['bounds'])
    lines.append(f'        new Region({row["map"]}U, WoWFactionGroup.{row["team"]}, {numbers}, "{row["id"]}"),')
lines += ['    };', '', '    internal static IEnumerable<Vector2[]> For(uint map, WoWFactionGroup actor)', '    {',
          '        if (actor == WoWFactionGroup.Neutral) yield break;',
          '        foreach (var region in Regions)',
          '            if (region.Map == map && region.Owner != actor)',
          '                yield return new[] { new Vector2(region.MinX, region.MinY), new Vector2(region.MaxX, region.MinY),',
          '                    new Vector2(region.MaxX, region.MaxY), new Vector2(region.MinX, region.MaxY) };',
          '    }', '}', '']
generated = '\n'.join(lines)
source_path = REPO / 'Styx/Logic/Pathing/FlightorNavigation/AerialSettlementData.cs'
facts_path = REPO / 'docs/audit/2026-10-04/aerial-settlements.json'
result['regions'] = regions
result['generator_sha256'] = hashlib.sha256(Path(__file__).read_bytes()).hexdigest()
facts = json.dumps(result, indent=2) + '\n'
if args.write:
    facts_path.parent.mkdir(parents=True, exist_ok=True)
    source_path.write_bytes(generated.encode('utf-8'))
    facts_path.write_bytes(facts.encode('utf-8'))
elif source_path.read_text(encoding='utf-8-sig') != generated or json.loads(facts_path.read_text(encoding='utf-8-sig')) != result:
    raise RuntimeError('Tracked aerial settlement facts differ from their pinned primary inputs')
print(json.dumps({'regions_reviewed': len(regions), 'static': sum(not row['conditional'] for row in regions),
                  'conditional': sum(row['conditional'] for row in regions), 'verified': True}))
