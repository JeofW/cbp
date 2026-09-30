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


def unique_quest_index(rows, key='quest_id', label='quest evidence'):
    """Reject ambiguity before a dictionary could silently discard evidence."""
    result = {}
    for row in rows:
        ident = row.get(key)
        if type(ident) is not int or ident <= 0:
            raise ValueError(f'{label}: {key} must be a positive integer: {ident!r}')
        if ident in result:
            raise ValueError(f'{label}: duplicate quest ID {ident}')
        result[ident] = row
    return result


def validate_closure_inputs(baseline_rows, model_rows, simulation_rows, expected_count=4335):
    baseline = unique_quest_index(baseline_rows, label='baseline')
    model = unique_quest_index(model_rows, key='Id', label='effective model')
    simulations = unique_quest_index(simulation_rows, label='simulation')
    if len(model) != expected_count:
        raise ValueError(f'Effective model count must be {expected_count}, found {len(model)}')
    for label, index in [('baseline', baseline), ('simulation', simulations)]:
        if set(index) != set(model):
            raise ValueError(f'{label} membership differs: missing={sorted(set(model) - set(index))}, '
                             f'phantom={sorted(set(index) - set(model))}')
    return baseline, model, simulations


def classification_partition(rows, expected_ids, expected_count=4335):
    index = unique_quest_index(rows, label='classification ledger')
    expected = list(expected_ids)
    if (len(expected) != expected_count or len(set(expected)) != expected_count
            or any(type(ident) is not int or ident <= 0 for ident in expected)):
        raise ValueError(f'Expected dataset must contain {expected_count} unique positive integer IDs')
    if set(index) != set(expected):
        raise ValueError(f'Classification membership differs: missing={sorted(set(expected) - set(index))}, '
                         f'phantom={sorted(set(index) - set(expected))}')
    result = {name: [] for name in CLASSIFICATIONS}
    for ident, row in sorted(index.items()):
        category = row.get('classification')
        if category not in result:
            raise ValueError(f'Unknown classification for quest {ident}: {category!r}')
        result[category].append(ident)
    if sum(map(len, result.values())) != expected_count:
        raise ValueError('Classification partition count differs from the original dataset')
    return result


def validate_strategy_results(rows, simulations, strategy_sha256):
    strategies = unique_quest_index(rows, label='strategy receipt')
    for ident, receipt in strategies.items():
        if ident not in simulations:
            raise ValueError(f'Strategy receipt names unknown quest {ident}')
        for field in ('dataset_sha256', 'repair_sha256', 'execution_fingerprint'):
            expected = simulations[ident].get(field)
            if not expected or receipt.get(field) != expected:
                raise ValueError(f'Strategy receipt {ident} has mismatched {field}')
        if not strategy_sha256 or receipt.get('strategy_sha256') != strategy_sha256:
            raise ValueError(f'Strategy receipt {ident} has mismatched strategy_sha256')
    return strategies


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
    if strategy_status not in ('MISSING', 'VALIDATED-PIPELINE'):
        return 'UNSUPPORTED-SCRIPTED'
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


def retained_secondary_evidence(previous):
    names = [name for name in ('secondary_reference', 'secondary_evidence_retained') if name in previous]
    if not names or any(previous[name] != previous[names[0]] for name in names):
        raise ValueError('A prior ledger must retain one unambiguous secondary evidence record')
    return copy.deepcopy(previous[names[0]])


def retained_item_route_matches(route, quest_id, primary, base_sha):
    if (not route or route.get('route_type') != 'item-starter' or route.get('quest_id') != quest_id
            or route.get('dataset_sha256') != base_sha or route.get('failed_cases') != 0
            or route.get('controlled_pipeline_passed') is not True or not primary):
        return False
    observation = route.get('source_observations', {}).get('observation', {})
    normal = [primary[f'RequiredNpcOrGo{i}'] for i in range(1, 5)]
    normal = [value if value >= 0 else -(2**31) | abs(value) for value in normal]
    return (observation.get('normal_ids') == normal
            and observation.get('normal_counts') == [primary[f'RequiredNpcOrGoCount{i}'] for i in range(1, 5)]
            and observation.get('item_ids') == [primary[f'RequiredItemId{i}'] for i in range(1, 7)]
            and observation.get('item_counts') == [primary[f'RequiredItemCount{i}'] for i in range(1, 7)])


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
    parser.add_argument('--strategy-pack', type=Path)
    parser.add_argument('--retained-routes', type=Path)
    parser.add_argument('--baseline-evidence-commit', required=True,
                        help='Exact full Git commit containing the retained baseline ledger')
    args = parser.parse_args()
    if not re.fullmatch(r'[0-9a-f]{40}', args.baseline_evidence_commit):
        parser.error('--baseline-evidence-commit must be the full 40-character Git identity')
    if bool(args.strategy_results) != bool(args.strategy_pack):
        parser.error('--strategy-results and --strategy-pack must identify the same validated run')
    if args.output.exists():
        raise FileExistsError('Closure ledgers are create-only')
    from quest_repair_pack_335 import load_verified_tables, digest, BASE_FIELDS, ADDON_FIELDS, OPTIONAL_FIELDS
    from quest_dependency_source_335 import primary_dependency_index, dependency_membership
    from quest_collection_source_335 import CollectionSourceIndex
    tables, table_receipts = load_verified_tables(args.reference)
    old_rows = [json.loads(line) for line in gzip.decompress(args.baseline.read_bytes()).splitlines()]
    simulation_rows = list(map(json.loads, args.simulation.read_text(encoding='utf-8').splitlines()))
    effective = json.loads(args.effective.read_text(encoding='utf-8'))
    old, model, simulations = validate_closure_inputs(old_rows, effective['Quests'], simulation_rows)
    baseline_ids = classification_partition(old_rows, model)
    dependencies = {int(key): value for key, value in json.loads(args.dependencies.read_text()).items()}
    repair = json.loads(gzip.decompress(args.repair_evidence.read_bytes()))
    repair_rows = unique_quest_index(repair['quests'], label='repair evidence')
    if set(repair_rows) != set(model):
        raise ValueError('Repair evidence membership differs from the original dataset')
    if any(row['failed_cases'] for row in simulations.values()):
        raise ValueError('A failing dataset sweep cannot become a closure ledger')
    source = json.loads((args.reference / 'source-receipt.json').read_text())
    source_sql = source['members'][0]['sha256']
    revision = source['release_commit']
    input_hashes = {name: _sha(getattr(args, name)) for name in
                    ['baseline', 'simulation', 'effective', 'dependencies', 'repair_evidence']}
    input_hashes.update({name: _sha(getattr(args, name)) for name in
                        ['strategy_results', 'strategy_pack', 'retained_routes'] if getattr(args, name)})
    qt = {row['ID']: row for row in tables['quest_template']}
    qa = {row['ID']: row for row in tables['quest_template_addon']}
    primary_dependencies, dependency_edges = primary_dependency_index(qt, qa)
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
    collection_sources = CollectionSourceIndex(tables, condition_codes)
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
        strategies = validate_strategy_results(map(json.loads, args.strategy_results.read_text(encoding='utf-8').splitlines()),
                                               simulations, input_hashes['strategy_pack'])
    retained_routes = {}
    if args.retained_routes:
        retained_routes = unique_quest_index((row for row in map(json.loads,
            args.retained_routes.read_text(encoding='utf-8').splitlines()) if row.get('route_type') == 'item-starter'),
            label='retained item route')
        if not set(retained_routes).issubset(model):
            raise ValueError('Retained item routes contain unknown quest IDs')
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

    output_rows = []; transitions = []
    for ident in sorted(old):
        prior = old[ident]; quest = model[ident]; sim = simulations[ident]; template = qt.get(ident); addon = qa.get(ident, {})
        obligations = []; source_evidence = []; related_actors = []; acquisition = None
        dependency_review = dependency_membership(quest, primary_dependencies, dependency_edges)
        before_class = prior['classification']; strategy = strategies.get(ident)
        strategy_validated = bool(strategy and strategy.get('pipeline_status') == 'PASS' and strategy.get('failed_cases') == 0)
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
            if not dependency_review['matches']:
                obligations.append('source:dependent-previous-membership-mismatch')
            for edge in dependency_review['edges']:
                reference = row_ref('quest_template_addon', qa[edge['table_row']], 'ID')
                if reference not in source_evidence:
                    source_evidence.append(reference)
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
            if special & 0x20 and not strategy_validated:
                obligations.append('script:cast-credit-no-strategy')
            if special & 0x02 and not strategy_validated:
                obligations.append('script:exploration-or-event-no-strategy')
            normal = [(template[f'RequiredNpcOrGo{i}'], template[f'RequiredNpcOrGoCount{i}']) for i in range(1, 5) if template[f'RequiredNpcOrGo{i}']]
            required = {template[f'RequiredItemId{i}']: template[f'RequiredItemCount{i}'] for i in range(1, 7) if template[f'RequiredItemId{i}']}
            acquisition = collection_sources.review(quest, effective, required)
            obligations.extend(acquisition['obligations'])
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
        retained_route = retained_routes.get(ident)
        retained_matches = retained_item_route_matches(retained_route, ident, template, sim['dataset_sha256'])
        if retained_matches:
            # Preserve the separately proved carried-item pickup path, not a
            # blanket grandfathering of old generic labels. New primary item,
            # normal, eligibility or script conflicts remain real obligations.
            obligations = [value for value in obligations if value not in
                           ('data:giver-relation-unrepresented', 'data:giver-geometry-missing')]
        obligations = sorted(set(obligations))
        strategy_status = 'MISSING'
        chosen_simulation = sim
        if strategy:
            strategy_status = 'VALIDATED-PIPELINE' if strategy_validated else 'DECLARED'
            chosen_simulation = strategy
        elif retained_matches:
            chosen_simulation = {'pipeline_status': 'PASS', 'failed_cases': 0,
                'evidence': 'retained actual carried-item route with primary requirements matched', 'route_result': retained_route}
        classification = final_classification(obligations, chosen_simulation, strategy_status, before_class)
        protected = before_class in ('GENERIC-PROVEN', 'STRATEGY-PROVEN')
        row = {'schema': 'primary-closure-quest-v1', 'quest_id': ident, 'name': quest['Name'],
            'classification': classification, 'before_classification': before_class,
            'baseline_record_sha256': digest(prior), 'baseline_ledger_sha256': input_hashes['baseline'],
            'baseline_evidence_commit': args.baseline_evidence_commit,
            'effective_model_sha256': input_hashes['effective'], 'model_record_sha256': digest(quest),
            'repair_sha256': sim.get('repair_sha256'), 'execution_fingerprint': sim.get('execution_fingerprint'),
            'primary_source_revision': revision, 'primary_database_sha256': source_sql,
            'primary_evidence': source_evidence, 'repair_disposition': repair_rows[ident],
            'primary_relations': primary_relations[ident], 'primary_conditions': conditions[ident],
            'primary_direct_quest_scripts': [script_ref(s) for s in direct_quests[ident]],
            'primary_objective_actors': related_actors, 'secondary_evidence_retained': retained_secondary_evidence(prior),
            'primary_collection_sources': acquisition,
            'primary_dependency_membership': dependency_review,
            'strategy': {'status': strategy_status, 'evidence': strategy},
            'simulation': sim, 'final_simulation_disposition': chosen_simulation,
            'retained_item_route_primary_matched': retained_matches,
            'remaining_obligations': obligations,
            'additional_primary_obligations_for_baseline_review': obligations if protected else [],
            'protected_baseline_retained': protected and classification == before_class,
            'baseline_has_new_contradicting_evidence': protected and classification != before_class, 'live_completion_proven': False,
            'claim_limit': 'Recorded controlled production-owner behavior under source-bound reference observations; not realm travel/combat/credit or live completion.'}
        if classification not in ('GENERIC-PROVEN', 'STRATEGY-PROVEN') and not obligations:
            row['remaining_obligations'] = ['script:strategy-pipeline-not-validated' if classification == 'UNSUPPORTED-SCRIPTED'
                                            else 'live:recorded-pipeline-remains-blocked']
        output_rows.append(row)
        if classification != before_class:
            transitions.append({'quest_id': ident, 'before': before_class, 'after': classification,
                                'repair_changes': repair_rows[ident]['changes']})
    ids = classification_partition(output_rows, model)
    unique_quest_index(transitions, label='classification transition')
    remaining_ids = sorted(row['quest_id'] for row in output_rows
                           if row['classification'] not in ('GENERIC-PROVEN', 'STRATEGY-PROVEN'))
    coverage = {'expected_quests': 4335, 'unique_quest_ids': len(output_rows),
                'classification_total': sum(map(len, ids.values())), 'missing_ids': [], 'phantom_ids': [],
                'duplicate_ids': [], 'remaining_quest_ids': remaining_ids, 'remaining_quests': len(remaining_ids),
                'secondary_obligations_are_separate': True, 'input_sha256': input_hashes}
    args.output.mkdir(parents=True)
    ledger_bytes = b'\n'.join(_encoded(row) for row in output_rows) + b'\n'
    with (args.output / 'quest-ledger.jsonl.gz').open('xb') as output:
        with gzip.GzipFile(fileobj=output, mode='wb', mtime=0) as stream:
            stream.write(ledger_bytes)
    summary = {'before': {name: len(values) for name, values in baseline_ids.items()},
        'after': {name: len(values) for name, values in ids.items()}, 'quest_count': len(output_rows),
        'classification_changes': len(transitions), 'new_proven_quests': sum(t['after'] in ('GENERIC-PROVEN', 'STRATEGY-PROVEN') for t in transitions),
        'previously_proven_reclassified_on_new_primary_evidence': sum(t['before'] in ('GENERIC-PROVEN', 'STRATEGY-PROVEN') and t['after'] not in ('GENERIC-PROVEN', 'STRATEGY-PROVEN') for t in transitions),
        'simulation_checks': sum(row['passed_cases'] for row in simulations.values()), 'simulation_failures': 0,
        'primary_source_revision': revision, 'primary_database_sha256': source_sql,
        'ledger_uncompressed_sha256': hashlib.sha256(ledger_bytes).hexdigest(), 'baseline_ledger_sha256': input_hashes['baseline'],
        'simulation_sha256': input_hashes['simulation'], 'effective_model_sha256': input_hashes['effective'],
        'dependency_metadata_sha256': input_hashes['dependencies'], 'repair_evidence_sha256': input_hashes['repair_evidence'],
        'tool_sha256': _sha(Path(__file__)), 'input_sha256': input_hashes, 'no_live_completion_claim': True}
    for name, content in [('summary.json', summary), ('classification-ids.json', ids), ('classification-transitions.json', transitions),
                           ('remaining-category-ids.json', category_index(output_rows)), ('primary-table-receipts.json', table_receipts),
                           ('coverage.json', coverage)]:
        (args.output / name).write_text(json.dumps(content, indent=2) + '\n', encoding='utf-8')
    print(json.dumps(summary, indent=2))


if __name__ == '__main__':
    main()
