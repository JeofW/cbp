"""Ordinary carried item predicates require complete exact item source evidence."""
import copy
import unittest
import test_quest_availability_source_335 as fixtures


class AvailabilityItemSourceTests(unittest.TestCase):
    def fixture(self):
        h=fixtures.QuestAvailabilitySourceTests();d,t,r=h.fixture()
        r.update(ConditionTypeOrReference=2,ConditionValue1=301,ConditionValue2=2,ConditionValue3=0)
        t['item_template']=[{'entry':301,'name':'Required carried item','__source_line':40}]
        return h,d,t,r

    def test_carried_item_has_its_own_namespace_and_source_reference(self):
        h,d,t,r=self.fixture();before=copy.deepcopy((d,t))
        contracts,reviews=h.run_export(d,t)
        self.assertEqual(len(contracts),1)
        self.assertEqual(contracts[0]['ReferencedQuests'],[])
        self.assertEqual(contracts[0]['ReferencedItems'][0]['ItemId'],301)
        self.assertIn(':item_template:301:',contracts[0]['ReferencedItems'][0]['SourceRef'])
        self.assertEqual(reviews[0]['referenced_primary_items'][0]['item']['table'],'item_template')
        self.assertEqual(before,(d,t))

    def test_item_quantity_and_negation_survive_group_export(self):
        h,d,t,r=self.fixture();r['NegativeCondition']=1
        t['conditions'].append(dict(r,ElseGroup=2,NegativeCondition=0,ConditionValue2=3))
        c,_=h.run_export(d,t);self.assertEqual(len(c),1)
        self.assertEqual([g['ElseGroup'] for g in c[0]['Groups']],[0,2])
        self.assertTrue(c[0]['Groups'][0]['Conditions'][0]['Negative'])
        self.assertEqual(c[0]['Groups'][1]['Conditions'][0]['Value2'],3)

    def test_item_and_quest_with_the_same_id_are_not_interchangeable(self):
        h,d,t,r=self.fixture();r['ConditionValue1']=201
        self.assertEqual(h.run_export(d,t)[0],[])
        t['item_template'].append({'entry':201,'name':'Separate item201','__source_line':41})
        t['conditions'].append(dict(r,ConditionTypeOrReference=8,ConditionValue2=0))
        c,_=h.run_export(d,t);self.assertEqual(len(c),1)
        self.assertEqual(c[0]['ReferencedQuests'][0]['QuestId'],201)
        self.assertEqual(c[0]['ReferencedItems'][0]['ItemId'],201)

    def test_bank_or_unknown_extra_predicate_cannot_be_dropped(self):
        h,d,t,r=self.fixture();r['ConditionValue3']=1
        self.assertEqual(h.run_export(d,t)[0],[])
        # Positive spell25 now has a separate verified owner; phase26 remains
        # unsupported and must still prevent exporting a partial item group.
        r['ConditionValue3']=0;t['conditions'].append(dict(r,ConditionTypeOrReference=26,ConditionValue2=0))
        self.assertEqual(h.run_export(d,t)[0],[])

    def test_missing_item_or_table_remains_source_uncertain(self):
        for change in ('missing-row','missing-table'):
            h,d,t,r=self.fixture()
            if change=='missing-row':t['item_template']=[]
            else:t.pop('item_template')
            c,review=h.run_export(d,t);self.assertEqual(c,[]);self.assertFalse(review[0]['supported'])

    def test_invalid_count_id_target_or_bank_flag_is_rejected(self):
        for change in ({'ConditionValue2':0},{'ConditionValue2':-1},{'ConditionValue2':True},
                       {'ConditionValue2':2**31},{'ConditionValue1':0},{'ConditionValue1':True},
                       {'ConditionValue3':2},{'ConditionTarget':1}):
            with self.subTest(change=change):
                h,d,t,r=self.fixture();r.update(change);self.assertEqual(h.run_export(d,t)[0],[])

    def test_existing_nonitem_contract_bytes_do_not_gain_empty_item_metadata(self):
        h,d,t,r=self.fixture();r.update(ConditionTypeOrReference=8,ConditionValue1=201,ConditionValue2=0)
        c,_=h.run_export(d,t);self.assertEqual(len(c),1);self.assertNotIn('ReferencedItems',c[0])

    def test_subject_conflict_still_prevents_item_contract_export(self):
        h,d,t,r=self.fixture();d['Quests'][0]['MinLevel']=2
        self.assertEqual(h.run_export(d,t)[0],[])


if __name__=='__main__':unittest.main()
