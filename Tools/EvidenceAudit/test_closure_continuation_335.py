"""A smaller audit must consume the previous exact ledger without rewriting it."""
import copy
import importlib.util
from pathlib import Path
import tempfile
import unittest


class ClosureContinuationTests(unittest.TestCase):
    def operation(self, filename, name):
        spec = importlib.util.spec_from_file_location('continuation_test_' + filename, Path(__file__).with_name(filename))
        module = importlib.util.module_from_spec(spec); spec.loader.exec_module(module)
        value = getattr(module, name, None)
        self.assertTrue(callable(value), 'Continuation needs ' + name)
        return value

    def test_legacy_secondary_evidence_is_retained(self):
        previous = {'secondary_reference': {'source': 'retained-original-comparison'}}
        result = self.operation('primary_closure_335.py', 'retained_secondary_evidence')(previous)
        self.assertEqual(result, previous['secondary_reference'])

    def test_prior_closure_secondary_evidence_is_retained(self):
        previous = {'secondary_evidence_retained': {'source': 'retained-original-comparison'}}
        result = self.operation('primary_closure_335.py', 'retained_secondary_evidence')(previous)
        self.assertEqual(result, previous['secondary_evidence_retained'])

    def test_missing_secondary_provenance_is_not_replaced_with_empty_evidence(self):
        with self.assertRaisesRegex(ValueError, 'secondary'):
            self.operation('primary_closure_335.py', 'retained_secondary_evidence')({})

    def test_disagreeing_legacy_and_closure_provenance_is_rejected(self):
        with self.assertRaisesRegex(ValueError, 'secondary'):
            self.operation('primary_closure_335.py', 'retained_secondary_evidence')(
                {'secondary_reference': {'id': 1}, 'secondary_evidence_retained': {'id': 2}})

    def test_continuation_cannot_mutate_prior_secondary_evidence(self):
        previous = {'secondary_evidence_retained': {'nested': {'id': 1}}}; before = copy.deepcopy(previous)
        result = self.operation('primary_closure_335.py', 'retained_secondary_evidence')(previous)
        result['nested']['id'] = 2
        self.assertEqual(previous, before)

    def test_new_fixture_directory_is_selected_from_the_actual_knowledge_manifest(self):
        with tempfile.TemporaryDirectory() as folder:
            root = Path(folder)
            result = self.operation('run_quest_closure_335.py', 'closure_evidence_directory')(
                root, {'closure_evidence_directory': 'docs/audit/2026-09-30/quest-dependencies'})
            self.assertEqual(result, (root / 'docs/audit/2026-09-30/quest-dependencies').resolve())

    def test_legacy_knowledge_retains_its_existing_fixture(self):
        with tempfile.TemporaryDirectory() as folder:
            root = Path(folder)
            result = self.operation('run_quest_closure_335.py', 'closure_evidence_directory')(root, {})
            self.assertEqual(result, (root / 'docs/audit/2026-09-29/wholesome-primary-closure').resolve())

    def test_fixture_path_cannot_escape_the_repo_or_come_from_a_live_folder(self):
        for relative in ['../external', '/external', 'D:/external', 'runtime-snapshot/Bots', 'docs/audit/../../../external']:
            with self.subTest(relative=relative), tempfile.TemporaryDirectory() as folder:
                with self.assertRaisesRegex(ValueError, 'evidence directory'):
                    self.operation('run_quest_closure_335.py', 'closure_evidence_directory')(
                        Path(folder), {'closure_evidence_directory': relative})


if __name__ == '__main__':
    unittest.main()
