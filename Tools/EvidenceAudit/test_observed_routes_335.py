import copy
import unittest
import quest_ledger_335


class ObservedRouteLedgerTests(unittest.TestCase):
    def apply(self, record, route):
        callback = getattr(quest_ledger_335, 'apply_observed_route_evidence', None)
        self.assertTrue(callable(callback), 'Observed route proof must be reconciled without changing static evidence')
        callback(record, [route])
        return record

    def record(self):
        return {'quest_id':136, 'classification':'DATA-INVALID/INCOMPLETE',
                'execution_requirements':['pickup-route-unrepresented'], 'source_obligations':[],
                'secondary_reference':{'unrepresented_secondary_requirements':{}},
                'source_review':{'secondary_availability_conditions':[], 'secondary_direct_quest_scripts':[]},
                'simulation':{'pipeline_status':'BLOCKED','failed_cases':0},
                'flag_dispositions':[{'flag':'missing-giver-relations','disposition':'item-started-route-not-implemented'}]}

    def route(self):
        return {'quest_id':136, 'route_type':'item-starter','route_result':'OBSERVED-ITEM-PICKUP-PROVEN',
                'controlled_pipeline_passed':True,'passed_cases':10,'failed_cases':0}

    def test_complete_observed_item_pipeline_proves_a_runtime_route(self):
        row=self.apply(self.record(),self.route())
        self.assertEqual(row['classification'],'GENERIC-PROVEN')
        self.assertEqual(row['simulation']['pipeline_status'],'BLOCKED','Original static-route evidence must remain intact')
        self.assertTrue(row['observed_route_proof']['whole_quest_pipeline'])

    def test_a_pickup_alone_does_not_prove_remaining_objectives(self):
        route=self.route();route['controlled_pipeline_passed']=False
        self.assertEqual(self.apply(self.record(),route)['classification'],'DATA-INVALID/INCOMPLETE')

    def test_other_data_gaps_prevent_blanket_promotion(self):
        record=self.record();record['execution_requirements'].append('objective-static-geometry-missing-or-invalid')
        self.assertEqual(self.apply(record,self.route())['classification'],'DATA-INVALID/INCOMPLETE')

    def test_a_failed_route_cannot_promote_classification(self):
        route=self.route();route['failed_cases']=1
        self.assertEqual(self.apply(self.record(),route)['classification'],'DATA-INVALID/INCOMPLETE')

    def test_reference_conditions_still_need_realm_evidence(self):
        record=self.record();record['source_review']['secondary_availability_conditions']=[{'SourceEntry':136}]
        self.assertEqual(self.apply(record,self.route())['classification'],'SOURCE-UNCERTAIN')

    def test_an_alias_objective_does_not_claim_whole_quest_completion(self):
        route=self.route();route.update(route_type='creature-credit',route_result='OBSERVED-ORDINARY-ALIAS-PROVEN',controlled_pipeline_passed=False)
        row=self.apply(self.record(),route)
        self.assertEqual(row['classification'],'DATA-INVALID/INCOMPLETE')
        self.assertFalse(row['observed_route_proof']['whole_quest_pipeline'])

    def test_another_quest_cannot_supply_a_proof(self):
        route=self.route();route['quest_id']=594
        with self.assertRaises(ValueError):self.apply(self.record(),route)

    def test_script_only_credit_keeps_its_obligations(self):
        route=self.route();route.update(route_type='creature-credit',route_result='SCRIPT-ONLY-NO-ACTION-INVENTED',controlled_pipeline_passed=False)
        row=self.apply(self.record(),route)
        self.assertIn('script-credit-needs-explicit-source-backed-strategy',row['execution_requirements'])
        self.assertFalse(row['observed_route_proof']['whole_quest_pipeline'])

    def test_event_flag_values_cannot_replace_event_type_names(self):
        import extend_quest_routes_335
        callback=getattr(extend_quest_routes_335,'event_type_names',None)
        self.assertTrue(callable(callback),'Script event types must be isolated from flag bit values')
        names=callback({'SMART_EVENT_SPELLHIT':{'value':8},'SMART_EVENT_FLAG_DIFFICULTY_2':{'value':8},
                        'SMART_EVENT_GOSSIP_HELLO':{'value':64},'SMART_EVENT_FLAG_RESERVED_6':{'value':64}})
        self.assertEqual(names,{8:'SMART_EVENT_SPELLHIT',64:'SMART_EVENT_GOSSIP_HELLO'})


if __name__=='__main__':unittest.main()
