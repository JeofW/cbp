"""New collection routes retain the exact item owner and fail closed on source gaps."""
import copy
import importlib.util
from pathlib import Path
import unittest

import test_quest_repair_pack_335 as fixtures


class CollectionRouteExporterTests(unittest.TestCase):
    def fixture(self):
        data, tables = fixtures.PrimaryRepairExporterTests().fixture()
        quest = data['Quests'][0]
        quest['Objectives'] = [{'Type':'CollectItem','MobId':900,'GameObjectId':0,'ItemId':301,
                                'Index':0,'KillCount':0,'CollectCount':2,'GameObjectName':''}]
        tables['quest_template'][0].update(RequiredNpcOrGo1=0, RequiredNpcOrGoCount1=0,
                                          RequiredItemId1=301, RequiredItemCount1=2)
        tables['item_template'] = [{'entry':301,'name':'Required item','startquest':0,'__source_line':30}]
        tables['creature_loot_template'] = [self.loot(201)]
        tables['creature_template'].append({'entry':900,'name':'Unproved old owner','lootid':900,'AIName':'','ScriptName':''})
        tables['creature_template'].append({'entry':901,'name':'Giver','lootid':0,'AIName':'','ScriptName':''})
        data['QuestGivers'] = [{'QuestId':101,'GiverId':901,'GiverType':'Creature','GiverName':'Giver'}]
        data['CreatureSpawns']['901'] = [{'Map':0,'X':0.0,'Y':0.0,'Z':0.0}]
        tables['creature_queststarter'] = [{'id':901,'quest':101,'__source_line':1}]
        return data, tables

    @staticmethod
    def loot(entry, **changes):
        row={'Entry':entry,'Item':301,'Reference':0,'Chance':50.0,'QuestRequired':1,
             'LootMode':1,'GroupId':0,'MinCount':1,'MaxCount':1,'__source_line':1}
        row.update(changes); return row

    def derive(self, data, tables, previous=None, ids=None):
        path=Path(__file__).with_name('quest_collection_routes_335.py')
        self.assertTrue(path.is_file(), 'A source-bound collection route exporter is required')
        spec=importlib.util.spec_from_file_location('route_export_under_test',path)
        module=importlib.util.module_from_spec(spec); spec.loader.exec_module(module)
        codes={1:{'name':'CONDITION_SOURCE_TYPE_CREATURE_LOOT_TEMPLATE'},
               4:{'name':'CONDITION_SOURCE_TYPE_GAMEOBJECT_LOOT_TEMPLATE'},
               10:{'name':'CONDITION_SOURCE_TYPE_REFERENCE_LOOT_TEMPLATE'}}
        return module.derive(data,tables,previous or {},codes,
            {'CoreRevision':'1'*40,'DatabaseRevision':'fixture','SourceSqlSha256':'2'*64},
            {101} if ids is None else ids)

    def test_unproved_final_item_owner_is_replaced_without_input_mutation(self):
        data,tables=self.fixture(); original=copy.deepcopy((data,tables))
        result,review=self.derive(data,tables)
        patch=result['CollectionRouteRepairs'][0]
        self.assertEqual((patch['Operation'],patch['ExpectedTargetId'],patch['CreatureId']),('Replace',900,201))
        self.assertEqual((patch['ItemId'],patch['RequiredCount'],patch['ObjectiveIndex'],patch['NewObjectiveIndex']),(301,2,0,0))
        self.assertEqual(result['SpawnAdditions'][0]['Entry'],201)
        self.assertNotIn('IsKnownReachable',result['SpawnAdditions'][0]['Points'][0])
        self.assertEqual((data,tables),original)
        self.assertFalse(review['live_completion_proven'])

    def test_valid_old_source_without_geometry_gets_an_alternative(self):
        data,tables=self.fixture(); tables['creature_loot_template'].append(self.loot(900))
        patch=self.derive(data,tables)[0]['CollectionRouteRepairs'][0]
        self.assertEqual((patch['Operation'],patch['ExpectedTargetId'],patch['CreatureId'],patch['NewObjectiveIndex']),('Append',900,201,1))

    def test_valid_existing_route_is_unchanged(self):
        data,tables=self.fixture(); tables['creature_loot_template'].append(self.loot(900))
        data['CreatureSpawns']['900']=[{'Map':0,'X':1,'Y':2,'Z':3}]
        self.assertEqual(self.derive(data,tables)[0]['CollectionRouteRepairs'],[])

    def test_usable_valid_alternative_prevents_redundant_append(self):
        data,tables=self.fixture(); tables['creature_loot_template'].append(self.loot(900))
        data['Quests'][0]['Objectives'].append(dict(data['Quests'][0]['Objectives'][0],MobId=201,Index=1))
        data['CreatureSpawns']['201']=[{'Map':0,'X':10,'Y':20,'Z':30}]
        self.assertEqual(self.derive(data,tables)[0]['CollectionRouteRepairs'],[])

    def test_auxiliary_item_and_mismatched_count_are_never_rewritten(self):
        for field,value in (('ItemId',302),('CollectCount',3),('KillCount',1)):
            data,tables=self.fixture(); data['Quests'][0]['Objectives'][0][field]=value
            self.assertEqual(self.derive(data,tables)[0]['CollectionRouteRepairs'],[])

    def test_wrong_gameobject_item_source_can_use_an_ordinary_creature(self):
        data,tables=self.fixture(); data['Quests'][0]['Objectives'][0].update(Type='CollectFromGameObject',MobId=0,GameObjectId=800,GameObjectName='Chair')
        tables['gameobject_template'].append({'entry':800,'type':7,'name':'Chair','Data1':0,'AIName':'','ScriptName':''})
        patch=self.derive(data,tables)[0]['CollectionRouteRepairs'][0]
        self.assertEqual((patch['ExpectedObjectiveType'],patch['ExpectedTargetId'],patch['CreatureId']),('CollectFromGameObject',800,201))

    def test_scripts_and_source_conflicts_do_not_gain_a_route(self):
        for mode in ('subject','flag','old-script','new-script','smart-script'):
            data,tables=self.fixture()
            if mode=='subject':tables['quest_template'][0]['AllowableRaces']=690
            if mode=='flag':data['Quests'][0]['SpecialFlags']=tables['quest_template_addon'][0]['SpecialFlags']=32
            if mode=='old-script':tables['creature_template'][1]['ScriptName']='requires_review'
            if mode=='new-script':tables['creature_template'][0]['ScriptName']='requires_review'
            if mode=='smart-script':tables['smart_scripts']=[{'source_type':0,'entryorguid':201}]
            self.assertEqual(self.derive(data,tables)[0]['CollectionRouteRepairs'],[],mode)

    def test_grouped_conditional_and_nondefault_loot_stay_unproven(self):
        for mode in ('group','chance','mode','condition','missing-item'):
            data,tables=self.fixture()
            if mode=='group':tables['creature_loot_template'][0].update(Chance=0,GroupId=1)
            if mode=='chance':tables['creature_loot_template'][0]['Chance']=0
            if mode=='mode':tables['creature_loot_template'][0]['LootMode']=2
            if mode=='condition':tables['conditions']=[{'SourceTypeOrReferenceId':1,'SourceGroup':201,'SourceEntry':301,'ConditionTypeOrReference':8,'__source_line':1}]
            if mode=='missing-item':tables['item_template']=[]
            self.assertEqual(self.derive(data,tables)[0]['CollectionRouteRepairs'],[],mode)

    def test_event_pool_phase_and_scripted_spawn_cannot_donate_geometry(self):
        for mode in ('phase','event','pool','script','respawn'):
            data,tables=self.fixture()
            if mode=='phase':tables['creature'][0]['phaseMask']=2
            if mode=='event':tables['game_event_creature']=[{'guid':77}]
            if mode=='pool':tables['pool_members']=[{'type':0,'spawnId':77}]
            if mode=='script':tables['creature'][0]['ScriptName']='special_spawn'
            if mode=='respawn':tables['creature'][0]['spawntimesecs']=-1
            self.assertEqual(self.derive(data,tables)[0]['CollectionRouteRepairs'],[],mode)

    def test_new_route_cannot_overwrite_existing_veto_or_unmatched_geometry(self):
        for point in ({'Map':0,'X':10,'Y':20,'Z':30,'IsKnownSafe':False}, {'Map':0,'X':1,'Y':2,'Z':3}):
            data,tables=self.fixture(); data['CreatureSpawns']['201']=[point]
            self.assertEqual(self.derive(data,tables)[0]['CollectionRouteRepairs'],[])

    def test_giver_map_and_primary_relation_are_required(self):
        for mode in ('map','relation','anchor'):
            data,tables=self.fixture()
            if mode=='map':tables['creature'][0]['map']=1
            if mode=='relation':tables['creature_queststarter']=[]
            if mode=='anchor':data['CreatureSpawns']['901']=[]
            self.assertEqual(self.derive(data,tables)[0]['CollectionRouteRepairs'],[],mode)

    def test_previous_count_or_identity_repair_is_not_repaired_again(self):
        for name in ('ObjectiveCountRepairs','GameObjectObjectiveRepairs','CollectionRouteRepairs'):
            data,tables=self.fixture()
            self.assertEqual(self.derive(data,tables,{name:[{'QuestId':101,'RowIndex':0}]})[0]['CollectionRouteRepairs'],[])

    def test_negative_geometry_on_original_source_is_preserved(self):
        data,tables=self.fixture(); tables['creature_loot_template'].append(self.loot(900))
        data['CreatureSpawns']['900']=[{'Map':0,'X':1,'Y':2,'Z':3,'IsKnownSafe':False}]
        # A data repair must not route around a deliberate original-owner veto.
        self.assertEqual(self.derive(data,tables)[0]['CollectionRouteRepairs'],[])

    def test_scope_and_source_order_are_deterministic(self):
        data,tables=self.fixture(); first=self.derive(data,tables)[0]
        for values in tables.values():values.reverse()
        self.assertEqual(self.derive(data,tables)[0],first)
        self.assertEqual(self.derive(data,tables,ids=set())[0]['CollectionRouteRepairs'],[])

    def test_duplicate_primary_actor_identity_is_rejected(self):
        data,tables=self.fixture();tables['creature_template'].append(copy.deepcopy(tables['creature_template'][0]))
        with self.assertRaises(ValueError):self.derive(data,tables)


if __name__=='__main__':unittest.main()
