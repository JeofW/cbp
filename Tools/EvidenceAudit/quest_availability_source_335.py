"""Export complete original-TC335 quest availability contracts for current owners.

Permanent quest reward history, ordinary active state, carried-item quantities,
level, area and daily membership use their separate verified owners. Unsupported
targets, references, scripts, bank and other seasonal/repeatable semantics remain
explicit obligations; a partial condition list is never exported.
"""
from __future__ import annotations

import argparse
from collections import defaultdict
import gzip
import hashlib
import json
from pathlib import Path
import re

from quest_repair_pack_335 import BASE_FIELDS, ADDON_FIELDS, digest, encoded, indexed, load_verified_tables

SOURCE_TYPE = 19
SUPPORTED_TYPES = {2, 8, 9, 14, 23, 25, 27, 28, 43, 47}
STATUS_MASK = (1 << 0) | (1 << 1) | (1 << 3) | (1 << 5) | (1 << 6)
SEASONAL_SORTS = {-22, -284, -366, -369, -370, -374, -376}


def permanent_reward_reference(template, addon):
    """GetQuestRewardStatus depends on repeatable/seasonal lifecycle, not auto-completion."""
    method, flags, sort = template.get('QuestType'), addon.get('SpecialFlags', 0), template.get('QuestSortID')
    return (type(method) is int and method in (0, 2) and type(flags) is int and flags >= 0
            and not flags & 1 and type(sort) is int and sort not in SEASONAL_SORTS)


def permanent_reference(template, addon):
    """Raw accepted-state inference remains limited to ordinary quests."""
    return template.get('QuestType') == 2 and permanent_reward_reference(template, addon)


def daily_reference(template, addon):
    """IsDailyQuestDone checks an existing template against the daily ID array.

    Its membership observation is separate from repeatable/seasonal reward history.
    """
    return (type(template.get('QuestType')) is int and template['QuestType'] in (0, 2)
            and type(addon.get('SpecialFlags', 0)) is int and addon.get('SpecialFlags', 0) >= 0
            and type(template.get('QuestSortID')) is int)


def build_contracts(data, tables, source):
    for name in ('quest_template', 'quest_template_addon', 'conditions'):
        if name not in tables:
            raise ValueError('Missing primary availability table: ' + name)
    base = indexed(data['Quests'], 'Id')
    templates = indexed(tables['quest_template'], 'ID')
    addons = indexed(tables['quest_template_addon'], 'ID')
    items = indexed(tables.get('item_template', []), 'entry')
    conditions = defaultdict(list)
    for row in tables['conditions']:
        if row.get('SourceTypeOrReferenceId') == SOURCE_TYPE and row.get('SourceEntry') in base:
            conditions[row['SourceEntry']].append(row)
    prefix = f"tc335:{source['CoreRevision']}:{source['DatabaseRevision']}:{source['SourceSqlSha256']}"

    def reference(table, row):
        return {'table': table, 'source_line': row.get('__source_line'), 'row_sha256': digest(row)}

    contracts, reviews = [], []
    for ident, rows in sorted(conditions.items()):
        quest = base[ident]; template = templates.get(ident); addon = addons.get(ident, {})
        problems = []
        if template is None:
            problems.append('primary-subject-absent')
        elif (any(field in template and quest.get(field) != template[field] for field in BASE_FIELDS)
              or any(quest.get(field) != addon.get(field, 0) for field in ADDON_FIELDS)):
            problems.append('subject-primary-field-conflict')
        groups = defaultdict(list); refs = {}; item_refs = {}; spell_refs = {}; keys = set(); evidence = []
        for row in rows:
            evidence.append(dict(reference('conditions', row), row=row))
            fields = ('SourceGroup', 'SourceId', 'ElseGroup', 'ConditionTypeOrReference', 'ConditionTarget',
                      'ConditionValue1', 'ConditionValue2', 'ConditionValue3', 'NegativeCondition')
            if any(type(row.get(field)) is not int for field in fields):
                problems.append('invalid-condition-integer'); continue
            kind, value = row['ConditionTypeOrReference'], row['ConditionValue1']
            if (row['SourceGroup'] != 0 or row['SourceId'] != 0 or row['ConditionTarget'] != 0
                    or row.get('ScriptName') != '' or not 0 <= row['ElseGroup'] <= 2**31 - 1):
                problems.append('unsupported-condition-scope-target-or-script'); continue
            if kind not in SUPPORTED_TYPES:
                problems.append('unsupported-condition-type:' + str(kind)); continue
            if kind == 2 and row['ConditionValue3'] != 0:
                problems.append('bank-inventory-observation-unavailable'); continue
            if kind == 25 and row['NegativeCondition'] != 0:
                problems.append('negative-spell-absence-not-proven'); continue
            if (row['NegativeCondition'] not in (0, 1) or value <= 0 or value > 2**31 - 1
                    or row['ConditionValue3'] != 0 or (kind not in (2, 27, 47) and row['ConditionValue2'] != 0)
                    or (kind == 2 and not 0 < row['ConditionValue2'] <= 2**31 - 1)
                    or (kind == 27 and not 0 <= row['ConditionValue2'] <= 4)
                    or (kind == 47 and (row['ConditionValue2'] <= 0 or row['ConditionValue2'] & ~STATUS_MASK))):
                problems.append('invalid-condition-values'); continue
            if kind == 2:
                item = items.get(value)
                if item is None or type(item.get('entry')) is not int or item['entry'] != value:
                    problems.append('referenced-item-template-absent-or-invalid:' + str(value)); continue
                item_refs[value] = {'ItemId': value, 'SourceRef': prefix + ':item_template:' + str(value) + ':' + digest(item)}
            elif kind == 25:
                # The exact primary condition is the source of this query ID.
                # A client list is not an invented complete server spell table.
                spell_refs[value] = {'SpellId': value, 'SourceRef': prefix + ':conditions:spell=' + str(value)}
            elif kind not in (23, 27):
                referenced = templates.get(value); referenced_addon = addons.get(value, {})
                if referenced is None:
                    problems.append('referenced-quest-absent:' + str(value)); continue
                valid_reference = daily_reference if kind == 43 else permanent_reward_reference if kind == 8 else permanent_reference
                if not valid_reference(referenced, referenced_addon):
                    problems.append('referenced-history-not-ordinary-permanent:' + str(value)); continue
                if value in base and (base[value].get('QuestSortID') != referenced['QuestSortID']
                                      or base[value].get('SpecialFlags') != referenced_addon.get('SpecialFlags', 0)):
                    problems.append('referenced-quest-source-conflict:' + str(value)); continue
                refs[value] = {'QuestId': value, 'QuestType': referenced['QuestType'],
                               'SpecialFlags': referenced_addon.get('SpecialFlags', 0),
                               'QuestSortID': referenced['QuestSortID'],
                               'SourceRef': prefix + ':quest_template+addon:' + str(value)}
            key = tuple(row[field] for field in ('ElseGroup', 'ConditionTypeOrReference', 'ConditionValue1',
                                                'ConditionValue2', 'ConditionValue3', 'NegativeCondition'))
            if key in keys:
                problems.append('duplicate-condition'); continue
            keys.add(key)
            groups[row['ElseGroup']].append({'Type': kind, 'Value1': value, 'Value2': row['ConditionValue2'],
                'Value3': 0, 'Negative': bool(row['NegativeCondition']),
                'SourceRef': prefix + ':conditions:' + str(ident) + ':' + digest(row)})
        if len(groups) > 32 or any(len(group) > 128 for group in groups.values()) or len(rows) > 256:
            problems.append('condition-contract-exceeds-bounds')
        supported = not problems and bool(groups) and sum(map(len, groups.values())) == len(rows)
        contract = {'QuestId': ident, 'SourceRef': prefix + ':conditions:quest=' + str(ident),
                    'ReferencedQuests': [refs[key] for key in sorted(refs)],
                    'Groups': [{'ElseGroup': group, 'Conditions': sorted(values, key=lambda item:
                        (item['Type'], item['Value1'], item['Value2'], item['Negative']))} for group, values in sorted(groups.items())]}
        if item_refs:
            contract['ReferencedItems'] = [item_refs[key] for key in sorted(item_refs)]
        if spell_refs:
            contract['ReferencedSpells'] = [spell_refs[key] for key in sorted(spell_refs)]
        if supported:
            contracts.append(contract)
        reviews.append({'quest_id': ident, 'supported': supported, 'remaining': sorted(set(problems)),
            'conditions': evidence, 'referenced_primary_quests': [{'template': reference('quest_template', templates[key]),
                'addon': reference('quest_template_addon', addons[key]) if key in addons else {'absent': True},
                'contract': value} for key, value in sorted(refs.items())],
            'contract_sha256': digest(contract) if supported else None, 'live_completion_proven': False})
        if item_refs:
            reviews[-1]['referenced_primary_items'] = [{'item': reference('item_template', items[key]), 'contract': value}
                                                       for key, value in sorted(item_refs.items())]
    return contracts, reviews


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    for name in ('dataset', 'reference', 'output'):
        parser.add_argument('--' + name, type=Path, required=True)
    args = parser.parse_args()
    if args.output.exists():
        raise FileExistsError('Availability export is create-only')
    tables, receipts = load_verified_tables(args.reference)
    header = (args.reference / 'contracts/ConditionMgr.h').read_text(encoding='utf-8')
    expected = {'CONDITION_SOURCE_TYPE_QUEST_AVAILABLE': SOURCE_TYPE, 'CONDITION_QUESTREWARDED': 8,
                'CONDITION_QUESTTAKEN': 9, 'CONDITION_QUEST_NONE': 14, 'CONDITION_QUEST_COMPLETE': 28,
                'CONDITION_QUESTSTATE': 47, 'CONDITION_LEVEL': 27, 'CONDITION_AREAID': 23,
                'CONDITION_DAILY_QUEST_DONE': 43, 'CONDITION_ITEM': 2, 'CONDITION_SPELL': 25}
    for name, value in expected.items():
        match = re.search(r'\b' + name + r'\s*=\s*(0x[0-9a-fA-F]+|\d+)\s*,', header)
        if not match or int(match[1], 0) != value:
            raise ValueError('The pinned condition namespace differs: ' + name)
    binding = json.loads((args.reference / 'source-receipt.json').read_bytes())
    raw = args.dataset.read_bytes()
    source = {'CoreRevision': binding['release_commit'], 'DatabaseRevision': binding['release'],
              'SourceSqlSha256': binding['members'][0]['sha256'], 'QuestDataSha256': hashlib.sha256(raw).hexdigest()}
    contracts, review = build_contracts(json.loads(raw), tables, source)
    args.output.mkdir(parents=True)
    (args.output / 'availability-contracts.json').write_bytes(encoded(contracts) + b'\n')
    report = {'source': source, 'generator_sha256': hashlib.sha256(Path(__file__).read_bytes()).hexdigest(),
              'reference_tables': receipts, 'quests': review, 'live_completion_proven': False}
    (args.output / 'availability-review.json.gz').write_bytes(gzip.compress(encoded(report), mtime=0))
    summary = {'source': source, 'condition_quests_reviewed': len(review), 'supported_quests': len(contracts),
               'supported_condition_rows': sum(len(g['Conditions']) for c in contracts for g in c['Groups']),
               'unresolved_ids': [r['quest_id'] for r in review if not r['supported']],
               'supported_ids': [c['QuestId'] for c in contracts], 'live_completion_proven': False}
    (args.output / 'summary.json').write_text(json.dumps(summary, indent=2) + '\n', encoding='utf-8')
    print(json.dumps(summary, indent=2))


if __name__ == '__main__':
    main()
