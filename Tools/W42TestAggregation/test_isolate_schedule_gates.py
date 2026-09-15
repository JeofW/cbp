"""Explicit legacy fixture topology migration, not production execution."""
import importlib
from pathlib import Path
import tempfile
import unittest


class ScheduleGateMigrationTests(unittest.TestCase):
    def setUp(self):
        self.module = importlib.import_module('isolate_schedule_gates')
        self.files = {name: ('before\n' + old + '\nAssert(original_oracle);\n').encode()
                      for name, old in self.module.REPLACEMENTS.items()}
        self.files[self.module.HELPER] = b'// helper present\n'

    def test_changes_only_one_factory_per_legacy_file_and_records_both_hashes(self):
        sources = {name: (raw, raw.decode()) for name, raw in self.files.items()}
        migrated, records = self.module.isolate(sources)
        self.assertEqual(len(records), 7)
        for name, old in self.module.REPLACEMENTS.items():
            raw, text = migrated[name]
            self.assertEqual(raw, self.files[name])
            self.assertEqual(text.replace(self.module.replacement(old), old), raw.decode())
            self.assertIn('Assert(original_oracle);', text)
            row = next(r for r in records if r['file'] == name)
            self.assertNotEqual(row['before_sha256'], row['after_sha256'])
            self.assertEqual(row['changes'], 1)

    def test_full_root_suites_and_unlisted_files_are_untouched(self):
        sources = {name: (raw, raw.decode()) for name, raw in self.files.items()}
        for name in ['QuestActionFreshnessRegressionTests.cs', 'QuestRootPreemptionRegressionTests.cs', 'Other.cs']:
            sources[name] = (b'var root = (GroupComposite)bot.Root;', 'var root = (GroupComposite)bot.Root;')
        migrated, _ = self.module.isolate(sources)
        for name in ['QuestActionFreshnessRegressionTests.cs', 'QuestRootPreemptionRegressionTests.cs', 'Other.cs']:
            self.assertEqual(migrated[name], sources[name])

    def test_missing_changed_or_duplicate_contract_is_error(self):
        for alteration in ['missing', 'changed', 'duplicate']:
            sources = {name: (raw, raw.decode()) for name, raw in self.files.items()}
            name, old = next(iter(self.module.REPLACEMENTS.items()))
            if alteration == 'missing': del sources[name]
            elif alteration == 'changed': sources[name] = (b'x', 'changed construction')
            else: sources[name] = (b'x', old + old)
            with self.subTest(alteration=alteration), self.assertRaises(ValueError):
                self.module.isolate(sources)

    def test_no_opt_in_helper_means_no_migration(self):
        sources = {'Program.cs': (b'untouched', 'untouched')}
        migrated, records = self.module.isolate(sources)
        self.assertEqual(migrated, sources)
        self.assertEqual(records, [])

    def test_input_mapping_is_not_mutated_and_double_migration_fails(self):
        sources = {name: (raw, raw.decode()) for name, raw in self.files.items()}
        saved = dict(sources)
        migrated, _ = self.module.isolate(sources)
        self.assertEqual(saved, sources)
        with self.assertRaises(ValueError): self.module.isolate(migrated)

    def test_normalize_runs_and_manifests_the_explicit_migration(self):
        from normalize import normalize, WHOLESOME_ENTRY
        with tempfile.TemporaryDirectory() as directory:
            project = Path(directory) / 'WholesomeQuestRecoveryRegressionTests'; project.mkdir()
            for name, raw in self.files.items(): (project / name).write_bytes(raw)
            program = project / 'Program.cs'; program.write_text(WHOLESOME_ENTRY + program.read_text())
            (project / 'Example.cs').write_text('internal static class Example { [ModuleInitializer] internal static void Run() { } }')
            before = {p.name: p.read_bytes() for p in project.glob('*.cs')}
            out = project / 'obj' / 'normalized'
            manifest = normalize(project, out)
            self.assertEqual(len(manifest['fixture_topology_migrations']), 7)
            self.assertEqual(before, {p.name: p.read_bytes() for p in project.glob('*.cs')})
            self.assertIn('LegacyScheduleGateFixture.Create(bot)', (out / 'Program.cs').read_text())
            self.assertFalse(manifest['production_modified'])


if __name__ == '__main__': unittest.main()
