"""Behavioral tests for audit measurements, not simulations of the game client."""
import unittest
from runtime import parse_records, analyze_text, cluster_navigation, distribution


class RuntimeTests(unittest.TestCase):
    def test_continuation_stack_is_one_record(self):
        r = parse_records('[12:00:00.000] System.NullReferenceException\n   at A.B()\n[12:00:01.000] next')
        self.assertEqual(len(r), 2)
        self.assertEqual(r[0]['end_line'], 2)
        self.assertIn('A.B()', r[0]['text'])

    def test_midnight_is_a_rollover_not_negative_duration(self):
        r = parse_records('[23:59:59.500] old\n[00:00:00.250] new')
        self.assertEqual(r[1]['ms'] - r[0]['ms'], 750)
        self.assertEqual(r[1]['clock_segment'], 0)

    def test_small_backward_clock_starts_new_segment(self):
        r = parse_records('[12:00:01.000] old\n[12:00:00.250] new')
        self.assertNotEqual(r[1]['clock_segment'], r[0]['clock_segment'])

    def test_restarts_have_separate_counts_not_cumulative_counts(self):
        text = '\n'.join(['[12:00:00.000] Starting the bot.', '[12:00:00.010] Building spell book',
                         '[12:01:00.000] Starting the bot.', '[12:01:00.010] Building spell book',
                         '[12:01:00.020] Building spell book'])
        result = analyze_text(text, 'test.log')
        self.assertEqual([x['spellbook_builds'] for x in result['runs']], [1, 2])
        self.assertEqual(result['raw_line_matches']['Building spell book'], 3)

    def test_bot_names_with_spaces_are_not_dropped(self):
        r = analyze_text('[12:00:00.000] [Pulse] Slow bot root: 260ms bot=Wholesome Auto Quest poi=Sell combat=False', 'test.log')
        self.assertEqual(r['samples'].get('root:all'), [260])

    def test_plugin_and_state_metrics_do_not_mix(self):
        text = '\n'.join(['[12:00:00.000] [Pulse] Slow plugin: name=AutoEquip2 elapsed=550ms',
                         '[12:00:01.000] [Pulse] Slow bot root: 260ms bot=Questing poi=Sell combat=False'])
        result = analyze_text(text, 'test.log')
        self.assertEqual(result['samples']['plugin:AutoEquip2'], [550])
        self.assertEqual(result['samples']['root:Questing:Sell:False'], [260])

    def test_cluster_merges_repeated_partial_route_not_other_floor(self):
        base = dict(capture='test.log', run=1, clock_segment=0, map=1, destination=[10, 0, 40],
                    poi='Sell', location=[0, 0, 0], signature='partial_path', line=1, ms=0)
        events = [base, dict(base, line=2, ms=5000), dict(base, line=3, ms=6000, location=[0, 0, 50])]
        clusters = cluster_navigation(events, radius=20, window_ms=30000)
        self.assertEqual(sorted(c['event_count'] for c in clusters), [1, 2])

    def test_unknown_locations_are_not_silently_spatially_grouped(self):
        e = dict(capture='test.log', run=1, clock_segment=0, map=None, destination=None,
                 poi=None, location=None, signature='path_failure', line=1, ms=0)
        self.assertEqual(len(cluster_navigation([e, dict(e, line=2, ms=1)])), 2)

    def test_cluster_does_not_chain_drift_away_from_anchor(self):
        base = dict(capture='test.log', run=1, clock_segment=0, map=1, destination=[100, 0, 0],
                    poi='Sell', signature='stuck', line=1, ms=0, location=[0, 0, 0])
        events = [base, dict(base, line=2, ms=1000, location=[15, 0, 0]),
                  dict(base, line=3, ms=2000, location=[30, 0, 0])]
        self.assertEqual(len(cluster_navigation(events, radius=20)), 2)

    def test_percentiles_are_linear_and_empty_is_explicit(self):
        self.assertEqual(distribution([]), {'n': 0})
        self.assertEqual(distribution([0, 100])['p50'], 50)
        self.assertEqual(distribution([100])['p99'], 100)
        self.assertEqual(distribution([0, 100])['p95'], 95)

    def test_raw_counts_are_lines_not_number_of_occurrences(self):
        r = analyze_text('[12:00:00.000] Exception Exception Blackspot blackspot', 'test.log')
        self.assertEqual(r['raw_line_matches']['Exception'], 1)
        self.assertEqual(r['raw_line_matches']['Blackspot'], 1)
        self.assertEqual(r['blackspot_case_insensitive_lines'], 1)

    def test_navigation_coordinates_are_parsed_without_inferring_platform_position(self):
        r = analyze_text('[12:00:00.000] [Nav] MoveTo failed: map=1 from=(1.00, 2.00, 3.00) to=(4.00, 5.00, 60.00) pathCount=2 pathIndex=2 partial=True', 'x.log')
        self.assertEqual(r['navigation'][0]['location'], [1., 2., 3.])
        self.assertEqual(r['navigation'][0]['destination'], [4., 5., 60.])
        self.assertNotIn('platform_location', r['navigation'][0])

    def test_nearby_time_does_not_create_causal_label(self):
        r = analyze_text('[12:00:00.000] [Pulse] Slow plugin: name=Foo elapsed=550ms\n[12:00:00.001] We are stuck! loc: (1, 2, 3)', 'x.log')
        self.assertNotIn('causes', r)


if __name__ == '__main__':
    unittest.main()
