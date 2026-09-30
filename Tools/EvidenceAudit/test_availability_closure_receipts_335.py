"""A declaration or borrowed generic pass is not availability-condition proof."""
import copy
import unittest
import primary_closure_335 as owner


class AvailabilityClosureReceiptTests(unittest.TestCase):
    def fixture(self):
        contract = {'QuestId': 101, 'SourceRef': 'controlled-source',
            'ReferencedQuests': [{'QuestId': 201, 'QuestType': 2, 'QuestSortID': 0, 'SpecialFlags': 0, 'SourceRef': 'reference'}],
            'Groups': [{'ElseGroup': 0, 'Conditions': [{'Type': 8, 'Value1': 201, 'Value2': 0,
                'Value3': 0, 'Negative': False, 'SourceRef': 'condition'}]}]}
        names = [f'availability-reference=201:state={state}' for state in (0, 1, 3, 5, 6)] + [
            'availability-missing-observation-revokes-publication', 'availability-fixture-constrained-by-source-and-prerequisites']
        simulation = {'quest_id': 101, 'failed_cases': 0, 'pipeline_status': 'PASS',
            'cases': [{'name': name, 'status': 'PASS'} for name in names],
            'availability_condition_validation': {'contract': copy.deepcopy(contract),
                'passed_cases': len(names), 'failed_cases': 0, 'fixture_satisfiable': True}}
        return contract, simulation

    def validate(self, contract, simulation):
        callback = getattr(owner, 'validate_availability_result', None)
        self.assertTrue(callable(callback), 'Availability closure needs exact contract and real owner receipt validation')
        return callback(contract, simulation)

    def test_complete_condition_receipt_is_accepted(self):
        self.assertTrue(self.validate(*self.fixture()))

    def test_a_declared_contract_without_executed_cases_stays_unproven(self):
        contract, simulation = self.fixture(); simulation.pop('availability_condition_validation')
        self.assertFalse(self.validate(contract, simulation))

    def test_other_quest_cannot_borrow_a_condition_receipt(self):
        contract, simulation = self.fixture(); simulation['quest_id'] = 102
        with self.assertRaises(ValueError): self.validate(contract, simulation)

    def test_changed_group_or_reference_cannot_borrow_a_receipt(self):
        contract, simulation = self.fixture(); simulation['availability_condition_validation']['contract']['Groups'][0]['ElseGroup'] = 1
        with self.assertRaises(ValueError): self.validate(contract, simulation)

    def test_all_five_states_and_publication_boundary_are_required(self):
        for index in (0, 3, 5, 6):
            with self.subTest(index=index):
                contract, simulation = self.fixture(); simulation['cases'].pop(index)
                with self.assertRaises(ValueError): self.validate(contract, simulation)

    def test_failed_or_skipped_boundary_is_not_proof(self):
        for status in ('FAIL', 'NOT-APPLICABLE'):
            with self.subTest(status=status):
                contract, simulation = self.fixture(); simulation['cases'][0]['status'] = status
                with self.assertRaises(ValueError): self.validate(contract, simulation)

    def test_duplicate_case_cannot_replace_missing_state(self):
        contract, simulation = self.fixture(); simulation['cases'][0] = simulation['cases'][1]
        with self.assertRaises(ValueError): self.validate(contract, simulation)

    def test_counter_cannot_hide_failed_condition_validation(self):
        contract, simulation = self.fixture(); simulation['availability_condition_validation']['failed_cases'] = 1
        with self.assertRaises(ValueError): self.validate(contract, simulation)


if __name__ == '__main__':
    unittest.main()
