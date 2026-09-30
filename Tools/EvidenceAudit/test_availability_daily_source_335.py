"""Daily membership is neither permanent reward history nor a partial contract."""
import copy
import unittest
import test_quest_availability_source_335 as fixtures


class AvailabilityDailySourceTests(unittest.TestCase):
    def fixture(self):
        helper=fixtures.QuestAvailabilitySourceTests()
        data,tables,row=helper.fixture()
        row.update(ConditionTypeOrReference=43,ConditionValue1=201,ConditionValue2=0,ConditionValue3=0)
        return helper,data,tables,row

    def test_complete_daily_contract_has_its_exact_reference(self):
        h,d,t,r=self.fixture();before=copy.deepcopy((d,t))
        contracts,review=h.run_export(d,t)
        self.assertEqual(len(contracts),1)
        self.assertEqual([q['QuestId'] for q in contracts[0]['ReferencedQuests']],[201])
        self.assertTrue(review[0]['supported']);self.assertEqual((d,t),before)

    def test_repeatable_daily_reference_does_not_require_permanent_history(self):
        h,d,t,r=self.fixture()
        next(q for q in t['quest_template_addon'] if q['ID']==201)['SpecialFlags']=1
        contracts,_=h.run_export(d,t)
        self.assertEqual(len(contracts),1)
        self.assertEqual(contracts[0]['ReferencedQuests'][0]['SpecialFlags'],1)

    def test_daily_reference_does_not_relax_reward_predicates(self):
        h,d,t,r=self.fixture()
        next(q for q in t['quest_template_addon'] if q['ID']==201)['SpecialFlags']=1
        t['conditions'].append(dict(r,ConditionTypeOrReference=8))
        self.assertEqual(h.run_export(d,t)[0],[])

    def test_missing_daily_template_prevents_export(self):
        h,d,t,r=self.fixture();r['ConditionValue1']=999999
        self.assertEqual(h.run_export(d,t)[0],[])

    def test_daily_negation_and_alternative_groups_are_preserved(self):
        h,d,t,r=self.fixture();r['NegativeCondition']=1
        t['conditions'].append(dict(r,ElseGroup=1,NegativeCondition=0))
        c,_=h.run_export(d,t);self.assertEqual(len(c),1)
        self.assertEqual([g['ElseGroup'] for g in c[0]['Groups']],[0,1])
        self.assertTrue(c[0]['Groups'][0]['Conditions'][0]['Negative'])

    def test_wrong_target_extra_values_and_unknown_predicates_do_not_disappear(self):
        for change in ({'ConditionValue2':1},{'ConditionValue3':1},{'ConditionTarget':1},
                       {'ConditionValue1':True},{'ConditionValue1':0},{'ConditionValue1':-1}):
            with self.subTest(change=change):
                h,d,t,r=self.fixture();r.update(change);self.assertEqual(h.run_export(d,t)[0],[])
        h,d,t,r=self.fixture();t['conditions'].append(dict(r,ConditionTypeOrReference=2,ConditionValue2=1))
        self.assertEqual(h.run_export(d,t)[0],[])


if __name__=='__main__':unittest.main()
