"""Export conservative, byte-bound data repairs from a pinned TC335 reference.

The program never executes SQL and never writes production. Existing dataset
values are not overwritten. Every source addition is paired with immutable row
evidence, and source conflicts remain explicit. A reference is not the realm.
"""
from __future__ import annotations
import argparse
from collections import defaultdict
import copy
import gzip
import hashlib
import json
import math
from pathlib import Path

REQUIRED_TABLES = {
    'quest_template', 'quest_template_addon', 'creature', 'gameobject',
    'creature_template', 'gameobject_template', 'creature_queststarter',
    'creature_questender', 'gameobject_queststarter', 'gameobject_questender',
    'item_template', 'creature_loot_template', 'gameobject_loot_template',
    'reference_loot_template', 'conditions', 'smart_scripts',
    'game_event_creature', 'game_event_gameobject', 'pool_members',
}
OPTIONAL_FIELDS = (
    'AllowableClasses', 'MaxLevel', 'RequiredSkillID', 'RequiredSkillPoints',
    'RequiredMinRepFaction', 'RequiredMinRepValue', 'RequiredMaxRepFaction',
    'RequiredMaxRepValue', 'RequiredFactionValue1', 'RequiredFactionValue2',
)
BASE_FIELDS = ('MinLevel', 'QuestLevel', 'AllowableRaces', 'Flags', 'QuestSortID',
               'QuestInfoID', 'RequiredFactionId1', 'RequiredFactionId2', 'StartItem')
ADDON_FIELDS = ('PrevQuestID', 'NextQuestID', 'ExclusiveGroup', 'SpecialFlags')


def encoded(value):
    return json.dumps(value, sort_keys=True, separators=(',', ':'), ensure_ascii=True, allow_nan=False).encode()


def digest(value):
    return hashlib.sha256(encoded(value)).hexdigest()


def indexed(rows, key):
    result = {}
    for row in rows:
        identity = row[key]
        if identity in result:
            raise ValueError(f'Duplicate primary identity {key}={identity}')
        result[identity] = row
    return result


def build_repairs(data: dict, primary: dict, protected_ids: set[int], source: dict) -> tuple[dict, dict]:
    missing = REQUIRED_TABLES - primary.keys()
    if missing:
        raise ValueError('Missing required primary tables: ' + ', '.join(sorted(missing)))
    base = indexed(data['Quests'], 'Id')
    templates = indexed(primary['quest_template'], 'ID')
    addons = indexed(primary['quest_template_addon'], 'ID')
    creatures = indexed(primary['creature_template'], 'entry')
    objects = indexed(primary['gameobject_template'], 'entry')
    items = indexed(primary['item_template'], 'entry')
    for key, length in [('CoreRevision', 40), ('SourceSqlSha256', 64)]:
        if len(source[key]) != length or any(c not in '0123456789abcdefABCDEF' for c in source[key]):
            raise ValueError('Unbound source identity: ' + key)
    prefix = f"tc335:{source['CoreRevision']}:{source['DatabaseRevision']}:{source['SourceSqlSha256']}"
    source_ref = lambda table, key: prefix + ':' + table + ':' + str(key)

    def evidence(table, row, key):
        return {'source_ref': source_ref(table, row[key]), 'table': table, 'key': {key: row[key]},
                'source_line': row.get('__source_line'), 'row_sha256': digest(row)}

    spawns = {kind: defaultdict(list) for kind in ['Creature', 'GameObject']}
    excluded_spawns = defaultdict(list)
    # Pinned PoolMgr::LoadFromDB uses type=0 for creatures, 1 for gameobjects
    # and 2 for subpools. Their numeric IDs do not share an object namespace.
    pooled_guids = {
        'Creature': {r['spawnId'] for r in primary['pool_members'] if r.get('type') == 0},
        'GameObject': {r['spawnId'] for r in primary['pool_members'] if r.get('type') == 1},
    }
    for kind, table in [('Creature', 'creature'), ('GameObject', 'gameobject')]:
        event_guids = {r['guid'] for r in primary['game_event_' + table]}
        for row in primary[table]:
            ordinary = (isinstance(row.get('map'), int) and row['map'] >= 0 and row.get('phaseMask') == 1
                        and isinstance(row.get('spawnMask'), int) and row['spawnMask'] & 1
                        and row.get('spawntimesecs', -1) > 0 and row['guid'] not in event_guids
                        and row['guid'] not in pooled_guids[kind]
                        and all(isinstance(row.get('position_' + axis), (int, float))
                                and not isinstance(row['position_' + axis], bool)
                                and math.isfinite(row['position_' + axis]) for axis in 'xyz'))
            if ordinary:
                spawns[kind][row['id']].append(row)
            else:
                excluded_spawns[(kind, row['id'])].append(row['guid'])
    relations = {role: defaultdict(list) for role in ['Giver', 'Ender']}
    for kind, table in [('Creature', 'creature'), ('GameObject', 'gameobject')]:
        for role, suffix in [('Giver', 'starter'), ('Ender', 'ender')]:
            for row in primary[table + '_quest' + suffix]:
                relations[role][row['quest']].append((kind, row, table + '_quest' + suffix))
    loots = {}
    for table in ['creature_loot_template', 'gameobject_loot_template', 'reference_loot_template']:
        by_entry = defaultdict(list)
        for row in primary[table]:
            by_entry[row['Entry']].append(row)
        loots[table] = by_entry

    def loot_route(table, entry, item, visited=frozenset()):
        owner = (table, entry)
        if owner in visited or len(visited) > 32:
            return []
        for row in loots[table][entry]:
            if row.get('Chance', 0) <= 0 and row.get('GroupId', 0) <= 0:
                continue
            if row.get('Reference', 0) > 0:
                path = loot_route('reference_loot_template', row['Reference'], item, visited | {owner})
                if path:
                    return [(table, row)] + path
            elif row.get('Item') == item and row.get('MaxCount', 0) > 0:
                return [(table, row)]
        return []

    pack = {'Schema': 'quest-data-repair-pack-335-v1', 'ClientBuild': 12340,
            'QuestDataSha256': source.get('QuestDataSha256', digest(data)), 'SourceCore': 'trinitycore-3.3.5',
            'CoreRevision': source['CoreRevision'], 'DatabaseRevision': source['DatabaseRevision'],
            'SourceSqlSha256': source['SourceSqlSha256'], 'QuestMetadata': [], 'SpawnAdditions': [],
            'RelationAdditions': [], 'DependencyMetadata': [], 'ObjectiveCountRepairs': []}
    review = {'schema': 'primary-quest-repair-review-v1', 'source': dict(source),
              'live_completion_proven': False, 'original_dataset_unchanged': True,
              'quests': [], 'spawn_evidence': [], 'dependency_evidence': []}
    added_spawns = set()

    def add_geometry(kind, entry, record):
        key = str(entry)
        if data.get(kind + 'Spawns', {}).get(key):
            return
        rows = spawns[kind][entry]
        if not rows:
            record['remaining'].append('no-ordinary-primary-' + kind.lower() + '-spawn:' + str(entry))
            return
        if (kind, entry) not in added_spawns:
            added_spawns.add((kind, entry))
            points = sorted({(r['map'], r['position_x'], r['position_y'], r['position_z']) for r in rows})
            ref = source_ref(kind.lower(), 'entry=' + str(entry))
            pack['SpawnAdditions'].append({'ObjectType': kind, 'Entry': entry, 'SourceRef': ref,
                'Points': [{'Map': p[0], 'X': p[1], 'Y': p[2], 'Z': p[3]} for p in points]})
            review['spawn_evidence'].append({'source_ref': ref, 'type': kind, 'entry': entry,
                'rows': [evidence(kind.lower(), r, 'guid') for r in rows],
                'excluded_conditional_or_invalid_guids': excluded_spawns[(kind, entry)]})
        record['changes'].append('geometry:' + kind + ':' + str(entry))

    requested_dependencies = set()
    negative_groups = defaultdict(set)
    for row in primary['quest_template_addon']:
        if row.get('ExclusiveGroup', 0) < 0:
            negative_groups[row['ExclusiveGroup']].add(row['ID'])
    for quest in sorted(data['Quests'], key=lambda q: q['Id']):
        ident = quest['Id']
        record = {'quest_id': ident, 'protected_baseline': ident in protected_ids, 'changes': [], 'remaining': [], 'evidence': []}
        review['quests'].append(record)
        template = templates.get(ident)
        if template is None:
            record['remaining'].append('primary-quest-template-absent')
            continue
        addon = addons.get(ident, {})
        record['evidence'].append(evidence('quest_template', template, 'ID'))
        record['evidence'].append(evidence('quest_template_addon', addon, 'ID') if addon else {
            'source_ref': source_ref('quest_template_addon', ident), 'absent': True,
            'default_contract': f"TrinityCore/{source['CoreRevision']}/src/server/game/Quests/QuestDef.h:zero-initialized-addon-fields"})
        if ident in protected_ids:
            continue
        conflicts = {field: {'base': quest.get(field), 'primary': template[field]}
                     for field in BASE_FIELDS if field in template and quest.get(field) != template[field]}
        conflicts.update({field: {'base': quest.get(field), 'primary': addon.get(field, 0)}
                          for field in ADDON_FIELDS if quest.get(field) != addon.get(field, 0)})
        if conflicts:
            record['remaining'].append('primary-field-conflict')
            record['conflicts'] = conflicts
            continue
        normal = [(template[f'RequiredNpcOrGo{i}'], template[f'RequiredNpcOrGoCount{i}'])
                  for i in range(1, 5) if template[f'RequiredNpcOrGo{i}']]
        required_pairs = [(template[f'RequiredItemId{i}'], template[f'RequiredItemCount{i}'])
                          for i in range(1, 7) if template[f'RequiredItemId{i}']]
        required = dict(required_pairs)
        if len(required) != len(required_pairs) or any(k <= 0 or v <= 0 for k, v in required_pairs):
            record['remaining'].append('ambiguous-primary-item-requirements')
            continue
        fields = {field: template.get(field, addon.get(field, 0)) for field in OPTIONAL_FIELDS if quest.get(field) is None}
        delivery = supplies = None
        if quest['Objectives'] and all(o['Type'] == 'TurnInOnly' for o in quest['Objectives']) and required and not normal and not addon.get('SpecialFlags', 0) & 0x22:
            if all(item in items for item in required):
                delivery = [{'ItemId': item, 'Count': count} for item, count in sorted(required.items())]
                supplies = []
                item = quest['StartItem']
                if item in required and item in items and items[item].get('startquest', 0) != ident:
                    # Player::GiveQuestSourceItem treats zero ProvidedItemCount as one.
                    supplies.append({'ItemId': item, 'Count': max(1, addon.get('ProvidedItemCount', 0))})
                record['changes'].append('explicit-delivery-contract')
                record['evidence'].extend(evidence('item_template', items[item], 'entry') for item in required)
            else:
                record['remaining'].append('delivery-item-template-absent')
        supplemental = None
        supplied_id = quest.get('StartItem', 0)
        if (delivery is None and supplied_id in required and supplied_id in items
                and items[supplied_id].get('startquest', 0) != ident
                and max(1, addon.get('ProvidedItemCount', 0)) >= required[supplied_id]
                and quest['Objectives'] and any(o['Type'] != 'TurnInOnly' for o in quest['Objectives'])
                and all(o.get('ItemId', 0) != supplied_id for o in quest['Objectives'])):
            supplemental = {'ItemId': supplied_id, 'RequiredCount': required[supplied_id],
                            'ProvidedCount': max(1, addon.get('ProvidedItemCount', 0))}
            record['changes'].append('supplemental-required-acceptance-item')
            record['evidence'].append(evidence('item_template', items[supplied_id], 'entry'))
        if fields or delivery is not None or supplemental is not None:
            pack['QuestMetadata'].append({'QuestId': ident, 'SourceRef': source_ref('quest_template+addon', ident),
                'Fields': fields, 'DeliveryItems': delivery, 'AcceptanceSupplies': supplies, 'SupplementalSupply': supplemental})
            if fields:
                record['changes'].append('absent-eligibility-metadata')
        for row_index, objective in enumerate(quest['Objectives']):
            kind = objective['Type']
            if kind == 'TurnInOnly':
                continue
            object_type = 'GameObject' if kind == 'CollectFromGameObject' else 'Creature'
            entry = objective.get('GameObjectId', 0) if object_type == 'GameObject' else objective.get('MobId', 0)
            actor = (objects if object_type == 'GameObject' else creatures).get(entry)
            item = objective.get('ItemId', 0)
            count = objective.get('KillCount', 0) if kind == 'KillMob' else objective.get('CollectCount', 0)
            normal_id = -entry if object_type == 'GameObject' else entry
            if (kind in ('CollectItem', 'CollectFromGameObject') and actor and item in items and item in required
                    and isinstance(count, int) and count > 0 and required[item] > 0 and count != required[item]
                    and not addon.get('SpecialFlags', 0) & 0x22):
                pack['ObjectiveCountRepairs'].append({'QuestId': ident, 'RowIndex': row_index,
                    'ObjectiveIndex': objective['Index'], 'ObjectiveType': kind, 'TargetId': entry, 'ItemId': item,
                    'ExpectedCount': count, 'RequiredCount': required[item], 'SourceRef': source_ref('quest_template', ident)})
                record['changes'].append('collection-count:' + str(row_index))
                record['evidence'].append(evidence('item_template', items[item], 'entry'))
                count = required[item]
            agrees = (required.get(item) == count) if item > 0 else normal.count((normal_id, count)) == 1
            if not actor or not agrees:
                record['remaining'].append('objective-primary-identity-or-count-unproven:' + str(objective.get('Index')))
                continue
            if item > 0:
                # GO loot selectors depend on type-specific union fields; do not
                # interpret those as creature loot IDs or invent an item source.
                loot_id = actor.get('lootid', 0) if object_type == 'Creature' else actor.get('data1', 0) if actor.get('type') == 3 else 0
                path = loot_route('creature_loot_template' if object_type == 'Creature' else 'gameobject_loot_template', loot_id, item)
                if not path:
                    record['remaining'].append('primary-loot-route-unproven:' + str(objective.get('Index')))
                    continue
                record['evidence'].extend(evidence(table, row, 'Entry') for table, row in path)
            record['evidence'].append(evidence('gameobject_template' if object_type == 'GameObject' else 'creature_template', actor, 'entry'))
            add_geometry(object_type, entry, record)
        for role, dataset_key, entry_key, type_key in [('Giver', 'QuestGivers', 'GiverId', 'GiverType'), ('Ender', 'QuestEnders', 'EnderId', 'EnderType')]:
            existing = [r for r in data[dataset_key] if r['QuestId'] == ident]
            primary_rows = relations[role][ident]
            if not existing:
                for kind, row, table in primary_rows:
                    actor = (creatures if kind == 'Creature' else objects).get(row['id'])
                    if actor is None:
                        continue
                    pack['RelationAdditions'].append({'Role': role, 'QuestId': ident, 'ObjectType': kind,
                        'Entry': row['id'], 'Name': actor.get('name') or f"{kind} {row['id']}", 'SourceRef': source_ref(table, str(row['id']) + '/' + str(ident))})
                    record['evidence'].append(evidence(table, row, 'quest'))
                    record['changes'].append(role.lower() + '-relation')
                    add_geometry(kind, row['id'], record)
            else:
                for relation in existing:
                    kind = relation.get(type_key, 'Creature')
                    if kind in (0, 1):
                        kind = 'Creature' if kind == 0 else 'GameObject'
                    if any(k == kind and r['id'] == relation[entry_key] for k, r, _ in primary_rows):
                        add_geometry(kind, relation[entry_key], record)
                    elif not data.get(kind + 'Spawns', {}).get(str(relation[entry_key])):
                        record['remaining'].append('primary-' + role.lower() + '-relation-unconfirmed:' + str(relation[entry_key]))
        external = {p for p in quest.get('PreviousQuestsIds', []) if p > 0 and p not in base}
        requested_dependencies.update(external)
        for member in external:
            if member not in templates:
                record['remaining'].append('external-predecessor-absent-from-primary:' + str(member))
        group = quest.get('ExclusiveGroup', 0)
        if group > 0:
            requested_dependencies.update(r['ID'] for r in primary['quest_template_addon'] if r.get('ExclusiveGroup') == group and r['ID'] not in base)

    for ident in sorted(requested_dependencies):
        if ident not in templates:
            continue
        group = addons.get(ident, {}).get('ExclusiveGroup', 0)
        ids = negative_groups[group] if group < 0 else {ident}
        if any(member not in templates or (member in base and base[member]['ExclusiveGroup'] != group) for member in ids):
            continue
        for member in sorted(ids):
            if any(row['QuestId'] == member for row in pack['DependencyMetadata']):
                continue
            pack['DependencyMetadata'].append({'QuestId': member, 'ExclusiveGroup': group,
                'GroupMembers': sorted(ids) if group < 0 else [], 'SourceRef': source_ref('quest_template+addon', member)})
            review['dependency_evidence'].append({'quest_id': member, 'template': evidence('quest_template', templates[member], 'ID'),
                'addon': evidence('quest_template_addon', addons[member], 'ID') if member in addons else {'absent': True}, 'group_members': sorted(ids) if group < 0 else []})
    for record in review['quests']:
        record['changes'] = sorted(set(record['changes']))
        record['remaining'] = sorted(set(record['remaining']))
    pack['SpawnAdditions'].sort(key=lambda r: (r['ObjectType'], r['Entry']))
    pack['RelationAdditions'].sort(key=lambda r: (r['QuestId'], r['Role'], r['ObjectType'], r['Entry']))
    pack['DependencyMetadata'].sort(key=lambda r: r['QuestId'])
    return pack, review


def load_verified_tables(reference: Path) -> tuple[dict, list]:
    summaries = json.loads((reference / 'parsed-summary.json').read_text(encoding='utf-8'))
    for path in sorted(reference.glob('extra-tables*-manifest.json')):
        summaries.extend(json.loads(path.read_text(encoding='utf-8'))['tables'])
    receipts = {}; tables = {}
    for receipt in summaries:
        name = receipt['table']; path = reference / 'parsed' / (name + '.json.gz')
        raw = path.read_bytes()
        if hashlib.sha256(raw).hexdigest() != receipt['parsed_sha256']:
            raise ValueError('Parsed primary reference hash changed: ' + name)
        if name in tables:
            raise ValueError('Duplicate verified table: ' + name)
        tables[name] = json.loads(gzip.decompress(raw)); receipts[name] = receipt
    if REQUIRED_TABLES - tables.keys():
        raise ValueError('The primary reference is incomplete')
    return tables, list(receipts.values())


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--dataset', type=Path, required=True)
    parser.add_argument('--reference', type=Path, required=True)
    parser.add_argument('--baseline-ledger', type=Path, required=True)
    parser.add_argument('--output', type=Path, required=True)
    parser.add_argument('--reopen-evidence', type=Path)
    args = parser.parse_args()
    if args.output.exists():
        raise FileExistsError('Repair evidence output must be new')
    raw = args.dataset.read_bytes(); data = json.loads(raw.decode('utf-8-sig'))
    old_bytes = args.baseline_ledger.read_bytes()
    old = [json.loads(line) for line in gzip.decompress(old_bytes).splitlines()]
    if {r['quest_id'] for r in old} != {q['Id'] for q in data['Quests']}:
        raise ValueError('Baseline ledger and dataset do not describe the same identities')
    tables, receipts = load_verified_tables(args.reference)
    source_receipt = json.loads((args.reference / 'source-receipt.json').read_text(encoding='utf-8'))
    source = {'CoreRevision': source_receipt['release_commit'], 'DatabaseRevision': source_receipt['release'],
              'SourceSqlSha256': source_receipt['members'][0]['sha256'], 'QuestDataSha256': hashlib.sha256(raw).hexdigest()}
    protected = {r['quest_id'] for r in old if r['classification'] in ('GENERIC-PROVEN', 'STRATEGY-PROVEN')}
    reopened = []
    if args.reopen_evidence:
        reopened = json.loads(args.reopen_evidence.read_text(encoding='utf-8'))['reopen']
        if (any(r['quest_id'] not in protected or r['source_revision'] != source['CoreRevision']
                or r['source_sql_sha256'] != source['SourceSqlSha256'] or not r.get('reason') for r in reopened)
                or len({r['quest_id'] for r in reopened}) != len(reopened)):
            raise ValueError('Reopened baseline records require unique, concrete evidence from this pinned primary source')
        protected -= {r['quest_id'] for r in reopened}
    pack, review = build_repairs(data, tables, protected, source)
    review.update(baseline_ledger_sha256=hashlib.sha256(old_bytes).hexdigest(), verified_reference_tables=receipts,
                  exporter_sha256=hashlib.sha256(Path(__file__).read_bytes()).hexdigest())
    review['reopened_baseline_evidence'] = reopened
    review['reopened_evidence_sha256'] = hashlib.sha256(args.reopen_evidence.read_bytes()).hexdigest() if args.reopen_evidence else None
    args.output.mkdir(parents=True)
    (args.output / 'quest_data.repairs.json').write_bytes(encoded(pack) + b'\n')
    with (args.output / 'repair-evidence.json.gz').open('xb') as output:
        with gzip.GzipFile(fileobj=output, mode='wb', mtime=0) as stream:
            stream.write(encoded(review))
    summary = {'base_sha256': source['QuestDataSha256'], 'protected_quests': len(protected),
               'metadata_quests': len(pack['QuestMetadata']), 'delivery_contracts': sum(r['DeliveryItems'] is not None for r in pack['QuestMetadata']),
               'supplemental_supply_contracts': sum(r.get('SupplementalSupply') is not None for r in pack['QuestMetadata']),
               'spawn_entries': len(pack['SpawnAdditions']), 'spawn_points': sum(len(r['Points']) for r in pack['SpawnAdditions']),
               'relations': len(pack['RelationAdditions']), 'dependency_records': len(pack['DependencyMetadata']),
               'collection_count_repairs': len(pack['ObjectiveCountRepairs']),
               'changed_quests': sum(bool(r['changes']) for r in review['quests']), 'no_live_completion_claim': True,
               'pack_sha256': hashlib.sha256((args.output / 'quest_data.repairs.json').read_bytes()).hexdigest()}
    (args.output / 'summary.json').write_text(json.dumps(summary, indent=2) + '\n', encoding='utf-8')
    print(json.dumps(summary, indent=2))


if __name__ == '__main__':
    main()
