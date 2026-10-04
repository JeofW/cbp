"""Check shipped execution-catalogue membership against its immutable source correlation audit."""
from collections import Counter
import gzip
import hashlib
import json
from pathlib import Path
import unittest
from quest_execution_catalog_335 import digest

ROOT=Path(__file__).resolve().parents[2]
KNOWLEDGE=ROOT/'runtime-snapshot/Bots/WholesomeAutoQuest-master/quest_data'
AUDIT=ROOT/'docs/audit/2026-10-04/fluid-travel-special-quests'

class ShippedCatalogTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.catalog=json.loads((KNOWLEDGE/'quest_execution_contracts.json').read_text())
        cls.audit=json.loads(gzip.decompress((AUDIT/'all-4335-execution-requirements.json.gz').read_bytes()))
        cls.summary=json.loads((AUDIT/'summary.json').read_text())

    def test_exact_4335_unique_members_and_exclusive_statuses(self):
        ids=[q['QuestId'] for q in self.catalog['Quests']]
        raw=json.loads((KNOWLEDGE/'quest_data.json').read_text())
        self.assertEqual(len(ids),4335);self.assertEqual(len(set(ids)),4335)
        self.assertEqual(set(ids),{q['Id'] for q in raw['Quests']})
        self.assertEqual(dict(Counter(q['Status'] for q in self.catalog['Quests'])),self.summary['exclusive_quest_status_counts'])
        self.assertEqual(sum(self.summary['exclusive_quest_status_counts'].values()),4335)

    def test_all_effective_objective_rows_match_source_audit(self):
        indexed={q['quest_id']:q for q in self.audit['records']}
        for quest in self.catalog['Quests']:
            self.assertEqual(quest['Objectives'],[o['Contract'] for o in indexed[quest['QuestId']]['objectives']])
        self.assertEqual(sum(len(q['Objectives']) for q in self.catalog['Quests']),5771)
        self.assertEqual(digest(self.catalog),self.audit['catalogue_digest'])

    def test_pinned_inputs_and_manifest_include_execution_catalog(self):
        manifest=json.loads((KNOWLEDGE/'quest_knowledge_manifest.json').read_text())
        for key,name in [('QuestDataSha256','quest_data.json'),('QuestDataRepairsSha256','quest_data.repairs.json'),('StrategyPackSha256','quest_strategies.json')]:
            self.assertEqual(self.catalog[key],hashlib.sha256((KNOWLEDGE/name).read_bytes()).hexdigest())
        self.assertEqual(self.catalog['ClientBuild'],12340)
        self.assertEqual(self.catalog['SourceRevision'],'95657f54779467effea8a1749a61ff93abc1d707')
        self.assertEqual(manifest['files']['quest_execution_contracts.json'],
                         hashlib.sha256((KNOWLEDGE/'quest_execution_contracts.json').read_bytes()).hexdigest())
        self.assertFalse(manifest['source_execution_catalog']['full_execution_certification'])

    def test_spell_hit_credit_cannot_be_published_as_ordinary_viera_kill(self):
        quest=next(q for q in self.catalog['Quests'] if q['QuestId']==9472)
        self.assertEqual(quest['Status'],'ImplementedStrategy')
        self.assertEqual([o['Driver'] for o in quest['Objectives']],['ArelionsMistress'])
        self.assertIn('non-death-credit-trigger',quest['Objectives'][0]['Reasons'])
        self.assertFalse(self.audit['live_completion_proven'])

if __name__=='__main__':unittest.main()
