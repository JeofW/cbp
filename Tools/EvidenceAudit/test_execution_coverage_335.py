"""Execution grades must never inherit a PASS from navigation or injected stock."""
import copy
import hashlib
import unittest
from pathlib import Path
import tempfile

try:
    import execution_coverage_335 as subject
except ImportError:
    subject = None


class ExecutionCoverageTests(unittest.TestCase):
    def setUp(self):
        self.assertIsNotNone(subject, 'strict execution-evidence grading is missing')

    def requirement(self, ident=10161):
        return {'quest_id': ident, 'primitive': 'ground-gameobject-loot', 'objective_key': 'item:28116',
                'semantics_sha256': 'a' * 64}

    def contract(self):
        req = self.requirement()
        return {'contract_id': 'controlled-ground-lifecycle', 'scope': 'controlled-production-owner-lifecycle',
                'primitive': req['primitive'], 'source_sha256': {'actual-owner.cs': 'b' * 64},
                'trace_sha256': 'c' * 64, 'case_ids': ['one-piece-then-thirty', 'interrupted-and-recovered'],
                'members': [req], 'stages': list(subject.REQUIRED_STAGES),
                'dispatch_count': 30, 'acknowledgement_count': 30, 'progress_after_ack': True,
                'authoritative_completion': True, 'authoritative_turnin': True,
                'manually_injected_progress_without_action': False, 'navigation_only': False}

    def test_legacy_generic_is_not_execution_proof(self):
        result = subject.grade('GENERIC-PROVEN', [self.requirement()], [])
        self.assertEqual(result['classification'], 'EXECUTION-UNVERIFIED')

    def test_legacy_strategy_pass_is_not_automatic_execution_proof(self):
        self.assertEqual(subject.grade('STRATEGY-PROVEN', [self.requirement()], [])['classification'], 'EXECUTION-UNVERIFIED')

    def test_complete_bound_contract_can_cover_exact_requirement(self):
        self.assertEqual(subject.grade('GENERIC-PROVEN', [self.requirement()], [self.contract()])['classification'], 'GENERIC-PROVEN')

    def test_coordinates_and_pipeline_pass_are_not_contract(self):
        record = {'pipeline_status': 'PASS', 'profile_generated': True, 'coordinates': [1, 2, 3]}
        with self.assertRaises(ValueError): subject.validate_contract(record)

    def test_every_lifecycle_stage_is_required(self):
        for stage in subject.REQUIRED_STAGES:
            with self.subTest(stage=stage):
                contract = self.contract(); contract['stages'].remove(stage)
                with self.assertRaises(ValueError): subject.validate_contract(contract)

    def test_injected_progress_is_explicitly_rejected(self):
        contract = self.contract(); contract['manually_injected_progress_without_action'] = True
        with self.assertRaises(ValueError): subject.validate_contract(contract)

    def test_no_action_dispatch_cannot_be_navigation_success(self):
        for key in ('dispatch_count', 'acknowledgement_count'):
            contract = self.contract(); contract[key] = 0
            with self.assertRaises(ValueError): subject.validate_contract(contract)

    def test_acknowledgements_cannot_exceed_dispatches(self):
        contract = self.contract(); contract['acknowledgement_count'] = 31
        with self.assertRaises(ValueError): subject.validate_contract(contract)

    def test_neither_completion_nor_turnin_may_be_guessed(self):
        for key in ('authoritative_completion', 'authoritative_turnin', 'progress_after_ack'):
            contract = self.contract(); contract[key] = False
            with self.assertRaises(ValueError): subject.validate_contract(contract)

    def test_wrong_id_or_semantics_never_borrows_a_family_pass(self):
        for key, value in (('quest_id', 10162), ('objective_key', 'item:123'), ('semantics_sha256', 'd' * 64)):
            requirement = self.requirement(); requirement[key] = value
            self.assertEqual(subject.grade('GENERIC-PROVEN', [requirement], [self.contract()])['classification'], 'EXECUTION-UNVERIFIED')

    def test_uncovered_second_primitive_prevents_quest_promotion(self):
        requirements = [self.requirement(), {**self.requirement(), 'primitive': 'gossip-dialogue', 'objective_key': 'gossip:1'}]
        self.assertEqual(subject.grade('GENERIC-PROVEN', requirements, [self.contract()])['classification'], 'EXECUTION-UNVERIFIED')

    def test_old_data_source_and_script_holds_remain(self):
        for classification in ('DATA-INVALID/INCOMPLETE', 'SOURCE-UNCERTAIN', 'UNSUPPORTED-SCRIPTED', 'LIVE-ACCEPTANCE-REQUIRED'):
            self.assertEqual(subject.grade(classification, [self.requirement()], [self.contract()])['classification'], classification)

    def test_empty_requirements_are_not_vacuous_success(self):
        self.assertEqual(subject.grade('GENERIC-PROVEN', [], [self.contract()])['classification'], 'EXECUTION-UNVERIFIED')

    def test_population_ids_are_exact_unique_and_positive(self):
        self.assertEqual(subject.population({'GENERIC-PROVEN': [1, 2], 'SOURCE-UNCERTAIN': [3]}), {1: 'GENERIC-PROVEN', 2: 'GENERIC-PROVEN', 3: 'SOURCE-UNCERTAIN'})
        for value in ({'GENERIC-PROVEN': [1, 1]}, {'GENERIC-PROVEN': [True]}, {'GENERIC-PROVEN': [0]}, {'GENERIC-PROVEN': [1], 'SOURCE-UNCERTAIN': [1]}):
            with self.assertRaises(ValueError): subject.population(value)

    def test_unrecognized_primitive_and_malformed_hash_are_rejected(self):
        contract = self.contract(); contract['primitive'] = 'fly-to-pin'
        with self.assertRaises(ValueError): subject.validate_contract(contract)
        contract = self.contract(); contract['trace_sha256'] = 'looks-good'
        with self.assertRaises(ValueError): subject.validate_contract(contract)

    def test_case_count_is_not_case_identity(self):
        contract = self.contract(); contract['case_ids'] = [180]
        with self.assertRaises(ValueError): subject.validate_contract(contract)

    def test_metadata_without_real_source_and_trace_bytes_is_not_installable(self):
        self.assertTrue(hasattr(subject, 'verify_artifacts'), 'contract artifact verification is missing')
        with tempfile.TemporaryDirectory() as folder:
            with self.assertRaises(ValueError): subject.verify_artifacts(self.contract(), Path(folder))

    def test_navigation_summary_cannot_be_installed_as_execution_trace(self):
        self.assertTrue(hasattr(subject, 'verify_artifacts'), 'contract artifact verification is missing')
        import json
        with tempfile.TemporaryDirectory() as folder:
            root = Path(folder); contract = self.contract()
            (root / 'actual-owner.cs').write_bytes(b'production owner fixture')
            contract['source_sha256'] = {'actual-owner.cs': hashlib.sha256((root / 'actual-owner.cs').read_bytes()).hexdigest()}
            (root / 'trace.json').write_text(json.dumps({'profile_generated': True, 'arrived': True}), encoding='utf-8')
            contract['trace_file'] = 'trace.json'; contract['trace_sha256'] = hashlib.sha256((root / 'trace.json').read_bytes()).hexdigest()
            with self.assertRaises(ValueError): subject.verify_artifacts(contract, root)


if __name__ == '__main__':
    unittest.main()
