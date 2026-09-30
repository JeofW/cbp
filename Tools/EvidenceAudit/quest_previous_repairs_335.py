"""Fill only empty, complete, source-consistent dependent predecessor sets.

Pinned TC335 ObjectMgr constructs these alternatives from positive direct Prev
and incoming Next edges. Only nonnegative predecessor groups are supported here:
their OR result is order independent. Existing lists, negative-group order and
source conflicts remain untouched. These records never supply player history.
"""
from collections import deque
from quest_dependency_source_335 import primary_dependency_index
from quest_repair_pack_335 import BASE_FIELDS, ADDON_FIELDS, indexed, digest


def derive(data, tables, source):
    quests = indexed(data['Quests'], 'Id')
    templates = indexed(tables['quest_template'], 'ID')
    addons = indexed(tables['quest_template_addon'], 'ID')
    dependencies, edges = primary_dependency_index(templates, addons)
    metadata = data.get('DependencyMetadata') or {}
    prefix = 'tc335:' + source['CoreRevision'] + ':' + source['DatabaseRevision'] + ':' + source['SourceSqlSha256']
    if len(source['CoreRevision']) != 40 or len(source['SourceSqlSha256']) != 64:
        raise ValueError('Predecessor repair requires pinned source identity')
    patches, records = [], []

    def evidence(table, row):
        return {'table': table, 'key': row['ID'], 'source_line': row.get('__source_line'), 'row_sha256': digest(row)}

    def cyclic_or_bounded(ident, parents):
        pending = deque(parents); seen = set()
        while pending:
            parent = pending.popleft()
            if parent == ident or len(seen) > 4096: return True
            if parent in seen: continue
            seen.add(parent); pending.extend(dependencies.get(parent, ()))
            direct = addons.get(parent, {}).get('PrevQuestID', 0)
            if direct < 0: pending.append(-direct)
        return False

    for ident, quest in sorted(quests.items()):
        actual = quest.get('PreviousQuestsIds')
        expected = sorted(dependencies.get(ident, set()))
        if actual == expected: continue
        record = {'quest_id': ident, 'expected_primary_ids': expected, 'recorded_ids': actual,
                  'disposition': 'unresolved', 'live_completion_proven': False}
        records.append(record)
        template = templates.get(ident); addon = addons.get(ident, {})
        if template is None:
            record['reason'] = 'subject-primary-template-absent'; continue
        if not isinstance(actual, list) or actual:
            record['reason'] = 'existing-list-or-invalid-list-must-be-preserved'; continue
        if not expected or len(expected) > 256:
            record['reason'] = 'empty-or-oversized-primary-membership'; continue
        conflicts = [field for field in BASE_FIELDS if field in template and quest.get(field) != template[field]]
        conflicts += [field for field in ADDON_FIELDS if quest.get(field) != addon.get(field, 0)]
        if conflicts or addon.get('BreadcrumbForQuestId', 0):
            record['reason'] = 'subject-source-conflict-or-breadcrumb'; record['conflicts'] = conflicts; continue
        references = []
        for parent in expected:
            group = addons.get(parent, {}).get('ExclusiveGroup', 0)
            modeled = quests.get(parent) or metadata.get(str(parent)) or metadata.get(parent)
            if (parent == ident or type(parent) is not int or parent <= 0 or type(group) is not int
                    or group < 0 or group > 2**31-1 or modeled is None
                    or type(modeled.get('ExclusiveGroup')) is not int or modeled['ExclusiveGroup'] != group
                    or parent not in quests and (modeled.get('QuestId') != parent or not modeled.get('SourceRef'))):
                break
            references.append({'QuestId': parent, 'ExclusiveGroup': group,
                               'SourceRef': prefix + ':quest_template+addon:' + str(parent)})
        if len(references) != len(expected):
            record['reason'] = 'incomplete-unknown-negative-or-conflicting-reference-group'; continue
        if cyclic_or_bounded(ident, expected):
            record['reason'] = 'cyclic-or-bounded-source-dependency'; continue
        patch = {'QuestId': ident, 'ExpectedPrevQuestId': quest['PrevQuestID'],
                 'ExpectedPreviousQuestIds': [], 'PreviousQuestIds': expected,
                 'ReferencedQuestGroups': references, 'SourceRef': prefix + ':dependent_previous:' + str(ident)}
        patches.append(patch)
        record.update(disposition='complete-empty-predecessor-repair', patch=patch,
            subject_template=evidence('quest_template',template),
            source_edges=[dict(edge, source=evidence('quest_template_addon',addons[edge['table_row']])) for edge in edges[ident]],
            reference_groups=[{'quest_id':parent,'template':evidence('quest_template',templates[parent]),
                'addon':evidence('quest_template_addon',addons[parent]) if parent in addons else {'absent':True}} for parent in expected])
    return patches, {'schema':'complete-dependent-previous-repair-335-v1', 'source':dict(source),
        'rows':records, 'live_completion_proven':False,
        'limit':'Only empty complete nonnegative-group alternative sets are filled; existing lists and all player observations remain unchanged.'}
