"""Attach explicit, source-bounded dispositions to every audited quest and flag.

Reads retained immutable data only. Numeric script/condition meanings are checked
against their retained source headers before joining records. A join records
evidence and follow-up obligations; it never manufactures a runnable strategy.
"""
from __future__ import annotations
import argparse
from collections import Counter, defaultdict
import gzip
import hashlib
import json
from pathlib import Path
import re
import shutil
import sys

from quest_ledger_335 import read_reference_table, apply_source_review_classification, CLASSIFICATIONS

def sha(path): return hashlib.sha256(path.read_bytes()).hexdigest()
def write(path, data):
    with path.open('x', encoding='utf-8') as stream: json.dump(data, stream, indent=2, ensure_ascii=True, allow_nan=False); stream.write('\n')

def constants(path):
    result = {}
    for line_number, line in enumerate(path.read_text(encoding='utf-8').splitlines(), 1):
        match = re.match(r'\s*((?:SMART_|CONDITION_|QUEST_FLAGS_)[A-Z0-9_]+)\s*=\s*(0x[0-9a-fA-F]+|\d+)\s*[,;]', line)
        if match: result[match.group(1)] = {'value': int(match.group(2), 0), 'file': path.name, 'line': line_number, 'declaration': line.strip()}
    return result

def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--input', type=Path, required=True)
    parser.add_argument('--output', type=Path, required=True)
    parser.add_argument('--evidence', type=Path, required=True, help='Retained pinned reference-tables and special-contracts directory')
    args = parser.parse_args(); base = args.evidence.resolve()
    if args.output.exists(): raise FileExistsError('Evidence output must be new')
    args.output.mkdir()
    records = [json.loads(line) for line in (args.input / 'quest-ledger.jsonl').read_text().splitlines()]
    header = constants(base / 'special-contracts/ac-ConditionMgr.h') | constants(base / 'special-contracts/ac-SmartScriptMgr.h')
    primary = constants(base / 'special-contracts/tc-ConditionMgr.h') | constants(base / 'special-contracts/tc-SmartScriptMgr.h') | constants(base / 'tc-questdef.h')
    def code(name):
        if name not in header: raise ValueError('Undeclared numeric contract: ' + name)
        return header[name]['value']
    conditions = list(read_reference_table(base / 'reference-tables/ac-conditions.sql'))
    scripts = list(read_reference_table(base / 'reference-tables/ac-smart_scripts.sql'))
    creatures = list(read_reference_table(base / 'reference-tables/ac-creature_template.sql'))
    availability = defaultdict(list); quest_conditions = defaultdict(list); direct_scripts = defaultdict(list)
    credit_scripts = defaultdict(list); aliases = defaultdict(list); summaries = Counter()
    condition_names = ['CONDITION_QUESTREWARDED', 'CONDITION_QUESTTAKEN', 'CONDITION_QUEST_NONE', 'CONDITION_QUEST_COMPLETE',
                       'CONDITION_DAILY_QUEST_DONE', 'CONDITION_QUESTSTATE', 'CONDITION_QUEST_OBJECTIVE_PROGRESS', 'CONDITION_QUEST_SATISFY_EXCLUSIVE']
    condition_codes = {code(name): name for name in condition_names}
    for row in conditions:
        if row['SourceTypeOrReferenceId'] == code('CONDITION_SOURCE_TYPE_QUEST_AVAILABLE'):
            availability[row['SourceEntry']].append(row)
        if row['ConditionTypeOrReference'] in condition_codes:
            quest_conditions[row['ConditionValue1']].append(dict(row, __condition_name=condition_codes[row['ConditionTypeOrReference']]))
    for row in creatures:
        for field in ['KillCredit1', 'KillCredit2']:
            if row.get(field): aliases[row[field]].append({'actor_entry': row['entry'], 'name': row['name'], 'field': field,
                'source': 'ac-creature_template.sql', 'line': row['__source_line']})
    action_quest = {code(name): (name, field) for name, field in [
        ('SMART_ACTION_FAIL_QUEST', 'action_param1'), ('SMART_ACTION_OFFER_QUEST', 'action_param1'),
        ('SMART_ACTION_CALL_AREAEXPLOREDOREVENTHAPPENS', 'action_param1'), ('SMART_ACTION_CALL_GROUPEVENTHAPPENS', 'action_param1'),
        ('SMART_ACTION_ESCORT_START', 'action_param4'), ('SMART_ACTION_ESCORT_STOP', 'action_param2')]}
    event_quest = {code(name): name for name in ['SMART_EVENT_ACCEPTED_QUEST', 'SMART_EVENT_REWARD_QUEST']}
    for row in scripts:
        if row['event_type'] in event_quest and row['event_param1']:
            direct_scripts[row['event_param1']].append(dict(row, __contract=event_quest[row['event_type']]))
        if row['action_type'] in action_quest:
            name, field = action_quest[row['action_type']]
            if row[field]: direct_scripts[row[field]].append(dict(row, __contract=name))
        if row['source_type'] == code('SMART_SCRIPT_TYPE_QUEST') and row['entryorguid'] > 0:
            direct_scripts[row['entryorguid']].append(dict(row, __contract='SMART_SCRIPT_TYPE_QUEST'))
        if row['action_type'] == code('SMART_ACTION_CALL_KILLEDMONSTER'):
            credit_scripts[row['action_param1']].append(row)
    source_codes = {item['value']: item for name, item in primary.items() if name.startswith('QUEST_FLAGS_') and item['value'] > 0 and item['value'] & (item['value'] - 1) == 0}
    for record in records:
        ident = record['quest_id']; comparison = record['secondary_reference']
        flags = record['flags']; matched = [dict(value, flag=value['declaration'].split('=')[0].strip()) for key, value in source_codes.items() if flags & key]
        decoded_mask = 0
        for key in source_codes: decoded_mask |= key
        script_refs = {row['__source_line']: row for row in direct_scripts[ident]}
        record['source_review'] = {
            'realm_equivalence_proven': False,
            'decoded_primary_flags': matched, 'flags_without_primary_definition': flags & ~decoded_mask,
            'secondary_availability_conditions': availability[ident],
            'secondary_quest_condition_references': quest_conditions[ident],
            'secondary_direct_quest_scripts': list(script_refs.values()),
            'source_contracts': {'conditions': 'ac-ConditionMgr.h', 'script_parameters': 'ac-SmartScriptMgr.h'},
            'conditions_claim_limit': 'Direct availability conditions belong to the reference server. Conditions mentioning this quest elsewhere are cross-references, not its own admission requirements.',
            'script_claim_limit': 'Explicit ID/credit joins are evidence of reference script participation, not a complete interaction recipe or proof this realm runs it.'
        }
        disposition = []
        if not record['giver_relations']:
            kind = 'item-started-route-not-implemented' if comparison['item_starters'] else 'no-ordinary-giver-or-item-starter-in-reference'
            disposition.append({'flag': 'missing-giver-relations', 'disposition': kind, 'server_invalid_proven': False,
                'item_starter_ids': [row['item_id'] for row in comparison['item_starters']],
                'direct_reference_script_lines': sorted(script_refs)})
            summaries[kind] += 1
        if not record['ender_relations']:
            disposition.append({'flag': 'missing-ender-relations', 'disposition': 'ordinary-turn-in-route-unrepresented; inspect explicit auto-reward/event contracts',
                'server_invalid_proven': False, 'primary_flag_declarations': matched,
                'reference_quest_type': comparison.get('missing_bot_fields', {}).get('QuestType'), 'direct_reference_script_lines': sorted(script_refs)})
        for role in ['giver', 'ender']:
            for relation in record[role + '_relations']:
                if relation['spawn_count'] == 0:
                    disposition.append({'flag': role + '-relation-without-spawns', 'entry': relation['entry'], 'object_type': relation['object_type'],
                        'disposition': 'stored-geometry-gap; a fresh typed live instance can supply geometry while retaining navigation/recovery checks',
                        'not_proven': 'Absence from bot spawn data does not establish that the server never spawns this actor.'})
        for objective in record['objectives']:
            if objective['kind'] != 'TurnInOnly' and objective['spawn_count'] == 0:
                entry = objective['target_entry']; credits = aliases[entry] if objective['target_type'] == 'Creature' else []
                credit_rows = credit_scripts[entry] if objective['target_type'] == 'Creature' else []
                disposition.append({'flag': 'objective-without-spawns', 'objective_pointer': objective['pointer'],
                    'kind': objective['kind'], 'entry': entry, 'item_id': objective['item_id'],
                    'disposition': 'reference-credit-alias-or-script-evidence; explicit strategy/route required' if credits or credit_rows else 'bot acquisition/interaction source unrepresented',
                    'reference_credit_aliases': credits, 'reference_credit_script_rows': credit_rows,
                    'invented_action': False, 'server_invalid_proven': False})
                summaries['missing-objective-with-credit-source-evidence' if credits or credit_rows else 'missing-objective-without-credit-source-evidence'] += 1
        if record['special_flags']:
            meanings = []
            for bit, name in [(1, 'repeatable'), (2, 'exploration-or-event'), (4, 'auto-accept'), (8, 'dungeon-finder'), (16, 'monthly'), (32, 'cast-credit-instead-of-kill'), (64, 'secondary-no-reputation-spillover; primary-core-difference')]:
                if record['special_flags'] & bit: meanings.append(name)
            disposition.append({'flag': 'nonzero-SpecialFlags', 'value': record['special_flags'], 'declared_meanings': meanings,
                'disposition': 'requires an explicit source-bound recipe for nonordinary credit; nonzero alone is not invalid',
                'strategy_pack': record['strategy']['status']})
        if record['start_item']:
            disposition.append({'flag': 'StartItem', 'item_id': record['start_item'], 'disposition': 'provided-on-acceptance; not an item-started-quest field and not an inferred use-item action',
                'reference_item_template_exists': comparison['provided_item_template_exists']})
        record['flag_dispositions'] = disposition
        apply_source_review_classification(record)
        record['state_coverage_note'] = 'Per-row deterministic cases are in simulation.cases; shared root-owner scenarios prove the common interruption and raw-observation guards. Neither is live server completion.'
        summaries['quests-with-reference-availability-conditions'] += bool(availability[ident])
        summaries['quests-with-direct-reference-script-joins'] += bool(script_refs)
    ledger_path = args.output / 'quest-ledger.jsonl'
    with ledger_path.open('x', encoding='utf-8') as stream:
        for record in records: stream.write(json.dumps(record, ensure_ascii=True, separators=(',', ':'), allow_nan=False) + '\n')
    with (args.output / 'quest-ledger.jsonl.gz').open('xb') as stream:
        with gzip.GzipFile(fileobj=stream, mode='wb', mtime=0) as compressed: compressed.write(ledger_path.read_bytes())
    for name in ['flagged-rows.json', 'structural-checks.json', 'sources.json']:
        shutil.copyfile(args.input / name, args.output / name)
    summary = json.loads((args.input / 'summary.json').read_text())
    classification_ids = {name: [row['quest_id'] for row in records if row['classification'] == name] for name in CLASSIFICATIONS}
    write(args.output / 'classification-ids.json', classification_ids)
    summary['classification_counts'] = {name: len(ids) for name, ids in classification_ids.items()}
    summary['classification_review_changes'] = sum(row['source_review_classification']['before'] != row['classification'] for row in records)
    summary['per_quest_ledger_sha256'] = sha(ledger_path)
    summary['source_disposition_counts'] = dict(summaries)
    summary['parent_ledger_sha256'] = sha(args.input / 'quest-ledger.jsonl')
    summary['source_review_script_sha256'] = sha(Path(__file__))
    summary['additional_contracts'] = json.loads((base / 'special-contracts/source-downloads.json').read_text())
    write(args.output / 'summary.json', summary)
    write(args.output / 'source-contract-codes.json', {'secondary': header, 'primary': primary})
    write(args.output / 'review-receipt.json', {'input': str(args.input), 'output': str(args.output), 'quest_rows': len(records),
        'source_headers': summary['additional_contracts'], 'script_sha256': sha(Path(__file__)), 'counts': dict(summaries),
        'classification_changes': summary['classification_review_changes'], 'action_recipes_invented': 0})
    print(json.dumps({'rows': len(records), 'classification_counts': summary['classification_counts'], 'source_dispositions': dict(summaries), 'output': str(args.output)}), flush=True)

if __name__ == '__main__': main()
