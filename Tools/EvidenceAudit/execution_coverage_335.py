"""Separate original-era structural/observation evidence from execution proof.

The legacy six-class ledger remains an immutable observation ledger. A new
execution grade requires exact primitive membership, matching semantics, actual
dispatch/acknowledgement evidence and the full lifecycle, including turn-in.
The primary-table inventory does not invent an unseen runtime plan or script.
"""
from __future__ import annotations
import argparse
from collections import Counter, defaultdict
import gzip
import hashlib
import json
from pathlib import Path
import re

REQUIRED_STAGES = ('admission', 'plan', 'travel', 'live-acquisition', 'safe-approach',
                   'action-dispatch', 'action-acknowledgement', 'authoritative-progress',
                   'partial-progress-and-reacquisition', 'bounded-recovery', 'objective-completion',
                   'ender-travel', 'turnin-dispatch', 'turnin-acknowledgement', 'next-scheduling')
PRIMITIVES = ('creature-kill', 'player-kill', 'creature-loot-item', 'ground-gameobject-loot',
              'direct-gameobject-interaction', 'npc-interaction', 'ordinary-delivery', 'quest-turnin',
              'inventory-start-item', 'acceptance-supplied-item', 'item-acquisition-unresolved',
              'item-container-loot', 'vendor-item-acquisition', 'spell-cast-credit',
              'use-item-self', 'use-item-unit', 'use-item-gameobject', 'use-item-location',
              'use-item-target-unresolved', 'gossip-dialogue', 'escort-follow', 'explore-area-trigger',
              'exploration-or-scripted-event', 'scripted-behaviour-unresolved', 'vehicle', 'transport',
              'reputation-objective', 'required-money', 'timed-deadline', 'unrepresented-primary-record')
LEGACY = ('GENERIC-PROVEN', 'STRATEGY-PROVEN', 'DATA-INVALID/INCOMPLETE',
          'UNSUPPORTED-SCRIPTED', 'SOURCE-UNCERTAIN', 'LIVE-ACCEPTANCE-REQUIRED')
HASH = re.compile(r'[0-9a-f]{64}\Z')


def population(groups: dict) -> dict[int, str]:
    result = {}
    for classification, ids in groups.items():
        if classification not in LEGACY or not isinstance(ids, list):
            raise ValueError('Unknown legacy classification or invalid ID population')
        for ident in ids:
            if type(ident) is not int or ident <= 0 or ident in result:
                raise ValueError('Quest IDs must be unique positive integers across all classes')
            result[ident] = classification
    return result


def requirement_key(value: dict) -> tuple:
    if (type(value.get('quest_id')) is not int or value['quest_id'] <= 0
            or value.get('primitive') not in PRIMITIVES
            or not isinstance(value.get('objective_key'), str) or not value['objective_key']
            or not HASH.fullmatch(value.get('semantics_sha256', ''))):
        raise ValueError('Invalid exact primitive membership')
    return value['quest_id'], value['primitive'], value['objective_key'], value['semantics_sha256']


def validate_contract(contract: dict) -> set[tuple]:
    if (not isinstance(contract.get('contract_id'), str) or not contract['contract_id']
            or contract.get('scope') not in ('controlled-production-owner-lifecycle', 'live-original-client-lifecycle')
            or contract.get('primitive') not in PRIMITIVES
            or contract.get('navigation_only') is not False
            or contract.get('manually_injected_progress_without_action') is not False
            or any(contract.get(key) is not True for key in ('progress_after_ack', 'authoritative_completion', 'authoritative_turnin'))):
        raise ValueError('Not a complete executable lifecycle contract')
    stages = contract.get('stages', [])
    if not isinstance(stages, list) or len(stages) != len(set(stages)) or not set(REQUIRED_STAGES).issubset(stages):
        raise ValueError('Every required lifecycle stage must have an execution receipt')
    source = contract.get('source_sha256')
    if (not isinstance(source, dict) or not source or any(not isinstance(name, str) or not name
            or not isinstance(digest, str) or not HASH.fullmatch(digest) for name, digest in source.items())
            or not HASH.fullmatch(contract.get('trace_sha256', ''))):
        raise ValueError('Actual source and trace hashes are required')
    cases = contract.get('case_ids')
    if not isinstance(cases, list) or not cases or any(not isinstance(name, str) or not name for name in cases) or len(cases) != len(set(cases)):
        raise ValueError('Named executed cases are required, not just a count')
    dispatch, ack = contract.get('dispatch_count'), contract.get('acknowledgement_count')
    if type(dispatch) is not int or type(ack) is not int or dispatch <= 0 or ack <= 0 or ack > dispatch:
        raise ValueError('Acknowledgements require corresponding actual action submissions')
    members = contract.get('members')
    if not isinstance(members, list) or not members:
        raise ValueError('A family must identify every exact quest/objective semantic it covers')
    keys = {requirement_key(member) for member in members}
    if len(keys) != len(members) or any(key[1] != contract['primitive'] for key in keys):
        raise ValueError('Duplicate or wrong-family membership')
    return keys


def grade(legacy: str, requirements: list[dict], contracts: list[dict]) -> dict:
    """Grade structural prerequisites of already artifact-verified contracts.

    Installation through the CLI always calls verify_artifacts first. This pure
    predicate alone is deliberately not a trace, source, or live acceptance gate.
    """
    if legacy not in LEGACY:
        raise ValueError('Unknown legacy observation class')
    covered = set()
    for contract in contracts:
        covered.update(validate_contract(contract))
    required = {requirement_key(row) for row in requirements}
    if len(required) != len(requirements):
        raise ValueError('Duplicate objective semantic requirements')
    missing = sorted(required - covered)
    result = legacy
    if legacy in ('GENERIC-PROVEN', 'STRATEGY-PROVEN') and (not required or missing):
        result = 'EXECUTION-UNVERIFIED'
    return {'classification': result, 'legacy_observation_classification': legacy,
            'required_primitive_count': len(required), 'covered_primitive_count': len(required & covered),
            'missing_primitive_keys': [list(key) for key in missing],
            'live_completion_proven': bool(required) and not missing and result in ('GENERIC-PROVEN', 'STRATEGY-PROVEN')
                and all(contract['scope'] == 'live-original-client-lifecycle' for contract in contracts)}


def verify_artifacts(contract: dict, root: Path) -> dict:
    """Require actual bound source/trace bytes and a causal lifecycle per member.

    These are controlled execution receipts unless an independently reviewed
    original-client run was supplied. Hash agreement is not realm certification.
    """
    members = validate_contract(contract)
    root = root.resolve()
    def content(name, digest):
        if not isinstance(name, str) or not name or Path(name).is_absolute() or '..' in Path(name).parts:
            raise ValueError('Artifact paths must remain inside the explicit evidence root')
        path = (root / name).resolve()
        if not path.is_relative_to(root) or not path.is_file() or path.is_symlink():
            raise ValueError('Missing or unsafe source/trace artifact')
        raw = path.read_bytes()
        if hashlib.sha256(raw).hexdigest() != digest: raise ValueError('Source/trace artifact changed')
        return raw
    for name, digest in contract['source_sha256'].items(): content(name, digest)
    raw = content(contract.get('trace_file'), contract['trace_sha256'])
    try: trace = json.loads(raw)
    except (ValueError, UnicodeDecodeError) as error: raise ValueError('Invalid execution trace') from error
    if not isinstance(trace, list) or not trace: raise ValueError('A navigation summary is not an execution trace')
    # A member cannot borrow an action, counter or missing stage from another
    # case. Global event ordering still permits independently interleaved cases.
    stages = defaultdict(set)
    latest = defaultdict(dict)
    last_counts = {}
    seen_cases = set()
    dispatched = {}
    acknowledged = set()
    previous_sequence = -1
    prerequisites = {
        'plan': 'admission', 'travel': 'plan', 'live-acquisition': 'travel',
        'safe-approach': 'live-acquisition', 'action-dispatch': 'safe-approach',
        'action-acknowledgement': 'action-dispatch', 'authoritative-progress': 'action-acknowledgement',
        'partial-progress-and-reacquisition': 'authoritative-progress',
        # A negative case may exercise bounded recovery before any progress.
        'bounded-recovery': 'plan', 'objective-completion': 'authoritative-progress',
        'ender-travel': 'objective-completion', 'turnin-dispatch': 'ender-travel',
        'turnin-acknowledgement': 'turnin-dispatch', 'next-scheduling': 'turnin-acknowledgement'}
    for event in trace:
        if not isinstance(event, dict): raise ValueError('Invalid trace event')
        key = requirement_key(event)
        case = event.get('case_id')
        if key not in members or not isinstance(case, str) or case not in contract['case_ids']:
            raise ValueError('Trace event does not belong to the exact member and executed case')
        pair = (key, case)
        sequence = event.get('sequence'); stage = event.get('stage')
        if type(sequence) is not int or sequence <= previous_sequence or stage not in REQUIRED_STAGES:
            raise ValueError('Trace sequence/stage is ambiguous')
        prior = prerequisites.get(stage)
        if prior is not None and prior not in stages[pair]:
            raise ValueError('Lifecycle stage lacks its preceding stage in the same member and case')
        previous_sequence = sequence
        seen_cases.add(case)
        action = event.get('action_id')
        if stage in ('action-dispatch', 'turnin-dispatch'):
            if not isinstance(action, str) or not action or action in dispatched:
                raise ValueError('Action identities must be unique actual dispatch receipts')
            if stage == 'action-dispatch':
                acquired = latest[pair].get('live-acquisition', -1)
                approached = latest[pair].get('safe-approach', -1)
                previous_action = latest[pair].get('action-dispatch', -1)
                if not previous_action < acquired < approached < sequence:
                    raise ValueError('Each collection action needs fresh live acquisition and safe approach')
            dispatched[action] = (pair, stage)
        elif stage in ('action-acknowledgement', 'turnin-acknowledgement'):
            expected = 'turnin-dispatch' if stage == 'turnin-acknowledgement' else 'action-dispatch'
            if not isinstance(action, str) or action in acknowledged or dispatched.get(action) != (pair, expected):
                raise ValueError('Acknowledgement lacks its own matching prior dispatch')
            if event.get('observation_authoritative') is not True:
                raise ValueError('Void submission or local arrival is not acknowledgement')
            acknowledged.add(action)
        elif stage == 'authoritative-progress':
            if (not isinstance(action, str) or action not in acknowledged
                    or dispatched.get(action) != (pair, 'action-dispatch')
                    or event.get('observation_authoritative') is not True):
                raise ValueError('Progress is not causally bound to an acknowledged action')
            before, after = event.get('before'), event.get('after')
            if type(before) is not int or type(after) is not int or not 0 <= before < after:
                raise ValueError('Injected or non-incrementing counter is not progress')
            if pair in last_counts and before != last_counts[pair]:
                raise ValueError('Repeated or disconnected counter transitions cannot add progress')
            last_counts[pair] = after
        elif stage == 'objective-completion' and event.get('observation_authoritative') is not True:
            raise ValueError('Objective completion is not authoritative')
        stages[pair].add(stage)
        latest[pair][stage] = sequence
    if seen_cases != set(contract['case_ids']):
        raise ValueError('A declared test case has no executed trace events')
    complete = {pair for pair, observed in stages.items() if set(REQUIRED_STAGES).issubset(observed)}
    if {key for key, case in complete} != members:
        raise ValueError('Each population member needs one complete lifecycle within a single case')
    if len(dispatched) != contract['dispatch_count'] or len(acknowledged) != contract['acknowledgement_count']:
        raise ValueError('Declared action counts differ from the real trace events')
    return {'verified_source_files': len(contract['source_sha256']), 'trace_events': len(trace),
            'dispatches': len(dispatched), 'acknowledgements': len(acknowledged),
            'trace_sha256': contract['trace_sha256'], 'exact_members': len(members),
            'observed_cases': len(seen_cases), 'complete_member_case_lifecycles': len(complete)}


def semantics(ident: int, primitive: str, key: str, fields: dict) -> dict:
    raw = json.dumps(fields, sort_keys=True, separators=(',', ':')).encode()
    return {'quest_id': ident, 'primitive': primitive, 'objective_key': key,
            'semantics_sha256': hashlib.sha256(raw).hexdigest(), 'primary_semantics': fields}


def inventory(tables: dict[str, list[dict]], ids: dict[int, str], contracts: list[dict]) -> dict:
    """Inventory source-required primitives and separately labelled candidate paths.

    Candidate item-source alternatives are OR alternatives, not an assertion that
    a quest must execute every source. Scripted input alone never supplies a recipe.
    """
    qt = {row['ID']: row for row in tables['quest_template']}
    qa = {row['ID']: row for row in tables['quest_template_addon']}
    ct = {row['entry']: row for row in tables['creature_template']}
    gt = {row['entry']: row for row in tables['gameobject_template']}
    items = {row['entry']: row for row in tables['item_template']}
    creature_loot = defaultdict(set); object_loot = defaultdict(set)
    for row in ct.values():
        if row.get('lootid', 0): creature_loot[row['lootid']].add(row['entry'])
    for row in gt.values():
        if row['type'] == 3 and row.get('Data1', 0): object_loot[row['Data1']].add(row['entry'])
    sources = defaultdict(lambda: defaultdict(set))
    for table, family, actors in (('creature_loot_template', 'creature-loot-item', creature_loot),
                                   ('gameobject_loot_template', 'ground-gameobject-loot', object_loot)):
        for row in tables[table]:
            if row['Item'] > 0 and row.get('Reference', 0) == 0:
                sources[row['Item']][family].update(actors[row['Entry']])
    for row in tables['item_loot_template']:
        if row['Item'] > 0 and row.get('Reference', 0) == 0:
            sources[row['Item']]['item-container-loot'].add(row['Entry'])
    for row in tables.get('npc_vendor', []):
        if row['item'] > 0: sources[row['item']]['vendor-item-acquisition'].add(row['entry'])
    starts = defaultdict(list)
    for row in items.values():
        if row.get('startquest', 0): starts[row['startquest']].append(row['entry'])
    ends = defaultdict(list)
    for table, kind in (('creature_questender', 'Creature'), ('gameobject_questender', 'GameObject')):
        for row in tables[table]: ends[row['quest']].append({'type': kind, 'entry': row['id']})
    area = defaultdict(list)
    for row in tables.get('areatrigger_involvedrelation', []): area[row['quest']].append(row['id'])
    rows = []; families = {family: {'required_ids': set(), 'candidate_source_ids': set(), 'tested_exact_ids': set()} for family in PRIMITIVES}
    validated = set().union(*(validate_contract(contract) for contract in contracts)) if contracts else set()
    for ident, old in sorted(ids.items()):
        quest = qt.get(ident); addon = qa.get(ident, {}); obligations = []; alternatives = []; notes = []
        def add(family, key, values):
            requirement = semantics(ident, family, key, values)
            obligations.append(requirement); families[family]['required_ids'].add(ident)
            if requirement_key(requirement) in validated: families[family]['tested_exact_ids'].add(ident)
        if quest is None:
            add('unrepresented-primary-record', 'source', {'primary_record_missing': True})
        else:
            special = addon.get('SpecialFlags', 0)
            for slot in range(1, 5):
                entry = quest[f'RequiredNpcOrGo{slot}']; amount = quest[f'RequiredNpcOrGoCount{slot}']
                if not entry: continue
                family = 'spell-cast-credit' if special & 0x20 else 'direct-gameobject-interaction' if entry < 0 else 'creature-kill'
                add(family, f'normal:{slot - 1}', {'typed_entry': entry, 'required_count': amount, 'special_flags': special})
                actor = gt.get(-entry) if entry < 0 else ct.get(entry)
                if actor and actor.get('ScriptName'):
                    notes.append({'kind': 'associated-script-not-a-recipe', 'entry': entry, 'script': actor['ScriptName']})
                if entry > 0 and actor and actor.get('VehicleId'):
                    notes.append({'kind': 'vehicle-creature-not-proof-player-must-ride', 'entry': entry, 'vehicle_id': actor['VehicleId']})
            for slot in range(1, 7):
                item = quest[f'RequiredItemId{slot}']; amount = quest[f'RequiredItemCount{slot}']
                if not item: continue
                possibilities = [{'primitive': family, 'source_entries': sorted(entries)} for family, entries in sorted(sources[item].items()) if entries]
                for possibility in possibilities: families[possibility['primitive']]['candidate_source_ids'].add(ident)
                alternatives.append({'objective_key': f'item:{slot - 1}', 'item_id': item, 'required_count': amount,
                                     'source_options': possibilities, 'alternatives_are_or': True,
                                     'selected_runtime_acquisition_path_verified': False})
                add('item-acquisition-unresolved', f'item:{slot - 1}', {'item_id': item, 'required_count': amount,
                    'candidate_sources': possibilities, 'source_options_do_not_prove_runtime_dispatch': True})
                if not possibilities: notes.append({'kind': 'item-source-or-acquisition-unresolved', 'item_id': item})
            if quest.get('StartItem', 0):
                supplied = quest['StartItem']
                add('acceptance-supplied-item', 'supplied', {'item_id': supplied, 'provided_count': addon.get('ProvidedItemCount', 0)})
                if any(items.get(supplied, {}).get('spellid_' + str(index), 0) > 0 for index in range(1, 6)):
                    notes.append({'kind': 'supplied-item-spell-is-not-a-targeted-quest-recipe', 'item_id': supplied})
            for item in sorted(starts[ident]): add('inventory-start-item', 'start:' + str(item), {'item_id': item, 'source': 'item_template.startquest'})
            if area[ident]: add('explore-area-trigger', 'area-trigger', {'ids': sorted(area[ident]), 'special_flags': special})
            elif special & 2: add('exploration-or-scripted-event', 'scripted-credit', {'special_flags': special})
            if quest.get('RequiredPlayerKills', 0): add('player-kill', 'player-kill', {'count': quest['RequiredPlayerKills']})
            if quest.get('RewardMoney', 0) < 0: add('required-money', 'money', {'money': -quest['RewardMoney']})
            for index in (1, 2):
                if quest.get('RequiredFactionValue' + str(index), 0):
                    add('reputation-objective', 'reputation:' + str(index), {'faction': quest.get('RequiredFactionId' + str(index)), 'value': quest['RequiredFactionValue' + str(index)]})
            if quest.get('TimeAllowed', 0): add('timed-deadline', 'time', {'seconds': quest['TimeAllowed']})
            if not obligations: add('ordinary-delivery', 'delivery', {'quest_type': quest.get('QuestType'), 'special_flags': special})
        add('quest-turnin', 'turnin', {'enders': sorted(ends[ident], key=lambda row: (row['type'], row['entry'])),
                                    'authoritative_reward_acknowledgement_required': True})
        result = grade(old, obligations, contracts)
        rows.append({'quest_id': ident, 'primary_name': quest.get('LogTitle') if quest else None,
                     **result, 'required_primitives': obligations, 'item_source_alternatives': alternatives,
                     'source_notes': notes, 'all_required_stages': REQUIRED_STAGES,
                     'runtime_plan_and_native_execution_not_inferred': True})
    formatted = {name: {key: sorted(values) for key, values in value.items()} for name, value in families.items()}
    return {'rows': rows, 'families': formatted, 'classification_counts': dict(Counter(row['classification'] for row in rows)),
            'legacy_counts': dict(Counter(ids.values())), 'quest_count': len(rows),
            'semantic_family_mapping_limit': 'Required source records plus explicitly labelled possible acquisition alternatives. Unseen runtime selections, gossip/escort/item target/vehicle/transport scripts remain unresolved, never guessed.',
            'unmapped_script_dimensions': ['use-item-self', 'use-item-unit', 'use-item-gameobject', 'use-item-location', 'gossip-dialogue', 'escort-follow', 'vehicle', 'transport'],
            'execution_proven_ids': [row['quest_id'] for row in rows if row['classification'] in ('GENERIC-PROVEN', 'STRATEGY-PROVEN')]}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--primary', type=Path, required=True)
    parser.add_argument('--population', type=Path, required=True)
    parser.add_argument('--output', type=Path, required=True)
    parser.add_argument('--contracts', type=Path)
    parser.add_argument('--artifact-root', type=Path)
    args = parser.parse_args()
    if args.output.exists(): raise FileExistsError('Preserve an existing coverage run; do not overwrite evidence')
    index = {row['table']: row for row in json.loads((args.primary / 'parsed-summary.json').read_bytes())}
    source = json.loads((args.primary / 'source-receipt.json').read_bytes())
    if source['release_commit'] != '95657f54779467effea8a1749a61ff93abc1d707': raise ValueError('Unreviewed primary source revision')
    names = ('quest_template', 'quest_template_addon', 'creature_template', 'gameobject_template',
             'creature_loot_template', 'gameobject_loot_template', 'item_template', 'item_loot_template',
             'npc_vendor', 'creature_questender', 'gameobject_questender', 'areatrigger_involvedrelation')
    tables = {}; hashes = {}; unavailable = []
    for name in names:
        if name not in index:
            if name not in ('npc_vendor', 'areatrigger_involvedrelation'):
                raise ValueError('Required primary table is unavailable: ' + name)
            tables[name] = []; unavailable.append(name)
            continue
        raw = (args.primary / 'parsed' / (name + '.json.gz')).read_bytes()
        digest = hashlib.sha256(raw).hexdigest()
        if digest != index[name]['parsed_sha256']: raise ValueError('Primary table hash mismatch: ' + name)
        table = json.loads(gzip.decompress(raw))
        if len(table) != index[name]['rows']: raise ValueError('Incomplete primary table: ' + name)
        tables[name] = table; hashes[name] = digest
    raw_population = args.population.read_bytes()
    ids = population(json.loads(raw_population))
    if len(ids) != 4335: raise ValueError('The exact 4,335-quest population is required')
    contracts = json.loads(args.contracts.read_bytes()) if args.contracts else []
    if contracts and args.artifact_root is None:
        raise ValueError('Nonempty execution contracts require an explicit artifact root')
    contract_checks = [verify_artifacts(contract, args.artifact_root) for contract in contracts]
    result = inventory(tables, ids, contracts)
    result.update(primary_source=source, table_sha256=hashes, population_sha256=hashlib.sha256(raw_population).hexdigest(),
                  accepted_contracts=len(contracts), generator_sha256=hashlib.sha256(Path(__file__).read_bytes()).hexdigest(),
                  contract_artifact_verification=contract_checks, unavailable_primary_tables=unavailable,
                  unavailable_table_is_not_empty_source_authority=True,
                  execution_evidence_gate='exact primitive semantics and full action-to-turnin lifecycle; legacy PASS is not enough')
    args.output.mkdir(parents=True, exist_ok=False)
    rows = result.pop('rows'); families = result.pop('families')
    (args.output / 'execution-ledger.jsonl').write_text(''.join(json.dumps(row, sort_keys=True) + '\n' for row in rows), encoding='utf-8')
    (args.output / 'objective-primitive-ids.json').write_text(json.dumps(families, indent=2) + '\n', encoding='utf-8')
    partition = defaultdict(list)
    for row in rows: partition[row['classification']].append(row['quest_id'])
    (args.output / 'execution-classification-ids.json').write_text(json.dumps(dict(partition), indent=2) + '\n', encoding='utf-8')
    result['output_sha256'] = {name: hashlib.sha256((args.output / name).read_bytes()).hexdigest()
                              for name in ('execution-ledger.jsonl', 'objective-primitive-ids.json', 'execution-classification-ids.json')}
    (args.output / 'summary.json').write_text(json.dumps(result, indent=2) + '\n', encoding='utf-8')
    print(json.dumps({key: result[key] for key in ('quest_count', 'legacy_counts', 'classification_counts', 'accepted_contracts', 'execution_proven_ids')}, indent=2))


if __name__ == '__main__': main()
