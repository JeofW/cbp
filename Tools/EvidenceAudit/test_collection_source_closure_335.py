"""Run the real ledger owner: supplied inventory cannot certify a loot source."""
import contextlib
import copy
import gzip
import io
import json
from pathlib import Path
import tempfile
import unittest
from unittest.mock import patch

import primary_closure_335 as owner
import quest_repair_pack_335 as exporter
import test_quest_repair_pack_335 as fixtures


class CollectionSourceClosureTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        data, tables = fixtures.PrimaryRepairExporterTests().fixture()
        base = data['Quests'][0]
        template = tables['quest_template'][0]
        addon = tables['quest_template_addon'][0]
        template.update(RequiredNpcOrGo1=0, RequiredNpcOrGoCount1=0)
        data['Quests'] = [dict(copy.deepcopy(base), Id=i, Name='Fixture ' + str(i),
                              Objectives=[{'Type': 'TurnInOnly', 'Index': 0}]) for i in range(1, 4336)]
        tables['quest_template'] = [dict(copy.deepcopy(template), ID=i) for i in range(1, 4336)]
        tables['quest_template_addon'] = [dict(copy.deepcopy(addon), ID=i) for i in range(1, 4336)]
        point = {'Map': 0, 'X': 1.0, 'Y': 2.0, 'Z': 3.0}
        data['CreatureSpawns']['201'] = [point]
        data['QuestGivers'] = [{'QuestId': i, 'GiverId': 201, 'GiverType': 'Creature'} for i in range(1, 4336)]
        data['QuestEnders'] = [{'QuestId': i, 'EnderId': 201, 'EnderType': 'Creature'} for i in range(1, 4336)]
        tables['item_template'] = [{'entry': 301, 'name': 'Required', 'startquest': 0, '__source_line': 1}]

        def collect(ident, kind='Creature', geometry=True, item=301):
            entry = 1000 + ident
            obj = {'Type': 'CollectItem' if kind == 'Creature' else 'CollectFromGameObject',
                   'MobId': entry if kind == 'Creature' else 0,
                   'GameObjectId': entry if kind == 'GameObject' else 0,
                   'ItemId': item, 'CollectCount': 2, 'KillCount': 0, 'Index': 0}
            data['Quests'][ident - 1]['Objectives'] = [obj]
            tables['quest_template'][ident - 1].update(RequiredItemId1=item, RequiredItemCount1=2)
            actor = {'entry': entry, 'name': 'Actor ' + str(entry), 'lootid': entry,
                     'type': 3, 'Data1': entry, '__source_line': ident + 10}
            tables['creature_template' if kind == 'Creature' else 'gameobject_template'].append(actor)
            if geometry:
                data[kind + 'Spawns'][str(entry)] = [dict(point)]
            return actor, obj

        def loot(table, entry, item=301, **changes):
            row = {'Entry': entry, 'Item': item, 'Reference': 0, 'Chance': 100,
                   'QuestRequired': 1, 'LootMode': 1, 'GroupId': 0, 'MinCount': 1,
                   'MaxCount': 1, '__source_line': entry}
            row.update(changes)
            tables[table].append(row)
            return row

        # A map position and a passing inventory simulation used to certify this wrong owner.
        collect(1)
        collect(2, 'GameObject'); loot('gameobject_loot_template', 1002)
        collect(3); loot('creature_loot_template', 1003, 0, Reference=9003, GroupId=1)
        loot('reference_loot_template', 9003, GroupId=2)
        collect(4); loot('creature_loot_template', 1004, 0, Reference=9004, GroupId=1)
        loot('reference_loot_template', 9004, GroupId=1)
        collect(5); loot('creature_loot_template', 1005, LootMode=2)
        collect(6); loot('creature_loot_template', 1006)
        tables['conditions'] = [{'SourceTypeOrReferenceId': 1, 'SourceGroup': 1006,
            'SourceEntry': 301, 'SourceId': 0, 'ConditionTypeOrReference': -123, '__source_line': 66}]
        collect(7); loot('creature_loot_template', 1007)
        collect(8, geometry=False); loot('creature_loot_template', 1008)
        wrong = dict(data['Quests'][7]['Objectives'][0], MobId=2008)
        data['Quests'][7]['Objectives'].append(wrong)
        tables['creature_template'].append({'entry': 2008, 'name': 'Wrong alternative', 'lootid': 2008})
        data['CreatureSpawns']['2008'] = [dict(point)]
        collect(9, item=399); loot('creature_loot_template', 1009, 399)
        collect(10); loot('creature_loot_template', 1010, 0, Reference=9010)
        loot('reference_loot_template', 9010, 0, Reference=9010)
        actor, _ = collect(11, 'GameObject'); actor['type'] = 25
        loot('gameobject_loot_template', 1011)
        actor, _ = collect(12, 'GameObject'); actor['type'] = 7
        loot('gameobject_loot_template', 1012)
        collect(13); loot('creature_loot_template', 1013, MinCount=0)
        collect(14); loot('creature_loot_template', 1014, Chance=0, GroupId=0)
        collect(15); loot('creature_loot_template', 1015, Chance=0, GroupId=1)
        collect(16); loot('creature_loot_template', 1016, 0, Reference=9016, MaxCount=0)
        loot('reference_loot_template', 9016)
        cls.inputs_before = copy.deepcopy((data, tables))
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            reference = root / 'reference'; (reference / 'contracts').mkdir(parents=True)
            (reference / 'source-receipt.json').write_text(json.dumps({
                'release_commit': '1' * 40, 'members': [{'sha256': '2' * 64}]}), encoding='utf-8')
            (reference / 'contracts/ConditionMgr.h').write_text(
                'CONDITION_SOURCE_TYPE_QUEST_AVAILABLE = 19,\n'
                'CONDITION_SOURCE_TYPE_CREATURE_LOOT_TEMPLATE = 1,\n'
                'CONDITION_SOURCE_TYPE_GAMEOBJECT_LOOT_TEMPLATE = 4,\n'
                'CONDITION_SOURCE_TYPE_REFERENCE_LOOT_TEMPLATE = 10,\n', encoding='utf-8')
            (reference / 'contracts/SmartScriptMgr.h').write_text('', encoding='utf-8')
            baseline = [{'quest_id': i, 'classification': 'GENERIC-PROVEN',
                         'secondary_reference': {'fixture': 'explicit-controlled-source'}} for i in range(1, 4336)]
            simulations = [{'quest_id': i, 'pipeline_status': 'PASS', 'failed_cases': 0, 'passed_cases': 10,
                            'dataset_sha256': 'a' * 64, 'repair_sha256': 'b' * 64,
                            'execution_fingerprint': 'controlled-inventory'} for i in range(1, 4336)]
            (root / 'baseline.gz').write_bytes(gzip.compress(b'\n'.join(json.dumps(r).encode() for r in baseline)))
            (root / 'sim.jsonl').write_text('\n'.join(json.dumps(r) for r in simulations), encoding='utf-8')
            (root / 'effective.json').write_text(json.dumps(data), encoding='utf-8')
            (root / 'dependencies.json').write_text('{}', encoding='utf-8')
            (root / 'repair.gz').write_bytes(gzip.compress(json.dumps({'quests': [
                {'quest_id': i, 'changes': [], 'remaining': [], 'evidence': []} for i in range(1, 4336)]}).encode()))
            argv = ['primary_closure_335.py', '--baseline', str(root / 'baseline.gz'),
                    '--simulation', str(root / 'sim.jsonl'), '--effective', str(root / 'effective.json'),
                    '--dependencies', str(root / 'dependencies.json'), '--repair-evidence', str(root / 'repair.gz'),
                    '--reference', str(reference), '--output', str(root / 'output'),
                    '--baseline-evidence-commit', '3' * 40]
            with patch('sys.argv', argv), patch.object(exporter, 'load_verified_tables', return_value=(tables, [])), contextlib.redirect_stdout(io.StringIO()):
                owner.main()
            cls.rows = {r['quest_id']: r for r in map(json.loads,
                gzip.decompress((root / 'output/quest-ledger.jsonl.gz').read_bytes()).splitlines())}
            cls.counts = json.loads((root / 'output/summary.json').read_text())['after']
        cls.inputs_after = (data, tables)

    def test_missing_source_is_reopened_despite_geometry_and_passing_inventory(self):
        self.assertEqual(self.rows[1]['classification'], 'SOURCE-UNCERTAIN')

    def test_actual_chest_Data1_and_ordinary_creature_still_pass(self):
        self.assertEqual([self.rows[i]['classification'] for i in (2, 7, 15)], ['GENERIC-PROVEN'] * 3)

    def test_reference_cannot_borrow_an_item_from_another_group(self):
        self.assertEqual(self.rows[3]['classification'], 'SOURCE-UNCERTAIN')
        self.assertEqual(self.rows[4]['classification'], 'GENERIC-PROVEN')

    def test_nondefault_loot_mode_requires_explicit_source_resolution(self):
        self.assertEqual(self.rows[5]['classification'], 'SOURCE-UNCERTAIN')

    def test_loot_condition_is_not_satisfied_by_controlled_inventory(self):
        self.assertEqual(self.rows[6]['classification'], 'SOURCE-UNCERTAIN')

    def test_geometry_and_loot_evidence_must_belong_to_the_same_candidate(self):
        self.assertEqual(self.rows[8]['classification'], 'DATA-INVALID/INCOMPLETE')

    def test_absent_item_template_and_reference_cycle_remain_unproven(self):
        self.assertEqual([self.rows[i]['classification'] for i in (9, 10)], ['SOURCE-UNCERTAIN'] * 2)

    def test_chair_and_fishing_hole_are_not_ordinary_chest_interactions(self):
        self.assertEqual([self.rows[i]['classification'] for i in (11, 12)], ['SOURCE-UNCERTAIN'] * 2)

    def test_invalid_loot_rows_do_not_become_acquisition_evidence(self):
        self.assertEqual([self.rows[i]['classification'] for i in (13, 14, 16)], ['SOURCE-UNCERTAIN'] * 3)

    def test_new_source_obligations_preserve_exact_partition_and_inputs(self):
        self.assertEqual(len(self.rows), 4335)
        self.assertEqual(sum(self.counts.values()), 4335)
        self.assertEqual(self.inputs_before, self.inputs_after)


if __name__ == '__main__':
    unittest.main()
