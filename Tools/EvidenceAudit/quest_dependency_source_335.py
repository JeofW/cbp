"""Pinned TC335 ObjectMgr predecessor membership; never rewrite execution order.

ObjectMgr.cpp:5328-5347 at 95657f54779467effea8a1749a61ff93abc1d707
adds positive non-breadcrumb PrevQuestID and incoming NextQuestID edges.
Player.cpp:15232-15320 evaluates direct and dependent requirements separately.
This reference graph is not proof that a customized realm uses the same edges.
"""
from __future__ import annotations
import copy


def primary_dependency_index(templates: dict, addons: dict) -> tuple[dict, dict]:
    dependencies = {ident: set() for ident in templates}
    evidence = {ident: [] for ident in templates}

    def add(parent, child, kind, row):
        dependencies[child].add(parent)
        evidence[child].append({'parent': parent, 'child': child, 'kind': kind,
                                'table_row': row['ID'], 'source_line': row.get('__source_line')})

    for ident, addon in sorted(addons.items()):
        if ident not in templates:
            continue
        parent = addon.get('PrevQuestID', 0)
        if parent > 0 and parent in templates and not addons.get(parent, {}).get('BreadcrumbForQuestId', 0):
            add(parent, ident, 'positive-direct-prev', addon)
        child = addon.get('NextQuestID', 0)
        if child > 0 and child in templates:
            add(ident, child, 'incoming-next', addon)
    return dependencies, evidence


def dependency_membership(quest: dict, dependencies: dict, evidence: dict) -> dict:
    ident = quest['Id']
    actual = list(quest.get('PreviousQuestsIds') or [])
    if any(type(parent) is not int or parent <= 0 for parent in actual):
        raise ValueError(f'Quest {ident}: dependent predecessor must be a positive integer')
    expected = dependencies.get(ident, set())
    return {'quest_id': ident, 'recorded_previous_ids': actual, 'primary_dependent_ids': sorted(expected),
            'missing_primary_dependencies': sorted(expected - set(actual)),
            'unconfirmed_model_dependencies': sorted(set(actual) - expected),
            'matches': ident in dependencies and set(actual) == expected,
            'edges': copy.deepcopy(evidence.get(ident, [])),
            'ordering_proven': False, 'runtime_relations_changed': False}
