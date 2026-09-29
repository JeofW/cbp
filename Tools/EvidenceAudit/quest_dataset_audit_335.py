"""Audit exact bot data against pinned reference snapshots, without SQL execution.

Reference rows are secondary comparison, never a replacement for this realm's
missing provenance. Output is create-only and includes every original quest row.
Use --simulations to merge a separately generated actual-owner simulation ledger.
"""
from __future__ import annotations
import argparse
from collections import Counter, defaultdict
from datetime import datetime, timezone
import gzip
import hashlib
import json
from pathlib import Path
from typing import Any

from quests import audit_database
from quest_ledger_335 import AC_REVISION, CLASSIFICATIONS, SOURCES, read_reference_table


def digest(path: Path) -> str:
    return hashlib.sha256(path.read_bytes()).hexdigest()


def write_json(path: Path, value: Any) -> None:
    with path.open('x', encoding='utf-8', newline='\n') as stream:
        json.dump(value, stream, indent=2, ensure_ascii=True, allow_nan=False)
        stream.write('\n')


def source_ref(row: dict, filename: str) -> dict:
    return {'source': filename, 'line': row['__source_line'], 'table': row['__source_table'],
            'role': 'secondary-reference-comparison', 'revision': AC_REVISION}


class References:
    def __init__(self, directory: Path):
        self.directory = directory
        self.tables: dict[str, list[dict]] = {}
        self.receipts: list[dict] = []
        expected = {entry['file']: entry for entry in json.loads((directory / 'source-downloads.json').read_text())}
        for path in sorted(directory.glob('ac-*.sql')):
            sha = digest(path)
            receipt = expected.get(path.name)
            if receipt is None or receipt.get('sha256') != sha:
                raise ValueError(f'Reference identity mismatch: {path}')
            rows = list(read_reference_table(path))
            table = path.stem.removeprefix('ac-')
            self.tables[table] = rows
            self.receipts.append(dict(receipt, parsed_rows=len(rows)))
            print(json.dumps({'reference': path.name, 'rows': len(rows), 'sha256': sha}), flush=True)
        self.quests = self.keyed('quest_template', 'ID')
        self.addons = self.keyed('quest_template_addon', 'ID')
        self.creatures = self.keyed('creature_template', 'entry')
        self.gameobjects = self.keyed('gameobject_template', 'entry')
        self.items = self.keyed('item_template', 'entry')
        self.item_starters = defaultdict(list)
        for row in self.tables['item_template']:
            if row.get('startquest', 0): self.item_starters[row['startquest']].append(row)
        self.relations = {}
        for table in ['creature_queststarter', 'creature_questender', 'gameobject_queststarter', 'gameobject_questender']:
            grouped = defaultdict(list)
            for row in self.tables[table]: grouped[row['quest']].append(row)
            self.relations[table] = grouped
        self.actor_scripts = defaultdict(list)
        for row in self.tables['smart_scripts']:
            self.actor_scripts[(row['source_type'], row['entryorguid'])].append(row)

    def keyed(self, table: str, key: str) -> dict:
        rows = self.tables[table]
        result = {row[key]: row for row in rows}
        if len(result) != len(rows): raise ValueError(f'Duplicate reference identity: {table}.{key}')
        return result

    def compare(self, quest: dict, ledger: dict) -> dict:
        ident = quest['Id']; template = self.quests.get(ident); addon = self.addons.get(ident)
        result: dict[str, Any] = {
            'status': 'FOUND' if template else 'ABSENT-FROM-SECONDARY',
            'realm_equivalence_proven': False, 'differences': [], 'missing_bot_fields': {},
            'template_source': source_ref(template, 'ac-quest_template.sql') if template else None,
            'addon_source': source_ref(addon, 'ac-quest_template_addon.sql') if addon else None,
            'item_starters': [{'item_id': row['entry'], 'name': row.get('name'),
                               'source': source_ref(row, 'ac-item_template.sql')} for row in self.item_starters.get(ident, [])],
            'objective_checks': [], 'relation_checks': [],
        }
        if template is None:
            ledger['source_obligations'].append('quest-absent-from-secondary-reference')
            return result
        for source in [template, addon or {}]:
            for field in ['MinLevel', 'QuestLevel', 'AllowableRaces', 'QuestInfoID', 'QuestSortID',
                          'RequiredFactionId1', 'RequiredFactionId2', 'Flags', 'StartItem', 'PrevQuestID',
                          'NextQuestID', 'ExclusiveGroup', 'SpecialFlags']:
                if field in source and field in quest and source[field] != quest[field]:
                    result['differences'].append({'field': field, 'bot': quest[field], 'secondary': source[field]})
            for field in ['QuestType', 'MaxLevel', 'AllowableClasses', 'RequiredSkillID', 'RequiredSkillPoints',
                          'RequiredMinRepFaction', 'RequiredMinRepValue', 'RequiredMaxRepFaction', 'RequiredMaxRepValue',
                          'RequiredFactionValue1', 'RequiredFactionValue2', 'RequiredPlayerKills', 'TimeAllowed', 'ProvidedItemCount']:
                if field in source and field not in quest: result['missing_bot_fields'][field] = source[field]
        if result['differences']: ledger['source_obligations'].append('secondary-template-differs-see-field-evidence')
        for side, role in [('giver', 'starter'), ('ender', 'ender')]:
            for relation in ledger[side + '_relations']:
                kind, entry = relation['object_type'], relation['entry']
                prefix = 'creature' if kind == 'Creature' else 'gameobject'
                table = prefix + '_quest' + role
                ref_relations = self.relations[table].get(ident, [])
                match = next((row for row in ref_relations if row['id'] == entry), None)
                entity = (self.creatures if kind == 'Creature' else self.gameobjects).get(entry)
                result['relation_checks'].append({'side': side, 'entry': entry, 'object_type': kind,
                    'template_exists': entity is not None, 'reference_relation_exists': match is not None,
                    'relation_source': source_ref(match, 'ac-' + table + '.sql') if match else None,
                    'entity_source': source_ref(entity, 'ac-' + prefix + '_template.sql') if entity else None,
                    'script_name': entity.get('ScriptName') if entity else None})
        normal = [(template[f'RequiredNpcOrGo{index}'], template[f'RequiredNpcOrGoCount{index}'])
                  for index in range(1, 5) if template[f'RequiredNpcOrGo{index}']]
        required_items = {template[f'RequiredItemId{index}']: template[f'RequiredItemCount{index}']
                          for index in range(1, 7) if template[f'RequiredItemId{index}']}
        result['required_normal_credits'] = [{'entry': entry, 'count': count,
            'object_type': 'GameObject' if entry < 0 else 'Creature'} for entry, count in normal]
        result['required_items'] = required_items
        result['verified_build'] = template.get('VerifiedBuild')
        result['special_flags'] = (addon or {}).get('SpecialFlags')
        for objective in quest.get('Objectives') or []:
            kind = objective['Type']; index = objective.get('Index'); item_id = objective.get('ItemId', 0)
            entry = objective.get('GameObjectId', 0) if kind == 'CollectFromGameObject' else objective.get('MobId', 0)
            target_table = 'gameobject_template' if kind == 'CollectFromGameObject' else 'creature_template'
            entity = (self.gameobjects if kind == 'CollectFromGameObject' else self.creatures).get(entry)
            amount = objective.get('KillCount', 0) if kind == 'KillMob' else objective.get('CollectCount', 0)
            signed_entry = -entry if kind == 'CollectFromGameObject' else entry
            check: dict[str, Any] = {'dataset_index': index, 'kind': kind, 'entry': entry, 'item_id': item_id,
                'target_template_exists': entity is not None if entry else None,
                'target_source': source_ref(entity, 'ac-' + target_table + '.sql') if entity else None,
                'item_template_exists': item_id in self.items if item_id else None,
                'reference_item_count_matches': required_items.get(item_id) == amount if item_id else None,
                'reference_normal_credit_matches': (signed_entry, amount) in normal if kind in ('KillMob', 'CollectFromGameObject') and not item_id else None,
                'script_name': entity.get('ScriptName') if entity else None,
                'creature_credit_aliases': [entity.get('KillCredit1'), entity.get('KillCredit2')] if entity and target_table == 'creature_template' else [],
                'gameobject_type': entity.get('type') if entity and target_table == 'gameobject_template' else None}
            linked_scripts = self.actor_scripts.get((1 if target_table == 'gameobject_template' else 0, entry), [])
            check['actor_script_references'] = [dict(source_ref(row, 'ac-smart_scripts.sql'), id=row['id'], link=row['link'],
                event_type=row['event_type'], action_type=row['action_type'], comment=row.get('comment', '')) for row in linked_scripts]
            check['script_claim_limit'] = 'An actor script is not proof it belongs to this quest; no action recipe is inferred.'
            if kind == 'TurnInOnly':
                check['reference_has_unrepresented_credit'] = bool(normal or required_items or template.get('RequiredPlayerKills')
                    or template.get('RequiredFactionId1') or ((addon or {}).get('SpecialFlags', 0) & (2 | 32)))
                if check['reference_has_unrepresented_credit']:
                    ledger['source_obligations'].append('turn-in-only-bot-row-has-other-credit-in-secondary')
            if check['reference_normal_credit_matches'] is False:
                ledger['source_obligations'].append('normal-objective-credit-differs-from-secondary')
            if check['reference_item_count_matches'] is False:
                ledger['source_obligations'].append('item-objective-count-differs-from-secondary')
            result['objective_checks'].append(check)
        start = quest.get('StartItem', 0)
        result['provided_item_template_exists'] = start in self.items if start else None
        result['unrepresented_secondary_requirements'] = {field: value for field, value in result['missing_bot_fields'].items()
            if value and field not in ('QuestType', 'ProvidedItemCount')}
        return result


def dependency_components(quests: list[dict]) -> list[list[int]]:
    """Kosaraju without recursion; report actual directed cycles, not all ancestors."""
    identities = {quest['Id'] for quest in quests}
    graph = {quest['Id']: set(value for value in ([abs(quest.get('PrevQuestID', 0))] + quest.get('PreviousQuestsIds', []))
              if value in identities) for quest in quests}
    reverse = {ident: set() for ident in identities}
    for ident, parents in graph.items():
        for parent in parents: reverse[parent].add(ident)
    visited, order = set(), []
    for ident in sorted(identities):
        if ident in visited: continue
        stack = [(ident, False)]
        while stack:
            node, done = stack.pop()
            if done: order.append(node); continue
            if node in visited: continue
            visited.add(node); stack.append((node, True))
            stack.extend((parent, False) for parent in sorted(graph[node], reverse=True) if parent not in visited)
    visited, components = set(), []
    for ident in reversed(order):
        if ident in visited: continue
        component, stack = [], [ident]
        while stack:
            node = stack.pop()
            if node in visited: continue
            visited.add(node); component.append(node); stack.extend(reverse[node] - visited)
        if len(component) > 1 or ident in graph[ident]: components.append(sorted(component))
    return sorted(components)


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--dataset', type=Path, required=True)
    parser.add_argument('--references', type=Path, required=True)
    parser.add_argument('--output', type=Path, required=True)
    parser.add_argument('--simulations', type=Path)
    args = parser.parse_args()
    if args.output.exists(): raise FileExistsError('Choose a new output directory; evidence is create-only')
    args.output.mkdir(parents=True)
    raw = args.dataset.read_bytes(); sha = hashlib.sha256(raw).hexdigest()
    data = json.loads(raw.decode('utf-8-sig')); result = audit_database(data)
    references = References(args.references)
    quests = data['Quests']; ledger = result.pop('quest_ledger')
    cycle_groups = dependency_components(quests)
    cycles = {ident: group for group in cycle_groups for ident in group}
    for quest, row in zip(quests, ledger):
        row['dataset_sha256'] = sha
        row['secondary_reference'] = references.compare(quest, row)
        row['strategy'] = {'status': 'ABSENT' if not args.dataset.with_name('quest_strategies.json').exists() else 'PRESENT-REQUIRES-LOADER-VALIDATION',
                           'recipe_ids': [], 'invented_actions': False}
        row['dataset_provenance'] = 'ABSENT' if not args.dataset.with_name('quest_data.provenance.json').exists() else 'PRESENT-REQUIRES-LOADER-VALIDATION'
        if quest['Id'] in cycles:
            row['dependencies']['cycle_component'] = cycles[quest['Id']]
            row['source_obligations'].append('directed-prerequisite-cycle-requires-source-review')
            row['classification'] = 'SOURCE-UNCERTAIN'
        semantic_conflicts = {'turn-in-only-bot-row-has-other-credit-in-secondary', 'normal-objective-credit-differs-from-secondary',
                              'item-objective-count-differs-from-secondary', 'quest-absent-from-secondary-reference'}
        if semantic_conflicts.intersection(row['source_obligations']) and row['classification'] == 'LIVE-ACCEPTANCE-REQUIRED':
            row['classification'] = 'SOURCE-UNCERTAIN'
        row['source_obligations'] = sorted(set(row['source_obligations']))
    if args.simulations:
        simulations = [json.loads(line) for line in args.simulations.read_text(encoding='utf-8-sig').splitlines() if line.strip()]
        by_id = {row['quest_id']: row for row in simulations}
        if len(by_id) != len(simulations) or set(by_id) != {q['Id'] for q in quests}:
            raise ValueError('Simulation ledger must cover every actual quest ID exactly once')
        for row in ledger:
            evidence = by_id[row['quest_id']]
            if evidence['dataset_sha256'] != sha: raise ValueError('Simulation input differs from audited dataset')
            row['simulation'] = dict(evidence, evidence_file_sha256=digest(args.simulations))
            if evidence.get('pipeline_status') == 'PASS' and evidence.get('failed_cases') == 0:
                if row['classification'] == 'LIVE-ACCEPTANCE-REQUIRED':
                    row['classification'] = 'GENERIC-PROVEN'
                    row['classification_scope'] = 'Production bot logic under the recorded controlled observations; live acceptance still required'
    with (args.output / 'quest-ledger.jsonl').open('x', encoding='utf-8', newline='\n') as stream:
        for row in ledger: stream.write(json.dumps(row, ensure_ascii=True, allow_nan=False, separators=(',', ':')) + '\n')
    with (args.output / 'quest-ledger.jsonl').open('rb') as source, (args.output / 'quest-ledger.jsonl.gz').open('xb') as target:
        with gzip.GzipFile(fileobj=target, mode='wb', mtime=0) as compressed: compressed.write(source.read())
    flags = defaultdict(list)
    for row in ledger:
        if not row['giver_relations']: flags['no_giver_relations'].append(row['quest_id'])
        if not row['ender_relations']: flags['no_ender_relations'].append(row['quest_id'])
        for role in ('giver', 'ender'):
            for relation in row[role + '_relations']:
                if relation['spawn_count'] == 0: flags[role + '_relations_without_spawns'].append({'quest_id': row['quest_id'], **relation})
        for objective in row['objectives']:
            if objective['kind'] != 'TurnInOnly' and objective['spawn_count'] == 0:
                flags[objective['kind'] + '_objectives_without_spawns'].append({'quest_id': row['quest_id'], **objective})
        if row['special_flags']: flags['nonzero_special_flags'].append(row['quest_id'])
        if row['start_item']: flags['start_item_quests'].append(row['quest_id'])
    classification_ids = {name: [row['quest_id'] for row in ledger if row['classification'] == name] for name in CLASSIFICATIONS}
    summary = {
        'schema': 'wholesome-quest-ledger-v1', 'created_utc': datetime.now(timezone.utc).isoformat(),
        'dataset_path': str(args.dataset.resolve()), 'dataset_sha256': sha, 'quest_count': len(quests),
        'unique_quest_ids': len({quest['Id'] for quest in quests}), 'giver_relations': len(data['QuestGivers']), 'ender_relations': len(data['QuestEnders']),
        'classification_counts': {name: len(ids) for name, ids in classification_ids.items()},
        'flag_counts': {name: len(rows) for name, rows in flags.items()},
        'secondary_comparison': {'reference_quests': len(references.quests),
            'quests_with_field_differences': sum(bool(row['secondary_reference']['differences']) for row in ledger),
            'quests_with_unrepresented_requirements': sum(bool(row['secondary_reference'].get('unrepresented_secondary_requirements')) for row in ledger)},
        'simulation_rows': sum(row['simulation'].get('status') != 'NOT-RUN' for row in ledger),
        'live_completion_proven': 0, 'directed_dependency_cycle_components': cycle_groups,
        'per_quest_ledger_sha256': digest(args.output / 'quest-ledger.jsonl'),
        'source_files': references.receipts,
        'claim_limits': ['Secondary rows are not realm facts.', 'A bot-data gap does not prove a server quest is broken.',
                         'Simulation success proves only recorded observations and exercised production owners.'],
    }
    assert sum(summary['classification_counts'].values()) == len(quests)
    write_json(args.output / 'summary.json', summary)
    write_json(args.output / 'classification-ids.json', classification_ids)
    write_json(args.output / 'flagged-rows.json', flags)
    write_json(args.output / 'structural-checks.json', result)
    write_json(args.output / 'sources.json', {'contracts': SOURCES, 'reference_files': references.receipts})
    print(json.dumps({key: summary[key] for key in ['quest_count', 'classification_counts', 'flag_counts', 'secondary_comparison', 'simulation_rows', 'live_completion_proven']}), flush=True)


if __name__ == '__main__':
    main()
