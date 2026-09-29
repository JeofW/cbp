import importlib.util
from pathlib import Path
import unittest


class PrimaryClosureClassificationTests(unittest.TestCase):
    def module(self):
        path=Path(__file__).with_name('primary_closure_335.py');spec=importlib.util.spec_from_file_location('closure_geometry_module',path)
        module=importlib.util.module_from_spec(spec);spec.loader.exec_module(module);return module

    def classify(self, obligations=(), pipeline='PASS', failures=0, strategy='MISSING', baseline='DATA-INVALID/INCOMPLETE'):
        path=Path(__file__).with_name('primary_closure_335.py')
        self.assertTrue(path.is_file(),'Primary closure needs a proof-bounded classification owner')
        spec=importlib.util.spec_from_file_location('closure_test_module',path);module=importlib.util.module_from_spec(spec);spec.loader.exec_module(module)
        return module.final_classification(list(obligations),{'pipeline_status':pipeline,'failed_cases':failures},strategy,baseline)

    def test_complete_generic_evidence_closes_prior_data_gap(self):
        self.assertEqual(self.classify(),'GENERIC-PROVEN')

    def test_missing_geometry_is_not_closed_by_a_simulated_receipt(self):
        self.assertEqual(self.classify(['data:objective-geometry-missing']),'DATA-INVALID/INCOMPLETE')

    def test_source_conflict_is_not_closed_by_generic_controlled_inputs(self):
        self.assertEqual(self.classify(['source:primary-field-conflict']),'SOURCE-UNCERTAIN')

    def test_external_availability_condition_needs_bound_evidence(self):
        self.assertEqual(self.classify(['source:server-condition-not-modeled']),'SOURCE-UNCERTAIN')

    def test_script_contract_without_a_recipe_remains_unsupported(self):
        self.assertEqual(self.classify(['script:cast-credit-no-strategy']),'UNSUPPORTED-SCRIPTED')

    def test_missing_pipeline_never_becomes_generic_proof(self):
        self.assertEqual(self.classify(pipeline='BLOCKED'),'LIVE-ACCEPTANCE-REQUIRED')

    def test_failed_checks_never_promote_a_quest(self):
        self.assertEqual(self.classify(failures=1),'LIVE-ACCEPTANCE-REQUIRED')

    def test_strategy_availability_alone_is_not_strategy_proof(self):
        self.assertEqual(self.classify(strategy='DECLARED',pipeline='BLOCKED'),'LIVE-ACCEPTANCE-REQUIRED')

    def test_complete_bound_strategy_pipeline_is_separate_from_generic(self):
        self.assertEqual(self.classify(strategy='VALIDATED-PIPELINE'),'STRATEGY-PROVEN')

    def test_acquisition_gap_is_not_hidden_by_preloaded_stock(self):
        self.assertEqual(self.classify(['data:delivery-acquisition-route-missing']),'DATA-INVALID/INCOMPLETE')

    def test_live_only_obligation_survives_offline_pass(self):
        self.assertEqual(self.classify(['live:timed-route-acceptance']),'LIVE-ACCEPTANCE-REQUIRED')

    def test_script_and_data_obligations_are_not_dropped(self):
        self.assertEqual(self.classify(['data:target-spawn-missing','script:event-contract']),'DATA-INVALID/INCOMPLETE')

    def alternative(self, other_item=5, other_count=3, points=True, go=False):
        callback=getattr(self.module(),'has_collection_alternative_geometry',None)
        self.assertTrue(callable(callback),'Alternative item acquisition is not an additional mandatory objective')
        first={'Type':'CollectItem','ItemId':5,'CollectCount':3,'MobId':10}
        second={'Type':'CollectFromGameObject' if go else 'CollectItem','ItemId':other_item,'CollectCount':other_count,'MobId':11,'GameObjectId':11}
        data={'CreatureSpawns':{},'GameObjectSpawns':{}}
        if points:data['GameObjectSpawns' if go else 'CreatureSpawns']['11']=[{'Map':0,'X':1,'Y':2,'Z':3}]
        return callback({'Objectives':[first,second]},first,data,{5:3})

    def test_matching_other_creature_route_covers_the_required_item(self):self.assertTrue(self.alternative())
    def test_gameobject_item_source_can_cover_same_carried_requirement(self):self.assertTrue(self.alternative(go=True))
    def test_another_item_cannot_cover_missing_source(self):self.assertFalse(self.alternative(other_item=6))
    def test_another_count_cannot_cover_missing_source(self):self.assertFalse(self.alternative(other_count=2))
    def test_no_geometry_anywhere_remains_a_gap(self):self.assertFalse(self.alternative(points=False))

    def test_category_index_contains_each_quest_only_once(self):
        callback=getattr(self.module(),'category_index',None)
        self.assertTrue(callable(callback),'Category lists must retain unique quest identities')
        self.assertEqual(callback([{'quest_id':1,'remaining_obligations':['data:geometry:0','data:geometry:1']},
                                   {'quest_id':2,'remaining_obligations':['data:geometry:0']}]),{'data:geometry':[1,2]})

if __name__=='__main__':unittest.main()
