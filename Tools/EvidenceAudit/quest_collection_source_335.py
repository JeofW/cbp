"""Bind modeled ordinary collection owners to the pinned primary loot graph.

This is an audit owner, not a drop simulator or a production observation source.
A reference path demonstrates a possible ordinary item source, never a guaranteed
drop, live phase/path availability, successful interaction, or realm equivalence.
"""
from __future__ import annotations

from collections import defaultdict
import hashlib
import json
import math


def row_evidence(table, row, key):
    raw = json.dumps(row, sort_keys=True, separators=(',', ':'), ensure_ascii=True,
                     allow_nan=False).encode()
    return {'table': table, 'key': {key: row[key]}, 'source_line': row.get('__source_line'),
            'row_sha256': hashlib.sha256(raw).hexdigest()}


def unique_index(rows, key):
    result = {}
    for row in rows:
        if row[key] in result:
            raise ValueError('Duplicate primary collection identity: ' + str(row[key]))
        result[row[key]] = row
    return result


def usable_geometry(points):
    return any(isinstance(point, dict) and type(point.get('Map')) is int and point['Map'] >= 0
               and point.get('IsKnownReachable') is not False and point.get('IsKnownSafe') is not False
               and all(type(point.get(axis)) in (int, float) and math.isfinite(point[axis])
                       for axis in ('X', 'Y', 'Z')) for point in points)


class CollectionSourceIndex:
    """Index exact actor namespaces and ordinary default-mode loot possibilities."""

    def __init__(self, tables, condition_codes):
        required = ('creature_template', 'gameobject_template', 'item_template',
                    'creature_loot_template', 'gameobject_loot_template',
                    'reference_loot_template', 'conditions')
        if any(name not in tables for name in required):
            raise ValueError('Primary collection source tables are incomplete')
        self.actors = {'Creature': unique_index(tables['creature_template'], 'entry'),
                       'GameObject': unique_index(tables['gameobject_template'], 'entry')}
        self.items = unique_index(tables['item_template'], 'entry')
        self.loots = {}
        self.conditions = defaultdict(list)
        self.source_types = {}
        for table in ('creature_loot_template', 'gameobject_loot_template', 'reference_loot_template'):
            name = 'CONDITION_SOURCE_TYPE_' + table.upper()
            matches = [value for value, record in condition_codes.items() if record['name'] == name]
            if len(matches) != 1:
                raise ValueError('Missing or ambiguous primary condition namespace: ' + name)
            self.source_types[table] = matches[0]
            index = defaultdict(list)
            for row in tables[table]:
                index[row['Entry']].append(row)
            self.loots[table] = index
        for row in tables['conditions']:
            self.conditions[(row['SourceTypeOrReferenceId'], row.get('SourceGroup'), row['SourceEntry'])].append(row)
        self.cache = {}

    def _valid_row(self, row, selected_group):
        integer_fields = ('Item', 'Reference', 'LootMode', 'GroupId', 'MinCount', 'MaxCount')
        if any(type(row.get(field)) is not int for field in integer_fields):
            return False
        chance = row.get('Chance')
        if (type(chance) not in (int, float) or not math.isfinite(chance)
                or chance < 0 or (0 < chance < 0.000001) or not 0 <= row['GroupId'] < 128
                or row['Reference'] < 0 or row['MinCount'] <= 0 or row['MaxCount'] <= 0
                or not row['LootMode'] & 1):
            return False
        if selected_group and (row['Reference'] or row['GroupId'] != selected_group):
            return False
        if row['Reference']:
            return chance > 0
        return (row['Item'] in self.items and row['MaxCount'] >= row['MinCount']
                and (chance > 0 or row['GroupId'] > 0))

    def loot_paths(self, table, entry, item):
        """Retain path/group/mode evidence; bounded searches never imply absence."""
        key = (table, entry, item)
        if key in self.cache:
            return self.cache[key]
        paths = []
        budget = [10000]
        bounded = [False]

        def walk(current_table, current_entry, group, ancestors, chain):
            identity = (current_table, current_entry, group)
            if identity in ancestors:
                return
            if len(ancestors) >= 32:
                bounded[0] = True
                return
            for row in self.loots[current_table].get(current_entry, []):
                budget[0] -= 1
                if budget[0] < 0 or len(paths) >= 64:
                    bounded[0] = True
                    return
                if not self._valid_row(row, group):
                    continue
                step = {'source': row_evidence(current_table, row, 'Entry'),
                        'item': row['Item'], 'reference': row['Reference'], 'group': row['GroupId'],
                        'selected_group': group, 'loot_mode': row['LootMode'], 'chance': row['Chance'],
                        'min_count': row['MinCount'], 'max_count': row['MaxCount'],
                        'quest_required': row.get('QuestRequired', 0),
                        'conditions': [{'source': row_evidence('conditions', condition, 'SourceEntry'),
                                        'row': condition} for condition in self.conditions[
                            (self.source_types[current_table], current_entry, row['Item'])]]}
                following = chain + [step]
                if row['Reference']:
                    # A reference's GroupId selects a group inside the referenced
                    # template. It is not permission to search every child group.
                    walk('reference_loot_template', row['Reference'], row['GroupId'],
                         ancestors | {identity}, following)
                elif row['Item'] == item:
                    paths.append(following)

        walk(table, entry, 0, frozenset(), [])
        result = {'paths': paths, 'search_bounded': bounded[0]}
        self.cache[key] = result
        return result

    def review(self, quest, effective, primary_required):
        rows = []
        obligations = []
        by_item = defaultdict(list)
        for index, objective in enumerate(quest['Objectives']):
            if objective['Type'] not in ('CollectItem', 'CollectFromGameObject'):
                continue
            item = objective.get('ItemId', 0)
            kind = 'GameObject' if objective['Type'] == 'CollectFromGameObject' else 'Creature'
            entry = objective.get('GameObjectId', 0) if kind == 'GameObject' else objective.get('MobId', 0)
            actor = self.actors[kind].get(entry)
            actor_table = 'gameobject_template' if kind == 'GameObject' else 'creature_template'
            loot_table = 'gameobject_loot_template' if kind == 'GameObject' else 'creature_loot_template'
            loot_id = actor.get('lootid', 0) if actor and kind == 'Creature' else (
                actor.get('Data1', 0) if actor and actor.get('type') == 3 else 0)
            valid_count = type(objective.get('CollectCount')) is int and objective['CollectCount'] > 0
            agrees = valid_count and primary_required.get(item) == objective['CollectCount']
            path_result = self.loot_paths(loot_table, loot_id, item) if (
                agrees and item in self.items and type(loot_id) is int and loot_id > 0) else {'paths': [], 'search_bounded': False}
            unconditional = [path for path in path_result['paths'] if all(not step['conditions'] for step in path)]
            disposition = ('ORDINARY-LOOT-PATH' if unconditional else
                           'CONDITIONAL-LOOT-PATH' if path_result['paths'] else 'UNPROVEN')
            record = {'objective_row': index, 'object_type': kind, 'entry': entry, 'item_id': item,
                      'count': objective.get('CollectCount'), 'primary_count': primary_required.get(item),
                      'count_matches': agrees, 'actor_name': actor.get('name') if actor else None,
                      'actor_type': actor.get('type') if actor else None,
                      'actor_source': row_evidence(actor_table, actor, 'entry') if actor else None,
                      'item_source': row_evidence('item_template', self.items[item], 'entry') if item in self.items else None,
                      'loot_selector': {'table': loot_table, 'entry': loot_id, 'mode': 1},
                      'loot_paths': path_result['paths'], 'search_bounded': path_result['search_bounded'],
                      'has_geometry': usable_geometry(effective.get(kind + 'Spawns', {}).get(str(entry), [])),
                      'disposition': disposition, 'live_availability_proven': False}
            rows.append(record)
            if agrees:
                by_item[item].append(record)
                # The runtime may select any modeled alternative. A valid other
                # actor does not certify or disable this unproven candidate.
                if disposition == 'UNPROVEN':
                    obligations.append('source:collection-objective-owner-unproven:' + str(index))
                elif disposition == 'CONDITIONAL-LOOT-PATH':
                    obligations.append('source:collection-loot-condition-unmodeled:' + str(index))
        for item, candidates in by_item.items():
            bound = [row for row in candidates if row['disposition'] == 'ORDINARY-LOOT-PATH']
            if bound and not any(row['has_geometry'] for row in bound):
                obligations.append('data:collection-source-geometry-missing:' + str(item))
        return {'objective_sources': rows, 'obligations': sorted(set(obligations)),
                'authority': 'pinned-primary-ordinary-acquisition-correlation',
                'live_completion_proven': False,
                'limit': 'A possible default-mode loot path is not drop probability, configured realm equivalence, travel, lock access, phase availability or inventory/progress acknowledgement.'}
