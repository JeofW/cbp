"""Windows x86 comparison runner. Build/harness failures are never production defects."""
import argparse
from collections import Counter
import hashlib
import json
from pathlib import Path
import re
import shutil
import subprocess
import sys
import zipfile

BASELINE = '2d3728feb22938428fe64d8ff8204d5779cb4e9e'
SUITES = ['QuestLogObservationRegressionTests', 'WholesomeQuestRecoveryRegressionTests',
          'QuestCompletionOwnerRegressionTests', 'QuestObservationBoundaryRegressionTests']


def execute(arguments, log: Path, cwd: Path, timeout=600):
    with log.open('wb') as stream:
        try:
            result = subprocess.run([str(x) for x in arguments], cwd=cwd, stdout=stream,
                                    stderr=subprocess.STDOUT, timeout=timeout, check=False)
            return {'exit': result.returncode, 'infrastructure_error': None}
        except (OSError, subprocess.TimeoutExpired) as error:
            stream.write(('\nHARNESS ERROR: ' + str(error)).encode())
            return {'exit': None, 'infrastructure_error': type(error).__name__ + ': ' + str(error)}


def git(repo, *arguments):
    return subprocess.check_output(['git', *arguments], cwd=repo, text=True).strip()


def save(path, value):
    path.write_text(json.dumps(value, indent=2) + '\n', encoding='utf-8')


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--repo', type=Path, required=True)
    parser.add_argument('--out', type=Path, required=True)
    parser.add_argument('--runtime', type=Path, required=True)
    parser.add_argument('--fixtures', required=True)
    parser.add_argument('--variant', choices=['baseline', 'current'], required=True)
    args = parser.parse_args()
    repo, out, runtime = args.repo.resolve(), args.out.resolve(), args.runtime.resolve()
    out.mkdir(parents=True, exist_ok=True)
    source = BASELINE if args.variant == 'baseline' else args.fixtures
    git(repo, 'checkout', '--detach', source)
    fixture_paths = ['Tools/' + name for name in SUITES] + ['Tools/W42TestAggregation']
    git(repo, 'checkout', args.fixtures, '--', *fixture_paths)
    changes = git(repo, 'diff', source, '--name-only').splitlines()
    if any(not path.startswith('Tools/') for path in changes):
        raise RuntimeError('Fixture overlay unexpectedly changed non-test production files')
    identity = {'production_commit': git(repo, 'rev-parse', 'HEAD'),
                'production_tree': git(repo, 'rev-parse', 'HEAD^{tree}'),
                'fixture_commit': args.fixtures, 'variant': args.variant,
                'overlaid_test_paths': changes, 'game_attached': False,
                'target': 'original WoW 3.3.5a build 12340; Windows x86; .NET 10'}
    save(out/'source-identity.json', identity)
    tracked = git(repo, 'ls-files', '-z').split('\0')
    inputs = []
    for name in tracked:
        path = repo/name
        if path.is_file() and path.suffix in {'.cs', '.csproj', '.props', '.targets', '.py', '.yml'}:
            inputs.append({'path': name, 'sha256': hashlib.sha256(path.read_bytes()).hexdigest()})
    save(out/'working-source-hashes.json', inputs)
    with zipfile.ZipFile(out/'fixture-and-owner-sources.zip', 'w', zipfile.ZIP_DEFLATED) as archive:
        for item in inputs:
            name = item['path']
            if any(name.startswith(prefix + '/') for prefix in fixture_paths) or name in {
                'Styx/Logic/Questing/QuestLog.cs', 'Styx/Logic/Questing/PlayerQuest.cs',
                'runtime-snapshot/Bots/WholesomeAutoQuest-master/WholesomeAutoQuest.cs',
                'runtime-snapshot/Bots/WholesomeAutoQuest-master/QuestScheduler.cs',
                'runtime-snapshot/Bots/WholesomeAutoQuest-master/ProfileBuilder.cs'}:
                archive.write(repo/name, name)
    runtime_info = execute([runtime, '--info'], out/'runtime-info.txt', repo)
    utility = execute([sys.executable, '-m', 'unittest', 'discover', '-s',
                       repo/'Tools/W42TestAggregation', '-p', 'test_*.py', '-v'], out/'utility-tests.txt', repo)
    results = []
    for name in SUITES:
        project = repo/'Tools'/name
        extraction = None
        if name == 'QuestObservationBoundaryRegressionTests':
            extraction = execute([sys.executable, project/'extract_owners.py', '--repo', repo,
                                  '--ref', source, '--out', project/'Generated'], out/'extraction.txt', repo)
        build = execute(['dotnet', 'build', project/(name+'.csproj'), '-c', 'Release',
                         '-p:Platform=x86', '-p:PlatformTarget=x86', '-v', 'quiet'], out/(name+'-build.txt'), repo)
        run = {'exit': None, 'infrastructure_error': 'not run because build or extraction failed'}
        if build['exit'] == 0 and (extraction is None or extraction['exit'] == 0):
            dlls = [p for p in (project/'bin').rglob(name+'.dll') if not {'ref', 'refint'} & set(p.parts)]
            if len(dlls) == 1:
                arguments = [runtime, dlls[0]]
                if name == 'QuestCompletionOwnerRegressionTests':
                    arguments += [out/'completion-cases.json', out/'source-identity.json']
                elif name == 'QuestObservationBoundaryRegressionTests':
                    arguments += [out/'boundary-cases.json']
                run = execute(arguments, out/(name+'-run.txt'), repo, timeout=180)
            else:
                run['infrastructure_error'] = 'Expected exactly one executable; found ' + str(len(dlls))
        results.append({'suite': name, 'build': build, 'run': run, 'extraction': extraction,
                        'game_attached': False, 'architecture': 'x86'})
        for index, generated in enumerate(sorted((project/'obj').rglob('W42Normalized'))):
            if generated.is_dir():
                shutil.copytree(generated, out/'normalized'/name/str(index), dirs_exist_ok=True)
    cases = []
    for log in out.glob('*-run.txt'):
        for line in log.read_text(encoding='utf-8-sig', errors='replace').splitlines():
            match = re.match(r'^(PASS|FAIL) (observation|ready observation|scheduler observation): (.*)$', line)
            if not match:
                continue
            status, group, detail = match.groups()
            category = 'pass' if status == 'PASS' else 'reported-failure-needs-source-classification'
            if status == 'FAIL' and any(text in detail for text in (
                'missing observation contract:', 'missing snapshot field:', 'missing scheduler observation contract ')):
                category = 'missing-proposed-contract-not-production-reproduction'
            cases.append({'group': group, 'reported_status': status, 'category': category, 'detail': detail})
    counts = dict(Counter(item['group'] for item in cases))
    expected = {'observation': 25, 'ready observation': 5, 'scheduler observation': 5}
    save(out/'observation-case-report.json', {'cases': cases, 'counts': counts, 'expected_counts': expected,
                                          'all_groups_completed': counts == expected})
    save(out/'results.json', {'identity': identity, 'runtime_info': runtime_info, 'utility': utility,
                             'suites': results, 'all_observation_cases_reported': counts == expected,
                             'scope': 'Controlled/offline owner execution; not live client acceptance. Counts overlap across suites.'})
    save(out/'files-sha256.json', [{'path': str(p.relative_to(out)).replace('\\', '/'),
                                  'sha256': hashlib.sha256(p.read_bytes()).hexdigest()}
                                 for p in sorted(out.rglob('*')) if p.is_file() and p.name != 'files-sha256.json'])
    failed = any(item['build']['exit'] != 0 or item['run']['exit'] != 0 for item in results)
    return int(failed or counts != expected or runtime_info['exit'] != 0 or utility['exit'] != 0)


if __name__ == '__main__':
    sys.exit(main())
