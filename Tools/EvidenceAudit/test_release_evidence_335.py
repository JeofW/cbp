"""A release cannot borrow another commit's green results or omit runtime data."""
import importlib.util
from pathlib import Path
import subprocess
import tempfile
import unittest


class ReleaseEvidenceTests(unittest.TestCase):
    def owner(self):
        path = Path(__file__).with_name('release_evidence_335.py')
        self.assertTrue(path.is_file(), 'Release verification must bind the actual commit and runtime data')
        spec = importlib.util.spec_from_file_location('release_evidence_test_owner', path)
        module = importlib.util.module_from_spec(spec)
        spec.loader.exec_module(module)
        return module

    def gate(self, **changes):
        candidate = 'a' * 40
        identity = {'production_commit': candidate, 'fixture_commit': candidate, 'working_tree_modified': False}
        summary = {'base': candidate, 'source_stable': True, 'failed': [], 'stages': 34}
        inputs = {'runtime-snapshot/Bots/WholesomeAutoQuest-master/quest_data/quest_data.repairs.json': 'b' * 64}
        values = dict(candidate=candidate, identity=identity, summary=summary,
                      before=inputs.copy(), after=inputs.copy(), candidate_inputs=inputs.copy())
        values.update(changes)
        return self.owner().validate_release_gate(**values)

    def test_exact_clean_candidate_and_complete_matching_inputs_pass(self):
        self.gate()

    def test_same_short_prefix_is_not_the_same_full_commit(self):
        with self.assertRaisesRegex(ValueError, 'commit'):
            self.gate(candidate='a' * 8 + 'c' * 32)

    def test_abbreviated_sha_is_rejected(self):
        with self.assertRaisesRegex(ValueError, 'full.*commit'):
            self.gate(candidate='a' * 8)

    def test_dirty_working_tree_cannot_be_released_as_its_base_commit(self):
        with self.assertRaisesRegex(ValueError, 'working tree'):
            self.gate(identity={'production_commit': 'a' * 40, 'fixture_commit': 'a' * 40, 'working_tree_modified': True})

    def test_missing_clean_tree_receipt_is_not_assumed_clean(self):
        with self.assertRaisesRegex(ValueError, 'working tree'):
            self.gate(identity={'production_commit': 'a' * 40, 'fixture_commit': 'a' * 40})

    def test_green_summary_from_another_base_is_rejected(self):
        with self.assertRaisesRegex(ValueError, 'commit'):
            self.gate(summary={'base': 'c' * 40, 'source_stable': True, 'failed': [], 'stages': 34})

    def test_missing_runtime_json_from_candidate_manifest_is_rejected(self):
        with self.assertRaisesRegex(ValueError, 'inputs'):
            self.gate(candidate_inputs={})

    def test_changed_json_after_tests_is_rejected(self):
        with self.assertRaisesRegex(ValueError, 'inputs'):
            self.gate(after={'runtime-snapshot/Bots/WholesomeAutoQuest-master/quest_data/quest_data.repairs.json': 'c' * 64})

    def test_failed_stage_cannot_be_hidden_by_matching_source(self):
        with self.assertRaisesRegex(ValueError, 'successful'):
            self.gate(summary={'base': 'a' * 40, 'source_stable': True, 'failed': ['strategy'], 'stages': 34})

    def test_zero_stages_do_not_prove_a_release(self):
        with self.assertRaisesRegex(ValueError, 'successful'):
            self.gate(summary={'base': 'a' * 40, 'source_stable': True, 'failed': [], 'stages': 0})

    def test_runtime_json_change_is_included_in_source_fingerprint(self):
        owner = self.owner()
        with tempfile.TemporaryDirectory() as folder:
            root = Path(folder)
            subprocess.run(['git', 'init', '-q', str(root)], check=True, capture_output=True)
            path = root / 'runtime-snapshot/Bots/WholesomeAutoQuest-master/quest_data/quest_data.repairs.json'
            path.parent.mkdir(parents=True)
            path.write_text('{"revision":1}\n', encoding='utf-8')
            before = owner.source_inputs(root)
            self.assertIn(path.relative_to(root).as_posix(), before)
            path.write_text('{"revision":2}\n', encoding='utf-8')
            self.assertNotEqual(before, owner.source_inputs(root))

    def test_deleted_tracked_input_is_not_silently_omitted(self):
        owner = self.owner()
        with tempfile.TemporaryDirectory() as folder:
            root = Path(folder)
            subprocess.run(['git', 'init', '-q', str(root)], check=True, capture_output=True)
            path = root / 'quest_strategies.json'
            path.write_text('{}', encoding='utf-8')
            subprocess.run(['git', 'add', 'quest_strategies.json'], cwd=root, check=True, capture_output=True)
            path.unlink()
            with self.assertRaises(FileNotFoundError):
                owner.source_inputs(root)


if __name__ == '__main__':
    unittest.main()
