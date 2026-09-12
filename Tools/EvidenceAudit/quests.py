"""Read-only data-integrity checks. Missing data never implies a quest is impossible."""
from collections import Counter, defaultdict
import math

SUPPORTED = {'KillMob', 'CollectItem', 'CollectFromGameObject', 'TurnInOnly'}


def audit_database(data):
    issues, ids, kinds = [], defaultdict(list), Counter()
    def issue(code, classification, pointers, quest_id=None, **details):
        issues.append(dict(code=code, classification=classification, pointers=pointers,
                           quest_id=quest_id, **details))
    quests = data.get('Quests', [])
    for n, quest in enumerate(quests):
        ids[quest.get('Id')].append(f'/Quests/{n}/Id')
    for ident, pointers in ids.items():
        if not isinstance(ident, int) or isinstance(ident, bool) or ident <= 0:
            issue('invalid_quest_id', 'invalid_data', pointers, ident)
        if len(pointers) > 1:
            issue('duplicate_quest_id', 'invalid_data', pointers, ident)
    relations = {}
    for section, role in [('QuestGivers', 'Giver'), ('QuestEnders', 'Ender')]:
        present = set()
        for n, relation in enumerate(data.get(section, [])):
            ident = relation.get('QuestId'); present.add(ident)
            ptr = f'/{section}/{n}'
            if ident not in ids:
                issue('external_relation_quest', 'data_gap', [ptr], ident)
            typ, entry = relation.get(role + 'Type'), relation.get(role + 'Id')
            group = {'Creature': 'CreatureSpawns', 'GameObject': 'GameObjectSpawns'}.get(typ)
            if group is None:
                issue('unsupported_relation_type', 'needs_contract_review', [ptr], ident, value=typ)
            elif not data.get(group, {}).get(str(entry)):
                issue('missing_relation_spawn', 'data_gap', [ptr], ident, entry=entry, group=group)
        relations[section] = present
    for n, quest in enumerate(quests):
        ident, prefix = quest.get('Id'), f'/Quests/{n}'
        for section in ('QuestGivers', 'QuestEnders'):
            if ident not in relations[section]:
                issue('missing_' + section.lower(), 'data_gap', [prefix], ident)
        dependencies = list(quest.get('PreviousQuestsIds') or [])
        prev = quest.get('PrevQuestID', 0)
        if isinstance(prev, int) and prev > 0:
            dependencies.append(prev)
        for predecessor in sorted(set(dependencies)):
            if predecessor == ident:
                issue('self_prerequisite', 'needs_contract_review', [prefix], ident)
            elif predecessor > 0 and predecessor not in ids:
                issue('external_prerequisite', 'data_gap', [prefix], ident, predecessor=predecessor)
        objectives = quest.get('Objectives') or []
        if not objectives:
            issue('no_objectives', 'needs_contract_review', [prefix + '/Objectives'], ident)
        indices = defaultdict(list)
        for index, objective in enumerate(objectives):
            ptr = f'{prefix}/Objectives/{index}'
            typ = objective.get('Type'); kinds[str(typ)] += 1
            indices[objective.get('Index')].append(ptr)
            if typ not in SUPPORTED:
                issue('unsupported_objective_type', 'needs_contract_review', [ptr], ident, value=typ)
                continue
            required = {'KillMob': ('MobId', 'KillCount'), 'CollectItem': ('MobId', 'ItemId', 'CollectCount'),
                        'CollectFromGameObject': ('GameObjectId', 'CollectCount'), 'TurnInOnly': ()}[typ]
            for field in required:
                value = objective.get(field, 0)
                if not isinstance(value, (int, float)) or not math.isfinite(value) or value <= 0:
                    issue('missing_objective_requirement', 'needs_contract_review', [ptr + '/' + field], ident, field=field)
            entry, group = (objective.get('GameObjectId'), 'GameObjectSpawns') if typ == 'CollectFromGameObject' else (objective.get('MobId'), 'CreatureSpawns')
            if typ != 'TurnInOnly' and entry and not data.get(group, {}).get(str(entry)):
                issue('missing_objective_spawn', 'data_gap', [ptr], ident, entry=entry, group=group)
        for index, pointers in indices.items():
            if len(pointers) > 1:
                issue('shared_objective_index', 'needs_contract_review', pointers, ident, index=index)
    spawn_count = 0
    for group in ('CreatureSpawns', 'GameObjectSpawns'):
        for entry, points in data.get(group, {}).items():
            for n, point in enumerate(points):
                spawn_count += 1
                ptr = f'/{group}/{entry}/{n}'
                coordinates = [point.get(k) for k in ('X', 'Y', 'Z')]
                if not all(isinstance(v, (int, float)) and not isinstance(v, bool) and math.isfinite(v) for v in coordinates):
                    issue('nonfinite_spawn', 'invalid_data', [ptr])
                if not isinstance(point.get('Map'), int) or point['Map'] < 0:
                    issue('invalid_spawn_map', 'invalid_data', [ptr])
    return dict(coverage='static data only; not live quest completion', quest_count=len(quests),
                unique_quest_ids=len(ids), spawn_count=spawn_count, objective_types=dict(kinds),
                issue_counts=dict(Counter(i['code'] for i in issues)), issues=issues)


def compare_zones(canonical, zones):
    by_id = {quest['Id']: quest for quest in canonical.get('Quests', [])}
    differences, references = [], 0
    for path, zone in sorted(zones.items()):
        for index, quest in enumerate(zone.get('Quests', [])):
            references += 1
            ident = quest.get('Id')
            if by_id.get(ident) != quest:
                differences.append(dict(path=path, pointer=f'/Quests/{index}', quest_id=ident,
                                        kind='missing_from_global' if ident not in by_id else 'record_differs'))
    return dict(zone_files=len(zones), quest_references=references, differences=differences)
