"""Adversarial receipt validation, not simulated gameplay or live quest proof."""
import copy
import hashlib
import json
from pathlib import Path
import tempfile
import unittest

import execution_coverage_335 as subject


class ExecutionTraceIntegrityTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory()
        self.addCleanup(self.temporary.cleanup)
        self.root = Path(self.temporary.name)
        source = b'controlled receipt-validator fixture; not an executed game owner'
        (self.root / 'owner.cs').write_bytes(source)
        self.member = {'quest_id': 10161, 'primitive': 'ground-gameobject-loot',
                       'objective_key': 'item:28116', 'semantics_sha256': 'a' * 64}
        self.contract = {
            'contract_id': 'synthetic-validator-control', 'scope': 'controlled-production-owner-lifecycle',
            'primitive': self.member['primitive'], 'source_sha256': {'owner.cs': hashlib.sha256(source).hexdigest()},
            'trace_file': 'trace.json', 'trace_sha256': 'b' * 64, 'case_ids': ['whole-lifecycle'],
            'members': [self.member], 'stages': list(subject.REQUIRED_STAGES),
            'dispatch_count': 2, 'acknowledgement_count': 2,
            'progress_after_ack': True, 'authoritative_completion': True, 'authoritative_turnin': True,
            'manually_injected_progress_without_action': False, 'navigation_only': False}

    def trace(self, case='whole-lifecycle', member=None):
        result = []
        for index, stage in enumerate(subject.REQUIRED_STAGES):
            event = {**(member or self.member), 'sequence': index, 'stage': stage, 'case_id': case}
            if stage in ('action-dispatch', 'action-acknowledgement', 'authoritative-progress'):
                event['action_id'] = case + '-collect'
            if stage in ('turnin-dispatch', 'turnin-acknowledgement'):
                event['action_id'] = case + '-turnin'
            if stage in ('action-acknowledgement', 'authoritative-progress', 'objective-completion', 'turnin-acknowledgement'):
                event['observation_authoritative'] = True
            if stage == 'authoritative-progress':
                event.update(before=0, after=1)
            result.append(event)
        return result

    def verify(self, trace):
        for index, event in enumerate(trace):
            event['sequence'] = index
        raw = json.dumps(trace).encode('utf-8')
        (self.root / 'trace.json').write_bytes(raw)
        self.contract['trace_sha256'] = hashlib.sha256(raw).hexdigest()
        return subject.verify_artifacts(self.contract, self.root)

    def test_one_complete_ordered_lifecycle_is_accepted(self):
        self.assertEqual(self.verify(self.trace())['exact_members'], 1)

    def test_unexecuted_case_name_cannot_inflate_coverage(self):
        self.contract['case_ids'].append('edge-case-that-did-not-run')
        with self.assertRaises(ValueError):
            self.verify(self.trace())

    def test_two_half_cases_cannot_be_joined_into_a_complete_lifecycle(self):
        trace = self.trace()
        self.contract['case_ids'] = ['first-half', 'second-half']
        for index, event in enumerate(trace):
            event['case_id'] = 'first-half' if index < 9 else 'second-half'
        with self.assertRaises(ValueError):
            self.verify(trace)

    def test_acknowledgement_cannot_borrow_another_case_dispatch(self):
        trace = self.trace()
        self.contract['case_ids'].append('unrelated-case')
        next(event for event in trace if event['stage'] == 'action-acknowledgement')['case_id'] = 'unrelated-case'
        with self.assertRaises(ValueError):
            self.verify(trace)

    def test_travel_cannot_precede_the_plan(self):
        trace = self.trace()
        trace[1], trace[2] = trace[2], trace[1]
        with self.assertRaises(ValueError):
            self.verify(trace)

    def test_completion_cannot_precede_the_acquisition_action(self):
        trace = self.trace()
        completion = next(event for event in trace if event['stage'] == 'objective-completion')
        trace.remove(completion)
        trace.insert(5, completion)
        with self.assertRaises(ValueError):
            self.verify(trace)

    def test_next_schedule_cannot_precede_turnin_acknowledgement(self):
        trace = self.trace()
        trace[-1], trace[-2] = trace[-2], trace[-1]
        with self.assertRaises(ValueError):
            self.verify(trace)

    def test_duplicate_counter_transition_cannot_create_more_progress(self):
        trace = self.trace()
        progress = next(event for event in trace if event['stage'] == 'authoritative-progress')
        trace.insert(trace.index(progress) + 1, copy.deepcopy(progress))
        with self.assertRaises(ValueError):
            self.verify(trace)

    def test_disconnected_counter_transition_is_not_a_continuous_lifecycle(self):
        trace = self.trace()
        progress = next(event for event in trace if event['stage'] == 'authoritative-progress')
        trace.insert(trace.index(progress) + 1, {**progress, 'before': 20, 'after': 21})
        with self.assertRaises(ValueError):
            self.verify(trace)

    def test_contiguous_later_observation_of_same_acknowledged_action_is_allowed(self):
        trace = self.trace()
        progress = next(event for event in trace if event['stage'] == 'authoritative-progress')
        trace.insert(trace.index(progress) + 1, {**progress, 'before': 1, 'after': 2})
        self.assertEqual(self.verify(trace)['acknowledgements'], 2)

    def test_interleaved_complete_cases_are_independently_validated(self):
        self.contract['case_ids'].append('second-complete-case')
        left, right = self.trace(), self.trace('second-complete-case')
        self.contract.update(dispatch_count=4, acknowledgement_count=4)
        combined = [event for pair in zip(left, right) for event in pair]
        self.assertEqual(self.verify(combined)['dispatches'], 4)

    def test_later_dispatch_must_acquire_a_fresh_live_target_and_approach(self):
        trace = self.trace()
        progress = next(event for event in trace if event['stage'] == 'authoritative-progress')
        insertion = trace.index(progress) + 1
        extra = [{**self.member, 'case_id': 'whole-lifecycle', 'stage': stage, 'action_id': 'second-collect',
                  'observation_authoritative': True, 'before': 1, 'after': 2}
                 for stage in ('action-dispatch', 'action-acknowledgement', 'authoritative-progress')]
        trace[insertion:insertion] = extra
        self.contract.update(dispatch_count=3, acknowledgement_count=3)
        with self.assertRaises(ValueError):
            self.verify(trace)

    def test_real_reacquisition_can_support_another_collection_action(self):
        trace = self.trace()
        progress = next(event for event in trace if event['stage'] == 'authoritative-progress')
        insertion = trace.index(progress) + 1
        extra = [{**self.member, 'case_id': 'whole-lifecycle', 'stage': stage, 'action_id': 'second-collect',
                  'observation_authoritative': True, 'before': 1, 'after': 2}
                 for stage in ('live-acquisition', 'safe-approach', 'action-dispatch',
                               'action-acknowledgement', 'authoritative-progress')]
        trace[insertion:insertion] = extra
        self.contract.update(dispatch_count=3, acknowledgement_count=3)
        self.assertEqual(self.verify(trace)['dispatches'], 3)


if __name__ == '__main__':
    unittest.main()
