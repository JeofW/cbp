"""Source predicate semantics distinguish reward history from active status."""
import copy
import unittest
import test_quest_availability_source_335 as availability_fixtures


class AvailabilityScalarSourceTests(unittest.TestCase):
    def fixture(self):
        helper = availability_fixtures.QuestAvailabilitySourceTests()
        return helper, *helper.fixture()

    def test_autocomplete_permanent_reward_reference_is_exported(self):
        helper, data, tables, _ = self.fixture()
        tables['quest_template'][1]['QuestType'] = 0
        before = copy.deepcopy((data, tables))
        contracts, review = helper.run_export(data, tables)
        self.assertEqual(len(contracts), 1)
        self.assertEqual(contracts[0]['ReferencedQuests'][0]['QuestType'], 0)
        self.assertTrue(review[0]['supported'])
        self.assertEqual((data, tables), before)

    def test_autocomplete_raw_states_are_still_not_exported(self):
        for kind in (9, 14, 28, 47):
            with self.subTest(kind=kind):
                helper, data, tables, row = self.fixture()
                tables['quest_template'][1]['QuestType'] = 0
                row.update(ConditionTypeOrReference=kind, ConditionValue2=1 if kind == 47 else 0)
                self.assertEqual(helper.run_export(data, tables)[0], [])

    def test_level_uses_comparison_without_a_quest_reference(self):
        for comparison in range(5):
            with self.subTest(comparison=comparison):
                helper, data, tables, row = self.fixture()
                row.update(ConditionTypeOrReference=27, ConditionValue1=75, ConditionValue2=comparison)
                contracts, _ = helper.run_export(data, tables)
                self.assertEqual(len(contracts), 1)
                self.assertEqual(contracts[0]['ReferencedQuests'], [])
                self.assertEqual(contracts[0]['Groups'][0]['Conditions'][0]['Value2'], comparison)

    def test_unknown_comparison_or_extra_values_are_rejected(self):
        for changes in ({'ConditionValue2': -1}, {'ConditionValue2': 5}, {'ConditionValue3': 1},
                        {'ConditionValue1': 0}, {'ConditionValue2': True}):
            with self.subTest(changes=changes):
                helper, data, tables, row = self.fixture()
                row.update(ConditionTypeOrReference=27, ConditionValue1=75, ConditionValue2=3)
                row.update(changes)
                self.assertEqual(helper.run_export(data, tables)[0], [])

    def test_level_and_history_groups_remain_complete(self):
        helper, data, tables, row = self.fixture()
        tables['conditions'].append(dict(row, ConditionTypeOrReference=27, ConditionValue1=75, ConditionValue2=3))
        contracts, _ = helper.run_export(data, tables)
        self.assertEqual(len(contracts), 1)
        self.assertEqual(len(contracts[0]['Groups'][0]['Conditions']), 2)
        self.assertEqual(len(contracts[0]['ReferencedQuests']), 1)

    def test_new_supported_row_does_not_erase_unhandled_bank_predicate(self):
        helper, data, tables, row = self.fixture()
        row.update(ConditionTypeOrReference=27, ConditionValue1=75, ConditionValue2=3)
        tables['conditions'].append(dict(row, ConditionTypeOrReference=2, ConditionValue1=301, ConditionValue2=1, ConditionValue3=1))
        self.assertEqual(helper.run_export(data, tables)[0], [])

    def test_reward_reference_still_rejects_repeatable_seasonal_unknown_and_invalid_kinds(self):
        for kind, flags, sort in ((0, 1, 0), (0, 0, -22), (1, 0, 0), (False, 0, 0), (0, -2, 0)):
            with self.subTest(kind=kind, flags=flags, sort=sort):
                helper, data, tables, _ = self.fixture()
                tables['quest_template'][1].update(QuestType=kind, QuestSortID=sort)
                tables['quest_template_addon'][1]['SpecialFlags'] = flags
                self.assertEqual(helper.run_export(data, tables)[0], [])


if __name__ == '__main__': unittest.main()
