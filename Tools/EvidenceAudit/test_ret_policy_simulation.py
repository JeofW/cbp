"""Test evidence identity, numerical invariants and explicit policy trade-offs."""
import itertools
import json
import math
from pathlib import Path
import unittest
from xml.etree import ElementTree as ET

from ret_policy_simulation import FACT_PATH, POLICIES, Scenario, load_facts, simulate, spell_facts


class RetPolicySimulationTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.facts = load_facts()

    def test_original_mechanics_and_rank_availability(self):
        self.assertEqual(self.facts['rules']['art_of_war_partial_id'], 53489)
        self.assertEqual(self.facts['rules']['art_of_war_instant_id'], 59578)
        self.assertEqual(self.facts['rules']['divine_storm_cap'], 4)
        level60 = spell_facts(self.facts, 60)
        self.assertNotIn('Divine Plea', level60)
        self.assertEqual(level60['Consecration']['cost_base_percent'], 22)
        self.assertEqual(level60['Holy Wrath']['cost_base_percent'], 20)
        self.assertEqual(level60['Crusader Strike']['cooldown_seconds'], 4)
        self.assertEqual(level60['Divine Storm']['cost_base_percent'], 12)
        self.assertIn('Divine Plea', spell_facts(self.facts, 80))

    def test_same_scenario_is_deterministic(self):
        scenario = Scenario(targets=3, durability=3, undead=True)
        self.assertEqual(simulate(scenario, 'retained', self.facts), simulate(scenario, 'retained', self.facts))

    def test_all_policy_resource_and_time_invariants(self):
        for level, targets, mana, policy in itertools.product((60, 80), (1, 2, 3, 4, 6), (.2, 1.), POLICIES):
            with self.subTest(level=level, targets=targets, mana=mana, policy=policy):
                result = simulate(Scenario(level=level, targets=targets, mana_fraction=mana), policy, self.facts)
                for key in ('kill_seconds', 'mana_spent', 'refill_seconds', 'healing', 'overkill'):
                    self.assertTrue(math.isfinite(result[key]))
                    self.assertGreaterEqual(result[key], 0)
                self.assertLessEqual(result['kill_seconds'], 120)
                self.assertGreaterEqual(result['mana_end_fraction'], 0)
                self.assertLessEqual(result['mana_end_fraction'], 1)

    def test_moving_packs_never_receive_a_ground_patch(self):
        for policy in POLICIES:
            result = simulate(Scenario(targets=4, durability=3, moving=True), policy, self.facts)
            self.assertEqual(result['ability_counts'].get('Consecration', 0), 0)

    def test_holy_wrath_never_targets_an_ordinary_pack(self):
        for policy in POLICIES:
            result = simulate(Scenario(targets=4, durability=3), policy, self.facts)
            self.assertEqual(result['ability_counts'].get('Holy Wrath', 0), 0)

    def test_corruption_and_vengeance_have_matching_numeric_behavior(self):
        left = simulate(Scenario(durability=6, seal='Vengeance'), 'retained', self.facts)
        right = simulate(Scenario(durability=6, seal='Corruption'), 'retained', self.facts)
        left.pop('scenario'); right.pop('scenario')
        self.assertEqual(left, right)

    def test_drink_speed_changes_only_the_explicit_refill_assumption(self):
        scenario = Scenario(targets=4, durability=3, wise=False)
        slow = simulate(scenario, 'retained', self.facts, drink_rate=.02)
        fast = simulate(scenario, 'retained', self.facts, drink_rate=.08)
        self.assertEqual(slow['kill_seconds'], fast['kill_seconds'])
        self.assertEqual(slow['ability_counts'], fast['ability_counts'])
        self.assertGreaterEqual(slow['refill_seconds'], fast['refill_seconds'])

    def test_invalid_input_is_not_a_simulation_result(self):
        for scenario, policy in ((Scenario(targets=0), 'retained'), (Scenario(mana_fraction=2), 'retained'), (Scenario(), 'unknown')):
            with self.assertRaises(ValueError):
                simulate(scenario, policy, self.facts)

    def test_actual_template_has_original_names_and_valid_leveling_progression(self):
        evidence = json.loads(FACT_PATH.with_name('ret_335a_talents.json').read_text(encoding='utf-8'))
        self.assertEqual(evidence['client_build'], 12340)
        facts = {(row['tab'], row['name']): row for row in evidence['entries']}
        by_id = {row['id']: row for row in evidence['entries']}
        repo = Path(__file__).resolve().parents[2]
        template = ET.parse(repo / 'runtime-snapshot/Plugins/Talented/Talent Builds/Paladin - Retribution.xml')
        wanted = [(int(row.attrib['Tab']), row.attrib['Name'], int(row.attrib['Count'])) for row in template.findall('Talent')]
        self.assertEqual(sum(count for _, _, count in wanted), 71)
        self.assertEqual(len(wanted), len({(tab, name) for tab, name, _ in wanted}))
        learned = {row['id']: 0 for row in facts.values()}
        first_level = {}
        for level in range(10, 81):
            for tab, name, count in wanted:
                actual = facts[tab, name]
                self.assertLessEqual(count, actual['maximum'])
                spent = sum(learned[row['id']] for row in facts.values() if row['tab'] == tab)
                prerequisite = by_id.get(actual['prerequisite'])
                if learned[actual['id']] >= count or spent < actual['tier'] * 5:
                    continue
                if prerequisite and learned[prerequisite['id']] <= actual['prerequisite_rank_zero_based']:
                    continue
                learned[actual['id']] += 1
                first_level.setdefault(name, level)
                break
            else:
                self.fail(f'No tier/rank-valid template point at level {level}; live prerequisite observation remains separately required')
        self.assertEqual(first_level['The Art of War'], 40)
        self.assertEqual(first_level['Crusader Strike'], 50)
        self.assertEqual(first_level['Divine Storm'], 60)


if __name__ == '__main__':
    unittest.main()
