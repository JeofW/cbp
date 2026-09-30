"""Complete source-derived alternatives may fill an empty predecessor list only."""
import copy
import importlib.util
from pathlib import Path
import unittest

import test_quest_repair_pack_335 as fixtures


class PreviousQuestRepairTests(unittest.TestCase):
    def fixture(self):
        data, tables = fixtures.PrimaryRepairExporterTests().fixture()
        for ident in (201, 202):
            parent = dict(copy.deepcopy(data['Quests'][0]), Id=ident, Name='Parent ' + str(ident), NextQuestID=101)
            data['Quests'].append(parent)
            tables['quest_template'].append(dict(copy.deepcopy(tables['quest_template'][0]), ID=ident))
            tables['quest_template_addon'].append(dict(copy.deepcopy(tables['quest_template_addon'][0]), ID=ident, NextQuestID=101))
        return data, tables

    def derive(self, data, tables):
        path = Path(__file__).with_name('quest_previous_repairs_335.py')
        self.assertTrue(path.is_file(), 'Complete source-bound predecessor exporter is missing')
        spec = importlib.util.spec_from_file_location('previous_source_under_test', path)
        module = importlib.util.module_from_spec(spec); spec.loader.exec_module(module)
        return module.derive(data, tables, {'CoreRevision':'1'*40, 'DatabaseRevision':'fixture', 'SourceSqlSha256':'2'*64})

    def test_complete_empty_alternative_set_is_added_without_mutation(self):
        data, tables = self.fixture(); before = copy.deepcopy((data, tables))
        patch, review = self.derive(data, tables)
        self.assertEqual(len(patch), 1)
        self.assertEqual(patch[0]['QuestId'], 101)
        self.assertEqual(patch[0]['ExpectedPreviousQuestIds'], [])
        self.assertEqual(patch[0]['PreviousQuestIds'], [201, 202])
        self.assertEqual({r['QuestId'] for r in patch[0]['ReferencedQuestGroups']}, {201,202})
        self.assertTrue(all(r['ExclusiveGroup'] >= 0 for r in patch[0]['ReferencedQuestGroups']))
        self.assertEqual((data,tables),before)
        self.assertFalse(review['live_completion_proven'])

    def test_existing_partial_or_complete_list_is_never_overwritten(self):
        for ids in ([201],[201,202],[999]):
            data,tables=self.fixture(); data['Quests'][0]['PreviousQuestsIds']=ids
            self.assertEqual(self.derive(data,tables)[0], [])

    def test_direct_requirement_is_retained_independently(self):
        data,tables=self.fixture()
        data['Quests'][0]['PrevQuestID']=201; tables['quest_template_addon'][0]['PrevQuestID']=201
        patch,_=self.derive(data,tables)
        self.assertEqual(patch[0]['ExpectedPrevQuestId'],201)
        self.assertEqual(patch[0]['PreviousQuestIds'],[201,202])

    def test_subject_source_conflict_withholds_patch(self):
        data,tables=self.fixture(); tables['quest_template'][0]['AllowableRaces']=690
        self.assertEqual(self.derive(data,tables)[0],[])

    def test_negative_group_is_not_reordered_or_partially_exported(self):
        data,tables=self.fixture(); data['Quests'][1]['ExclusiveGroup']=-9; tables['quest_template_addon'][1]['ExclusiveGroup']=-9
        self.assertEqual(self.derive(data,tables)[0],[])

    def test_positive_group_mismatch_rejects_the_whole_contract(self):
        data,tables=self.fixture(); tables['quest_template_addon'][1]['ExclusiveGroup']=9
        self.assertEqual(self.derive(data,tables)[0],[])

    def test_unknown_external_group_cannot_be_assumed_zero(self):
        data,tables=self.fixture(); data['Quests'].pop()
        self.assertEqual(self.derive(data,tables)[0],[])

    def test_bound_external_nonnegative_metadata_remains_metadata_only(self):
        data,tables=self.fixture(); data['Quests'].pop()
        data['DependencyMetadata']={'202':{'QuestId':202,'ExclusiveGroup':0,'GroupMembers':[],'SourceRef':'controlled://source'}}
        patch,_=self.derive(data,tables)
        self.assertEqual(patch[0]['PreviousQuestIds'],[201,202])
        self.assertEqual(len(data['Quests']),2)

    def test_missing_subject_or_no_edges_creates_no_contract(self):
        for mode in ('subject','edges'):
            data,tables=self.fixture()
            if mode=='subject': tables['quest_template']=tables['quest_template'][1:]
            else:
                for row in tables['quest_template_addon']: row['NextQuestID']=0
                for row in data['Quests']: row['NextQuestID']=0
            self.assertEqual(self.derive(data,tables)[0],[])

    def test_null_duplicate_or_signed_existing_list_stays_unmodified(self):
        for ids in (None,[201,201],[-201]):
            data,tables=self.fixture(); data['Quests'][0]['PreviousQuestsIds']=ids
            self.assertEqual(self.derive(data,tables)[0],[])

    def test_reference_source_cycle_is_not_exported(self):
        data,tables=self.fixture(); tables['quest_template_addon'][0]['NextQuestID']=201; data['Quests'][0]['NextQuestID']=201
        self.assertEqual(self.derive(data,tables)[0],[])

    def test_duplicate_primary_identity_is_rejected(self):
        data,tables=self.fixture(); tables['quest_template'].append(copy.deepcopy(tables['quest_template'][0]))
        with self.assertRaises(ValueError): self.derive(data,tables)

    def test_source_order_does_not_change_nonnegative_or_membership(self):
        data,tables=self.fixture(); expected=self.derive(data,tables)[0]
        tables['quest_template_addon'].reverse()
        self.assertEqual(self.derive(data,tables)[0],expected)


if __name__=='__main__': unittest.main()
