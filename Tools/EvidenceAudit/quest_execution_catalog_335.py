"""Derive original-335 objective mechanisms from pinned primary rows.

This classifies execution requirements; a source mechanism is not an observed
action, quest completion or a proof that every live route is reachable.
"""
from __future__ import annotations
from collections import Counter, defaultdict
import hashlib
import json
import re


def digest(value: object) -> str:
    return hashlib.sha256(json.dumps(value, sort_keys=True, separators=(',', ':'),
                                    ensure_ascii=True).encode('utf-8')).hexdigest()


def derive(data: dict, tables: dict, codes: dict[str, int], source: dict,
           cpp_files: list[dict], known_recipes: list[dict]) -> tuple[dict, dict]:
    quests = data.get('Quests', [])
    if not quests or len({q['Id'] for q in quests}) != len(quests):
        raise ValueError('Execution catalog requires a unique explicit quest population')
    primary = {r['ID']: r for r in tables['quest_template']}
    addons = {r['ID']: r for r in tables['quest_template_addon']}
    creatures = {r['entry']: r for r in tables['creature_template']}
    objects = {r['entry']: r for r in tables['gameobject_template']}
    items = {r['entry']: r for r in tables['item_template']}
    by_owner, links, callers = defaultdict(list), defaultdict(list), defaultdict(list)
    credits, events, aliases = defaultdict(list), defaultdict(list), defaultdict(list)
    event_names = {value: key for key, value in codes.items() if key.startswith('SMART_EVENT_') and '_FLAG_' not in key}
    action_names = {value: key for key, value in codes.items() if key.startswith('SMART_ACTION_')}
    for actor in creatures.values():
        for field in ('KillCredit1', 'KillCredit2'):
            if actor.get(field, 0) > 0: aliases[actor[field]].append(actor['entry'])
    for row in tables['smart_scripts']:
        by_owner[(row['source_type'], row['entryorguid'])].append(row)
        if row['link']: links[(row['source_type'], row['entryorguid'], row['link'])].append(row)
        action = action_names.get(row['action_type'], '')
        if action == 'SMART_ACTION_CALL_TIMED_ACTIONLIST': callers[row['action_param1']].append(row)
        elif action == 'SMART_ACTION_CALL_RANDOM_TIMED_ACTIONLIST':
            for i in range(1, 7):
                if row.get('action_param' + str(i), 0): callers[row['action_param' + str(i)]].append(row)
        if action == 'SMART_ACTION_CALL_KILLEDMONSTER': credits[row['action_param1']].append(row)
        if action in ('SMART_ACTION_CALL_AREAEXPLOREDOREVENTHAPPENS', 'SMART_ACTION_CALL_GROUPEVENTHAPPENS',
                      'SMART_ACTION_COMPLETE_QUEST'):
            events[row['action_param1']].append(row)

    def evidence(table: str, row: dict) -> dict:
        return {'Table': table, 'Key': str(row.get('ID', row.get('entry', row.get('entryorguid', '')))),
                'Line': row.get('__source_line', 0), 'Sha256': digest(row)}

    def roots(row: dict, seen=frozenset()) -> list[dict]:
        key = (row['source_type'], row['entryorguid'], row['id'])
        if key in seen or len(seen) > 32: return [{'unresolved': 'cyclic-smart-chain'}]
        parents = callers[row['entryorguid']] if row['source_type'] == 9 else (
            links[key] if row['event_type'] == codes.get('SMART_EVENT_LINK') else [])
        if parents: return [root for parent in parents for root in roots(parent, seen | {key})]
        if row['source_type'] == 9: return [{'unresolved': 'timed-list-without-resolved-caller', 'row': evidence('smart_scripts', row)}]
        return [{'event': event_names.get(row['event_type'], str(row['event_type'])),
                 'source_type': row['source_type'], 'entry': row['entryorguid'],
                 'parameters': [row.get('event_param' + str(i), 0) for i in range(1, 6)],
                 'phase': row.get('event_phase_mask', 0), 'row': evidence('smart_scripts', row)}]

    cpp_cache = {}
    def cpp_owners(name: str) -> list[dict]:
        if not name: return []
        if name not in cpp_cache:
            token = re.compile(r'\b' + re.escape(name) + r'\b')
            cpp_cache[name] = [{'Path': f['path'], 'Sha256': f['sha256'],
                               'Lines': [i for i, line in enumerate(f['text'].splitlines(), 1) if token.search(line)]}
                              for f in cpp_files if token.search(f['text'])]
        return cpp_cache[name]

    known = defaultdict(list)
    for recipe in known_recipes: known[(recipe['QuestId'], recipe['ObjectiveIndex'])].append(recipe)
    records, audit = [], []
    for quest in sorted(quests, key=lambda q: q['Id']):
        ident = quest['Id']; template = primary.get(ident); addon = addons.get(ident, {})
        flags = int(addon.get('SpecialFlags', quest.get('SpecialFlags', 0)))
        quest_reasons = []
        if template is None: quest_reasons.append('primary-template-unavailable')
        if flags & 0x22: quest_reasons.append('event-or-cast-special-flags')
        event_sources = [{'Credit': evidence('smart_scripts', r), 'Roots': roots(r)} for r in events[ident]]
        if event_sources: quest_reasons.append('server-script-completion-event')
        tool = items.get(template.get('StartItem', 0), {}) if template else {}
        tool_spells = [tool.get('spellid_' + str(i), 0) for i in range(1, 6)
                       if tool.get('spelltrigger_' + str(i), -1) == 0 and tool.get('spellid_' + str(i), 0) > 0]
        objective_records, objective_audit = [], []
        for ordinal, objective in enumerate(quest.get('Objectives', [])):
            kind = objective['Type']; mob = int(objective.get('MobId', 0)); go = int(objective.get('GameObjectId', 0))
            item = int(objective.get('ItemId', 0)); index = int(objective.get('Index', ordinal))
            count = int(objective.get('KillCount', 0) if kind == 'KillMob' else objective.get('CollectCount', 0))
            actor = creatures.get(mob) if mob else objects.get(go)
            actor_table = 'creature_template' if mob else 'gameobject_template'
            reasons = list(quest_reasons)
            credit_sources = [{'Credit': evidence('smart_scripts', r), 'Roots': roots(r)} for r in credits[mob]] if kind == 'KillMob' else []
            cpp = cpp_owners(actor.get('ScriptName', '')) if actor else []
            if kind == 'KillMob':
                if mob <= 0 or count <= 0 or count > 65535: reasons.append('invalid-normal-credit')
                if template and sum(template.get('RequiredNpcOrGo' + str(i)) == mob and
                                    template.get('RequiredNpcOrGoCount' + str(i)) == count for i in range(1, 5)) != 1:
                    reasons.append('normal-credit-does-not-match-primary-template')
                if not actor and not aliases[mob]: reasons.append('credit-actor-unavailable')
                if actor and actor.get('npcflag', 0): reasons.append('service-or-gossip-credit-target')
                if actor and actor.get('ScriptName'): reasons.append('cpp-objective-owner')
                if any(root.get('event') not in ('SMART_EVENT_DEATH', 'SMART_EVENT_KILL')
                       for path in credit_sources for root in path['Roots']): reasons.append('non-death-credit-trigger')
                if tool_spells: reasons.append('provided-use-item-requires-contract')
            elif kind == 'CollectItem':
                if item <= 0 or count <= 0: reasons.append('invalid-item-objective')
                if template and not any(template.get('RequiredItemId' + str(i)) == item and
                                        template.get('RequiredItemCount' + str(i)) == count for i in range(1, 7)):
                    reasons.append('item-objective-does-not-match-primary-template')
                if mob <= 0: reasons.append('item-acquisition-owner-required')
                if actor and actor.get('ScriptName'): reasons.append('cpp-item-producer-owner')
            elif kind == 'CollectFromGameObject':
                if go <= 0 or actor is None: reasons.append('gameobject-source-unavailable')
                if actor and actor.get('ScriptName'): reasons.append('cpp-gameobject-owner')
                # Generic collection admits only the existing object-use/loot
                # primitive. Event/transport/vehicle/focus objects require a
                # specific executor; the live CanUse/credit checks remain later.
                if actor and actor.get('type') not in (0, 1, 3, 10): reasons.append('special-gameobject-mechanism')
                if item > 0:
                    if count <= 0 or template and not any(template.get('RequiredItemId' + str(i)) == item
                            and template.get('RequiredItemCount' + str(i)) == count for i in range(1, 7)):
                        reasons.append('gameobject-item-does-not-match-primary-template')
                elif template and not any(template.get('RequiredNpcOrGo' + str(i)) == -go for i in range(1, 5)):
                    reasons.append('gameobject-credit-does-not-match-primary-template')
            elif kind == 'TurnInOnly':
                if template and any(template.get('RequiredNpcOrGo' + str(i), 0) for i in range(1, 5)):
                    reasons.append('turn-in-row-omits-normal-objectives')
                # Required item stock/delivery and acceptance are guarded by the
                # existing validated repair and inventory policies, not granted
                # by this absence of an action objective.
            else: reasons.append('unknown-objective-kind')

            driver = 'Primitive' if not reasons else 'Unsupported'
            recipe = known.get((ident, index), [])
            if len(recipe) == 1 and recipe[0].get('CreditId') == mob and recipe[0].get('CreditCount') == count:
                driver = 'DeclaredStrategy'
            if ident == 9472 and template and kind == 'KillMob' and mob == 17226 and count == 1:
                viera = by_owner[(0, 17226)]
                hit = [r for r in viera if r['event_type'] == codes.get('SMART_EVENT_SPELLHIT') and r['event_param1'] == 30077]
                lure = [r for r in viera if r['event_type'] == codes.get('SMART_EVENT_REWARD_QUEST') and r['event_param1'] == 9483]
                reward = primary.get(9483, {})
                vendor = any(r.get('entry') == 18907 and r.get('item') == 29112 for r in tables.get('npc_vendor', []))
                if (template.get('StartItem') == 23693 and 30077 in tool_spells and hit and lure and vendor
                        and reward.get('RequiredItemId1') == 29112 and reward.get('RequiredItemCount1') == 1
                        and any(root.get('event') == 'SMART_EVENT_SPELLHIT' and root.get('entry') == 17226
                                and root['parameters'][0] == 30077 for path in credit_sources for root in path['Roots'])):
                    driver = 'ArelionsMistress'
                else: reasons.append('compound-lure-source-chain-incomplete')

            record = {'RowIndex': ordinal, 'ObjectiveIndex': index, 'Type': kind, 'MobId': mob,
                      'ItemId': item, 'GameObjectId': go, 'KillCount': int(objective.get('KillCount', 0)),
                      'CollectCount': int(objective.get('CollectCount', 0)), 'Driver': driver,
                      'Reasons': sorted(set(reasons)), 'PrimaryActorSha256': digest(actor) if actor else '',
                      'CreditEvidenceSha256': digest(credit_sources)}
            objective_records.append(record)
            objective_audit.append({'Contract': record, 'PrimaryActor': evidence(actor_table, actor) if actor else None,
                                    'CreditSources': credit_sources, 'CppOwners': cpp, 'Aliases': aliases[mob] if kind == 'KillMob' else [],
                                    'SourceItem': evidence('item_template', tool) if tool else None, 'ItemSpells': tool_spells})
        if not objective_records: status = 'SourceUnresolved'
        elif any(r['Driver'] == 'Unsupported' for r in objective_records): status = 'HandlerOrSourceRequired'
        elif any(r['Driver'] != 'Primitive' for r in objective_records): status = 'ImplementedStrategy'
        else: status = 'PrimitiveCandidate'
        records.append({'QuestId': ident, 'Status': status, 'PrimaryQuestSha256': digest(template) if template else '',
                        'PrimaryAddonSha256': digest(addon) if addon else '', 'Objectives': objective_records})
        audit.append({'quest_id': ident, 'name': quest.get('Name'), 'status': status,
                      'primary_quest': evidence('quest_template', template) if template else None,
                      'quest_script_events': event_sources, 'objectives': objective_audit})
    catalog = {'SchemaVersion': 1, 'ClientBuild': 12340, 'SourceCore': 'TrinityCore', 'SourceBranch': '3.3.5',
               'SourceRevision': source['revision'], 'DatabaseRevision': source['database_revision'],
               'QuestDataSha256': source['dataset_sha256'], 'QuestDataRepairsSha256': source['repairs_sha256'],
               'StrategyPackSha256': source['strategy_sha256'], 'PrimarySqlSha256': source['sql_sha256'],
               'QuestCount': len(records), 'Quests': records}
    summary = {'schema': 'quest-execution-source-audit-335-v1', 'source': source, 'quest_count': len(records),
               'objective_rows': sum(len(r['Objectives']) for r in records),
               'exclusive_quest_status_counts': dict(Counter(r['Status'] for r in records)),
               'objective_driver_counts': dict(Counter(o['Driver'] for r in records for o in r['Objectives'])),
               'obligation_counts_nonexclusive': dict(Counter(reason for r in records for o in r['Objectives'] for reason in o['Reasons'])),
               'records': audit, 'catalogue_digest': digest(catalog), 'live_completion_proven': False,
               'qualification': 'Every dataset member is source-correlated. PrimitiveCandidate is a mechanism/admission category; it is not a live completion or full-execution certificate.'}
    return catalog, summary
