"""Item availability cannot borrow quest-status receipts or omit loss/unknown tests."""
import copy
import unittest
from primary_closure_335 import validate_availability_result


class AvailabilityItemReceiptTests(unittest.TestCase):
    def fixture(self):
        c={'QuestId':101,'SourceRef':'controlled','ReferencedQuests':[],
           'ReferencedItems':[{'ItemId':301,'SourceRef':'controlled:item:301'}],
           'Groups':[{'ElseGroup':0,'Conditions':[{'Type':2,'Value1':301,'Value2':2,'Value3':0,'Negative':False,'SourceRef':'controlled:condition'}]}]}
        names=[f'availability-item=301:observed={v}' for v in ('unknown','lost','0','1','2','3')]+[
            'availability-missing-observation-revokes-publication','availability-fixture-constrained-by-source-and-prerequisites']
        r={'quest_id':101,'failed_cases':0,'cases':[{'name':n,'status':'PASS'} for n in names],
           'availability_condition_validation':{'contract':copy.deepcopy(c),'passed_cases':len(names),'failed_cases':0,'fixture_satisfiable':True}}
        return c,r

    def test_complete_item_boundaries_and_loss_are_required(self):
        c,r=self.fixture()
        try:result=validate_availability_result(c,r)
        except ValueError:result=False
        self.assertTrue(result,'Complete actual item cases must be recognized')

    def test_quest_states_cannot_replace_inventory_cases(self):
        c,r=self.fixture()
        names=[f'availability-reference=301:state={s}' for s in (0,1,3,5,6)]+[
            'availability-missing-observation-revokes-publication','availability-fixture-constrained-by-source-and-prerequisites']
        r['cases']=[{'name':n,'status':'PASS'} for n in names];r['availability_condition_validation']['passed_cases']=len(names)
        with self.assertRaises(ValueError):validate_availability_result(c,r)

    def test_missing_unknown_loss_or_exact_threshold_is_not_proof(self):
        for index in (0,1,4):
            c,r=self.fixture();r['cases'].pop(index);r['availability_condition_validation']['passed_cases']-=1
            with self.subTest(index=index),self.assertRaises(ValueError):validate_availability_result(c,r)

    def test_failed_duplicate_or_other_contract_receipt_is_rejected(self):
        for change in ('failed','duplicate','quantity'):
            c,r=self.fixture()
            if change=='failed':r['cases'][0]['status']='FAIL'
            elif change=='duplicate':r['cases'][1]=copy.deepcopy(r['cases'][0])
            else:r['availability_condition_validation']['contract']['Groups'][0]['Conditions'][0]['Value2']=3
            with self.subTest(change=change),self.assertRaises(ValueError):validate_availability_result(c,r)


if __name__=='__main__':unittest.main()
