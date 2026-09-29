"""Reconcile every quest against tested effective data and pinned primary evidence.

No source join, available strategy, supplied counter or report-only change is
itself execution proof. Earlier row evidence is preserved by immutable digest;
new classifications require a recorded passing production-owner pipeline.
"""
from __future__ import annotations
import argparse
from collections import Counter, defaultdict
import copy
import gzip
import hashlib
import json
import math
from pathlib import Path
import re

CLASSIFICATIONS = ('GENERIC-PROVEN', 'STRATEGY-PROVEN', 'DATA-INVALID/INCOMPLETE',
                   'UNSUPPORTED-SCRIPTED', 'SOURCE-UNCERTAIN', 'LIVE-ACCEPTANCE-REQUIRED')


def final_classification(obligations, simulation, strategy_status='MISSING', baseline='DATA-INVALID/INCOMPLETE'):
    if any(value.startswith('data:') for value in obligations):
        return 'DATA-INVALID/INCOMPLETE'
    if any(value.startswith('source:') for value in obligations):
        return 'SOURCE-UNCERTAIN'
    if any(value.startswith('script:') for value in obligations):
        return 'UNSUPPORTED-SCRIPTED'
    if (any(value.startswith('live:') for value in obligations)
            or simulation.get('failed_cases', 1) != 0 or simulation.get('pipeline_status') != 'PASS'):
        return 'LIVE-ACCEPTANCE-REQUIRED'
    return 'STRATEGY-PROVEN' if strategy_status == 'VALIDATED-PIPELINE' else 'GENERIC-PROVEN'


def has_collection_alternative_geometry(quest, objective, effective, primary_required):
    item = objective.get('ItemId', 0); count = objective.get('CollectCount', 0)
    if (objective.get('Type') not in ('CollectItem', 'CollectFromGameObject') or item <= 0
            or count <= 0 or primary_required.get(item) != count):
        return False
    for candidate in quest['Objectives']:
        if (candidate.get('Type') not in ('CollectItem', 'CollectFromGameObject') or candidate.get('ItemId') != item
                or candidate.get('CollectCount') != count):
            continue
        kind = 'GameObject' if candidate['Type'] == 'CollectFromGameObject' else 'Creature'
        entry = candidate.get('GameObjectId', 0) if kind == 'GameObject' else candidate.get('MobId', 0)
        if any(point and point.get('Map', -1) >= 0 and all(isinstance(point.get(axis), (int, float)) and math.isfinite(point[axis])
                for axis in ['X', 'Y', 'Z']) for point in effective.get(kind + 'Spawns', {}).get(str(entry), [])):
            return True
    return False


def category_index(rows):
    categories = defaultdict(set)
    for row in rows:
        for obligation in row['remaining_obligations']:
            name = obligation.rsplit(':', 1)[0] if obligation.rsplit(':', 1)[-1].isdigit() else obligation
            categories[name].add(row['quest_id'])
    return {name: sorted(ids) for name, ids in sorted(categories.items())}


def _encoded(value):
    return json.dumps(value, ensure_ascii=True, sort_keys=True, separators=(',', ':'), allow_nan=False).encode()


def _sha(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def _constants(path, prefix):
    result = {}
    for number, line in enumerate(path.read_text(encoding='utf-8').splitlines(), 1):
        match = re.match(r'\s*(' + prefix + r'[A-Z0-9_]+)\s*=\s*(0x[0-9A-Fa-f]+|\d+)\s*,', line)
        if match and not any(part in match[1] for part in ['_FLAG_', '_FLAGS_', '_END', '_MAX']):
            result[int(match[2], 0)] = {'name': match[1], 'source_file': path.name, 'line': number}
    return result


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    for name in ['baseline', 'simulation', 'effective', 'dependencies', 'repair_evidence', 'reference', 'output']:
        parser.add_argument('--' + name.replace('_', '-'), type=Path, required=True)
    parser.add_argument('--strategy-results', type=Path)
    args = parser.parse_args()
    if args.output.exists():
        raise FileExistsError('Closure ledgers are create-only')
    from quest_repair_pack_335 import load_verified_tables, digest, BASE_FIELDS, ADDON_FIELDS, OPTIONAL_FIELDS
    tables, table_receipts = load_verified_tables(args.reference)
    old_rows = [json.loads(line) for line in gzip.decompress(args.baseline.read_bytes()).splitlines()]
    old = {row['quest_id']: row for row in old_rows}
    simulations = {row['quest_id']: row for row in map(json.loads, args.simulation.read_text(encoding='utf-8').splitlines())}
    effective = json.loads(args.effective.read_text(encoding='utf-8'))
    model = {row['Id']: row for row in effective['Quests']}
    dependencies = {int(key): value for key, value in json.loads(args.dependencies.read_text()).items()}
    repair = json.loads(gzip.decompress(args.repair_evidence.read_bytes()))
    repair_rows = {row['quest_id']: row for row in repair['quests']}
    if set(old) != set(model) or set(old) != set(simulations) or len(old_rows) != len(old) or len(old) != 4335:
        raise ValueError('Every original quest must occur exactly once in each closure input')
    if any(row['failed_cases'] for row in simulations.values()):
        raise ValueError('A failing dataset sweep cannot become a closure ledger')
    source = json.loads((args.reference / 'source-receipt.json').read_text())
    source_sql = source['members'][0]['sha256']
    revision = source['release_commit']
    input_hashes = {name: _sha(getattr(args, name)) for name in
                    ['baseline', 'simulation', 'effective', 'dependencies', 'repair_evidence']}
    qt = {row['ID']: row for row in tables['quest_template']}
    qa = {row['ID']: row for row in tables['quest_template_addon']}
    actors = {'Creature': {row['entry']: row for row in tables['creature_template']},
              'GameObject': {row['entry']: row for row in tables['gameobject_template']}}
    primary_relations = defaultdict(list)
    for kind, table in [('Creature', 'creature'), ('GameObject', 'gameobject')]:
        for role in ['starter', 'ender']:
            for row in tables[table + '_quest' + role]:
                primary_relations[row['quest']].append({'role': role, 'object_type': kind, 'row': row,
                                                       'table': table + '_quest' + role})
    conditions = defaultdict(list)
    condition_codes = _constants(args.reference / 'contracts/ConditionMgr.h', 'CONDITION_SOURCE_TYPE_')
    available_code = next(key for key, value in condition_codes.items() if value['name'] == 'CONDITION_SOURCE_TYPE_QUEST_AVAILABLE')
    for row in tables['conditions']:
        if row['SourceTypeOrReferenceId'] == available_code:
            conditions[row['SourceEntry']].append(row)
    events = _constants(args.reference / 'contracts/SmartScriptMgr.h', 'SMART_EVENT_')
    actions = _constants(args.reference / 'contracts/SmartScriptMgr.h', 'SMART_ACTION_')
    script_index = defaultdict(list); credit_scripts = defaultdict(list); direct_quests = defaultdict(list)
    action_quest_fields = {'SMART_ACTION_FAIL_QUEST': 'action_param1', 'SMART_ACTION_OFFER_QUEST': 'action_param1',
        'SMART_ACTION_CALL_AREAEXPLOREDOREVENTHAPPENS': 'action_param1', 'SMART_ACTION_CALL_GROUPEVENTHAPPENS': 'action_param1',
        'SMART_ACTION_WP_START': 'action_param4', 'SMART_ACTION_WP_STOP': 'action_param2'}
    for row in tables['smart_scripts']:
        script_index[(row['source_type'], row['entryorguid'])].append(row)
        action = actions.get(row['action_type'], {}).get('name', 'UNKNOWN')
        event = events.get(row['event_type'], {}).get('name', 'UNKNOWN')
        if action == 'SMART_ACTION_CALL_KILLEDMONSTER':
            credit_scripts[row['action_param1']].append(row)
        field = action_quest_fields.get(action)
        if field and row.get(field):
            direct_quests[row[field]].append(row)
        if event in ('SMART_EVENT_ACCEPTED_QUEST', 'SMART_EVENT_REWARD_QUEST') and row['event_param1']:
            direct_quests[row['event_param1']].append(row)
    cpp = {}
    cpp_root = args.reference / 'tc-source/src/server/scripts'
    if cpp_root.exists():
        for path in cpp_root.rglob('*.cpp'):
            raw = path.read_bytes(); text = raw.decode('utf-8-sig', errors='replace')
            for name in set(re.findall(r'\b(?:CreatureScript|GameObjectScript|AreaTriggerScript)\s*\(\s*"([^"]+)"', text)
                            + re.findall(r'\bRegister(?:Creature|GameObject)AI\s*\(\s*([A-Za-z0-9_]+)\s*\)', text)):
                cpp.setdefault(name, []).append({'path': path.relative_to(args.reference / 'tc-source').as_posix(),
                    'revision': revision, 'sha256': hashlib.sha256(raw).hexdigest(),
                    'declaration_lines': [i for i, line in enumerate(text.splitlines(), 1) if name in line]})
    strategies = {}
    if args.strategy_results:
        strategies = {row['quest_id']: row for row in map(json.loads, args.strategy_results.read_text().splitlines())}
    source_ref = lambda table, key: f"tc335:{revision}:{source_sql}:{table}:{key}"

    def row_ref(table, row, key):
        return {'ref': source_ref(table, row[key]), 'source_line': row.get('__source_line'),
                'row_sha256': digest(row), 'key': {key: row[key]}}

    def script_ref(row):
        return {'identity': {key: row[key] for key in ['entryorguid', 'source_type', 'id', 'link']},
                'source': row_ref('smart_scripts', row, 'entryorguid'),
                'event': events.get(row['event_type'], {'name': 'UNKNOWN:' + str(row['event_type'])}),
                'action': actions.get(row['action_type'], {'name': 'UNKNOWN:' + str(row['action_type'])}),
                'row': row}

    def valid_points(kind, entry):
        return [point for point in effective.get(kind + 'Spawns', {}).get(str(entry), [])
                if point and point.get('Map', -1) >= 0 and all(isinstance(point.get(axis), (int, float))
                    and math.isfinite(point[axis]) for axis in ['X', 'Y', 'Z'])]

    output_rows = []; categories = defaultdict(list); transitions = []
    for ident in sorted(old):
        prior = old[ident]; quest = model[ident]; sim = simulations[ident]; template = qt.get(ident); addon = qa.get(ident, {})
        obligations = []; source_evidence = []; related_actors = []
        before_class = prior['classification']; strategy = strategies.get(ident)
        if template is None:
            obligations.append('source:primary-quest-template-absent')
        else:
            source_evidence.append(row_ref('quest_template', template, 'ID'))
            if addon:
                source_evidence.append(row_ref('quest_template_addon', addon, 'ID'))
            conflicts = [field for field in BASE_FIELDS if field in template and quest.get(field) != template[field]]
            conflicts += [field for field in ADDON_FIELDS if quest.get(field) != addon.get(field, 0)]
            if conflicts:
                obligations.append('source:primary-field-conflict:' + ','.join(sorted(conflicts)))
            for field in OPTIONAL_FIELDS:
                value = template.get(field, addon.get(field, 0))
                if value and quest.get(field) != value:
                    obligations.append('source:unrepresented-eligibility:' + field)
            if conditions[ident]:
                obligations.append('source:server-condition-not-modeled')
            if template.get('RewardMoney', 0) < 0:
                obligations.append('live:required-money-observation-and-reward-acceptance')
            if template.get('TimeAllowed', 0) > 0:
                obligations.append('live:timed-route-deadline-acceptance')
            if template.get('RequiredPlayerKills', 0) > 0:
                obligations.append('script:player-kill-objective-owner-unrepresented')
            special = addon.get('SpecialFlags', 0)
            if special & 0x20 and not strategy:
                obligations.append('script:cast-credit-no-strategy')
            if special & 0x02 and not strategy:
                obligations.append('script:exploration-or-event-no-strategy')
            normal = [(template[f'RequiredNpcOrGo{i}'], template[f'RequiredNpcOrGoCount{i}']) for i in range(1, 5) if template[f'RequiredNpcOrGo{i}']]
            required = {template[f'RequiredItemId{i}']: template[f'RequiredItemCount{i}'] for i in range(1, 7) if template[f'RequiredItemId{i}']}
            represented_normals = set(); represented_items = {}; objectives = quest['Objectives']
            for index, objective in enumerate(objectives):
                kind = objective['Type']
                if kind == 'TurnInOnly':
                    continue
                actor_kind = 'GameObject' if kind == 'CollectFromGameObject' else 'Creature'
                entry = objective['GameObjectId'] if actor_kind == 'GameObject' else objective['MobId']
                actor = actors[actor_kind].get(entry)
                associated = script_index[(1 if actor_kind == 'GameObject' else 0, entry)]
                related_actors.append({'type': actor_kind, 'entry': entry,
                    'template': row_ref(actor_kind.lower() + '_template', actor, 'entry') if actor else None,
                    'AIName': actor.get('AIName', '') if actor else '', 'ScriptName': actor.get('ScriptName', '') if actor else '',
                    'cpp_sources': cpp.get(actor.get('ScriptName', ''), []) if actor else [],
                    'smart_scripts': [script_ref(row) for row in associated],
                    'credit_scripts': [script_ref(row) for row in credit_scripts[entry]]})
                count = objective['KillCount'] if kind == 'KillMob' else objective['CollectCount']
                item = objective.get('ItemId', 0)
                if item > 0:
                    if required.get(item) != count:
                        obligations.append('data:auxiliary-or-mismatched-item-objective:' + str(index))
                    else:
                        represented_items[item] = count
                else:
                    pair = (-entry if actor_kind == 'GameObject' else entry, count)
                    if normal.count(pair) != 1:
                        obligations.append('data:normal-objective-identity-or-count-unrepresented:' + str(index))
                    else:
                        represented_normals.add(pair)
                if not valid_points(actor_kind, entry) and not has_collection_alternative_geometry(quest, objective, effective, required):
                    obligations.append('data:objective-geometry-missing:' + str(index))
            if quest.get('DeliveryItems'):
                represented_items.update({item['ItemId']: item['Count'] for item in quest['DeliveryItems']})
                supplied = {item['ItemId']: item['Count'] for item in quest.get('AcceptanceSupplies') or []}
                if any(supplied.get(item, 0) < count for item, count in required.items()):
                    obligations.append('data:delivery-acquisition-route-missing')
            supplemental = quest.get('SupplementalSupply')
            if supplemental:
                item = supplemental['ItemId']
                if (item == quest.get('StartItem') and required.get(item) == supplemental['RequiredCount']
                        and supplemental['ProvidedCount'] >= supplemental['RequiredCount']):
                    represented_items[item] = supplemental['RequiredCount']
                else:
                    obligations.append('data:supplemental-supply-contract-disagrees-with-primary')
            if any(pair not in represented_normals for pair in normal):
                obligations.append('data:primary-normal-requirement-unrepresented')
            if any(represented_items.get(item) != count for item, count in required.items()):
                obligations.append('data:primary-item-requirement-unrepresented')
            for role, collection, entry_name, type_name in [('giver', 'QuestGivers', 'GiverId', 'GiverType'), ('ender', 'QuestEnders', 'EnderId', 'EnderType')]:
                bound_relations = [relation for relation in effective[collection] if relation['QuestId'] == ident]
                if not bound_relations:
                    obligations.append('data:' + role + '-relation-unrepresented')
                elif not any(valid_points(relation.get(type_name, 'Creature'), relation[entry_name]) for relation in bound_relations):
                    obligations.append('data:' + role + '-geometry-missing')
            for parent in quest.get('PreviousQuestsIds') or []:
                if parent > 0 and parent not in model and parent not in dependencies:
                    obligations.append('source:external-predecessor-contract-missing:' + str(parent))
        obligations = sorted(set(obligations))
        strategy_status = 'MISSING'
        chosen_simulation = sim
        if strategy:
            strategy_status = 'VALIDATED-PIPELINE' if strategy.get('pipeline_status') == 'PASS' and strategy.get('failed_cases') == 0 else 'DECLARED'
            chosen_simulation = strategy
        classification = final_classification(obligations, chosen_simulation, strategy_status, before_class)
        protected = before_class in ('GENERIC-PROVEN', 'STRATEGY-PROVEN')
        # The baseline includes five separately proved observed-item routes that
        # intentionally lack ordinary static givers. Preserve their evidence;
        # this primary-static sweep is additional, not a replacement for those
        # actual observed-route tests. Any new contradiction is retained below.
        if protected:
            classification = before_class
        row = {'schema': 'primary-closure-quest-v1', 'quest_id': ident, 'name': quest['Name'],
            'classification': classification, 'before_classification': before_class,
            'baseline_record_sha256': digest(prior), 'baseline_ledger_sha256': input_hashes['baseline'],
            'baseline_evidence_commit': '12a6fb40c9adc932ea1c65dd4098355833d3fb90',
            'effective_model_sha256': input_hashes['effective'], 'model_record_sha256': digest(quest),
            'repair_sha256': sim.get('repair_sha256'), 'execution_fingerprint': sim.get('execution_fingerprint'),
            'primary_source_revision': revision, 'primary_database_sha256': source_sql,
            'primary_evidence': source_evidence, 'repair_disposition': repair_rows[ident],
            'primary_relations': primary_relations[ident], 'primary_conditions': conditions[ident],
            'primary_direct_quest_scripts': [script_ref(s) for s in direct_quests[ident]],
            'primary_objective_actors': related_actors, 'secondary_evidence_retained': prior['secondary_reference'],
            'strategy': {'status': strategy_status, 'evidence': strategy},
            'simulation': sim, 'remaining_obligations': [] if protected else obligations,
            'additional_primary_obligations_for_baseline_review': obligations if protected else [],
            'protected_baseline_retained': protected, 'live_completion_proven': False,
            'claim_limit': 'Recorded controlled production-owner behavior under source-bound reference observations; not realm travel/combat/credit or live completion.'}
        if not protected and classification not in ('GENERIC-PROVEN', 'STRATEGY-PROVEN') and not obligations:
            row['remaining_obligations'] = ['live:recorded-pipeline-remains-blocked']
        output_rows.append(row)
        if classification != before_class:
            transitions.append({'quest_id': ident, 'before': before_class, 'after': classification,
                                'repair_changes': repair_rows[ident]['changes']})
        for obligation in row['remaining_obligations']:
            categories[obligation.rsplit(':', 1)[0] if obligation.rsplit(':', 1)[-1].isdigit() else obligation].append(ident)
    args.output.mkdir(parents=True)
    ledger_bytes = b'\n'.join(_encoded(row) for row in output_rows) + b'\n'
    with (args.output / 'quest-ledger.jsonl.gz').open('xb') as output:
        with gzip.GzipFile(fileobj=output, mode='wb', mtime=0) as stream:
            stream.write(ledger_bytes)
    ids = {name: [row['quest_id'] for row in output_rows if row['classification'] == name] for name in CLASSIFICATIONS}
    summary = {'before': {name: sum(row['classification'] == name for row in old_rows) for name in CLASSIFICATIONS},
        'after': {name: len(values) for name, values in ids.items()}, 'quest_count': len(output_rows),
        'classification_changes': len(transitions), 'new_proven_quests': sum(t['after'] in ('GENERIC-PROVEN', 'STRATEGY-PROVEN') for t in transitions),
        'simulation_checks': sum(row['passed_cases'] for row in simulations.values()), 'simulation_failures': 0,
        'primary_source_revision': revision, 'primary_database_sha256': source_sql,
        'ledger_uncompressed_sha256': hashlib.sha256(ledger_bytes).hexdigest(), 'baseline_ledger_sha256': input_hashes['baseline'],
        'simulation_sha256': input_hashes['simulation'], 'effective_model_sha256': input_hashes['effective'],
        'dependency_metadata_sha256': input_hashes['dependencies'], 'repair_evidence_sha256': input_hashes['repair_evidence'],
        'tool_sha256': _sha(Path(__file__)), 'no_live_completion_claim': True}
    for name, content in [('summary.json', summary), ('classification-ids.json', ids), ('classification-transitions.json', transitions),
                           ('remaining-category-ids.json', category_index(output_rows)), ('primary-table-receipts.json', table_receipts)]:
        (args.output / name).write_text(json.dumps(content, indent=2) + '\n', encoding='utf-8')
    print(json.dumps(summary, indent=2))


if __name__ == '__main__':
    main()
