"""Public validation receipts must distinguish observation counts from execution."""
from collections import Counter
import importlib.util
from pathlib import Path
import unittest

import run_quest_closure_335 as closure


class ExecutionScopeReportingTests(unittest.TestCase):
    def report(self, groups):
        self.assertTrue(hasattr(closure, 'execution_scope_report'),
                        'closure verification still omits explicit execution scope')
        return closure.execution_scope_report(groups)

    def test_legacy_pass_does_not_become_lifecycle_coverage(self):
        value = self.report({'GENERIC-PROVEN': [10161], 'STRATEGY-PROVEN': [9066]})
        self.assertEqual(value['execution_classification_counts'], {'EXECUTION-UNVERIFIED': 2})
        self.assertEqual(value['end_to_end_proven_quest_ids'], [])
        self.assertFalse(value['live_completion_proven'])
        self.assertEqual(value['evidence_scope'], 'controlled-observation-and-declared-owner-checks')

    def test_source_and_data_holds_are_retained(self):
        groups = {'GENERIC-PROVEN': [1], 'DATA-INVALID/INCOMPLETE': [2],
                  'UNSUPPORTED-SCRIPTED': [3], 'SOURCE-UNCERTAIN': [4],
                  'LIVE-ACCEPTANCE-REQUIRED': [5]}
        result = self.report(groups)
        self.assertEqual(sum(result['execution_classification_counts'].values()), 5)
        for key in groups.keys() - {'GENERIC-PROVEN'}:
            self.assertEqual(result['execution_classification_counts'][key], 1)

    def test_duplicate_membership_cannot_make_a_receipt(self):
        with self.assertRaises(ValueError):
            self.report({'GENERIC-PROVEN': [10161], 'SOURCE-UNCERTAIN': [10161]})

    def test_actual_shipped_population_is_exact_and_conservative(self):
        import json
        root = Path(__file__).resolve().parents[2]
        groups = json.loads((root / 'docs/audit/2026-10-01/required-stock/classification-ids.json').read_bytes())
        result = self.report(groups)
        self.assertEqual(sum(result['execution_classification_counts'].values()), 4335)
        self.assertEqual(result['execution_classification_counts']['EXECUTION-UNVERIFIED'], 3030)
        self.assertEqual(result['end_to_end_proven_quest_ids'], [])


if __name__ == '__main__':
    unittest.main()
