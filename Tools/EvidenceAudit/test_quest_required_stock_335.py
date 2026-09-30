import copy
import importlib.util
from pathlib import Path
import unittest
import test_quest_repair_pack_335 as base_fixtures


class RequiredStockSourceTests(unittest.TestCase):
    def fixture(self):
        data,tables=base_fixtures.PrimaryRepairExporterTests().fixture()
        tables['quest_template'][0].update(RequiredItemId1=301,RequiredItemCount1=2,
            RequiredItemId2=302,RequiredItemCount2=1)
        tables['item_template']=[{'entry':i,'name':'Controlled item '+str(i),'startquest':0,'__source_line':i} for i in (301,302,303)]
        return data,tables

    def derive(self,data,tables,ids=(101,)):
        path=Path(__file__).with_name('quest_required_stock_335.py')
        self.assertTrue(path.is_file(),'Required stock needs a complete source-bound exporter')
        spec=importlib.util.spec_from_file_location('required_stock_under_test',path)
        module=importlib.util.module_from_spec(spec);spec.loader.exec_module(module)
        return module.build_contracts(data,tables,{'CoreRevision':'1'*40,'DatabaseRevision':'controlled-fixture',
            'SourceSqlSha256':'2'*64,'QuestDataSha256':'3'*64},set(ids))

    def test_missing_return_items_are_stock_requirements_not_new_actions(self):
        data,tables=self.fixture();before=copy.deepcopy((data,tables));contracts,review=self.derive(data,tables)
        self.assertEqual(contracts[0]['Items'],[{'ItemId':301,'Count':2},{'ItemId':302,'Count':1}])
        self.assertEqual(contracts[0]['QuestId'],101)
        self.assertEqual(set(contracts[0]),{'QuestId','Items','SourceRef'})
        self.assertIn('tc335:'+'1'*40,contracts[0]['SourceRef'])
        self.assertEqual((data,tables),before)
        self.assertFalse(review[0]['acquisition_proven'])

    def test_a_supplied_supplemental_item_remains_separately_owned(self):
        data,tables=self.fixture();q=data['Quests'][0]
        q['StartItem']=301;q['SupplementalSupply']={'ItemId':301,'RequiredCount':2,'ProvidedCount':2}
        tables['quest_template'][0]['StartItem']=301;tables['quest_template_addon'][0]['ProvidedItemCount']=2
        self.assertEqual(self.derive(data,tables)[0][0]['Items'],[{'ItemId':302,'Count':1}])

    def test_existing_collection_owner_is_never_replaced_by_stock(self):
        data,tables=self.fixture();q=data['Quests'][0]
        q['Objectives'].append({'Type':'CollectItem','MobId':201,'GameObjectId':0,'ItemId':301,'Index':1,'CollectCount':2,'KillCount':0})
        self.assertEqual(self.derive(data,tables)[0][0]['Items'],[{'ItemId':302,'Count':1}])

    def test_existing_complete_contract_is_not_reexported(self):
        data,tables=self.fixture();data['Quests'][0]['RequiredStockItems']=[{'ItemId':301,'Count':2},{'ItemId':302,'Count':1}]
        self.assertEqual(self.derive(data,tables)[0],[])

    def test_conflicting_existing_stock_is_not_overwritten(self):
        data,tables=self.fixture();data['Quests'][0]['RequiredStockItems']=[{'ItemId':301,'Count':9}]
        contracts,review=self.derive(data,tables);self.assertEqual(contracts,[])
        self.assertIn('existing-stock-conflicts-with-primary',review[0]['remaining'])

    def test_selection_is_explicit_and_does_not_expand_to_other_quests(self):
        data,tables=self.fixture();self.assertEqual(self.derive(data,tables,ids=())[0],[])

    def test_pure_delivery_and_scripted_owners_remain_separate(self):
        for changes in ({'SpecialFlags':2},{'SpecialFlags':32},{'Objectives':[{'Type':'TurnInOnly','Index':0}]}):
            data,tables=self.fixture();data['Quests'][0].update(changes)
            if 'SpecialFlags' in changes:tables['quest_template_addon'][0]['SpecialFlags']=changes['SpecialFlags']
            self.assertEqual(self.derive(data,tables)[0],[])

    def test_primary_field_conflict_does_not_make_a_stock_contract(self):
        data,tables=self.fixture();tables['quest_template'][0]['MinLevel']=9
        self.assertEqual(self.derive(data,tables)[0],[])

    def test_missing_item_metadata_refuses_the_whole_contract(self):
        data,tables=self.fixture();tables['item_template']=[tables['item_template'][0]]
        self.assertEqual(self.derive(data,tables)[0],[])

    def test_invalid_or_duplicate_primary_quantities_remain_unresolved(self):
        for field,value in (('RequiredItemCount1',0),('RequiredItemCount1',True),('RequiredItemId1',-1),('RequiredItemId2',301)):
            data,tables=self.fixture();tables['quest_template'][0][field]=value
            self.assertEqual(self.derive(data,tables)[0],[])

    def test_unverified_start_item_is_not_a_promised_or_carried_requirement(self):
        data,tables=self.fixture();data['Quests'][0]['StartItem']=301;tables['quest_template'][0]['StartItem']=301
        self.assertEqual(self.derive(data,tables)[0],[])

    def test_mismatched_existing_objective_quantity_cannot_be_hidden(self):
        data,tables=self.fixture();data['Quests'][0]['Objectives'].append(
            {'Type':'CollectItem','MobId':201,'GameObjectId':0,'ItemId':301,'Index':1,'CollectCount':3,'KillCount':0})
        self.assertEqual(self.derive(data,tables)[0],[])

    def test_existing_pure_delivery_contract_is_never_combined(self):
        data,tables=self.fixture();data['Quests'][0]['DeliveryItems']=[{'ItemId':301,'Count':2}]
        data['Quests'][0]['AcceptanceSupplies']=[]
        self.assertEqual(self.derive(data,tables)[0],[])

    def test_duplicate_source_quest_identity_is_rejected(self):
        data,tables=self.fixture();tables['quest_template'].append(copy.deepcopy(tables['quest_template'][0]))
        with self.assertRaises(ValueError):self.derive(data,tables)

    def test_all_required_items_already_represented_needs_no_patch(self):
        data,tables=self.fixture();tables['quest_template'][0].update(RequiredItemId1=0,RequiredItemCount1=0,RequiredItemId2=0,RequiredItemCount2=0)
        self.assertEqual(self.derive(data,tables)[0],[])


if __name__=='__main__':unittest.main()
