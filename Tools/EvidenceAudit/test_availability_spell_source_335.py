"""Self spell presence is useful evidence; server-side absence is not inferred."""
import copy
import unittest
import test_quest_availability_source_335 as fixtures


class AvailabilitySpellSourceTests(unittest.TestCase):
    def fixture(self):
        h=fixtures.QuestAvailabilitySourceTests();data,tables,row=h.fixture()
        row.update(ConditionTypeOrReference=25,ConditionValue1=54197,ConditionValue2=0,ConditionValue3=0,NegativeCondition=0)
        return h,data,tables,row

    def test_positive_spell_has_exact_independent_source_namespace(self):
        h,d,t,r=self.fixture();before=copy.deepcopy((d,t))
        contracts,review=h.run_export(d,t)
        self.assertEqual(len(contracts),1)
        self.assertEqual(contracts[0]['ReferencedQuests'],[])
        self.assertEqual(contracts[0]['ReferencedSpells'][0]['SpellId'],54197)
        self.assertIn(':conditions:spell=54197',contracts[0]['ReferencedSpells'][0]['SourceRef'])
        self.assertTrue(review[0]['supported']);self.assertEqual((d,t),before)

    def test_negative_spell_cannot_borrow_client_spellbook_absence(self):
        h,d,t,r=self.fixture();r['NegativeCondition']=1
        contracts,review=h.run_export(d,t)
        self.assertEqual(contracts,[])
        self.assertIn('negative-spell-absence-not-proven',review[0]['remaining'])

    def test_unknown_row_prevents_a_partial_positive_contract(self):
        h,d,t,r=self.fixture();t['conditions'].append(dict(r,ConditionTypeOrReference=26))
        self.assertEqual(h.run_export(d,t)[0],[])

    def test_spell_and_quest_with_same_id_remain_separate(self):
        h,d,t,r=self.fixture();r['ConditionValue1']=201
        t['conditions'].append(dict(r,ConditionTypeOrReference=8))
        contracts,_=h.run_export(d,t)
        self.assertEqual(len(contracts),1)
        self.assertEqual(contracts[0]['ReferencedSpells'][0]['SpellId'],201)
        self.assertEqual(contracts[0]['ReferencedQuests'][0]['QuestId'],201)

    def test_multiple_positive_spells_and_groups_survive(self):
        h,d,t,r=self.fixture();t['conditions'].append(dict(r,ConditionValue1=123,ElseGroup=1))
        contracts,_=h.run_export(d,t)
        self.assertEqual(len(contracts),1)
        self.assertEqual([s['SpellId'] for s in contracts[0]['ReferencedSpells']],[123,54197])
        self.assertEqual([g['ElseGroup'] for g in contracts[0]['Groups']],[0,1])

    def test_nonspell_contract_bytes_keep_the_old_schema(self):
        h,d,t,r=self.fixture();r.update(ConditionTypeOrReference=8,ConditionValue1=201)
        contracts,_=h.run_export(d,t)
        self.assertEqual(len(contracts),1);self.assertNotIn('ReferencedSpells',contracts[0])

    def test_invalid_spell_values_target_and_source_conflict_stay_unresolved(self):
        for change in ({'ConditionValue1':0},{'ConditionValue1':True},{'ConditionValue1':2**31},
                       {'ConditionValue2':1},{'ConditionValue3':1},{'ConditionTarget':1},{'ScriptName':'custom'}):
            with self.subTest(change=change):
                h,d,t,r=self.fixture();r.update(change);self.assertEqual(h.run_export(d,t)[0],[])
        h,d,t,r=self.fixture();d['Quests'][0]['MinLevel']=2
        self.assertEqual(h.run_export(d,t)[0],[])


if __name__=='__main__':unittest.main()
