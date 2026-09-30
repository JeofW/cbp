"""Exercise the case-preserving TDB335 GameObject loot selector and guards."""
import copy
import unittest
import test_quest_repair_pack_335 as exporter_fixture


class ChestAcquisitionExportTests(unittest.TestCase):
    def fixture(self):
        data, tables = exporter_fixture.PrimaryRepairExporterTests().fixture()
        data['Quests'][0]['Objectives'] = [{'Type': 'CollectFromGameObject', 'MobId': 0,
            'GameObjectId': 401, 'ItemId': 301, 'Index': 0, 'KillCount': 0, 'CollectCount': 2}]
        tables['quest_template'][0].update(RequiredNpcOrGo1=0, RequiredNpcOrGoCount1=0,
            RequiredItemId1=301, RequiredItemCount1=2)
        # The pinned SQL column names are Data0, Data1, ...; chest Data1 is lootId.
        tables['gameobject_template'] = [{'entry': 401, 'name': 'Controlled chest',
            'type': 3, 'Data0': 99, 'Data1': 501, 'Data2': 0, '__source_line': 30}]
        tables['item_template'] = [{'entry': 301, 'name': 'Controlled item', 'startquest': 0, '__source_line': 40}]
        tables['gameobject_loot_template'] = [{'Entry': 501, 'Item': 301, 'Reference': 0,
            'Chance': 100, 'GroupId': 0, 'MinCount': 1, 'MaxCount': 1, '__source_line': 50}]
        tables['gameobject'] = [dict(tables['creature'][0], id=401)]
        return data, tables

    def export(self, data, tables):
        return exporter_fixture.PrimaryRepairExporterTests().derive(data, tables)

    def test_actual_sql_Data1_resolves_chest_loot_geometry(self):
        data, tables = self.fixture(); before = copy.deepcopy(data)
        pack, review = self.export(data, tables)
        self.assertEqual([(r['ObjectType'], r['Entry']) for r in pack['SpawnAdditions']], [('GameObject', 401)])
        self.assertEqual(pack['SpawnAdditions'][0]['Points'], [{'Map': 0, 'X': 10.0, 'Y': 20.0, 'Z': 30.0}])
        self.assertTrue(any(r['table'] == 'gameobject_loot_template' for r in review['quests'][0]['evidence']))
        self.assertEqual(data, before)

    def test_chest_reference_loot_retains_every_source_edge(self):
        data, tables = self.fixture()
        item_row = dict(tables['gameobject_loot_template'][0], Entry=601)
        tables['gameobject_loot_template'][0].update(Item=0, Reference=601)
        tables['reference_loot_template'] = [item_row]
        pack, review = self.export(data, tables)
        self.assertEqual(len(pack['SpawnAdditions']), 1)
        self.assertTrue(any(r['table'] == 'reference_loot_template' for r in review['quests'][0]['evidence']))

    def test_non_chest_Data1_is_not_interpreted_as_loot(self):
        for kind in (0, 1, 5, 8, 10, 25):
            with self.subTest(kind=kind):
                data, tables = self.fixture(); tables['gameobject_template'][0]['type'] = kind
                self.assertEqual(self.export(data, tables)[0]['SpawnAdditions'], [])

    def test_lowercase_alias_cannot_replace_the_pinned_sql_column(self):
        data, tables = self.fixture(); row = tables['gameobject_template'][0]
        row['data1'] = row.pop('Data1')
        self.assertEqual(self.export(data, tables)[0]['SpawnAdditions'], [])

    def test_an_unbound_lowercase_alias_cannot_override_actual_Data1(self):
        data, tables = self.fixture(); row = tables['gameobject_template'][0]
        row.update(Data1=999, data1=501)
        self.assertEqual(self.export(data, tables)[0]['SpawnAdditions'], [])

    def test_Data0_lock_identifier_cannot_be_used_as_loot(self):
        data, tables = self.fixture(); tables['gameobject_template'][0].update(Data0=501, Data1=999)
        self.assertEqual(self.export(data, tables)[0]['SpawnAdditions'], [])

    def test_unrelated_primary_item_cannot_donate_chest_geometry(self):
        data, tables = self.fixture(); tables['gameobject_loot_template'][0]['Item'] = 302
        self.assertEqual(self.export(data, tables)[0]['SpawnAdditions'], [])

    def test_event_phase_and_gameobject_pool_exclusions_survive(self):
        for mode in ('event', 'phase', 'pool'):
            with self.subTest(mode=mode):
                data, tables = self.fixture()
                if mode == 'event': tables['game_event_gameobject'] = [{'guid': 77, 'eventEntry': 1}]
                if mode == 'phase': tables['gameobject'][0]['phaseMask'] = 2
                if mode == 'pool': tables['pool_members'] = [{'type': 1, 'spawnId': 77, 'poolSpawnId': 9}]
                self.assertEqual(self.export(data, tables)[0]['SpawnAdditions'], [])

    def test_creature_pool_with_same_guid_does_not_hide_a_chest(self):
        data, tables = self.fixture(); tables['pool_members'] = [{'type': 0, 'spawnId': 77, 'poolSpawnId': 9}]
        self.assertEqual(len(self.export(data, tables)[0]['SpawnAdditions']), 1)

    def test_existing_reachability_veto_is_not_replaced(self):
        data, tables = self.fixture()
        data['GameObjectSpawns']['401'] = [{'Map': 0, 'X': 1, 'Y': 2, 'Z': 3, 'IsKnownReachable': False}]
        before = copy.deepcopy(data)
        self.assertEqual(self.export(data, tables)[0]['SpawnAdditions'], [])
        self.assertEqual(data, before)

    def test_primary_quest_conflicts_still_reject_chest_additions(self):
        data, tables = self.fixture(); tables['quest_template'][0]['AllowableRaces'] = 690
        pack, review = self.export(data, tables)
        self.assertEqual(pack['SpawnAdditions'], [])
        self.assertIn('primary-field-conflict', review['quests'][0]['remaining'])


if __name__ == '__main__':
    unittest.main()
