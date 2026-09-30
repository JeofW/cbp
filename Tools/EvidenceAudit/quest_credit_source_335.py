"""Export typed search locations for source-backed ordinary kill-credit producers.

These locations belong to a quest objective and its actual producer, not to the
credit placeholder's creature-spawn namespace. Fresh original-client identity,
attackability, progress, navigation and recovery remain execution requirements.
"""
from collections import defaultdict
import math

from quest_collection_source_335 import row_evidence, unique_index
from quest_repair_pack_335 import BASE_FIELDS, ADDON_FIELDS

# Pinned UnitDefines.h: reject nonattackable, player-controlled, immune, taxi,
# possessed and uninteractible actors. A named corpse is never classified by name.
BLOCKED_UNIT_FLAGS = 0x00000002 | 0x00000008 | 0x00000080 | 0x00000100 | 0x00010000 | 0x00100000 | 0x01000000 | 0x02000000 | 0x80000000
ORDINARY_EVENTS = {'SMART_EVENT_UPDATE_IC', 'SMART_EVENT_HEALTH_PCT', 'SMART_EVENT_MANA_PCT',
                   'SMART_EVENT_AGGRO', 'SMART_EVENT_KILL', 'SMART_EVENT_DEATH', 'SMART_EVENT_RESET',
                   'SMART_EVENT_RANGE', 'SMART_EVENT_VICTIM_CASTING', 'SMART_EVENT_LINK'}
ORDINARY_ACTIONS = {'SMART_ACTION_CAST', 'SMART_ACTION_FLEE_FOR_ASSIST', 'SMART_ACTION_TALK', 'SMART_ACTION_PLAY_EMOTE'}


def producer_rejection(actor, scripts, event_codes, action_codes):
    for field in ('npcflag', 'unit_flags', 'unit_flags2', 'dynamicflags', 'VehicleId'):
        if type(actor.get(field)) is not int or actor[field] < 0:
            return 'producer-state-field-unknown:' + field
    if (actor['npcflag'] or actor['unit_flags'] & BLOCKED_UNIT_FLAGS or actor['unit_flags2'] & 3
            or actor['dynamicflags'] & 0x20 or actor['VehicleId']):
        return 'producer-not-established-as-ordinary-target'
    if actor.get('ScriptName') or actor.get('AIName') not in ('', 'SmartAI'):
        return 'unreviewed-producer-script-owner'
    for row in scripts:
        if (event_codes.get(row['event_type'], {}).get('name') not in ORDINARY_EVENTS
                or action_codes.get(row['action_type'], {}).get('name') not in ORDINARY_ACTIONS):
            return 'producer-has-nonordinary-or-unresolved-script-trigger'
    return None


def addon_rejection(addon):
    if not addon:
        return None
    # The pinned TDB schema stores StandState separately, not legacy bytes1.
    if type(addon.get('StandState')) is not int or addon['StandState'] == 7:
        return 'dead-or-unknown-source-pose'
    if str(addon.get('auras') or '').strip():
        return 'unreviewed-source-aura-state'
    return None


def derive(data, tables, event_codes, action_codes, source):
    required = {'quest_template', 'quest_template_addon', 'creature_template', 'creature', 'creature_addon',
                'creature_template_addon', 'smart_scripts', 'game_event_creature', 'pool_members'}
    if required - tables.keys():
        raise ValueError('Required credit producer source tables are absent: ' + ','.join(sorted(required - tables.keys())))
    quests = unique_index(tables['quest_template'], 'ID')
    addons = unique_index(tables['quest_template_addon'], 'ID')
    template_addons = unique_index(tables['creature_template_addon'], 'entry')
    spawn_addons = unique_index(tables['creature_addon'], 'guid')
    sources = defaultdict(list)
    spawns = defaultdict(list)
    scripts = defaultdict(list)
    for actor in tables['creature_template']:
        for field in ('KillCredit1', 'KillCredit2'):
            if type(actor.get(field)) is int and actor[field] > 0:
                sources[actor[field]].append((actor, field))
    for row in tables['creature']:
        spawns[row['id']].append(row)
    for row in tables['smart_scripts']:
        scripts[(row['source_type'], row['entryorguid'])].append(row)
    events = {row['guid'] for row in tables['game_event_creature']}
    pools = {row['spawnId'] for row in tables['pool_members'] if row.get('type') == 0}
    already = {(row['QuestId'], row['RowIndex']) for row in data.get('ObjectiveCreditSources', [])}
    output = []
    records = []
    prefix = 'tc335:' + source['CoreRevision'] + ':' + source['DatabaseRevision'] + ':' + source['SourceSqlSha256']
    for quest in sorted(data['Quests'], key=lambda row: row['Id']):
        ident = quest['Id']
        primary = quests.get(ident)
        addon = addons.get(ident, {})
        for ordinal, objective in enumerate(quest['Objectives']):
            if objective['Type'] != 'KillMob':
                continue
            credit = objective.get('MobId', 0)
            count = objective.get('KillCount', 0)
            if (type(credit) is not int or credit <= 0 or type(count) is not int or count <= 0
                    or objective.get('ItemId', 0) or objective.get('GameObjectId', 0)
                    or data.get('CreatureSpawns', {}).get(str(credit)) or (ident, ordinal) in already):
                continue
            candidates = sources.get(credit, [])
            if not candidates:
                continue
            record = {'quest_id': ident, 'objective_row': ordinal, 'credit_id': credit, 'required_count': count,
                      'candidates': [], 'disposition': 'unresolved'}
            records.append(record)
            conflicts = [] if primary else ['primary-template-absent']
            if primary:
                conflicts.extend(field for field in BASE_FIELDS if field in primary and quest.get(field) != primary[field])
                conflicts.extend(field for field in ADDON_FIELDS if quest.get(field) != addon.get(field, 0))
            if (conflicts or quest.get('SpecialFlags', 0) & 0x22 or not primary
                    or sum(primary.get('RequiredNpcOrGo' + str(i)) == credit and
                           primary.get('RequiredNpcOrGoCount' + str(i)) == count for i in range(1, 5)) != 1
                    or sum(primary.get('RequiredNpcOrGo' + str(i)) == credit for i in range(1, 5)) != 1):
                record.update(reason='quest-source-or-objective-contract-unresolved', conflicts=conflicts)
                continue
            for actor, field in candidates:
                entry = actor['entry']
                candidate = {'entry': entry, 'credit_field': field, 'template': row_evidence('creature_template', actor, 'entry'),
                             'ordinary_spawns': [], 'excluded_spawns': []}
                record['candidates'].append(candidate)
                if entry == credit or sum(actor.get(name) == credit for name in ('KillCredit1', 'KillCredit2')) != 1:
                    candidate['reason'] = 'ambiguous-or-self-credit'
                    continue
                behavior = scripts[(0, entry)]
                rejection = producer_rejection(actor, behavior, event_codes, action_codes) or addon_rejection(template_addons.get(entry))
                candidate['script_evidence'] = [row_evidence('smart_scripts', row, 'entryorguid') for row in behavior]
                if rejection:
                    candidate['reason'] = rejection
                    continue
                for row in spawns[entry]:
                    reason = None
                    if (type(row.get('map')) is not int or row['map'] < 0 or row.get('phaseMask') != 1
                            or type(row.get('spawnMask')) is not int or not row['spawnMask'] & 1
                            or row.get('spawntimesecs', -1) <= 0 or row['guid'] in events or row['guid'] in pools
                            or row.get('ScriptName') or scripts[(0, -row['guid'])]):
                        reason = 'conditional-or-scripted-spawn'
                    elif not all(type(row.get('position_' + axis)) in (int, float) and math.isfinite(row['position_' + axis]) for axis in 'xyz'):
                        reason = 'invalid-source-position'
                    elif (row.get('unit_flags', 0) & BLOCKED_UNIT_FLAGS or row.get('dynamicflags', 0) & 0x20):
                        reason = 'spawn-state-not-ordinary'
                    else:
                        reason = addon_rejection(spawn_addons.get(row['guid']))
                    if reason:
                        candidate['excluded_spawns'].append({'guid': row['guid'], 'reason': reason})
                    else:
                        candidate['ordinary_spawns'].append(row_evidence('creature', row, 'guid'))
                valid_guids = {value['key']['guid'] for value in candidate['ordinary_spawns']}
                points = sorted({(row['map'], row['position_x'], row['position_y'], row['position_z'])
                                 for row in spawns[entry] if row['guid'] in valid_guids})
                if not points:
                    candidate['reason'] = 'ordinary-producer-position-unavailable'
                    continue
                source_ref = prefix + ':creature_template:' + str(entry) + ':' + field + '=' + str(credit)
                hint = {'QuestId': ident, 'RowIndex': ordinal, 'ObjectiveIndex': objective['Index'], 'CreditId': credit,
                        'RequiredCount': count, 'CreatureId': entry, 'CreditField': field, 'SourceRef': source_ref,
                        'Points': [{'Map': point[0], 'X': point[1], 'Y': point[2], 'Z': point[3]} for point in points]}
                output.append(hint)
                candidate['reason'] = 'ordinary-credit-search-hint'
                record['disposition'] = 'ordinary-source-hints-available'
    return {'ObjectiveCreditSources': output}, {'schema': 'quest-credit-source-review-335-v1', 'source': dict(source),
        'objectives': records, 'live_completion_proven': False,
        'limits': 'Typed producer search locations do not create actual credit-placeholder spawns, native observations, enemy reaction, path reachability, progress, successful kills or realm equivalence.'}


def validate_loaded_sources(data, tables, event_codes, action_codes, source):
    """Re-derive every loaded hint before it can close an audit geometry gap."""
    loaded = data.get('ObjectiveCreditSources', [])
    if not isinstance(loaded, list):
        raise ValueError('Loaded credit sources must be an explicit array')
    if not loaded:
        return {}
    if len(loaded) > 30000:
        raise ValueError('Loaded credit source count exceeds the loader contract')
    without_hints = {key: value for key, value in data.items() if key != 'ObjectiveCreditSources'}
    expected, _ = derive(without_hints, tables, event_codes, action_codes, source)
    expected_index = {(row['QuestId'], row['RowIndex'], row['CreatureId']): row for row in expected['ObjectiveCreditSources']}
    result = defaultdict(list)
    seen = set()
    fields = {'QuestId', 'RowIndex', 'ObjectiveIndex', 'CreditId', 'RequiredCount', 'CreatureId', 'CreditField', 'SourceRef', 'Points'}
    for row in loaded:
        if (not isinstance(row, dict) or set(row) != fields
                or any(type(row.get(field)) is not int for field in fields - {'CreditField', 'SourceRef', 'Points'})):
            raise ValueError('Loaded credit source has invalid identity fields')
        key = (row['QuestId'], row['RowIndex'], row['CreatureId'])
        if key in seen:
            raise ValueError('Duplicate loaded credit source: ' + repr(key))
        seen.add(key)
        points = row['Points']
        if not isinstance(points, list) or not points or any(
                not isinstance(p, dict) or set(p) != {'Map', 'X', 'Y', 'Z'} or type(p.get('Map')) is not int
                or any(type(p.get(axis)) not in (int, float) or not math.isfinite(p[axis]) for axis in 'XYZ') for p in points):
            raise ValueError('Loaded credit source has invalid reference coordinates')
        if expected_index.get(key) != row:
            raise ValueError('Loaded credit source disagrees with the pinned primary producer/quest/spawn contract: ' + repr(key))
        result[(row['QuestId'], row['RowIndex'])].append(row)
    return dict(result)
