"""Bind ordinary final-item collection routes to exact primary loot and geometry.

The exporter replaces an unproved collector or appends a valid alternative when
the existing source has no geometry. It never removes auxiliary objectives or
manufactures an item receipt, navigation success, vendor or scripted action.
"""
from collections import defaultdict
import math
import re

from quest_collection_source_335 import CollectionSourceIndex, row_evidence, usable_geometry, unique_index
from quest_repair_pack_335 import BASE_FIELDS, ADDON_FIELDS


def derive(data, tables, previous, condition_codes, source, eligible_ids):
    index = CollectionSourceIndex(tables, condition_codes)
    quests = unique_index(data['Quests'], 'Id')
    templates = unique_index(tables['quest_template'], 'ID')
    addons = unique_index(tables['quest_template_addon'], 'ID')
    if not re.fullmatch('[0-9a-fA-F]{40}', source['CoreRevision']) or not re.fullmatch('[0-9a-fA-F]{64}', source['SourceSqlSha256']):
        raise ValueError('Collection routes require complete pinned source identities')
    prefix = 'tc335:' + source['CoreRevision'] + ':' + source['DatabaseRevision'] + ':' + source['SourceSqlSha256']
    owned = {(row['QuestId'], row['RowIndex']) for name in
             ('ObjectiveCountRepairs', 'GameObjectObjectiveRepairs', 'CollectionRouteRepairs') for row in previous.get(name, [])}
    event = {row['guid'] for row in tables['game_event_creature']}
    pool = {row['spawnId'] for row in tables['pool_members'] if row.get('type') == 0}
    scripts = {(row['source_type'], row['entryorguid']) for row in tables['smart_scripts']}
    primary_spawns = defaultdict(list)
    for row in tables['creature']:
        if (type(row.get('map')) is int and row['map'] >= 0 and row.get('phaseMask') == 1
                and type(row.get('spawnMask')) is int and row['spawnMask'] & 1
                and row.get('spawntimesecs', -1) > 0 and row['guid'] not in event and row['guid'] not in pool
                and not row.get('ScriptName') and (0, -row['guid']) not in scripts
                and all(type(row.get('position_' + axis)) in (int, float)
                        and math.isfinite(row['position_' + axis]) for axis in 'xyz')):
            primary_spawns[row['id']].append(row)
    relations = set()
    for kind, table in (('Creature', 'creature_queststarter'), ('GameObject', 'gameobject_queststarter')):
        relations.update((row['quest'], kind, row['id']) for row in tables[table])
    givers = defaultdict(list)
    for giver in data['QuestGivers']:
        kind = giver['GiverType']
        if (giver['QuestId'], kind, giver['GiverId']) in relations:
            for point in data.get(kind + 'Spawns', {}).get(str(giver['GiverId']), []) or []:
                if usable_geometry([point]): givers[giver['QuestId']].append(point)

    roots_by_item, referencing, creatures_by_loot = defaultdict(set), defaultdict(set), defaultdict(list)
    for name in ('creature_loot_template', 'reference_loot_template'):
        for row in tables[name]:
            if row.get('Reference', 0) > 0:
                referencing[row['Reference']].add((name, row['Entry']))
            elif row.get('Item', 0) > 0:
                roots_by_item[row['Item']].add((name, row['Entry']))
    for actor in tables['creature_template']:
        if actor.get('lootid', 0) > 0:
            creatures_by_loot[actor['lootid']].append(actor)

    def geometry(kind, entry): return data.get(kind + 'Spawns', {}).get(str(entry), [])
    def veto(points):
        return points is None or any(point is None or point.get('IsKnownSafe') is False
                                     or point.get('IsKnownReachable') is False for point in points)
    def point_key(point): return (point['Map'], point['X'], point['Y'], point['Z'])
    def source_point(row): return (row['map'], row['position_x'], row['position_y'], row['position_z'])
    def loot_for(kind, entry, item):
        actor = index.actors[kind].get(entry)
        selector = actor.get('lootid', 0) if actor and kind == 'Creature' else (
            actor.get('Data1', 0) if actor and actor.get('type') == 3 else 0)
        return index.loot_paths('creature_loot_template' if kind == 'Creature' else 'gameobject_loot_template', selector, item) if selector else {'paths': [], 'search_bounded': False}
    def ordinary(paths): return any(all(not step['conditions'] for step in path) for path in paths['paths']) and not paths['search_bounded']

    source_cache = {}
    def sources(item):
        if item in source_cache: return source_cache[item]
        pending = list(roots_by_item[item]); seen = set(); roots = set()
        while pending:
            key = pending.pop()
            if key in seen: continue
            seen.add(key)
            if len(seen) > 10000:
                source_cache[item] = []; return []
            if key[0] == 'reference_loot_template': pending.extend(referencing[key[1]])
            else: roots.add(key[1])
        result = []
        for selector in sorted(roots):
            paths = index.loot_paths('creature_loot_template', selector, item)
            # Group capacity and ordering are a separate audit. New routes only
            # use positive ungrouped edges and cannot borrow a conditional child.
            clear = [path for path in paths['paths'] if all(not step['conditions'] and step['group'] == 0
                and step['selected_group'] == 0 and step['chance'] > 0 for step in path)]
            if paths['search_bounded'] or not clear: continue
            for actor in sorted(creatures_by_loot[selector], key=lambda row: row['entry']):
                entry = actor['entry']; existing = geometry('Creature', entry)
                if actor.get('ScriptName') or actor.get('AIName') or (0, entry) in scripts or veto(existing): continue
                points = primary_spawns[entry]
                if not points: continue
                primary_keys = {source_point(row) for row in points}
                if existing and (not usable_geometry(existing) or any(not usable_geometry([point]) or point_key(point) not in primary_keys for point in existing)):
                    continue
                usable = sorted({point_key(point) for point in existing} if existing else primary_keys)
                result.append({'entry': entry, 'actor': actor, 'points': usable,
                               'spawn_rows': sorted(points, key=lambda row: row['guid']), 'paths': clear,
                               'existing_geometry': bool(existing)})
        source_cache[item] = result
        return result

    repairs, additions, records = [], [], []
    added_entries = set()
    for ident in sorted(set(eligible_ids) & quests.keys()):
        quest = quests[ident]; template = templates.get(ident); addon = addons.get(ident, {})
        objectives = quest.get('Objectives', [])
        used_indices = {row.get('Index') for row in objectives}
        used_routes = {(row.get('MobId'), row.get('ItemId')) for row in objectives if row.get('Type') == 'CollectItem'}
        next_index = max(used_indices, default=-1) + 1 if all(type(i) is int for i in used_indices) else -1
        required = [(template.get('RequiredItemId' + str(i), 0), template.get('RequiredItemCount' + str(i), 0))
                    for i in range(1, 7) if template and template.get('RequiredItemId' + str(i), 0)]
        conflicts = ([field for field in BASE_FIELDS if field in template and quest.get(field) != template[field]] +
                     [field for field in ADDON_FIELDS if quest.get(field) != addon.get(field, 0)]) if template else ['missing-template']
        for row_index, objective in enumerate(objectives):
            if objective.get('Type') not in ('CollectItem', 'CollectFromGameObject'): continue
            kind = 'Creature' if objective['Type'] == 'CollectItem' else 'GameObject'
            old = objective.get('MobId', 0) if kind == 'Creature' else objective.get('GameObjectId', 0)
            item, count = objective.get('ItemId', 0), objective.get('CollectCount', 0)
            record = {'quest_id': ident, 'row_index': row_index, 'old_kind': kind, 'old_entry': old,
                      'item_id': item, 'count': count, 'disposition': 'unresolved'}
            records.append(record)
            if (conflicts or quest.get('SpecialFlags', 0) & 0x22 or quest.get('DeliveryItems') is not None
                    or len(used_indices) != len(objectives) or any(type(i) is not int or i < 0 or i >= 10000 for i in used_indices)):
                record['reason'] = 'subject-source-script-or-index-conflict'; continue
            if (any(type(value) is not int or value <= 0 for value in (old, item, count)) or item not in index.items
                    or objective.get('KillCount', 0) != 0 or (kind == 'Creature' and objective.get('GameObjectId', 0) != 0)
                    or (kind == 'GameObject' and objective.get('MobId', 0) != 0) or required.count((item, count)) != 1
                    or sum(pair[0] == item for pair in required) != 1 or (ident, row_index) in owned):
                record['reason'] = 'nonfinal-item-ambiguous-row-or-existing-repair'; continue
            old_geometry = geometry(kind, old)
            old_paths = loot_for(kind, old, item)
            valid_old = ordinary(old_paths)
            if veto(old_geometry):
                record['reason'] = 'original-source-geometry-veto'; continue
            if valid_old and usable_geometry(old_geometry):
                record['disposition'] = 'preserved-valid-source'; continue
            old_actor = index.actors[kind].get(old)
            if not valid_old and (old_paths['paths'] or old_paths['search_bounded'] or old_actor and
                    (old_actor.get('AIName') or old_actor.get('ScriptName') or (0 if kind == 'Creature' else 1, old) in scripts)):
                record['reason'] = 'old-source-needs-script-or-loot-review'; continue
            if valid_old:
                has_alternative = False
                for other in objectives:
                    if (other is objective or other.get('Type') not in ('CollectItem', 'CollectFromGameObject')
                            or other.get('ItemId') != item or other.get('CollectCount') != count): continue
                    other_kind = 'Creature' if other['Type'] == 'CollectItem' else 'GameObject'
                    other_entry = other.get('MobId', 0) if other_kind == 'Creature' else other.get('GameObjectId', 0)
                    if usable_geometry(geometry(other_kind, other_entry) or []) and ordinary(loot_for(other_kind, other_entry, item)):
                        has_alternative = True; break
                if has_alternative:
                    record['reason'] = 'valid-alternative-already-present'; continue
            candidates = []
            for candidate in sources(item):
                if (candidate['entry'], item) in used_routes: continue
                distances = [(point[1]-giver['X'])**2 + (point[2]-giver['Y'])**2 + (point[3]-giver['Z'])**2
                             for point in candidate['points'] for giver in givers[ident] if point[0] == giver['Map']]
                if distances: candidates.append((min(distances), candidate['entry'], candidate))
            if not candidates:
                record['reason'] = 'no-ungrouped-ordinary-source-with-matching-giver-map'; continue
            selected = min(candidates, key=lambda row: (row[0], row[1]))[2]
            new_index = next_index if valid_old else objective['Index']
            if new_index < 0 or new_index >= 10000:
                record['reason'] = 'new-index-out-of-bounds'; continue
            entry = selected['entry']
            source_ref = prefix + ':collection_route:' + str(ident) + ':' + str(row_index) + ':creature=' + str(entry)
            repair = {'Operation': 'Append' if valid_old else 'Replace', 'QuestId': ident, 'RowIndex': row_index,
                      'ObjectiveIndex': objective['Index'], 'ExpectedObjectiveType': objective['Type'], 'ExpectedTargetId': old,
                      'ItemId': item, 'RequiredCount': count, 'CreatureId': entry, 'NewObjectiveIndex': new_index, 'SourceRef': source_ref}
            repairs.append(repair); used_routes.add((entry, item))
            if valid_old: next_index += 1
            if not selected['existing_geometry'] and entry not in added_entries:
                additions.append({'ObjectType': 'Creature', 'Entry': entry, 'SourceRef': source_ref,
                    'Points': [{'Map':p[0],'X':p[1],'Y':p[2],'Z':p[3]} for p in selected['points']]})
                added_entries.add(entry)
            record.update(disposition='ordinary-collection-route-repair', patch=repair,
                subject_source=row_evidence('quest_template',template,'ID'),
                old_actor_source=row_evidence('creature_template' if kind=='Creature' else 'gameobject_template',old_actor,'entry') if old_actor else None,
                target_source=row_evidence('creature_template',selected['actor'],'entry'),
                item_source=row_evidence('item_template',index.items[item],'entry'),
                loot_paths=selected['paths'], spawn_sources=[row_evidence('creature',row,'guid') for row in selected['spawn_rows']],
                retained_original_source=valid_old, selection='nearest matching-map reference giver anchor, then creature ID',
                live_access_proven=False)
    return {'CollectionRouteRepairs':repairs,'SpawnAdditions':sorted(additions,key=lambda row:row['Entry'])}, {
        'schema':'ordinary-collection-route-review-335-v1','source':dict(source),'rows':records,
        'live_completion_proven':False,'limit':'Positive ungrouped ordinary loot sources only; reference possibility is not a guaranteed drop, player attackability, travel or live completion.'}
