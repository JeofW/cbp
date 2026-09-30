"""Reject ambiguous closure evidence before it can become published coverage."""
import copy
import importlib.util
from pathlib import Path
import unittest


class PrimaryClosureIntegrityTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        path = Path(__file__).with_name('primary_closure_335.py')
        spec = importlib.util.spec_from_file_location('closure_integrity_test_owner', path)
        cls.owner = importlib.util.module_from_spec(spec)
        spec.loader.exec_module(cls.owner)

    def operation(self, name):
        callback = getattr(self.owner, name, None)
        self.assertTrue(callable(callback), 'Closure evidence needs its strict ' + name + ' owner')
        return callback

    def inputs(self):
        baseline = [{'quest_id': 1}, {'quest_id': 2}]
        model = [{'Id': 1}, {'Id': 2}]
        simulations = [{'quest_id': 1}, {'quest_id': 2}]
        return baseline, model, simulations

    def validate(self, baseline=None, model=None, simulations=None):
        defaults = self.inputs()
        return self.operation('validate_closure_inputs')(
            defaults[0] if baseline is None else baseline,
            defaults[1] if model is None else model,
            defaults[2] if simulations is None else simulations,
            expected_count=2)

    def test_all_three_inputs_retain_the_same_unique_quest_ids(self):
        indexes = self.validate()
        self.assertEqual([set(index) for index in indexes], [{1, 2}] * 3)

    def test_duplicate_simulation_is_rejected_before_dictionary_overwrite(self):
        with self.assertRaisesRegex(ValueError, 'duplicate.*2'):
            self.validate(simulations=[{'quest_id': 1}, {'quest_id': 2}, {'quest_id': 2}])

    def test_duplicate_model_row_is_rejected_before_dictionary_overwrite(self):
        with self.assertRaisesRegex(ValueError, 'duplicate.*1'):
            self.validate(model=[{'Id': 1}, {'Id': 1}, {'Id': 2}])

    def test_duplicate_baseline_is_rejected(self):
        with self.assertRaisesRegex(ValueError, 'duplicate.*1'):
            self.validate(baseline=[{'quest_id': 1}, {'quest_id': 1}])

    def test_phantom_quest_cannot_replace_a_missing_dataset_id(self):
        with self.assertRaisesRegex(ValueError, 'membership'):
            self.validate(simulations=[{'quest_id': 1}, {'quest_id': 3}])

    def test_missing_simulation_is_rejected(self):
        with self.assertRaisesRegex(ValueError, 'membership|count'):
            self.validate(simulations=[{'quest_id': 1}])

    def test_boolean_id_is_not_quest_one(self):
        with self.assertRaisesRegex(ValueError, 'positive integer'):
            self.validate(model=[{'Id': True}, {'Id': 2}])

    def test_string_id_does_not_alias_an_integer_quest(self):
        with self.assertRaisesRegex(ValueError, 'positive integer'):
            self.validate(simulations=[{'quest_id': '1'}, {'quest_id': 2}])

    def partition(self, rows, ids=(1, 2), expected_count=2):
        return self.operation('classification_partition')(rows, ids, expected_count=expected_count)

    def test_top_level_partition_does_not_count_secondary_obligations(self):
        rows = [{'quest_id': 1, 'classification': 'DATA-INVALID/INCOMPLETE',
                 'remaining_obligations': ['data:geometry', 'script:escort', 'source:condition']},
                {'quest_id': 2, 'classification': 'GENERIC-PROVEN', 'remaining_obligations': []}]
        before = copy.deepcopy(rows)
        result = self.partition(rows)
        self.assertEqual(set(result), set(self.owner.CLASSIFICATIONS))
        self.assertEqual(sum(map(len, result.values())), 2)
        self.assertEqual(result['UNSUPPORTED-SCRIPTED'], [])
        self.assertEqual(result['DATA-INVALID/INCOMPLETE'], [1])
        self.assertEqual(rows, before)

    def test_same_quest_in_two_primary_classes_is_rejected(self):
        with self.assertRaisesRegex(ValueError, 'duplicate.*1'):
            self.partition([{'quest_id': 1, 'classification': 'GENERIC-PROVEN'},
                            {'quest_id': 1, 'classification': 'STRATEGY-PROVEN'}])

    def test_unknown_classification_cannot_disappear_from_six_counts(self):
        with self.assertRaisesRegex(ValueError, 'classification'):
            self.partition([{'quest_id': 1, 'classification': 'UNKNOWN'},
                            {'quest_id': 2, 'classification': 'GENERIC-PROVEN'}])

    def test_partition_membership_is_verified_not_only_total(self):
        with self.assertRaisesRegex(ValueError, 'membership'):
            self.partition([{'quest_id': 1, 'classification': 'GENERIC-PROVEN'},
                            {'quest_id': 3, 'classification': 'GENERIC-PROVEN'}])

    def test_exact_4335_partition_is_asserted_programmatically(self):
        rows = [{'quest_id': ident, 'classification': self.owner.CLASSIFICATIONS[ident % 6]}
                for ident in range(1, 4336)]
        result = self.partition(rows, range(1, 4336), 4335)
        self.assertEqual(sum(map(len, result.values())), 4335)
        self.assertEqual(sorted(ident for values in result.values() for ident in values), list(range(1, 4336)))

    def strategy(self, **changes):
        simulation = {'quest_id': 1, 'dataset_sha256': 'a' * 64, 'repair_sha256': 'b' * 64,
                      'execution_fingerprint': 'bound-observation-state'}
        receipt = dict(simulation, strategy_sha256='c' * 64, failed_cases=0, pipeline_status='PASS')
        receipt.update(changes)
        return simulation, receipt

    def validate_strategy(self, **changes):
        simulation, receipt = self.strategy(**changes)
        return self.operation('validate_strategy_results')([receipt], {1: simulation}, 'c' * 64)

    def test_strategy_evidence_matches_the_exact_dataset_repair_and_execution(self):
        result = self.validate_strategy()
        self.assertEqual(set(result), {1})

    def test_strategy_for_another_quest_is_rejected(self):
        with self.assertRaisesRegex(ValueError, 'unknown quest'):
            self.validate_strategy(quest_id=2)

    def test_duplicate_strategy_receipt_is_rejected(self):
        simulation, receipt = self.strategy()
        with self.assertRaisesRegex(ValueError, 'duplicate.*1'):
            self.operation('validate_strategy_results')([receipt, receipt], {1: simulation}, 'c' * 64)

    def test_stale_dataset_strategy_receipt_is_rejected(self):
        with self.assertRaisesRegex(ValueError, 'dataset_sha256'):
            self.validate_strategy(dataset_sha256='d' * 64)

    def test_stale_repair_strategy_receipt_is_rejected(self):
        with self.assertRaisesRegex(ValueError, 'repair_sha256'):
            self.validate_strategy(repair_sha256='d' * 64)

    def test_stale_execution_fingerprint_is_rejected(self):
        with self.assertRaisesRegex(ValueError, 'execution_fingerprint'):
            self.validate_strategy(execution_fingerprint='another-state')

    def test_another_strategy_pack_cannot_borrow_a_passing_receipt(self):
        with self.assertRaisesRegex(ValueError, 'strategy_sha256'):
            self.validate_strategy(strategy_sha256='d' * 64)

    def test_declared_strategy_with_generic_pass_is_not_generic_proof(self):
        result = self.owner.final_classification([], {'pipeline_status': 'PASS', 'failed_cases': 0}, 'DECLARED')
        self.assertEqual(result, 'UNSUPPORTED-SCRIPTED')


if __name__ == '__main__':
    unittest.main()
