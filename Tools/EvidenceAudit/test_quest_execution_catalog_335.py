import unittest
from quest_execution_catalog_335 import derive


class ExecutionMechanismTests(unittest.TestCase):
    def fixture(self, kind='KillMob'):
        source=dict(revision='a'*40,database_revision='TDB335.25101',dataset_sha256='b'*64,
                    repairs_sha256='c'*64,strategy_sha256='d'*64,sql_sha256='e'*64)
        data={'Quests':[dict(Id=9472,Name='Controlled',SpecialFlags=0,StartItem=23693,
            Objectives=[dict(Type=kind,Index=0,MobId=17226,ItemId=0,GameObjectId=0,KillCount=1,CollectCount=0)])]}
        tables=dict(quest_template=[dict(ID=9472,StartItem=23693,RequiredNpcOrGo1=17226,RequiredNpcOrGoCount1=1)],
            quest_template_addon=[dict(ID=9472,SpecialFlags=0)],creature_template=[dict(entry=17226,npcflag=3,ScriptName='')],
            gameobject_template=[],item_template=[dict(entry=23693,spellid_1=30077,spelltrigger_1=0)],smart_scripts=[])
        codes={'SMART_EVENT_SPELLHIT':8,'SMART_EVENT_REWARD_QUEST':20,'SMART_EVENT_DEATH':6,'SMART_EVENT_LINK':61,
               'SMART_ACTION_CALL_KILLEDMONSTER':33,'SMART_ACTION_CALL_TIMED_ACTIONLIST':80}
        def row(entry,kind,event,param,action,arg):
            return dict(entryorguid=entry,source_type=kind,id=0,link=0,event_type=event,event_param1=param,
                        event_phase_mask=0,action_type=action,action_param1=arg)
        tables['smart_scripts']=[row(17226,0,8,30077,80,1722601),row(1722601,9,0,0,33,17226)]
        return data,tables,codes,source,[],[]

    def test_spell_credit_is_not_death_even_when_special_flags_are_zero(self):
        catalog,_=derive(*self.fixture())
        row=catalog['Quests'][0]['Objectives'][0]
        self.assertEqual(row['Driver'],'Unsupported')
        self.assertIn('non-death-credit-trigger',row['Reasons'])

    def test_only_complete_compound_source_chain_enables_viera(self):
        args=list(self.fixture());tables=args[1]
        tables['quest_template'].append(dict(ID=9483,RequiredItemId1=29112,RequiredItemCount1=1))
        tables['npc_vendor']=[dict(entry=18907,item=29112)]
        tables['smart_scripts'].append(dict(entryorguid=17226,source_type=0,id=1,link=0,event_type=20,
            event_param1=9483,event_phase_mask=0,action_type=80,action_param1=1722600))
        catalog,_=derive(*args)
        self.assertEqual(catalog['Quests'][0]['Objectives'][0]['Driver'],'ArelionsMistress')
        tables['npc_vendor']=[]
        catalog,_=derive(*args)
        self.assertEqual(catalog['Quests'][0]['Objectives'][0]['Driver'],'Unsupported')

    def test_ordinary_death_has_no_item_or_gossip_contract(self):
        args=list(self.fixture());tables=args[1]
        tables['creature_template'][0]['npcflag']=0
        tables['quest_template'][0]['StartItem']=0
        tables['smart_scripts'][0].update(event_type=6,event_param1=0)
        catalog,_=derive(*args)
        self.assertEqual(catalog['Quests'][0]['Objectives'][0]['Driver'],'Primitive')

    def test_dangling_timed_credit_chain_is_not_death(self):
        args=list(self.fixture());args[1]['smart_scripts']=args[1]['smart_scripts'][1:]
        catalog,_=derive(*args)
        self.assertIn('non-death-credit-trigger',catalog['Quests'][0]['Objectives'][0]['Reasons'])

    def test_turnin_and_gameobject_are_known_dataset_types(self):
        args=list(self.fixture('TurnInOnly'));args[1]['quest_template'][0]['RequiredNpcOrGo1']=0
        catalog,_=derive(*args)
        self.assertNotIn('unknown-objective-kind',catalog['Quests'][0]['Objectives'][0]['Reasons'])
        args=list(self.fixture('CollectFromGameObject'));o=args[0]['Quests'][0]['Objectives'][0]
        o.update(MobId=0,GameObjectId=10)
        args[1]['quest_template'][0]['RequiredNpcOrGo1']=-10
        args[1]['gameobject_template']=[dict(entry=10,type=3,ScriptName='')]
        catalog,_=derive(*args)
        self.assertEqual(catalog['Quests'][0]['Objectives'][0]['Driver'],'Primitive')

    def test_unknown_kind_is_not_silently_executable(self):
        catalog,_=derive(*self.fixture('VehicleCombat'))
        self.assertEqual(catalog['Quests'][0]['Objectives'][0]['Driver'],'Unsupported')

    def test_unique_population_and_exclusive_accounting(self):
        args=list(self.fixture());args[0]['Quests'].append(dict(args[0]['Quests'][0]))
        with self.assertRaises(ValueError):derive(*args)
        catalog,audit=derive(*self.fixture())
        self.assertEqual(sum(audit['exclusive_quest_status_counts'].values()),len(catalog['Quests']))

if __name__=='__main__':unittest.main()
