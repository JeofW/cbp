"""Only a unique source-backed chest may replace a proved namespace mismatch."""
import copy
import importlib.util
from pathlib import Path
import unittest

import test_chest_acquisition_335 as fixtures


class QuestObjectIdentityExporterTests(unittest.TestCase):
    def fixture(self):
        data, tables = fixtures.ChestAcquisitionExportTests().fixture()
        # Imported object 501 is the loot selector; actual type-3 object is 401.
        data['Quests'][0]['Objectives'][0]['GameObjectId'] = 501
        tables['gameobject_template'].append({'entry': 501, 'name': 'Wrong sign', 'type': 5,
                                              'Data1': 0, '__source_line': 90})
        for row in tables['gameobject_template']:
            row.update(AIName='', ScriptName='', Data6=0, Data7=0)
        for row in tables['gameobject_loot_template']:
            row.update(LootMode=1, QuestRequired=1)
        return data, tables

    def derive(self, data, tables, previous=None):
        path = Path(__file__).with_name('quest_object_identity_335.py')
        self.assertTrue(path.is_file(), 'Identity corrections need a source-bound exporter')
        spec = importlib.util.spec_from_file_location('object_identity_under_test', path)
        owner = importlib.util.module_from_spec(spec); spec.loader.exec_module(owner)
        return owner.derive(data, tables, previous or {}, {
            1: {'name': 'CONDITION_SOURCE_TYPE_CREATURE_LOOT_TEMPLATE'},
            4: {'name': 'CONDITION_SOURCE_TYPE_GAMEOBJECT_LOOT_TEMPLATE'},
            10: {'name': 'CONDITION_SOURCE_TYPE_REFERENCE_LOOT_TEMPLATE'}},
            {'CoreRevision': '1' * 40, 'DatabaseRevision': 'fixture', 'SourceSqlSha256': '2' * 64})

    def test_unique_chest_loot_selector_repairs_exact_expected_objective(self):
        data, tables = self.fixture(); before = copy.deepcopy((data, tables))
        patch, review = self.derive(data, tables)
        self.assertEqual(len(patch['GameObjectObjectiveRepairs']), 1)
        row = patch['GameObjectObjectiveRepairs'][0]
        self.assertEqual((row['QuestId'], row['ExpectedGameObjectId'], row['GameObjectId'], row['ItemId'], row['RequiredCount']),
                         (101, 501, 401, 301, 2))
        self.assertEqual(patch['SpawnAdditions'][0]['Entry'], 401)
        self.assertEqual((data, tables), before)
        self.assertFalse(review['live_completion_proven'])

    def test_two_matching_templates_remain_ambiguous_even_with_one_spawned(self):
        data, tables = self.fixture()
        tables['gameobject_template'].append(dict(tables['gameobject_template'][0], entry=402))
        self.assertEqual(self.derive(data, tables)[0]['GameObjectObjectiveRepairs'], [])

    def test_valid_existing_actor_cannot_be_replaced(self):
        data, tables = self.fixture()
        tables['gameobject_template'][1].update(type=3, Data1=501)
        self.assertEqual(self.derive(data, tables)[0]['GameObjectObjectiveRepairs'], [])

    def test_wrong_item_or_count_does_not_authorize_identity_repair(self):
        for mode in ('item', 'count'):
            data, tables = self.fixture()
            if mode == 'item': tables['gameobject_loot_template'][0]['Item'] = 302
            else: data['Quests'][0]['Objectives'][0]['CollectCount'] = 1
            self.assertEqual(self.derive(data, tables)[0]['GameObjectObjectiveRepairs'], [])

    def test_script_event_trap_and_conditional_loot_remain_unresolved(self):
        for field in ('AIName', 'ScriptName', 'Data6', 'Data7', 'condition'):
            data, tables = self.fixture()
            if field == 'condition': tables['conditions'] = [{'SourceTypeOrReferenceId': 4, 'SourceGroup': 501,
                'SourceEntry': 301, 'ConditionTypeOrReference': -1}]
            else: tables['gameobject_template'][0][field] = 'script' if field.endswith('Name') else 1
            self.assertEqual(self.derive(data, tables)[0]['GameObjectObjectiveRepairs'], [])

    def test_source_conflicting_or_scripted_quest_is_preserved(self):
        for mode in ('field', 'script'):
            data, tables = self.fixture()
            if mode == 'field': tables['quest_template'][0]['AllowableRaces'] = 690
            else:
                data['Quests'][0]['SpecialFlags'] = 32
                tables['quest_template_addon'][0]['SpecialFlags'] = 32
            self.assertEqual(self.derive(data, tables)[0]['GameObjectObjectiveRepairs'], [])

    def test_event_phase_and_pool_spawns_cannot_supply_ordinary_geometry(self):
        for mode in ('event', 'phase', 'pool'):
            data, tables = self.fixture()
            if mode == 'event': tables['game_event_gameobject'] = [{'guid': 77}]
            if mode == 'phase': tables['gameobject'][0]['phaseMask'] = 2
            if mode == 'pool': tables['pool_members'] = [{'type': 1, 'spawnId': 77}]
            self.assertEqual(self.derive(data, tables)[0]['GameObjectObjectiveRepairs'], [])

    def test_existing_geometry_veto_is_preserved(self):
        data, tables = self.fixture()
        data['GameObjectSpawns']['401'] = [{'Map': 0, 'X': 1, 'Y': 2, 'Z': 3, 'IsKnownReachable': False}]
        self.assertEqual(self.derive(data, tables)[0]['GameObjectObjectiveRepairs'], [])

    def test_existing_positive_geometry_is_retained_without_duplicate_addition(self):
        data, tables = self.fixture()
        data['GameObjectSpawns']['401'] = [{'Map': 0, 'X': 1, 'Y': 2, 'Z': 3}]
        patch, _ = self.derive(data, tables)
        self.assertEqual(len(patch['GameObjectObjectiveRepairs']), 1)
        self.assertEqual(patch['SpawnAdditions'], [])

    def test_count_patch_for_the_same_row_is_not_combined_implicitly(self):
        data, tables = self.fixture()
        previous = {'ObjectiveCountRepairs': [{'QuestId': 101, 'RowIndex': 0}]}
        self.assertEqual(self.derive(data, tables, previous)[0]['GameObjectObjectiveRepairs'], [])


if __name__ == '__main__':
    unittest.main()
