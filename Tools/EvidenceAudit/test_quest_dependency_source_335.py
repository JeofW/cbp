"""Source dependency membership and protected metadata must have separate owners."""
import copy
import importlib.util
from pathlib import Path
import unittest

import test_quest_repair_pack_335 as repair_fixtures


class PrimaryDependencySourceTests(unittest.TestCase):
    def owner(self):
        path = Path(__file__).with_name('quest_dependency_source_335.py')
        self.assertTrue(path.is_file(), 'Dependent predecessor membership needs an exact primary-source owner')
        spec = importlib.util.spec_from_file_location('dependency_source_under_test', path)
        module = importlib.util.module_from_spec(spec)
        spec.loader.exec_module(module)
        return module

    def graph(self, addons, ids=(101, 102, 103)):
        templates = {ident: {'ID': ident} for ident in ids}
        return self.owner().primary_dependency_index(templates, {row['ID']: row for row in addons})

    def test_positive_direct_previous_is_a_dependent_predecessor(self):
        index, _ = self.graph([{'ID': 101, 'PrevQuestID': 102}])
        self.assertEqual(index[101], {102})

    def test_negative_direct_previous_is_active_requirement_not_rewarded_alternative(self):
        index, _ = self.graph([{'ID': 101, 'PrevQuestID': -102}])
        self.assertEqual(index[101], set())

    def test_external_incoming_next_is_a_dependent_predecessor(self):
        index, evidence = self.graph([{'ID': 102, 'NextQuestID': 101, '__source_line': 12}])
        self.assertEqual(index[101], {102})
        self.assertEqual(evidence[101][0]['table_row'], 102)
        self.assertEqual(evidence[101][0]['source_line'], 12)

    def test_direct_and_incoming_predecessors_are_both_retained(self):
        index, _ = self.graph([{'ID': 101, 'PrevQuestID': 102}, {'ID': 103, 'NextQuestID': 101}])
        self.assertEqual(index[101], {102, 103})

    def test_two_source_edges_for_one_parent_do_not_duplicate_membership(self):
        index, evidence = self.graph([{'ID': 101, 'PrevQuestID': 102}, {'ID': 102, 'NextQuestID': 101}])
        self.assertEqual(index[101], {102})
        self.assertEqual(len(evidence[101]), 2)

    def test_missing_parent_template_cannot_donate_a_prev_edge(self):
        index, _ = self.graph([{'ID': 101, 'PrevQuestID': 999}])
        self.assertEqual(index[101], set())

    def test_orphan_addon_cannot_donate_a_next_edge(self):
        index, _ = self.graph([{'ID': 999, 'NextQuestID': 101}])
        self.assertEqual(index[101], set())

    def test_missing_next_target_does_not_create_a_phantom_quest(self):
        index, _ = self.graph([{'ID': 102, 'NextQuestID': 999}])
        self.assertNotIn(999, index)

    def test_breadcrumb_parent_cannot_satisfy_positive_direct_previous(self):
        index, _ = self.graph([{'ID': 101, 'PrevQuestID': 102}, {'ID': 102, 'BreadcrumbForQuestId': 103}])
        self.assertEqual(index[101], set())

    def test_negative_next_is_not_an_invented_positive_edge(self):
        index, _ = self.graph([{'ID': 102, 'NextQuestID': -101}])
        self.assertEqual(index[101], set())

    def test_membership_disagreement_records_both_missing_and_unconfirmed_parents(self):
        index, evidence = self.graph([{'ID': 102, 'NextQuestID': 101}])
        review = self.owner().dependency_membership({'Id': 101, 'PreviousQuestsIds': [103]}, index, evidence)
        self.assertFalse(review['matches'])
        self.assertEqual(review['missing_primary_dependencies'], [102])
        self.assertEqual(review['unconfirmed_model_dependencies'], [103])

    def test_membership_review_does_not_reorder_the_actual_execution_list(self):
        index, evidence = self.graph([{'ID': 102, 'NextQuestID': 101}, {'ID': 103, 'NextQuestID': 101}])
        quest = {'Id': 101, 'PreviousQuestsIds': [103, 102]}
        before = copy.deepcopy(quest)
        review = self.owner().dependency_membership(quest, index, evidence)
        self.assertTrue(review['matches'])
        self.assertEqual(review['recorded_previous_ids'], [103, 102])
        self.assertEqual(quest, before)

    def test_boolean_model_parent_is_not_quest_one(self):
        index, evidence = self.graph([])
        with self.assertRaisesRegex(ValueError, 'positive integer'):
            self.owner().dependency_membership({'Id': 101, 'PreviousQuestsIds': [True]}, index, evidence)

    def test_mutating_review_cannot_modify_the_shared_primary_graph(self):
        index, evidence = self.graph([{'ID': 102, 'NextQuestID': 101}])
        review = self.owner().dependency_membership({'Id': 101, 'PreviousQuestsIds': [102]}, index, evidence)
        review['edges'][0]['parent'] = 999
        self.assertEqual(evidence[101][0]['parent'], 102)


class ProtectedDependencyExportTests(unittest.TestCase):
    def fixture(self):
        fixture = repair_fixtures.PrimaryRepairExporterTests()
        data, tables = fixture.fixture()
        data['Quests'][0]['PreviousQuestsIds'] = [102]
        template = copy.deepcopy(tables['quest_template'][0]); template['ID'] = 102
        addon = copy.deepcopy(tables['quest_template_addon'][0]); addon.update(ID=102, NextQuestID=101)
        tables['quest_template'].append(template)
        tables['quest_template_addon'].append(addon)
        return fixture, data, tables

    def test_protected_content_still_receives_source_bound_external_metadata(self):
        fixture, data, tables = self.fixture(); before = copy.deepcopy(data)
        pack, _ = fixture.derive(data, tables, protected={101})
        self.assertEqual([row['QuestId'] for row in pack['DependencyMetadata']], [102])
        self.assertTrue(pack['DependencyMetadata'][0]['SourceRef'])
        self.assertEqual(pack['QuestMetadata'], [])
        self.assertEqual(pack['SpawnAdditions'], [])
        self.assertEqual(pack['RelationAdditions'], [])
        self.assertEqual(data, before)

    def test_negative_group_metadata_contains_every_required_member(self):
        fixture, data, tables = self.fixture()
        tables['quest_template_addon'][1]['ExclusiveGroup'] = -102
        template = copy.deepcopy(tables['quest_template'][1]); template['ID'] = 103
        addon = copy.deepcopy(tables['quest_template_addon'][1]); addon.update(ID=103, NextQuestID=0)
        tables['quest_template'].append(template); tables['quest_template_addon'].append(addon)
        pack, _ = fixture.derive(data, tables, protected={101})
        self.assertEqual([row['QuestId'] for row in pack['DependencyMetadata']], [102, 103])
        self.assertTrue(all(row['GroupMembers'] == [102, 103] for row in pack['DependencyMetadata']))

    def test_conflicting_base_negative_group_is_rejected(self):
        fixture, data, tables = self.fixture()
        tables['quest_template_addon'][1]['ExclusiveGroup'] = -102
        sibling = copy.deepcopy(data['Quests'][0]); sibling.update(Id=103, ExclusiveGroup=0, PreviousQuestsIds=[])
        data['Quests'].append(sibling)
        template = copy.deepcopy(tables['quest_template'][1]); template['ID'] = 103
        addon = copy.deepcopy(tables['quest_template_addon'][1]); addon.update(ID=103, NextQuestID=0)
        tables['quest_template'].append(template); tables['quest_template_addon'].append(addon)
        pack, _ = fixture.derive(data, tables, protected={101, 103})
        self.assertEqual(pack['DependencyMetadata'], [])

    def test_protected_positive_exclusive_peer_is_metadata_only(self):
        fixture, data, tables = self.fixture()
        data['Quests'][0].update(ExclusiveGroup=77, PreviousQuestsIds=[])
        tables['quest_template_addon'][0]['ExclusiveGroup'] = 77
        tables['quest_template_addon'][1].update(ExclusiveGroup=77, NextQuestID=0)
        pack, _ = fixture.derive(data, tables, protected={101})
        self.assertEqual([row['QuestId'] for row in pack['DependencyMetadata']], [102])
        self.assertEqual(pack['DependencyMetadata'][0]['ExclusiveGroup'], 77)
        self.assertEqual(len(data['Quests']), 1)

    def test_protected_conflicting_primary_fields_do_not_authorize_metadata(self):
        fixture, data, tables = self.fixture()
        tables['quest_template'][0]['AllowableRaces'] = 690
        pack, review = fixture.derive(data, tables, protected={101})
        self.assertEqual(pack['DependencyMetadata'], [])
        self.assertIn('primary-field-conflict', review['quests'][0]['remaining'])

    def test_unconfirmed_protected_predecessor_edge_cannot_be_promoted(self):
        fixture, data, tables = self.fixture()
        tables['quest_template_addon'][1]['NextQuestID'] = 0
        pack, review = fixture.derive(data, tables, protected={101})
        self.assertEqual(pack['DependencyMetadata'], [])
        self.assertIn('dependent-previous-membership-mismatch', review['quests'][0]['remaining'])


if __name__ == '__main__':
    unittest.main()
