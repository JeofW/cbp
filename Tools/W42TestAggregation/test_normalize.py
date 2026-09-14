"""Utility tests only: these do not execute production quest owners."""
import hashlib
from pathlib import Path
import tempfile
import unittest
from normalize import normalize


class NormalizationTests(unittest.TestCase):
    def make_project(self, name='QuestLogObservationRegressionTests'):
        root = Path(self.temp.name) / name
        root.mkdir(exist_ok=True)
        program = ('using System;\nConsole.WriteLine("main");\nEnvironment.ExitCode=0;\n'
                   if name.startswith('QuestLog') else
                   'using System;\nvar utcNow = new DateTime(2026, 9, 3, 0, 0, 0, DateTimeKind.Utc);\n\ntry\n{\nConsole.WriteLine("main");\n}\ncatch(Exception){Environment.ExitCode=1;}\n')
        (root / 'Program.cs').write_text(program)
        group = 'internal static class Example {\n [ModuleInitializer]\n internal static void Run() { throw new System.InvalidOperationException("assertion retained"); }\n}\n'
        (root / 'Example.cs').write_text(group)
        return root, program, group

    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()

    def tearDown(self):
        self.temp.cleanup()

    def test_original_bytes_and_assertions_are_preserved(self):
        root, program, group = self.make_project()
        manifest = normalize(root, root / 'obj' / 'normalized')
        self.assertEqual((root/'Program.cs').read_text(), program)
        self.assertEqual((root/'Example.cs').read_text(), group)
        self.assertEqual((root/'obj/normalized/Example.cs').read_text(), group.replace('[ModuleInitializer]', ''))
        row = next(x for x in manifest['files'] if x['file'] == 'Example.cs')
        self.assertEqual(row['original_sha256'], hashlib.sha256(group.encode()).hexdigest())

    def test_observation_groups_follow_main_exit_assignment(self):
        root, _, _ = self.make_project()
        normalize(root, root/'obj/normalized')
        generated = (root/'obj/normalized/Program.cs').read_text()
        self.assertLess(generated.index('Environment.ExitCode=0;'), generated.index('W42PostInitializationGroups.Run();'))

    def test_wholesome_groups_run_after_initialization_before_legacy_main(self):
        root, _, _ = self.make_project('WholesomeQuestRecoveryRegressionTests')
        normalize(root, root/'obj/normalized')
        generated = (root/'obj/normalized/Program.cs').read_text()
        self.assertLess(generated.index('var utcNow'), generated.index('W42PostInitializationGroups.Run();'))
        self.assertLess(generated.index('W42PostInitializationGroups.Run();'), generated.index('Console.WriteLine("main")'))

    def test_each_group_has_a_failure_boundary_and_sticky_failure(self):
        root, _, _ = self.make_project()
        (root/'Later.cs').write_text('internal static class Later { [ModuleInitializer] internal static void Run() {} }')
        m = normalize(root, root/'obj/normalized')
        driver = (root/'obj/normalized/W42PostInitializationGroups.cs').read_text()
        self.assertEqual(m['groups'], ['Example', 'Later'])
        self.assertEqual(driver.count('catch (global::System.Exception error)'), 2)
        self.assertEqual(driver.count('global::System.Environment.ExitCode = 1;'), 2)
        self.assertNotIn('ExitCode = 0', driver)

    def test_unrecognized_initializer_is_an_error_not_dropped_coverage(self):
        root, _, group = self.make_project()
        (root/'Example.cs').write_text(group.replace('void Run()', 'void Different()'))
        with self.assertRaisesRegex(ValueError, 'initializer'):
            normalize(root, root/'obj/normalized')

    def test_unrecognized_main_is_an_error(self):
        root, _, _ = self.make_project('WholesomeQuestRecoveryRegressionTests')
        (root/'Program.cs').write_text('System.Console.WriteLine("different entry");')
        with self.assertRaisesRegex(ValueError, 'entry'):
            normalize(root, root/'obj/normalized')

    def test_generation_is_deterministic_and_removes_stale_generated_sources(self):
        root, _, _ = self.make_project()
        out = root/'obj/normalized'
        first = normalize(root, out)
        (out/'Stale.cs').write_text('invalid stale source')
        second = normalize(root, out)
        self.assertEqual(first, second)
        self.assertFalse((out/'Stale.cs').exists())

    def test_refuses_production_projects_or_source_overwrite(self):
        root, _, _ = self.make_project()
        with self.assertRaisesRegex(ValueError, 'obj'):
            normalize(root, root)
        other = Path(self.temp.name)/'Styx'
        other.mkdir()
        with self.assertRaisesRegex(ValueError, 'project'):
            normalize(other, other/'obj/normalized')


if __name__ == '__main__':
    unittest.main()
