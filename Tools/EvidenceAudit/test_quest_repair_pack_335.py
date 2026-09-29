import copy
import importlib.util
import json
from pathlib import Path
import unittest


class PrimaryRepairExporterTests(unittest.TestCase):
    def derive(self, data, tables, protected=()):
        path=Path(__file__).with_name('quest_repair_pack_335.py')
        self.assertTrue(path.is_file(),'Primary source repairs require a conservative reproducible exporter')
        spec=importlib.util.spec_from_file_location('repair_export_under_test',path)
        module=importlib.util.module_from_spec(spec);spec.loader.exec_module(module)
        return module.build_repairs(data,tables,set(protected),{'CoreRevision':'1'*40,'DatabaseRevision':'controlled-fixture','SourceSqlSha256':'2'*64})

    def fixture(self):
        q={'Id':101,'Name':'Controlled quest','MinLevel':1,'QuestLevel':1,'AllowableRaces':0,'Flags':0,
           'QuestSortID':1,'QuestInfoID':0,'RequiredFactionId1':0,'RequiredFactionId2':0,'StartItem':0,
           'PrevQuestID':0,'NextQuestID':0,'ExclusiveGroup':0,'SpecialFlags':0,'PreviousQuestsIds':[],
           'Objectives':[{'Type':'KillMob','MobId':201,'GameObjectId':0,'ItemId':0,'Index':0,'KillCount':3,'CollectCount':0}]}
        data={'Quests':[q],'QuestGivers':[],'QuestEnders':[],'CreatureSpawns':{},'GameObjectSpawns':{}}
        t={name:[] for name in ['quest_template','quest_template_addon','creature','gameobject','creature_template','gameobject_template',
              'creature_queststarter','creature_questender','gameobject_queststarter','gameobject_questender','item_template',
              'creature_loot_template','gameobject_loot_template','reference_loot_template','conditions','smart_scripts',
              'game_event_creature','game_event_gameobject','pool_members']}
        template={'ID':101,'LogTitle':'Controlled quest','QuestType':2,'MinLevel':1,'QuestLevel':1,'AllowableRaces':0,'Flags':0,
                  'QuestSortID':1,'QuestInfoID':0,'RequiredFactionId1':0,'RequiredFactionId2':0,'RequiredFactionValue1':0,'RequiredFactionValue2':0,
                  'StartItem':0,'RewardMoney':0,'TimeAllowed':0,'RequiredPlayerKills':0,'__source_line':5}
        for i in range(1,5):template.update({f'RequiredNpcOrGo{i}':201 if i==1 else 0,f'RequiredNpcOrGoCount{i}':3 if i==1 else 0})
        for i in range(1,7):template.update({f'RequiredItemId{i}':0,f'RequiredItemCount{i}':0})
        t['quest_template']=[template]
        t['quest_template_addon']=[{'ID':101,'MaxLevel':0,'AllowableClasses':0,'RequiredSkillID':0,'RequiredSkillPoints':0,
           'RequiredMinRepFaction':0,'RequiredMinRepValue':0,'RequiredMaxRepFaction':0,'RequiredMaxRepValue':0,
           'PrevQuestID':0,'NextQuestID':0,'ExclusiveGroup':0,'SpecialFlags':0,'ProvidedItemCount':0,'__source_line':8}]
        t['creature_template']=[{'entry':201,'name':'Actor','lootid':201,'KillCredit1':0,'KillCredit2':0,'AIName':'','ScriptName':'','__source_line':10}]
        t['creature']=[{'guid':77,'id':201,'map':0,'position_x':10.0,'position_y':20.0,'position_z':30.0,
                        'phaseMask':1,'spawnMask':1,'spawntimesecs':120,'ScriptName':'','__source_line':15}]
        return data,t

    def delivery(self):
        d,t=self.fixture();q=d['Quests'][0];q['StartItem']=301;q['Objectives']=[{'Type':'TurnInOnly','Index':0}]
        template=t['quest_template'][0];template.update(StartItem=301,RequiredNpcOrGo1=0,RequiredNpcOrGoCount1=0,RequiredItemId1=301,RequiredItemCount1=1)
        t['item_template']=[{'entry':301,'name':'Letter','startquest':0,'__source_line':30}]
        return d,t

    def test_matching_primary_kill_geometry_is_added(self):
        d,t=self.fixture();pack,review=self.derive(d,t)
        self.assertEqual(pack['SpawnAdditions'][0]['Entry'],201)
        self.assertEqual(pack['SpawnAdditions'][0]['Points'][0]['Z'],30)
        self.assertNotIn('IsKnownReachable',pack['SpawnAdditions'][0]['Points'][0])

    def test_wrong_namespace_cannot_donate_geometry(self):
        d,t=self.fixture();t['gameobject']=t['creature'];t['creature']=[]
        self.assertEqual(self.derive(d,t)[0]['SpawnAdditions'],[])

    def test_changed_normal_requirement_does_not_silently_repair_geometry(self):
        d,t=self.fixture();t['quest_template'][0]['RequiredNpcOrGoCount1']=4
        self.assertEqual(self.derive(d,t)[0]['SpawnAdditions'],[])

    def test_collection_count_mismatch_gets_a_typed_expected_value_patch(self):
        d,t=self.fixture();o=d['Quests'][0]['Objectives'][0];o.update(Type='CollectItem',ItemId=301,KillCount=0,CollectCount=3)
        t['quest_template'][0].update(RequiredNpcOrGo1=0,RequiredNpcOrGoCount1=0,RequiredItemId1=301,RequiredItemCount1=8)
        t['item_template']=[{'entry':301,'name':'Required item','startquest':0,'__source_line':30}]
        p,_=self.derive(d,t);self.assertIn('ObjectiveCountRepairs',p)
        self.assertEqual(p['ObjectiveCountRepairs'][0]['ExpectedCount'],3)
        self.assertEqual(p['ObjectiveCountRepairs'][0]['RequiredCount'],8)
        self.assertEqual(p['ObjectiveCountRepairs'][0]['TargetId'],201)
        self.assertEqual(d['Quests'][0]['Objectives'][0]['CollectCount'],3)

    def test_unknown_required_item_cannot_authorize_count_rewrite(self):
        d,t=self.fixture();o=d['Quests'][0]['Objectives'][0];o.update(Type='CollectItem',ItemId=301,KillCount=0,CollectCount=3)
        p,_=self.derive(d,t);self.assertEqual(p.get('ObjectiveCountRepairs',[]),[])

    def test_phase_specific_spawn_is_not_an_ordinary_route(self):
        d,t=self.fixture();t['creature'][0]['phaseMask']=2
        self.assertEqual(self.derive(d,t)[0]['SpawnAdditions'],[])

    def test_event_owned_spawn_is_not_an_ordinary_route(self):
        d,t=self.fixture();t['game_event_creature']=[{'eventEntry':1,'guid':77,'__source_line':90}]
        self.assertEqual(self.derive(d,t)[0]['SpawnAdditions'],[])

    def test_creature_pool_excludes_its_own_creature_guid(self):
        d,t=self.fixture();t['pool_members']=[{'type':0,'spawnId':77,'poolSpawnId':9}]
        self.assertEqual(self.derive(d,t)[0]['SpawnAdditions'],[])

    def test_gameobject_pool_cannot_exclude_a_same_numbered_creature(self):
        d,t=self.fixture();t['pool_members']=[{'type':1,'spawnId':77,'poolSpawnId':9}]
        self.assertEqual(len(self.derive(d,t)[0]['SpawnAdditions']),1)

    def test_subpool_number_cannot_exclude_a_same_numbered_creature(self):
        d,t=self.fixture();t['pool_members']=[{'type':2,'spawnId':77,'poolSpawnId':9}]
        self.assertEqual(len(self.derive(d,t)[0]['SpawnAdditions']),1)

    def test_existing_vetoed_geometry_is_not_overwritten(self):
        d,t=self.fixture();d['CreatureSpawns']['201']=[{'Map':0,'X':1,'Y':2,'Z':3,'IsKnownSafe':False}]
        before=copy.deepcopy(d);self.assertEqual(self.derive(d,t)[0]['SpawnAdditions'],[]);self.assertEqual(d,before)

    def test_protected_green_quest_is_not_repaired(self):
        d,t=self.fixture();p,_=self.derive(d,t,protected={101})
        self.assertEqual(p['SpawnAdditions'],[]);self.assertEqual(p['QuestMetadata'],[])

    def test_provided_delivery_uses_primary_acceptance_contract(self):
        d,t=self.delivery();p,_=self.derive(d,t);m=p['QuestMetadata'][0]
        self.assertEqual(m['DeliveryItems'],[{'ItemId':301,'Count':1}]);self.assertEqual(m['AcceptanceSupplies'],[{'ItemId':301,'Count':1}])

    def test_same_item_starting_quest_is_not_promised_again(self):
        d,t=self.delivery();t['item_template'][0]['startquest']=101
        self.assertEqual(self.derive(d,t)[0]['QuestMetadata'][0]['AcceptanceSupplies'],[])

    def test_stock_delivery_does_not_invent_an_acquisition_action(self):
        d,t=self.delivery();d['Quests'][0]['StartItem']=0;t['quest_template'][0]['StartItem']=0
        m=self.derive(d,t)[0]['QuestMetadata'][0];self.assertEqual(m['AcceptanceSupplies'],[]);self.assertEqual(m['DeliveryItems'][0]['ItemId'],301)

    def test_event_or_cast_quest_is_not_relabelled_delivery(self):
        for flag in [2,32]:
            d,t=self.delivery();d['Quests'][0]['SpecialFlags']=flag;t['quest_template_addon'][0]['SpecialFlags']=flag
            m=self.derive(d,t)[0]['QuestMetadata'][0];self.assertIsNone(m['DeliveryItems'])

    def test_conflicting_known_primary_metadata_is_explicit_not_overwritten(self):
        d,t=self.fixture();t['quest_template'][0]['AllowableRaces']=690
        p,r=self.derive(d,t);self.assertEqual(p['QuestMetadata'],[]);self.assertIn('primary-field-conflict',r['quests'][0]['remaining'])

    def test_explicit_primary_eligibility_zero_is_preserved(self):
        d,t=self.fixture();m=self.derive(d,t)[0]['QuestMetadata'][0]
        self.assertEqual(m['Fields']['AllowableClasses'],0);self.assertEqual(m['Fields']['RequiredMinRepValue'],0)

    def test_unknown_optional_base_fields_are_not_overwritten(self):
        d,t=self.fixture();d['Quests'][0]['AllowableClasses']=2
        p,_=self.derive(d,t);self.assertNotIn('AllowableClasses',p['QuestMetadata'][0]['Fields'])

    def test_missing_required_reference_table_is_not_empty_evidence(self):
        d,t=self.fixture();del t['creature_template']
        with self.assertRaises(ValueError):self.derive(d,t)

    def test_duplicate_primary_identity_is_rejected(self):
        d,t=self.fixture();t['quest_template'].append(copy.deepcopy(t['quest_template'][0]))
        with self.assertRaises(ValueError):self.derive(d,t)

    def test_nonfinite_source_coordinates_are_never_exported(self):
        d,t=self.fixture();t['creature'][0]['position_z']=float('nan')
        self.assertEqual(self.derive(d,t)[0]['SpawnAdditions'],[])

    def test_all_changes_have_bound_source_references(self):
        d,t=self.delivery();p,r=self.derive(d,t)
        self.assertTrue(all(v['SourceRef'] for v in p['QuestMetadata']))
        self.assertTrue(r['quests'][0]['evidence']);self.assertFalse(r['live_completion_proven'])

    def test_supplied_return_item_alongside_kill_is_explicit_not_an_action(self):
        d,t=self.fixture();d['Quests'][0]['StartItem']=301
        t['quest_template'][0].update(StartItem=301,RequiredItemId1=301,RequiredItemCount1=1)
        t['item_template']=[{'entry':301,'name':'Return this item','startquest':0,'__source_line':30}]
        m=self.derive(d,t)[0]['QuestMetadata'][0]
        self.assertEqual(m.get('SupplementalSupply'),{'ItemId':301,'RequiredCount':1,'ProvidedCount':1})
        self.assertIsNone(m['DeliveryItems'])

    def test_source_starting_quest_item_is_not_promised_as_supplemental_supply(self):
        d,t=self.fixture();d['Quests'][0]['StartItem']=301
        t['quest_template'][0].update(StartItem=301,RequiredItemId1=301,RequiredItemCount1=1)
        t['item_template']=[{'entry':301,'name':'Starts same quest','startquest':101,'__source_line':30}]
        self.assertIsNone(self.derive(d,t)[0]['QuestMetadata'][0].get('SupplementalSupply'))

if __name__=='__main__':unittest.main()
