"""Credit search hints need ordinary producer and spawn evidence, never names."""
import copy
import importlib.util
from pathlib import Path
import unittest
import test_quest_repair_pack_335 as fixtures


class QuestCreditSourceExporterTests(unittest.TestCase):
    def fixture(self):
        data, tables = fixtures.PrimaryRepairExporterTests().fixture()
        data['Quests'][0]['Objectives'][0]['MobId'] = 299
        tables['quest_template'][0]['RequiredNpcOrGo1'] = 299
        actor = tables['creature_template'][0]
        actor.update(KillCredit1=299, KillCredit2=0, npcflag=0, unit_flags=0, unit_flags2=0, dynamicflags=0,
                     VehicleId=0, faction=14)
        tables['creature'][0].update(unit_flags=0, dynamicflags=0, ScriptName='')
        tables['creature_addon'] = []; tables['creature_template_addon'] = []
        return data, tables

    def derive(self, data, tables):
        path = Path(__file__).with_name('quest_credit_source_335.py')
        self.assertTrue(path.is_file(), 'Credit hints need their conservative source exporter')
        spec = importlib.util.spec_from_file_location('credit_source_under_test', path)
        owner = importlib.util.module_from_spec(spec); spec.loader.exec_module(owner)
        return owner.derive(data, tables, {}, {}, {'CoreRevision': '1' * 40, 'DatabaseRevision': 'fixture', 'SourceSqlSha256': '2' * 64})

    def test_explicit_credit_producer_keeps_original_quest_identity(self):
        data, tables = self.fixture(); before = copy.deepcopy((data, tables))
        patch, review = self.derive(data, tables)
        self.assertEqual(len(patch['ObjectiveCreditSources']), 1)
        row = patch['ObjectiveCreditSources'][0]
        self.assertEqual((row['QuestId'], row['CreditId'], row['CreatureId'], row['RequiredCount']), (101, 299, 201, 3))
        self.assertEqual(row['Points'], [{'Map': 0, 'X': 10.0, 'Y': 20.0, 'Z': 30.0}])
        self.assertEqual((data, tables), before)
        self.assertFalse(review['live_completion_proven'])

    def test_second_credit_slot_is_an_independent_valid_binding(self):
        data, tables = self.fixture(); tables['creature_template'][0].update(KillCredit1=0, KillCredit2=299)
        self.assertEqual(self.derive(data, tables)[0]['ObjectiveCreditSources'][0]['CreditField'], 'KillCredit2')

    def test_unrelated_or_duplicate_credit_slots_are_rejected(self):
        for first, second in ((300, 0), (299, 299)):
            data, tables = self.fixture(); tables['creature_template'][0].update(KillCredit1=first, KillCredit2=second)
            self.assertEqual(self.derive(data, tables)[0]['ObjectiveCreditSources'], [])

    def test_nonattackable_dead_vehicle_and_npc_roles_do_not_become_kill_hints(self):
        for field, value in (('unit_flags', 2), ('unit_flags', 256), ('unit_flags', 0x2000000),
                             ('unit_flags2', 1), ('dynamicflags', 32), ('VehicleId', 1), ('npcflag', 2)):
            data, tables = self.fixture(); tables['creature_template'][0][field] = value
            self.assertEqual(self.derive(data, tables)[0]['ObjectiveCreditSources'], [])

    def test_unresolved_script_name_or_state_trigger_is_not_ordinary_combat(self):
        for mode in ('cpp', 'unknown-ai', 'spellhit', 'timed-list'):
            data, tables = self.fixture()
            if mode == 'cpp': tables['creature_template'][0]['ScriptName'] = 'unverified'
            elif mode == 'unknown-ai': tables['creature_template'][0]['AIName'] = 'EventAI'
            else:
                tables['creature_template'][0]['AIName'] = 'SmartAI'
                tables['smart_scripts'] = [{'source_type': 0, 'entryorguid': 201, 'id': 0, 'link': 0,
                    'event_type': 8 if mode == 'spellhit' else 38, 'action_type': 11 if mode == 'spellhit' else 80}]
            self.assertEqual(self.derive(data, tables)[0]['ObjectiveCreditSources'], [])

    def test_template_or_spawn_dead_pose_is_not_a_live_target(self):
        for table, key, value in (('creature_template_addon', 'entry', 201), ('creature_addon', 'guid', 77)):
            data, tables = self.fixture(); tables[table] = [{key: value, 'bytes1': 7}]
            self.assertEqual(self.derive(data, tables)[0]['ObjectiveCreditSources'], [])

    def test_actual_pinned_StandState_dead_pose_is_not_a_live_target(self):
        for table, key, value in (('creature_template_addon', 'entry', 201), ('creature_addon', 'guid', 77)):
            data, tables = self.fixture(); tables[table] = [{key: value, 'StandState': 7, 'auras': ''}]
            self.assertEqual(self.derive(data, tables)[0]['ObjectiveCreditSources'], [])

    def test_missing_pinned_StandState_is_not_assumed_standing(self):
        data, tables = self.fixture(); tables['creature_template_addon'] = [{'entry': 201, 'auras': ''}]
        self.assertEqual(self.derive(data, tables)[0]['ObjectiveCreditSources'], [])

    def test_explicit_pinned_standing_state_retains_an_ordinary_source(self):
        data, tables = self.fixture(); tables['creature_template_addon'] = [{'entry': 201, 'StandState': 0, 'auras': ''}]
        self.assertEqual(len(self.derive(data, tables)[0]['ObjectiveCreditSources']), 1)

    def test_event_phase_pool_zero_respawn_and_spawn_flags_remain_conditional(self):
        for mode in ('event', 'phase', 'pool', 'respawn', 'flags'):
            data, tables = self.fixture()
            if mode == 'event': tables['game_event_creature'] = [{'guid': 77}]
            elif mode == 'phase': tables['creature'][0]['phaseMask'] = 2
            elif mode == 'pool': tables['pool_members'] = [{'type': 0, 'spawnId': 77}]
            elif mode == 'respawn': tables['creature'][0]['spawntimesecs'] = 0
            else: tables['creature'][0]['unit_flags'] = 256
            self.assertEqual(self.derive(data, tables)[0]['ObjectiveCreditSources'], [])

    def test_quest_field_conflict_or_script_credit_cannot_be_hidden(self):
        for mode in ('field', 'script', 'count'):
            data, tables = self.fixture()
            if mode == 'field': tables['quest_template'][0]['AllowableRaces'] = 690
            elif mode == 'script':
                data['Quests'][0]['SpecialFlags'] = 32; tables['quest_template_addon'][0]['SpecialFlags'] = 32
            else: tables['quest_template'][0]['RequiredNpcOrGoCount1'] = 4
            self.assertEqual(self.derive(data, tables)[0]['ObjectiveCreditSources'], [])

    def test_geometry_for_real_credit_entry_is_already_represented(self):
        data, tables = self.fixture(); data['CreatureSpawns']['299'] = [{'Map': 0, 'X': 1, 'Y': 2, 'Z': 3}]
        self.assertEqual(self.derive(data, tables)[0]['ObjectiveCreditSources'], [])

    def test_shared_numeric_gameobject_pool_does_not_hide_creature(self):
        data, tables = self.fixture(); tables['pool_members'] = [{'type': 1, 'spawnId': 77}]
        self.assertEqual(len(self.derive(data, tables)[0]['ObjectiveCreditSources']), 1)

    def test_missing_source_addon_table_is_not_proof_of_absence(self):
        data, tables = self.fixture(); del tables['creature_addon']
        with self.assertRaises(ValueError): self.derive(data, tables)

    def validate(self, data, tables):
        spec = importlib.util.spec_from_file_location('credit_source_validation_test', Path(__file__).with_name('quest_credit_source_335.py'))
        owner = importlib.util.module_from_spec(spec); spec.loader.exec_module(owner)
        callback = getattr(owner, 'validate_loaded_sources', None)
        self.assertTrue(callable(callback), 'Effective source hints need primary membership validation')
        return callback(data, tables, {}, {}, {'CoreRevision': '1' * 40, 'DatabaseRevision': 'fixture', 'SourceSqlSha256': '2' * 64})

    def loaded_fixture(self):
        data, tables = self.fixture()
        data['ObjectiveCreditSources'] = self.derive(data, tables)[0]['ObjectiveCreditSources']
        return data, tables

    def test_loaded_source_must_match_the_exact_primary_derived_record(self):
        data, tables = self.loaded_fixture(); before = copy.deepcopy(data)
        result = self.validate(data, tables)
        self.assertEqual(result[(101, 0)], data['ObjectiveCreditSources'])
        self.assertEqual(data, before)

    def test_changed_loaded_credit_target_count_point_or_reference_is_rejected(self):
        for key, value in (('CreditId', 300), ('CreatureId', 202), ('RequiredCount', 4), ('RowIndex', 1), ('SourceRef', 'unbound')):
            data, tables = self.loaded_fixture(); data['ObjectiveCreditSources'][0][key] = value
            with self.assertRaises(ValueError): self.validate(data, tables)
        data, tables = self.loaded_fixture(); data['ObjectiveCreditSources'][0]['Points'][0]['Z'] = 9999
        with self.assertRaises(ValueError): self.validate(data, tables)

    def test_duplicate_loaded_source_is_rejected_before_indexing(self):
        data, tables = self.loaded_fixture(); data['ObjectiveCreditSources'] *= 2
        with self.assertRaises(ValueError): self.validate(data, tables)

    def test_new_script_or_pose_evidence_invalidates_prior_source_claim(self):
        data, tables = self.loaded_fixture(); tables['creature_template'][0]['ScriptName'] = 'unreviewed'
        with self.assertRaises(ValueError): self.validate(data, tables)

    def test_no_loaded_hints_does_not_require_new_source_tables(self):
        self.assertEqual(self.validate({'ObjectiveCreditSources': []}, {}), {})


if __name__ == '__main__': unittest.main()
