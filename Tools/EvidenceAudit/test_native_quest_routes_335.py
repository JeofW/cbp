"""Source-bound route-case gates, not proof that a route or quest was travelled."""
import copy
import gzip
import hashlib
import importlib.util
import json
from pathlib import Path
import tempfile
import unittest

spec=importlib.util.spec_from_file_location('native_quest_route_runner',Path(__file__).resolve().parents[1]/'NativeMeshAudit/run_verified.py')
subject=importlib.util.module_from_spec(spec);spec.loader.exec_module(subject)


class NativeQuestRouteTests(unittest.TestCase):
    def setUp(self):
        self.temp=tempfile.TemporaryDirectory();self.addCleanup(self.temp.cleanup)
        self.root=Path(self.temp.name)
        self.family={'candidate_replay_ids':[953], 'quests':[{'quest_id':953,'replay_candidate':True,
            'additional_source_obligations':[], 'normal_objectives':[{'slot':0,'typed_entry':-17188,'count':1}],
            'primary_spawns':[{'id':17188,'map':1,'position_x':20,'position_y':10,'position_z':4}],
            'primary_starters':[{'id':3639}], 'primary_enders':[{'id':3639,'type':'Creature'}]}]}
        self.dataset={'CreatureSpawns':{'3639':[{'Map':1,'X':10,'Y':10,'Z':4}]}}

    def build(self,family=None,dataset=None):
        function=getattr(subject,'build_quest_route_cases',None)
        self.assertTrue(callable(function),'No source-bound quest-chain route producer exists')
        return function(self.family if family is None else family,self.dataset if dataset is None else dataset)

    def bound(self):
        cases=self.build()
        for name,value in [('family.json',self.family),('dataset.json',self.dataset)]:
            (self.root/name).write_bytes(json.dumps(value).encode('utf-8'))
        return {'family_file':'family.json','family_sha256':hashlib.sha256((self.root/'family.json').read_bytes()).hexdigest(),
                'dataset_file':'dataset.json','dataset_sha256':hashlib.sha256((self.root/'dataset.json').read_bytes()).hexdigest(),
                'cases':cases}

    def verify(self,value):
        function=getattr(subject,'validate_quest_route_cases',None)
        self.assertTrue(callable(function),'Native route cases lack source-byte and endpoint validation')
        return function(self.root,value)

    def test_exact_origin_giver_objective_ender_chain_and_control_provenance(self):
        data=self.bound();self.verify(data)
        cases=data['cases']
        self.assertEqual([row['leg'] for row in cases],['origin-to-giver','giver-to-objective','objective-to-ender'])
        self.assertTrue(all(row['quest_id']==953 and row['map_id']==1 for row in cases))
        self.assertEqual(cases[0]['end'],cases[1]['start'])
        self.assertEqual(cases[1]['end'],cases[2]['start'])
        self.assertEqual(cases[2]['end'],[10,10,4])
        self.assertEqual(cases[0]['start_authority'],'synthetic-near-giver-control')

    def test_changed_primary_bytes_cannot_reuse_source_receipt(self):
        data=self.bound();(self.root/'family.json').write_bytes(b'{}')
        with self.assertRaises(ValueError):self.verify(data)

    def test_changed_geometry_cannot_keep_the_same_source_hashes(self):
        data=self.bound();data['cases'][1]['end'][2]+=100
        with self.assertRaises(ValueError):self.verify(data)

    def test_extra_quest_or_missing_leg_cannot_borrow_family_membership(self):
        data=self.bound()
        for rows in (data['cases'][:-1],data['cases']+[dict(data['cases'][0],quest_id=999)]):
            value=copy.deepcopy(data);value['cases']=rows
            with self.assertRaises(ValueError):self.verify(value)

    def test_scripted_and_mixed_credit_candidates_remain_excluded(self):
        for change in ({'additional_source_obligations':['server-script-required']},
                       {'normal_objectives':[{'slot':0,'typed_entry':17188,'count':1}]},
                       {'replay_candidate':False}):
            data=copy.deepcopy(self.family);data['quests'][0].update(change)
            with self.assertRaises(ValueError):self.build(data)

    def test_missing_or_cross_map_ender_is_not_a_direct_ground_chain(self):
        for actors in ({},{'3639':[{'Map':530,'X':10,'Y':10,'Z':4}]}):
            with self.assertRaises(ValueError):self.build(dataset={'CreatureSpawns':actors})

    def test_nonfinite_geometry_and_ambiguous_membership_are_rejected(self):
        data=copy.deepcopy(self.family);data['quests'][0]['primary_spawns'][0]['position_z']=float('nan')
        with self.assertRaises(ValueError):self.build(data)
        data=copy.deepcopy(self.family);data['candidate_replay_ids']=[953,953]
        with self.assertRaises(ValueError):self.build(data)

    def test_source_paths_cannot_escape_the_pinned_checkout(self):
        data=self.bound();data['family_file']='../elsewhere.json'
        with self.assertRaises(ValueError):self.verify(data)

    def test_compressed_source_keeps_exact_semantics_and_stored_identity(self):
        data=self.bound();raw=(self.root/'family.json').read_bytes()
        compressed=gzip.compress(raw,mtime=0)
        (self.root/'family.json.gz').write_bytes(compressed);(self.root/'family.json').unlink()
        data['family_file']='family.json.gz';data['family_sha256']=hashlib.sha256(compressed).hexdigest()
        self.assertEqual(self.verify(data),data['cases'])

if __name__=='__main__':unittest.main()
