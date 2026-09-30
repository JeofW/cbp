"""Require complete, source-matched condition groups and permanent quest history."""
import copy
import importlib.util
from pathlib import Path
import unittest
import test_quest_repair_pack_335 as fixtures


class QuestAvailabilitySourceTests(unittest.TestCase):
    def fixture(self):
        data, tables = fixtures.PrimaryRepairExporterTests().fixture()
        tables['quest_template'].append(dict(tables['quest_template'][0], ID=201, LogTitle='Condition reference'))
        tables['quest_template_addon'].append(dict(tables['quest_template_addon'][0], ID=201))
        row = {'SourceTypeOrReferenceId': 19, 'SourceGroup': 0, 'SourceEntry': 101, 'SourceId': 0,
               'ElseGroup': 0, 'ConditionTypeOrReference': 8, 'ConditionTarget': 0,
               'ConditionValue1': 201, 'ConditionValue2': 0, 'ConditionValue3': 0,
               'NegativeCondition': 0, 'ScriptName': '', 'ErrorType': 0, 'ErrorTextId': 0, '__source_line': 30}
        tables['conditions'] = [row]
        return data, tables, row

    def run_export(self, data, tables):
        path = Path(__file__).with_name('quest_availability_source_335.py')
        self.assertTrue(path.exists(), 'A complete source-bound availability exporter is required')
        spec = importlib.util.spec_from_file_location('availability_source_test', path)
        module = importlib.util.module_from_spec(spec); spec.loader.exec_module(module)
        return module.build_contracts(data, tables, {'CoreRevision': '1' * 40,
            'DatabaseRevision': 'controlled-reference', 'SourceSqlSha256': '2' * 64})

    def test_whole_reward_contract_retains_subject_reference_and_provenance(self):
        data, tables, _ = self.fixture(); before = copy.deepcopy((data, tables))
        contracts, review = self.run_export(data, tables)
        self.assertEqual([r['QuestId'] for r in contracts], [101])
        self.assertEqual(contracts[0]['ReferencedQuests'][0]['QuestId'], 201)
        self.assertEqual(contracts[0]['Groups'][0]['Conditions'][0]['Type'], 8)
        self.assertTrue(contracts[0]['Groups'][0]['Conditions'][0]['SourceRef'])
        self.assertTrue(review[0]['supported'])
        self.assertEqual((data, tables), before)

    def test_group_ids_and_negation_survive_without_flattening(self):
        data, tables, row = self.fixture()
        tables['conditions'].append(dict(row, ElseGroup=3, ConditionTypeOrReference=28, NegativeCondition=1))
        contracts, _ = self.run_export(data, tables)
        self.assertEqual([g['ElseGroup'] for g in contracts[0]['Groups']], [0, 3])
        self.assertTrue(contracts[0]['Groups'][1]['Conditions'][0]['Negative'])

    def test_unsupported_row_cannot_disappear_from_an_otherwise_supported_group(self):
        data, tables, row = self.fixture()
        tables['conditions'].append(dict(row, ConditionTypeOrReference=2, ConditionValue2=1, ConditionValue3=1))
        contracts, review = self.run_export(data, tables)
        self.assertEqual(contracts, [])
        self.assertFalse(review[0]['supported'])

    def test_repeatable_and_seasonal_reward_history_are_not_permanent(self):
        for changes in ({'SpecialFlags': 1}, {'QuestSortID': -22}):
            with self.subTest(changes=changes):
                data, tables, _ = self.fixture()
                for table in ('quest_template', 'quest_template_addon'):
                    tables[table][1].update(changes)
                self.assertEqual(self.run_export(data, tables)[0], [])

    def test_autocomplete_does_not_supply_ordinary_raw_quest_state(self):
        data, tables, row = self.fixture()
        tables['quest_template'][1]['QuestType'] = 0
        row['ConditionTypeOrReference'] = 9
        self.assertEqual(self.run_export(data, tables)[0], [])

    def test_incorrect_target_script_reference_or_extra_values_are_not_guessed(self):
        for changes in ({'ConditionTarget': 1}, {'ScriptName': 'custom'}, {'ConditionTypeOrReference': -7},
                        {'ConditionValue3': 1}, {'ConditionValue2': 1}, {'NegativeCondition': 2}):
            with self.subTest(changes=changes):
                data, tables, row = self.fixture(); row.update(changes)
                self.assertEqual(self.run_export(data, tables)[0], [])

    def test_quest_state_mask_uses_original_status_bits_only(self):
        data, tables, row = self.fixture(); row.update(ConditionTypeOrReference=47, ConditionValue2=1 | 8 | 32)
        self.assertEqual(len(self.run_export(data, tables)[0]), 1)
        row['ConditionValue2'] = 4
        self.assertEqual(self.run_export(data, tables)[0], [])

    def test_no_missing_referenced_quest_is_invented(self):
        data, tables, row = self.fixture(); row['ConditionValue1'] = 999
        self.assertEqual(self.run_export(data, tables)[0], [])

    def test_subject_source_conflict_does_not_receive_an_unreviewed_condition(self):
        data, tables, _ = self.fixture(); data['Quests'][0]['MinLevel'] = 2
        self.assertEqual(self.run_export(data, tables)[0], [])

    def test_other_source_namespace_or_group_is_not_quest_availability(self):
        data, tables, row = self.fixture(); row['SourceTypeOrReferenceId'] = 1
        self.assertEqual(self.run_export(data, tables)[0], [])
        row.update(SourceTypeOrReferenceId=19, SourceGroup=1)
        self.assertEqual(self.run_export(data, tables)[0], [])

    def test_duplicate_conditions_are_not_silently_counted_as_two_contracts(self):
        data, tables, row = self.fixture(); tables['conditions'].append(dict(row))
        self.assertEqual(self.run_export(data, tables)[0], [])


if __name__ == '__main__':
    unittest.main()
