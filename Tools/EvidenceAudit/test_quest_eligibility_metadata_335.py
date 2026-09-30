"""A prior generic label cannot hide stronger explicit eligibility requirements."""
import copy
import unittest
import test_quest_repair_pack_335 as fixtures


class QuestEligibilityMetadataTests(unittest.TestCase):
    def fixture(self):
        helper = fixtures.PrimaryRepairExporterTests()
        data, tables = helper.fixture()
        tables['quest_template_addon'][0].update(RequiredMinRepFaction=922, RequiredMinRepValue=3000)
        return helper, data, tables

    def export(self, data, tables, protected=True):
        return fixtures.PrimaryRepairExporterTests().derive(data, tables, protected={101} if protected else ())

    def fields(self, pack):
        return pack['QuestMetadata'][0]['Fields'] if pack['QuestMetadata'] else {}

    def test_protected_quest_still_receives_explicit_missing_reputation_contract(self):
        _, data, tables = self.fixture(); before = copy.deepcopy((data, tables))
        pack, review = self.export(data, tables)
        self.assertEqual(self.fields(pack), {'RequiredMinRepFaction': 922, 'RequiredMinRepValue': 3000})
        self.assertEqual(pack['SpawnAdditions'], [])
        self.assertEqual(pack['RelationAdditions'], [])
        self.assertEqual((data, tables), before)
        self.assertTrue(any('eligibility' in change for change in review['quests'][0]['changes']))

    def test_zero_threshold_belongs_to_an_explicit_active_faction(self):
        _, data, tables = self.fixture(); tables['quest_template_addon'][0]['RequiredMinRepValue'] = 0
        self.assertEqual(self.fields(self.export(data, tables)[0]), {'RequiredMinRepFaction': 922, 'RequiredMinRepValue': 0})

    def test_negative_reputation_threshold_is_not_a_missing_requirement(self):
        _, data, tables = self.fixture(); tables['quest_template_addon'][0]['RequiredMinRepValue'] = -3000
        self.assertEqual(self.fields(self.export(data, tables)[0]).get('RequiredMinRepValue'), -3000)

    def test_matching_known_half_is_preserved_while_missing_half_is_filled(self):
        for known, value, missing, expected in (('RequiredMinRepFaction', 922, 'RequiredMinRepValue', 3000),
                                               ('RequiredMinRepValue', 3000, 'RequiredMinRepFaction', 922)):
            _, data, tables = self.fixture(); data['Quests'][0][known] = value
            self.assertEqual(self.fields(self.export(data, tables)[0]), {missing: expected})

    def test_conflicting_known_half_never_borrows_the_other_factions_threshold(self):
        for protected in (False, True):
            _, data, tables = self.fixture(); data['Quests'][0]['RequiredMinRepFaction'] = 933
            fields = self.fields(self.export(data, tables, protected)[0])
            self.assertNotIn('RequiredMinRepValue', fields)
            self.assertNotIn('RequiredMinRepFaction', fields)

    def test_conflicting_known_threshold_never_borrows_the_source_faction(self):
        for protected in (False, True):
            _, data, tables = self.fixture(); data['Quests'][0]['RequiredMinRepValue'] = 9000
            self.assertNotIn('RequiredMinRepFaction', self.fields(self.export(data, tables, protected)[0]))

    def test_already_complete_contract_is_not_rewritten(self):
        _, data, tables = self.fixture(); data['Quests'][0].update(RequiredMinRepFaction=922, RequiredMinRepValue=3000)
        self.assertEqual(self.fields(self.export(data, tables)[0]), {})

    def test_default_only_protected_fields_do_not_expand_the_pack(self):
        _, data, tables = self.fixture(); tables['quest_template_addon'][0].update(RequiredMinRepFaction=0, RequiredMinRepValue=0)
        self.assertEqual(self.export(data, tables)[0]['QuestMetadata'], [])

    def test_primary_base_conflict_still_blocks_supplementation(self):
        _, data, tables = self.fixture(); tables['quest_template'][0]['AllowableRaces'] = 690
        self.assertEqual(self.export(data, tables)[0]['QuestMetadata'], [])

    def test_boolean_or_invalid_paired_primary_identity_does_not_enter_metadata(self):
        for field, value in (('RequiredMinRepFaction', True), ('RequiredMinRepFaction', -922), ('RequiredMinRepValue', False)):
            _, data, tables = self.fixture(); tables['quest_template_addon'][0][field] = value
            fields = self.fields(self.export(data, tables, protected=False)[0])
            self.assertNotIn('RequiredMinRepFaction', fields)
            self.assertNotIn('RequiredMinRepValue', fields)

    def test_maximum_reputation_pair_is_kept_separate_from_minimum(self):
        _, data, tables = self.fixture(); tables['quest_template_addon'][0].update(RequiredMaxRepFaction=933, RequiredMaxRepValue=9000)
        fields = self.fields(self.export(data, tables)[0])
        self.assertEqual(fields, {'RequiredMinRepFaction':922, 'RequiredMinRepValue':3000,
                                  'RequiredMaxRepFaction':933, 'RequiredMaxRepValue':9000})

    def test_skill_identity_and_threshold_are_not_crossed_between_sources(self):
        _, data, tables = self.fixture(); tables['quest_template_addon'][0].update(RequiredSkillID=164, RequiredSkillPoints=75)
        data['Quests'][0]['RequiredSkillID'] = 171
        fields = self.fields(self.export(data, tables, protected=False)[0])
        self.assertNotIn('RequiredSkillPoints', fields)

    def test_explicit_class_level_and_skill_constraints_survive_protection(self):
        _, data, tables = self.fixture(); tables['quest_template_addon'][0].update(AllowableClasses=2, MaxLevel=60, RequiredSkillID=164, RequiredSkillPoints=0)
        fields = self.fields(self.export(data, tables)[0])
        self.assertEqual({name:fields.get(name) for name in ('AllowableClasses','MaxLevel','RequiredSkillID','RequiredSkillPoints')},
                         {'AllowableClasses':2,'MaxLevel':60,'RequiredSkillID':164,'RequiredSkillPoints':0})


if __name__ == '__main__': unittest.main()
