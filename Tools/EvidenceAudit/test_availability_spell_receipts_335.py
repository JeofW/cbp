"""Spell proof requires its own observations, not same-number quest states."""
import copy
import unittest
import test_availability_closure_receipts_335 as fixtures


class SpellReceiptTests(unittest.TestCase):
    def fixture(self):
        helper=fixtures.AvailabilityClosureReceiptTests();contract,sim=helper.fixture()
        contract['ReferencedQuests']=[]
        contract['ReferencedSpells']=[{'SpellId':54197,'SourceRef':'controlled:condition:54197'}]
        contract['Groups'][0]['Conditions'][0].update(Type=25,Value1=54197)
        names=[f'availability-spell=54197:observed={state}' for state in ('unknown','empty','other','known','lost')]+[
            'availability-missing-observation-revokes-publication','availability-fixture-constrained-by-source-and-prerequisites']
        sim['cases']=[{'name':name,'status':'PASS'} for name in names]
        sim['availability_condition_validation'].update(contract=copy.deepcopy(contract),passed_cases=len(names))
        return helper,contract,sim

    def test_exact_positive_and_unknown_spell_receipts_are_recognized(self):
        h,c,s=self.fixture()
        try:valid=h.validate(c,s)
        except ValueError:valid=False
        self.assertTrue(valid,'The exact spell-input and publication cases are required')

    def test_raw_quest_states_cannot_certify_same_number_spell(self):
        h,c,s=self.fixture()
        names=[f'availability-reference=54197:state={state}' for state in (0,1,3,5,6)]+[
            'availability-missing-observation-revokes-publication','availability-fixture-constrained-by-source-and-prerequisites']
        s['cases']=[{'name':name,'status':'PASS'} for name in names]
        s['availability_condition_validation']['passed_cases']=len(names)
        with self.assertRaises(ValueError):h.validate(c,s)

    def test_missing_changed_or_failed_spell_observations_stay_unproven(self):
        for index in range(5):
            with self.subTest(index=index):
                h,c,s=self.fixture();s['cases'].pop(index);s['availability_condition_validation']['passed_cases']-=1
                with self.assertRaises(ValueError):h.validate(c,s)
        h,c,s=self.fixture();s['cases'][3]['status']='FAIL'
        with self.assertRaises(ValueError):h.validate(c,s)


if __name__=='__main__':unittest.main()
