"""Real output-byte and population checks; none of these tests prove gameplay."""
import copy
import gzip
import hashlib
import json
from pathlib import Path
import tempfile
import unittest

import execution_coverage_335 as subject


def fixture():
    rows = []
    families = {name: {'required_ids': [], 'candidate_source_ids': [], 'tested_exact_ids': []}
                for name in subject.PRIMITIVES}
    for ident in (10161, 10162):
        requirement = subject.semantics(ident, 'quest-turnin', 'turnin',
            {'enders': [{'entry': 19367, 'type': 'Creature'}], 'authoritative_reward_acknowledgement_required': True})
        rows.append({'quest_id': ident, **subject.grade('GENERIC-PROVEN', [requirement], []),
                     'required_primitives': [requirement], 'item_source_alternatives': [],
                     'all_required_stages': list(subject.REQUIRED_STAGES)})
    families['quest-turnin']['required_ids'] = [10161, 10162]
    return {'rows': rows, 'families': families, 'quest_count': 2,
            'classification_counts': {'EXECUTION-UNVERIFIED': 2},
            'legacy_counts': {'GENERIC-PROVEN': 2}, 'accepted_contracts': 0, 'execution_proven_ids': []}


class ExecutionInventoryIntegrityTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory()
        self.addCleanup(self.temporary.cleanup)
        self.root = Path(self.temporary.name)

    def export(self, result=None):
        self.assertTrue(callable(getattr(subject, 'write_inventory_outputs', None)),
                        'published inventory has no canonical-byte output writer')
        result = fixture() if result is None else result
        subject.write_inventory_outputs(self.root, result)

    def verify(self, root=None):
        self.assertTrue(callable(getattr(subject, 'verify_inventory_outputs', None)),
                        'published inventory has no byte-and-membership verification gate')
        return subject.verify_inventory_outputs(root or self.root)

    def rewrite(self, name, value, rows=False):
        raw = (''.join(json.dumps(row, sort_keys=True) + '\n' for row in value)
               if rows else json.dumps(value, indent=2) + '\n').encode('utf-8')
        (self.root / name).write_bytes(raw)
        summary = json.loads((self.root / 'summary.json').read_bytes())
        summary['output_sha256'][name] = hashlib.sha256(raw).hexdigest()
        (self.root / 'summary.json').write_bytes((json.dumps(summary, indent=2) + '\n').encode())

    def test_real_published_plain_files_match_declared_logical_hashes(self):
        root = Path(__file__).resolve().parents[2] / 'docs/audit/2026-10-01/execution-coverage'
        summary = json.loads((root / 'summary.json').read_bytes())
        for name in ('objective-primitive-ids.json', 'execution-classification-ids.json'):
            with self.subTest(name=name):
                self.assertEqual(hashlib.sha256((root / name).read_bytes()).hexdigest(), summary['output_sha256'][name])

    def test_export_is_utf8_lf_and_does_not_mutate_inputs(self):
        data = fixture()
        original = copy.deepcopy(data)
        self.export(data)
        self.assertEqual(data, original)
        for path in self.root.iterdir():
            raw = path.read_bytes()
            self.assertNotIn(b'\r', raw)
            self.assertTrue(raw.endswith(b'\n'))
            raw.decode('utf-8')
        self.assertEqual(self.verify()['quest_count'], 2)

    def test_complete_plain_and_gzip_ledgers_have_equal_logical_identity(self):
        self.export()
        first = self.verify()
        ledger = self.root / 'execution-ledger.jsonl'
        (self.root / 'execution-ledger.jsonl.gz').write_bytes(gzip.compress(ledger.read_bytes(), mtime=0))
        ledger.unlink()
        second = self.verify()
        self.assertEqual(first['logical_sha256'], second['logical_sha256'])
        self.assertNotEqual(first['stored_sha256'], second['stored_sha256'])
        self.assertTrue(second['integrity_only_not_gameplay_proof'])

    def test_conflicting_plain_and_compressed_members_are_rejected(self):
        self.export()
        raw = (self.root / 'execution-ledger.jsonl').read_bytes()
        (self.root / 'execution-ledger.jsonl.gz').write_bytes(gzip.compress(raw, mtime=0))
        with self.assertRaises(ValueError): self.verify()

    def test_raw_byte_change_without_new_receipt_is_rejected(self):
        self.export()
        path = self.root / 'objective-primitive-ids.json'
        path.write_bytes(path.read_bytes().replace(b'\n', b'\r\n'))
        with self.assertRaises(ValueError): self.verify()

    def test_duplicate_quest_is_rejected_even_with_updated_hash(self):
        self.export()
        rows = fixture()['rows']
        self.rewrite('execution-ledger.jsonl', rows + [copy.deepcopy(rows[0])], rows=True)
        with self.assertRaises(ValueError): self.verify()

    def test_partition_cannot_drop_a_quest_or_reassign_its_class(self):
        self.export()
        for value in ({'EXECUTION-UNVERIFIED': [10161]},
                      {'EXECUTION-UNVERIFIED': [10161], 'GENERIC-PROVEN': [10162]}):
            with self.subTest(value=value):
                self.rewrite('execution-classification-ids.json', value)
                with self.assertRaises(ValueError): self.verify()

    def test_family_membership_cannot_borrow_an_unknown_or_untested_id(self):
        self.export()
        for key in ('required_ids', 'candidate_source_ids', 'tested_exact_ids'):
            with self.subTest(key=key):
                families = fixture()['families']
                families['quest-turnin'][key].append(55555 if key != 'tested_exact_ids' else 10161)
                self.rewrite('objective-primitive-ids.json', families)
                with self.assertRaises(ValueError): self.verify()

    def test_semantic_fields_cannot_keep_an_old_semantics_hash(self):
        self.export()
        rows = fixture()['rows']
        rows[0]['required_primitives'][0]['primary_semantics']['enders'][0]['entry'] = 99999
        self.rewrite('execution-ledger.jsonl', rows, rows=True)
        with self.assertRaises(ValueError): self.verify()

    def test_manifest_cannot_select_paths_outside_its_inventory(self):
        self.export()
        summary = json.loads((self.root / 'summary.json').read_bytes())
        summary['output_sha256']['../foreign.json'] = 'a' * 64
        (self.root / 'summary.json').write_bytes(json.dumps(summary).encode())
        with self.assertRaises(ValueError): self.verify()

    def test_existing_outputs_are_never_overwritten(self):
        self.export()
        before = {path.name: path.read_bytes() for path in self.root.iterdir()}
        with self.assertRaises(FileExistsError): self.export()
        self.assertEqual({path.name: path.read_bytes() for path in self.root.iterdir()}, before)

    def test_real_dataset_remains_exactly_4335_without_new_proof_claims(self):
        root = Path(__file__).resolve().parents[2] / 'docs/audit/2026-10-01/execution-coverage'
        value = self.verify(root)
        self.assertEqual(value['quest_count'], 4335)
        self.assertEqual(value['classification_counts']['EXECUTION-UNVERIFIED'], 3030)
        self.assertEqual(value['accepted_contracts'], 0)
        self.assertTrue(value['integrity_only_not_gameplay_proof'])


if __name__ == '__main__':
    unittest.main()
