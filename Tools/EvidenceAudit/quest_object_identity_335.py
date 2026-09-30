"""Derive exact ordinary chest identity patches from pinned source namespaces.

The old number must be an unproved actor and the loot selector of exactly one
ordinary chest with the required item and conservative geometry. This does not
reconstruct the old exporter, prove the customized realm, or certify live access.
"""
from collections import defaultdict
import math

from quest_collection_source_335 import CollectionSourceIndex, row_evidence, unique_index, usable_geometry
from quest_repair_pack_335 import BASE_FIELDS, ADDON_FIELDS


def derive(data, tables, previous, condition_codes, source):
    index = CollectionSourceIndex(tables, condition_codes)
    templates = unique_index(tables['quest_template'], 'ID')
    addons = unique_index(tables['quest_template_addon'], 'ID')
    by_loot = defaultdict(list)
    for actor in tables['gameobject_template']:
        if actor.get('type') == 3 and type(actor.get('Data1')) is int and actor['Data1'] > 0:
            by_loot[actor['Data1']].append(actor)
    spawns = defaultdict(list)
    for row in tables['gameobject']:
        spawns[row['id']].append(row)
    event = {row['guid'] for row in tables['game_event_gameobject']}
    pool = {row['spawnId'] for row in tables['pool_members'] if row.get('type') == 1}
    count_owners = {(row['QuestId'], row['RowIndex']) for row in previous.get('ObjectiveCountRepairs', [])}
    prior_identities = {(row['QuestId'], row['RowIndex']) for row in previous.get('GameObjectObjectiveRepairs', [])}
    additions = []
    corrections = []
    records = []
    added_spawns = set()
    new_owners = set()
    prefix = 'tc335:' + source['CoreRevision'] + ':' + source['DatabaseRevision'] + ':' + source['SourceSqlSha256']

    def ordinary_spawn(row):
        return (type(row.get('map')) is int and row['map'] >= 0 and row.get('phaseMask') == 1
                and type(row.get('spawnMask')) is int and row['spawnMask'] & 1
                and row.get('spawntimesecs', -1) > 0 and row['guid'] not in event and row['guid'] not in pool
                and not row.get('ScriptName')
                and all(type(row.get('position_' + axis)) in (int, float)
                        and math.isfinite(row['position_' + axis]) for axis in 'xyz'))

    for quest in sorted(data['Quests'], key=lambda row: row['Id']):
        ident = quest['Id']
        template = templates.get(ident)
        addon = addons.get(ident, {})
        for row_index, objective in enumerate(quest['Objectives']):
            if objective.get('Type') != 'CollectFromGameObject':
                continue
            old = objective.get('GameObjectId', 0)
            item = objective.get('ItemId', 0)
            count = objective.get('CollectCount', 0)
            record = {'quest_id': ident, 'row_index': row_index, 'old_entry': old,
                      'item_id': item, 'required_count': count, 'disposition': 'unresolved'}
            records.append(record)
            if (not template or type(old) is not int or old <= 0 or type(item) is not int or item <= 0
                    or type(count) is not int or count <= 0 or item not in index.items
                    or type(objective.get('Index')) is not int or objective['Index'] < 0):
                record['reason'] = 'missing-or-invalid-required-source-identity'
                continue
            required = [(template.get('RequiredItemId' + str(i), 0), template.get('RequiredItemCount' + str(i), 0))
                        for i in range(1, 7) if template.get('RequiredItemId' + str(i), 0)]
            conflicts = [field for field in BASE_FIELDS if field in template and quest.get(field) != template[field]]
            conflicts += [field for field in ADDON_FIELDS if quest.get(field) != addon.get(field, 0)]
            if conflicts or quest.get('SpecialFlags', 0) & 0x22:
                record['reason'] = 'quest-source-conflict-or-scripted-owner'
                record['conflicting_fields'] = sorted(conflicts)
                continue
            if (required.count((item, count)) != 1 or sum(pair[0] == item for pair in required) != 1
                    or objective.get('MobId', 0) != 0 or objective.get('KillCount', 0) != 0
                    or (ident, row_index) in count_owners or (ident, row_index) in prior_identities):
                record['reason'] = 'objective-contract-conflict-or-existing-repair'
                continue
            original_actor = index.actors['GameObject'].get(old)
            if original_actor:
                record['old_actor'] = row_evidence('gameobject_template', original_actor, 'entry')
                old_loot = original_actor.get('Data1', 0) if original_actor.get('type') == 3 else 0
                if (original_actor.get('AIName') or original_actor.get('ScriptName')
                        or (old_loot and index.loot_paths('gameobject_loot_template', old_loot, item)['paths'])):
                    record['reason'] = 'existing-actor-has-loot-or-script-evidence'
                    continue
            matches = by_loot.get(old, [])
            record['matching_template_ids'] = [actor['entry'] for actor in matches]
            if len(matches) != 1 or matches[0]['entry'] == old:
                record['reason'] = 'missing-or-ambiguous-chest-template'
                continue
            actor = matches[0]
            entry = actor['entry']
            if actor.get('AIName') or actor.get('ScriptName') or actor.get('Data6') or actor.get('Data7'):
                record['reason'] = 'candidate-has-script-event-or-trap-owner'
                continue
            paths = index.loot_paths('gameobject_loot_template', actor['Data1'], item)
            ordinary_paths = [path for path in paths['paths'] if all(not edge['conditions'] for edge in path)]
            if not ordinary_paths:
                record['reason'] = 'required-item-has-no-unconditional-ordinary-loot-path'
                continue
            if ((ident, entry, item) in new_owners or any(other is not objective and
                    other.get('Type') == 'CollectFromGameObject' and other.get('GameObjectId') == entry
                    and other.get('ItemId') == item for other in quest['Objectives'])):
                record['reason'] = 'would-duplicate-an-existing-objective-owner'
                continue
            key = str(entry)
            existing = data.get('GameObjectSpawns', {}).get(key, [])
            eligible = [row for row in spawns[entry] if ordinary_spawn(row)]
            if existing is None or (existing and not usable_geometry(existing)) or (not existing and not eligible):
                record['reason'] = 'candidate-geometry-unknown-conditional-or-vetoed'
                continue
            source_ref = prefix + ':gameobject_template:' + str(entry) + ':loot=' + str(old)
            correction = {'QuestId': ident, 'RowIndex': row_index, 'ObjectiveIndex': objective['Index'],
                          'ObjectiveType': 'CollectFromGameObject', 'ExpectedGameObjectId': old,
                          'GameObjectId': entry, 'ItemId': item, 'RequiredCount': count, 'SourceRef': source_ref}
            corrections.append(correction)
            new_owners.add((ident, entry, item))
            if not existing and entry not in added_spawns:
                points = sorted({(row['map'], row['position_x'], row['position_y'], row['position_z']) for row in eligible})
                additions.append({'ObjectType': 'GameObject', 'Entry': entry, 'SourceRef': source_ref,
                                  'Points': [{'Map': p[0], 'X': p[1], 'Y': p[2], 'Z': p[3]} for p in points]})
                added_spawns.add(entry)
            record.update(disposition='exact-ordinary-identity-patch', correction=correction,
                          target_template=row_evidence('gameobject_template', actor, 'entry'),
                          required_item=row_evidence('item_template', index.items[item], 'entry'),
                          quest_template=row_evidence('quest_template', template, 'ID'),
                          loot_paths=ordinary_paths, spawn_rows=[row_evidence('gameobject', row, 'guid') for row in eligible],
                          preserved_existing_geometry=bool(existing), lock_id=actor.get('Data0'),
                          live_lock_and_phase_access_proven=False)
    additions.sort(key=lambda row: row['Entry'])
    return {'GameObjectObjectiveRepairs': corrections, 'SpawnAdditions': additions}, {
        'schema': 'quest-object-identity-review-335-v1', 'source': dict(source), 'rows': records,
        'live_completion_proven': False,
        'inference_limit': 'The exact required item uses the imported actor number as a unique primary chest loot selector. The historical exporter and customized realm are not inferred.'}
