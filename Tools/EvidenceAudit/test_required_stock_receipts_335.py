import copy
import unittest
import primary_closure_335 as owner


class RequiredStockReceiptTests(unittest.TestCase):
    def fixture(self):
        quest={'Id':101,'RequiredStockItems':[{'ItemId':301,'Count':2},{'ItemId':302,'Count':1}]}
        names={f'required-stock-item={item}:observed={state}' for item in (301,302)
               for state in ('unknown','zero','partial','full','lost')}
        names.update({'required-stock-missing-observation-withholds-pickup',
                      'required-stock-held-items-not-server-ready','required-stock-keeps-ordinary-objectives'})
        simulation={'quest_id':101,'failed_cases':0,'pipeline_status':'PASS',
                    'cases':[{'name':name,'status':'PASS'} for name in sorted(names)],
                    'required_stock_validation':{'quest_id':101,'items':copy.deepcopy(quest['RequiredStockItems']),
                        'passed_cases':len(names),'failed_cases':0,'acquisition_proven':False}}
        return quest,simulation

    def validate(self,q,s):
        self.assertTrue(callable(getattr(owner,'validate_required_stock_result',None)),
                        'Required stock classification needs actual per-item owner receipts')
        return owner.validate_required_stock_result(q,s)

    def test_complete_matching_actual_cases_are_accepted(self):
        self.assertTrue(self.validate(*self.fixture()))

    def test_missing_receipt_does_not_become_evidence(self):
        q,s=self.fixture();s.pop('required_stock_validation');self.assertFalse(self.validate(q,s))

    def test_changed_identity_or_quantities_reject_receipt(self):
        for field,value in (('quest_id',999),('items',[{'ItemId':301,'Count':3}]),
                            ('failed_cases',1),('passed_cases',True),('acquisition_proven',True)):
            with self.subTest(field=field):
                q,s=self.fixture();s['required_stock_validation'][field]=value
                with self.assertRaises(ValueError):self.validate(q,s)

    def test_missing_duplicate_failed_or_extra_named_cases_are_not_proof(self):
        for change in ('missing','duplicate','failed','extra'):
            with self.subTest(change=change):
                q,s=self.fixture()
                if change=='missing':s['cases'].pop()
                elif change=='duplicate':s['cases'].append(copy.deepcopy(s['cases'][0]))
                elif change=='failed':s['cases'][0]['status']='FAIL'
                else:s['cases'].append({'name':'required-stock-fake','status':'PASS'})
                with self.assertRaises(ValueError):self.validate(q,s)

    def test_foreign_simulation_or_failing_dataset_is_rejected(self):
        for field,value in (('quest_id',102),('failed_cases',1)):
            q,s=self.fixture();s[field]=value
            with self.assertRaises(ValueError):self.validate(q,s)

    def test_carried_stock_cannot_close_the_acquisition_obligation(self):
        q,s=self.fixture();self.assertTrue(self.validate(q,s))
        self.assertEqual(owner.final_classification(['data:required-stock-acquisition-route-missing'],s),'DATA-INVALID/INCOMPLETE')


if __name__=='__main__':unittest.main()
