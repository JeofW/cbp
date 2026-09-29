"""Per-row bot-data evidence; never equate a missing route with a broken server quest.

Static rows start unexecuted. Only a separately hashed production-owner simulation
can promote a row to GENERIC-PROVEN/STRATEGY-PROVEN. Reference SQL is parsed as
data: this module never opens a database connection or executes SQL statements.
"""
from __future__ import annotations
from collections import Counter, defaultdict
import math
from pathlib import Path
import re
from typing import Any, Iterator

CLASSIFICATIONS = ('GENERIC-PROVEN', 'STRATEGY-PROVEN', 'DATA-INVALID/INCOMPLETE',
                   'UNSUPPORTED-SCRIPTED', 'SOURCE-UNCERTAIN', 'LIVE-ACCEPTANCE-REQUIRED')
ELIGIBILITY_FIELDS = ('MinLevel', 'QuestLevel', 'MaxLevel', 'AllowableRaces', 'AllowableClasses',
                      'RequiredSkillID', 'RequiredSkillPoints', 'RequiredMinRepFaction', 'RequiredMinRepValue',
                      'RequiredMaxRepFaction', 'RequiredMaxRepValue', 'RequiredFactionId1', 'RequiredFactionValue1',
                      'RequiredFactionId2', 'RequiredFactionValue2')
TC_REVISION = '8fda442f6c30ca21a622638063ab8b28376f1b25'
AC_REVISION = '8337a378ac325e62a6a91e00c6a5e944205e8536'
SOURCES = {
    'TC-PLAYER': {'role': 'primary-server-contract', 'revision': TC_REVISION,
        'url': f'https://github.com/TrinityCore/TrinityCore/blob/{TC_REVISION}/src/server/game/Entities/Player/Player.cpp',
        'claims': ['MinLevel/MaxLevel admission; QuestLevel is not an upper admission limit',
                   'class/skill/reputation predicates and exclusive-group/previous-quest semantics']},
    'TC-QUEST': {'role': 'primary-server-contract', 'revision': TC_REVISION,
        'url': f'https://github.com/TrinityCore/TrinityCore/blob/{TC_REVISION}/src/server/game/Quests/QuestDef.h',
        'claims': ['SpecialFlags bit 0x20 requires cast rather than ordinary kill credit',
                   'primary allowed DB special flags are bits 0x01 through 0x20']},
    'TC-LOADER': {'role': 'primary-server-contract', 'revision': TC_REVISION,
        'url': f'https://github.com/TrinityCore/TrinityCore/blob/{TC_REVISION}/src/server/game/Globals/ObjectMgr.cpp',
        'claims': ['invalid special flags are masked by the primary reference loader; database identity still matters']},
    'AC-QUEST': {'role': 'secondary-server-contract', 'revision': AC_REVISION,
        'url': f'https://github.com/azerothcore/azerothcore-wotlk/blob/{AC_REVISION}/src/server/game/Quests/QuestDef.h',
        'claims': ['AzerothCore bit 0x40 means NO_REP_SPILLOVER, unlike the primary source allowance']},
}


def _integer(value: Any) -> bool:
    return isinstance(value, int) and not isinstance(value, bool)


def valid_point(point: Any) -> bool:
    return isinstance(point, dict) and _integer(point.get('Map')) and point['Map'] >= 0 and all(
        isinstance(point.get(axis), (int, float)) and not isinstance(point[axis], bool) and math.isfinite(point[axis])
        for axis in ('X', 'Y', 'Z'))


def build_ledger(data: dict, issues: list[dict]) -> list[dict]:
    quests = data.get('Quests') or []
    identities = Counter(quest.get('Id') for quest in quests)
    by_id = {quest.get('Id'): quest for quest in quests}
    findings = defaultdict(list)
    for finding in issues:
        if finding.get('quest_id') is not None:
            findings[finding['quest_id']].append(finding)
    relations: dict[str, dict[int, list[dict]]] = {}
    groups = defaultdict(list)
    for quest in quests:
        group = quest.get('ExclusiveGroup', 0)
        if group: groups[group].append(quest.get('Id'))
    for section, role in [('QuestGivers', 'Giver'), ('QuestEnders', 'Ender')]:
        index = defaultdict(list)
        for position, relation in enumerate(data.get(section) or []):
            entry, kind = relation.get(role + 'Id'), relation.get(role + 'Type')
            spawn_group = {'Creature': 'CreatureSpawns', 'GameObject': 'GameObjectSpawns'}.get(kind)
            points = data.get(spawn_group, {}).get(str(entry), []) if spawn_group else []
            index[relation.get('QuestId')].append({
                'pointer': f'/{section}/{position}', 'entry': entry, 'object_type': kind,
                'name': relation.get(role + 'Name'), 'spawn_pointer': f'/{spawn_group}/{entry}' if spawn_group else None,
                'spawn_count': len(points), 'valid_spawn_count': sum(valid_point(point) for point in points),
                'maps': sorted({point['Map'] for point in points if valid_point(point)}),
                'position_authority': 'stored dataset only; a fresh typed nearby object can supply runtime geometry',
            })
        relations[section] = index

    rows = []
    for position, quest in enumerate(quests):
        ident = quest.get('Id')
        requirements, obligations, objects, local_findings = [], [], [], list(findings.get(ident, []))
        invalid = not _integer(ident) or ident <= 0 or identities[ident] != 1
        if invalid: requirements.append('invalid-or-duplicate-quest-id')
        giver, ender = relations['QuestGivers'].get(ident, []), relations['QuestEnders'].get(ident, [])
        if not giver: requirements.append('pickup-route-unrepresented')
        if not ender: requirements.append('turn-in-route-unrepresented')
        for role, relation_rows in [('giver', giver), ('ender', ender)]:
            for relation in relation_rows:
                if not _integer(relation['entry']) or relation['entry'] <= 0 or relation['object_type'] not in ('Creature', 'GameObject'):
                    invalid = True; requirements.append(f'invalid-{role}-identity-or-type')
                if relation['valid_spawn_count'] == 0:
                    requirements.append(f'{role}-static-geometry-missing-or-invalid')
        previous = quest.get('PrevQuestID', 0)
        alternate = quest.get('PreviousQuestsIds') or []
        next_id = quest.get('NextQuestID', 0)
        if not _integer(previous):
            invalid = True; requirements.append('invalid-prev-quest-id'); previous = 0
        if not isinstance(alternate, list) or any(not _integer(value) for value in alternate):
            invalid = True; requirements.append('invalid-previous-quest-list'); alternate = []
        if previous == ident or ident in alternate:
            invalid = True; requirements.append('self-prerequisite')
        if next_id and next_id not in by_id: obligations.append('next-quest-outside-dataset')
        external = sorted({abs(value) for value in [previous] + alternate if value and abs(value) not in by_id})
        if external: obligations.append('prerequisite-outside-dataset-requires-authoritative-history-or-source')
        if any(value < 0 for value in alternate): obligations.append('negative-dependent-list-entry-requires-source-review')
        flags = quest.get('SpecialFlags', 0)
        if not _integer(flags) or flags < 0:
            invalid = True; requirements.append('invalid-special-flags'); flags = 0
        if flags & 0x40: obligations.append('special-flag-64-core-dependent')
        if flags & ~0x7f: obligations.append('unknown-special-flags')
        if flags & 2: requirements.append('exploration-or-event-requires-strategy')
        objective_rows = quest.get('Objectives') or []
        if not objective_rows: requirements.append('objective-contract-unrepresented')
        for ordinal, objective in enumerate(objective_rows):
            kind = objective.get('Type')
            pointer = f'/Quests/{position}/Objectives/{ordinal}'
            object_type = 'GameObject' if kind == 'CollectFromGameObject' else 'Creature'
            entry = objective.get('GameObjectId', 0) if kind == 'CollectFromGameObject' else objective.get('MobId', 0)
            amount = objective.get('KillCount', 0) if kind == 'KillMob' else objective.get('CollectCount', 0)
            spawn_group = 'GameObjectSpawns' if object_type == 'GameObject' else 'CreatureSpawns'
            points = data.get(spawn_group, {}).get(str(entry), [])
            normal = kind in ('KillMob', 'CollectItem', 'CollectFromGameObject', 'TurnInOnly')
            if not normal: requirements.append('unimplemented-objective-kind:' + str(kind))
            elif kind != 'TurnInOnly':
                if not _integer(entry) or entry <= 0 or not _integer(amount) or amount <= 0:
                    invalid = True; requirements.append('invalid-objective-identity-or-count')
                if kind == 'CollectItem' and (not _integer(objective.get('ItemId')) or objective['ItemId'] <= 0):
                    invalid = True; requirements.append('invalid-collection-item')
                if not any(valid_point(point) for point in points): requirements.append('objective-static-geometry-missing-or-invalid')
            if kind == 'KillMob' and flags & 0x20: requirements.append('cast-credit-requires-strategy')
            if not _integer(objective.get('Index')) or objective['Index'] < 0:
                invalid = True; requirements.append('invalid-objective-index')
            objects.append({
                'pointer': pointer, 'dataset_index': objective.get('Index'), 'kind': kind,
                'target_type': object_type if kind != 'TurnInOnly' else None, 'target_entry': entry,
                'item_id': objective.get('ItemId', 0), 'required_count': amount,
                'spawn_pointer': f'/{spawn_group}/{entry}' if entry else None,
                'spawn_count': len(points), 'valid_spawn_count': sum(valid_point(point) for point in points),
                'maps': sorted({point['Map'] for point in points if valid_point(point)}),
                'client_slot_authority': 'requires-runtime-identity-mapping',
                'progress_authority': 'carried item count' if kind == 'CollectItem' or (kind == 'CollectFromGameObject' and objective.get('ItemId', 0))
                    else 'whole-quest completion flag' if kind == 'TurnInOnly' else 'matched normal objective identity/count and packed progress',
                'ordinary_action_declared': normal and not (kind == 'KillMob' and flags & 0x20),
            })
        for field in ('MinLevel', 'MaxLevel', 'RequiredSkillID', 'RequiredSkillPoints', 'RequiredMinRepFaction', 'RequiredMaxRepFaction'):
            value = quest.get(field)
            if value is not None and (not _integer(value) or value < 0):
                invalid = True; requirements.append('invalid-eligibility-field:' + field)
        classification = 'LIVE-ACCEPTANCE-REQUIRED'
        if invalid or any('geometry' in value or 'route-unrepresented' in value or 'contract-unrepresented' in value for value in requirements):
            classification = 'DATA-INVALID/INCOMPLETE'
        if any('requires-strategy' in value or value.startswith('unimplemented-objective-kind:') for value in requirements):
            classification = 'UNSUPPORTED-SCRIPTED'
        if any(value in obligations for value in ('special-flag-64-core-dependent', 'unknown-special-flags', 'negative-dependent-list-entry-requires-source-review')):
            classification = 'SOURCE-UNCERTAIN'
        if invalid: classification = 'DATA-INVALID/INCOMPLETE'
        rows.append({
            'quest_id': ident, 'name': quest.get('Name'), 'pointer': f'/Quests/{position}',
            'classification': classification, 'classification_scope': 'bot route/data and declared strategy; simulations pending',
            'live_completion_proven': False, 'server_quest_invalid_proven': False,
            'findings': local_findings,
            'eligibility': {field: {'value': quest.get(field), 'evidence': 'declared' if field in quest and quest[field] is not None else 'absent'}
                            for field in ELIGIBILITY_FIELDS},
            'level_semantics': 'MinLevel is pickup minimum; explicit MaxLevel is inclusive maximum; QuestLevel is not a pickup ceiling',
            'dependencies': {'positive_previous': previous if previous > 0 else None,
                             'active_parent': -previous if previous < 0 else None,
                             'dependent_previous_alternatives': alternate, 'next_quest_id': next_id,
                             'external_ids': external, 'exclusive_group': quest.get('ExclusiveGroup', 0),
                             'exclusive_group_members': sorted(groups.get(quest.get('ExclusiveGroup', 0), [])),
                             'negative_group_semantics': 'negative group on a prerequisite requires all its members; it is not mutual exclusion'},
            'giver_relations': giver, 'ender_relations': ender, 'objectives': objects,
            'flags': quest.get('Flags', 0), 'special_flags': flags,
            'start_item': quest.get('StartItem', 0),
            'start_item_semantics': 'provided-on-acceptance; not proof of an item-started quest or an item-use recipe',
            'execution_requirements': sorted(set(requirements)), 'source_obligations': sorted(set(obligations)),
            'strategy': {'status': 'NOT-SUPPLIED', 'recipe_ids': [], 'invented_actions': False},
            'simulation': {'status': 'NOT-RUN', 'case_ids': [], 'production_owners': []},
            'sources': ['TC-PLAYER', 'TC-QUEST'] + (['TC-LOADER', 'AC-QUEST'] if flags & 0x40 else []),
            'remaining_live_requirements': ['configured realm offer/acceptance', 'runtime objective identity/progress',
                                            'navigation at actual endpoints', 'server-confirmed reward/next scheduling'],
        })
    return rows


def apply_source_review_classification(record: dict) -> None:
    """Reconcile proof labels with evidence found after the simulation sweep.

    A supplied acceptance observation does not discharge an omitted requirement
    or an explicit reference escort/event contract. Reference-only evidence also
    does not prove the configured realm uses that contract.
    """
    prior = record['classification']
    reasons = []
    review = record.get('source_review', {})
    comparison = record.get('secondary_reference', {})
    obligations = record.setdefault('source_obligations', [])
    requirements = record.setdefault('execution_requirements', [])
    if comparison.get('unrepresented_secondary_requirements'):
        reasons.append('secondary-requirements-not-present-in-bot-dataset')
    if review.get('secondary_availability_conditions'):
        reasons.append('secondary-availability-conditions-not-evaluated-by-simulation')
    nonordinary = {'SMART_ACTION_ESCORT_START', 'SMART_ACTION_ESCORT_STOP',
                   'SMART_ACTION_CALL_AREAEXPLOREDOREVENTHAPPENS', 'SMART_ACTION_CALL_GROUPEVENTHAPPENS'}
    if any(row.get('__contract') in nonordinary for row in review.get('secondary_direct_quest_scripts', [])):
        reasons.append('secondary-nonordinary-credit-contract-needs-realm-confirmation')
        requirements.append('secondary-nonordinary-credit-requires-bound-strategy')
    obligations.extend(reasons)
    if reasons and prior in ('GENERIC-PROVEN', 'LIVE-ACCEPTANCE-REQUIRED'):
        record['classification'] = 'SOURCE-UNCERTAIN'
    simulation = record.get('simulation', {})
    if record['classification'] == 'GENERIC-PROVEN' and (
            simulation.get('pipeline_status') != 'PASS' or simulation.get('failed_cases') != 0):
        record['classification'] = 'LIVE-ACCEPTANCE-REQUIRED'
        reasons.append('generic-pipeline-not-proven')
    record['source_obligations'] = sorted(set(obligations))
    record['execution_requirements'] = sorted(set(requirements))
    record['source_review_classification'] = {'before': prior, 'after': record['classification'], 'reasons': reasons}
    record['classification_scope'] = (
        'Generic planning, profile construction and behavior completion/acknowledgement under recorded controlled observations; '
        'native interaction, combat, travel and real server completion are not proven.'
        if record['classification'] == 'GENERIC-PROVEN'
        else 'See per-row data, source and execution obligations; a simulation PASS does not override these limitations.')


def read_reference_table(path: Path) -> Iterator[dict]:
    """Read a MySQL text dump with a declared schema and strict tuple widths.

    Handles escaped quotes/backslashes, NULL, signed numbers and multiple tuples
    per line. Unrecognized scalar syntax fails instead of silently shifting the
    following columns. DROP/LOCK/other SQL statements are never executed.
    """
    columns, table = [], None
    in_schema = in_insert = in_row = quoted = escaped = token_quoted = False
    token, row = [], []
    row_line = 0
    escapes = {'0': '\0', 'n': '\n', 'r': '\r', 't': '\t', 'b': '\b', 'Z': '\x1a'}

    def scalar() -> Any:
        value = ''.join(token)
        if token_quoted: return value
        value = value.strip()
        if value.upper() == 'NULL': return None
        if re.fullmatch(r'[+-]?\d+', value): return int(value)
        if re.fullmatch(r'[+-]?(?:\d+\.\d*|\d*\.\d+)(?:[eE][+-]?\d+)?|[+-]?\d+[eE][+-]?\d+', value):
            result = float(value)
            if math.isfinite(result): return result
        raise ValueError(f'{path}:{row_line}: unrecognized SQL scalar {value[:80]!r}')

    with Path(path).open(encoding='utf-8-sig') as stream:
        for line_number, line in enumerate(stream, 1):
            if not in_insert:
                match = re.match(r'\s*CREATE TABLE `([^`]+)`\s*\(', line)
                if match:
                    if table and table != match.group(1): raise ValueError(f'{path}: multiple table schemas are not supported')
                    table = match.group(1); in_schema = True; continue
                if in_schema:
                    match = re.match(r'\s*`([^`]+)`\s+', line)
                    if match: columns.append(match.group(1))
                    if line.lstrip().startswith(')'): in_schema = False
                    continue
                match = re.match(r'\s*INSERT INTO `([^`]+)` VALUES\s*', line)
                if not match: continue
                if match.group(1) != table or not columns: raise ValueError(f'{path}:{line_number}: missing/mismatched schema')
                in_insert = True; line = line[match.end():]
            index = 0
            while index < len(line):
                char = line[index]; index += 1
                if quoted:
                    if escaped:
                        token.append(escapes.get(char, char)); escaped = False
                    elif char == '\\': escaped = True
                    elif char == "'":
                        if index < len(line) and line[index] == "'": token.append("'"); index += 1
                        else: quoted = False
                    else: token.append(char)
                    continue
                if not in_row:
                    if char == '(':
                        in_row = True; token = []; row = []; token_quoted = False; row_line = line_number
                    elif char == ';': in_insert = False
                    elif not char.isspace() and char != ',': raise ValueError(f'{path}:{line_number}: unexpected tuple syntax')
                    continue
                if char == "'":
                    if ''.join(token).strip(): raise ValueError(f'{path}:{line_number}: unexpected literal prefix')
                    token = []; quoted = token_quoted = True
                elif char in ',)':
                    row.append(scalar()); token = []; token_quoted = False
                    if char == ')':
                        if len(row) != len(columns): raise ValueError(f'{path}:{row_line}: {len(row)} values for {len(columns)} columns')
                        yield dict(zip(columns, row), __source_line=row_line, __source_table=table)
                        in_row = False
                elif not token_quoted or not char.isspace(): token.append(char)
        if quoted or escaped or in_row or in_insert: raise ValueError(f'{path}: unterminated SQL data')
