"""Complete source area contracts cannot be confused with quest references."""
import copy
import unittest
import test_quest_availability_source_335 as fixtures


class AvailabilityAreaSourceTests(unittest.TestCase):
    def fixture(self):
        helper=fixtures.QuestAvailabilitySourceTests()
        data,tables,row=helper.fixture()
        row.update(ConditionTypeOrReference=23,ConditionValue1=99,ConditionValue2=0,ConditionValue3=0)
        return helper,data,tables,row

    def test_area_is_not_a_quest_reference(self):
        helper,data,tables,row=self.fixture()
        before=copy.deepcopy((data,tables))
        contracts,review=helper.run_export(data,tables)
        self.assertEqual(len(contracts),1)
        self.assertEqual(contracts[0]['ReferencedQuests'],[])
        self.assertEqual(contracts[0]['Groups'][0]['Conditions'][0]['Value1'],99)
        self.assertTrue(review[0]['supported'])
        self.assertEqual((data,tables),before)

    def test_negation_is_preserved(self):
        helper,data,tables,row=self.fixture();row['NegativeCondition']=1
        contracts,_=helper.run_export(data,tables)
        self.assertEqual(len(contracts),1)
        self.assertTrue(contracts[0]['Groups'][0]['Conditions'][0]['Negative'])

    def test_mixed_area_and_reward_group_is_complete(self):
        helper,data,tables,row=self.fixture()
        tables['conditions'].append(dict(row,ConditionTypeOrReference=8,ConditionValue1=201))
        contracts,_=helper.run_export(data,tables)
        self.assertEqual(len(contracts),1)
        self.assertEqual(len(contracts[0]['Groups'][0]['Conditions']),2)
        self.assertEqual([r['QuestId'] for r in contracts[0]['ReferencedQuests']],[201])

    def test_mixed_group_with_absent_reward_reference_is_not_exported(self):
        helper,data,tables,row=self.fixture()
        tables['conditions'].append(dict(row,ConditionTypeOrReference=8,ConditionValue1=2))
        self.assertEqual(helper.run_export(data,tables)[0],[])

    def test_unknown_area_extra_values_and_wrong_target_are_rejected(self):
        for changes in ({'ConditionValue1':0},{'ConditionValue1':-1},{'ConditionValue1':True},
                        {'ConditionValue2':1},{'ConditionValue3':1},{'ConditionTarget':1}):
            with self.subTest(changes=changes):
                helper,data,tables,row=self.fixture();row.update(changes)
                self.assertEqual(helper.run_export(data,tables)[0],[])

    def test_area_does_not_erase_unhandled_item_or_subject_conflicts(self):
        helper,data,tables,row=self.fixture()
        tables['conditions'].append(dict(row,ConditionTypeOrReference=2,ConditionValue1=301,ConditionValue2=1,ConditionValue3=1))
        self.assertEqual(helper.run_export(data,tables)[0],[])
        tables['conditions'].pop();data['Quests'][0]['MinLevel']=80
        self.assertEqual(helper.run_export(data,tables)[0],[])


if __name__=='__main__':unittest.main()
