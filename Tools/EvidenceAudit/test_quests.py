import copy
import unittest
from quests import audit_database, compare_zones


def fixture():
    return {'Quests': [{'Id': 1, 'PrevQuestID': 0, 'PreviousQuestsIds': [],
                        'Objectives': [{'Type': 'KillMob', 'MobId': 2, 'KillCount': 1, 'Index': 0}]}],
            'QuestGivers': [{'QuestId': 1, 'GiverId': 2, 'GiverType': 'Creature'}],
            'QuestEnders': [{'QuestId': 1, 'EnderId': 2, 'EnderType': 'Creature'}],
            'CreatureSpawns': {'2': [{'X': 0, 'Y': 1, 'Z': 2, 'Map': 0}]}, 'GameObjectSpawns': {}}


class QuestTests(unittest.TestCase):
    def test_valid_data_is_not_claimed_live_validated(self):
        result = audit_database(fixture())
        self.assertEqual(result['issues'], [])
        self.assertEqual(result['coverage'], 'static data only; not live quest completion')

    def test_duplicates_retain_both_json_pointers(self):
        data = fixture(); data['Quests'].append(copy.deepcopy(data['Quests'][0]))
        issues = audit_database(data)['issues']
        self.assertEqual(issues[0]['code'], 'duplicate_quest_id')
        self.assertEqual(issues[0]['pointers'], ['/Quests/0/Id', '/Quests/1/Id'])

    def test_missing_spawn_is_coverage_gap_not_unreachable_verdict(self):
        data = fixture(); data['CreatureSpawns'] = {}
        issues = audit_database(data)['issues']
        self.assertTrue(issues)
        self.assertTrue(all(i['classification'] == 'data_gap' for i in issues))

    def test_unknown_type_not_silently_treated_as_turnin(self):
        data = fixture(); data['Quests'][0]['Objectives'][0]['Type'] = 'Escort'
        self.assertIn('unsupported_objective_type', [i['code'] for i in audit_database(data)['issues']])

    def test_nonfinite_spawn_is_invalid(self):
        data = fixture(); data['CreatureSpawns']['2'][0]['Z'] = float('inf')
        self.assertIn('nonfinite_spawn', [i['code'] for i in audit_database(data)['issues']])

    def test_missing_prerequisite_is_not_automatically_removed(self):
        data = fixture(); data['Quests'][0]['PreviousQuestsIds'] = [99]
        issue = next(i for i in audit_database(data)['issues'] if i['code'] == 'external_prerequisite')
        self.assertEqual(issue['classification'], 'data_gap')
        self.assertEqual(data['Quests'][0]['PreviousQuestsIds'], [99])

    def test_same_index_alternatives_are_ambiguous_not_duplicates(self):
        data = fixture(); data['Quests'][0]['Objectives'].append({'Type': 'CollectItem', 'MobId': 2, 'ItemId': 5, 'CollectCount': 1, 'Index': 0})
        issue = next(i for i in audit_database(data)['issues'] if i['code'] == 'shared_objective_index')
        self.assertEqual(issue['classification'], 'needs_contract_review')

    def test_zone_drift_reports_exact_quest_not_file_order(self):
        canonical = fixture()
        zones = {'Zone_test.json': {'Quests': copy.deepcopy(canonical['Quests'])}}
        self.assertEqual(compare_zones(canonical, zones)['differences'], [])
        zones['Zone_test.json']['Quests'][0]['Objectives'][0]['MobId'] = 77
        result = compare_zones(canonical, zones)
        self.assertEqual(result['differences'][0]['quest_id'], 1)
        self.assertEqual(result['differences'][0]['path'], 'Zone_test.json')
