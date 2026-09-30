import copy
import unittest
import tempfile
from pathlib import Path
import quests
import quest_ledger_335
from quests import audit_database


def fixture():
    return {'Quests': [{'Id': 1, 'Name': 'Test', 'MinLevel': 58, 'QuestLevel': 62, 'AllowableRaces': 0,
                        'Flags': 0, 'SpecialFlags': 0, 'StartItem': 0, 'PrevQuestID': 0,
                        'NextQuestID': 0, 'PreviousQuestsIds': [], 'ExclusiveGroup': 0,
                        'Objectives': [{'Type': 'KillMob', 'MobId': 2, 'KillCount': 1, 'Index': 0}]}],
            'QuestGivers': [{'QuestId': 1, 'GiverId': 2, 'GiverType': 'Creature'}],
            'QuestEnders': [{'QuestId': 1, 'EnderId': 2, 'EnderType': 'Creature'}],
            'CreatureSpawns': {'2': [{'Map': 530, 'X': 1, 'Y': 2, 'Z': 3}]}, 'GameObjectSpawns': {}}


class QuestLedgerTests(unittest.TestCase):
    def ledger(self, data):
        result = audit_database(data)
        self.assertIn('quest_ledger', result, 'Every source row needs a persistent ledger entry')
        return result['quest_ledger']

    def test_static_validity_cannot_claim_executed_or_live(self):
        row = self.ledger(fixture())[0]
        self.assertEqual(row['classification'], 'LIVE-ACCEPTANCE-REQUIRED')
        self.assertEqual(row['simulation']['status'], 'NOT-RUN')
        self.assertFalse(row['live_completion_proven'])

    def test_duplicate_rows_retain_separate_pointers(self):
        data = fixture(); data['Quests'].append(copy.deepcopy(data['Quests'][0]))
        rows = self.ledger(data)
        self.assertEqual(len(rows), 2)
        self.assertNotEqual(rows[0]['pointer'], rows[1]['pointer'])
        self.assertTrue(all(row['classification'] == 'DATA-INVALID/INCOMPLETE' for row in rows))

    def test_start_item_does_not_imply_an_item_use_strategy(self):
        data = fixture(); data['Quests'][0]['StartItem'] = 77
        row = self.ledger(data)[0]
        self.assertNotEqual(row['classification'], 'UNSUPPORTED-SCRIPTED')
        self.assertIn('provided-on-acceptance', row['start_item_semantics'])

    def test_cast_credit_requires_a_recipe(self):
        data = fixture(); data['Quests'][0]['SpecialFlags'] = 32
        row = self.ledger(data)[0]
        self.assertEqual(row['classification'], 'UNSUPPORTED-SCRIPTED')
        self.assertIn('cast-credit-requires-strategy', row['execution_requirements'])

    def test_secondary_only_flag_is_source_uncertain(self):
        data = fixture(); data['Quests'][0]['SpecialFlags'] = 64
        row = self.ledger(data)[0]
        self.assertEqual(row['classification'], 'SOURCE-UNCERTAIN')
        self.assertIn('special-flag-64-core-dependent', row['source_obligations'])

    def test_missing_relation_is_not_a_server_impossibility_claim(self):
        data = fixture(); data['QuestGivers'] = []
        row = self.ledger(data)[0]
        self.assertEqual(row['classification'], 'DATA-INVALID/INCOMPLETE')
        self.assertIn('pickup-route-unrepresented', row['execution_requirements'])
        self.assertFalse(row['server_quest_invalid_proven'])

    def test_missing_optional_fields_remain_explicit(self):
        row = self.ledger(fixture())[0]
        self.assertEqual(row['eligibility']['AllowableClasses']['evidence'], 'absent')
        data = fixture(); data['Quests'][0]['AllowableClasses'] = 0
        self.assertEqual(self.ledger(data)[0]['eligibility']['AllowableClasses']['evidence'], 'declared')

    def test_negative_previous_requires_an_active_parent(self):
        data = fixture(); data['Quests'][0]['PrevQuestID'] = -99
        row = self.ledger(data)[0]
        self.assertEqual(row['dependencies']['active_parent'], 99)

    def test_objective_index_is_not_assumed_to_be_a_raw_client_slot(self):
        data = fixture(); data['Quests'][0]['Objectives'][0]['Index'] = 17
        row = self.ledger(data)[0]
        self.assertEqual(row['objectives'][0]['client_slot_authority'], 'requires-runtime-identity-mapping')

    def test_missing_spawn_is_a_bot_data_gap_even_with_a_valid_reference(self):
        data = fixture(); data['CreatureSpawns'] = {}
        row = self.ledger(data)[0]
        self.assertEqual(row['classification'], 'DATA-INVALID/INCOMPLETE')
        self.assertTrue(row['findings'])

    def test_missing_next_dependency_and_cycles_are_explicit(self):
        data = fixture(); data['Quests'][0]['NextQuestID'] = 99; data['Quests'][0]['PrevQuestID'] = 1
        row = self.ledger(data)[0]
        self.assertIn('next-quest-outside-dataset', row['source_obligations'])
        self.assertIn('self-prerequisite', row['execution_requirements'])

    def test_reference_sql_is_parsed_without_executing_statements(self):
        reader = getattr(quests, 'read_reference_table', None)
        self.assertTrue(callable(reader), 'Reference SQL needs a strict read-only parser')
        sql = "CREATE TABLE `example` (\n  `ID` int,\n  `Name` text,\n  `Value` int\n);\nINSERT INTO `example` VALUES\n(1,'a,b',NULL),(2,'a\\\'b',-3);\n"
        with tempfile.TemporaryDirectory() as folder:
            path = Path(folder) / 'example.sql'; path.write_text(sql, encoding='utf-8')
            rows = list(reader(path))
        self.assertEqual(rows[0]['Name'], 'a,b')
        self.assertIsNone(rows[0]['Value'])
        self.assertEqual(rows[1]['Name'], "a'b")
        self.assertEqual(rows[1]['Value'], -3)

    def test_reference_sql_rejects_schema_width_mismatch(self):
        reader = getattr(quests, 'read_reference_table', None)
        self.assertTrue(callable(reader), 'Reference SQL needs a strict read-only parser')
        with tempfile.TemporaryDirectory() as folder:
            path = Path(folder) / 'broken.sql'
            path.write_text("CREATE TABLE `x` (\n `ID` int,\n `Value` int\n);\nINSERT INTO `x` VALUES (1);\n", encoding='utf-8')
            with self.assertRaises(ValueError): list(reader(path))

    def reviewed(self, *, requirements=None, conditions=None, scripts=None, classification='GENERIC-PROVEN', pipeline='PASS'):
        review = getattr(quest_ledger_335, 'apply_source_review_classification', None)
        self.assertTrue(callable(review), 'Source review must reconsider a simulation-only classification')
        row = {'classification': classification, 'source_obligations': [], 'execution_requirements': [],
               'simulation': {'pipeline_status': pipeline, 'failed_cases': 0},
               'secondary_reference': {'unrepresented_secondary_requirements': requirements or {}},
               'source_review': {'secondary_availability_conditions': conditions or [],
                                 'secondary_direct_quest_scripts': scripts or [], 'realm_equivalence_proven': False}}
        review(row)
        return row

    def test_reference_escort_cannot_keep_a_generic_completion_label(self):
        row = self.reviewed(scripts=[{'__contract': 'SMART_ACTION_ESCORT_START', '__source_line': 100}])
        self.assertEqual(row['classification'], 'SOURCE-UNCERTAIN')
        self.assertIn('secondary-nonordinary-credit-requires-bound-strategy', row['execution_requirements'])

    def test_reference_reputation_requirement_missing_from_bot_is_explicit(self):
        row = self.reviewed(requirements={'RequiredMinRepFaction': 932, 'RequiredMinRepValue': 3000})
        self.assertEqual(row['classification'], 'SOURCE-UNCERTAIN')

    def test_reference_availability_conditions_prevent_unqualified_generic_claim(self):
        self.assertEqual(self.reviewed(conditions=[{'SourceEntry': 1}])['classification'], 'SOURCE-UNCERTAIN')

    def test_a_reward_hook_alone_does_not_invent_a_scripted_objective(self):
        self.assertEqual(self.reviewed(scripts=[{'__contract': 'SMART_EVENT_REWARD_QUEST'}])['classification'], 'GENERIC-PROVEN')

    def test_source_review_preserves_an_existing_data_gap(self):
        self.assertEqual(self.reviewed(classification='DATA-INVALID/INCOMPLETE', conditions=[{'SourceEntry': 1}])['classification'], 'DATA-INVALID/INCOMPLETE')

    def test_missing_pipeline_cannot_keep_a_generic_proof_label(self):
        self.assertEqual(self.reviewed(pipeline='BLOCKED')['classification'], 'LIVE-ACCEPTANCE-REQUIRED')


if __name__ == '__main__':
    unittest.main()
