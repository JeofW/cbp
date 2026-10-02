"""Bind acceptance evidence to complete checkout contents and an exact commit."""
from __future__ import annotations

import argparse
from datetime import datetime, timezone
import hashlib
import json
import os
from pathlib import Path
import re
import subprocess


def file_sha256(path: Path) -> str:
    absolute = str(path.resolve())
    native = ('\\\\?\\' + absolute) if os.name == 'nt' and not absolute.startswith('\\\\') else absolute
    with open(native, 'rb') as stream:
        return hashlib.file_digest(stream, 'sha256').hexdigest()


def git(repo: Path, *arguments: str) -> str:
    return subprocess.check_output(['git', '-c', 'core.quotepath=false', *arguments],
                                   cwd=repo, text=True, encoding='utf-8').strip()


def source_inputs(repo: Path) -> dict[str, str]:
    """Include every versioned/unignored input, including runtime JSON and DLLs.

    Build outputs follow the repository's ignore rules. A deleted tracked input
    is an error instead of being silently omitted from the comparison.
    """
    raw = subprocess.check_output(['git', '-c', 'core.quotepath=false', 'ls-files',
                                   '--cached', '--others', '--exclude-standard', '-z'], cwd=repo)
    names = sorted(set(name.decode('utf-8') for name in raw.split(b'\0') if name))
    root = repo.resolve()
    result = {}
    for name in names:
        path = root / name
        if path.is_symlink() or not path.resolve().is_relative_to(root):
            raise ValueError('Source input escapes its checkout: ' + name)
        result[name] = file_sha256(path)
    return result


def source_identity(repo: Path, allow_dirty: bool = False) -> dict:
    commit = git(repo, 'rev-parse', 'HEAD')
    modified = bool(git(repo, 'status', '--porcelain'))
    if modified and not allow_dirty:
        raise ValueError('The acceptance candidate working tree must be clean')
    return {'production_commit': commit, 'production_tree': git(repo, 'rev-parse', 'HEAD^{tree}'),
            'fixture_commit': commit, 'working_tree_modified': modified, 'game_attached': False}


def validate_release_gate(candidate: str, identity: dict, summary: dict,
                          before: dict, after: dict, candidate_inputs: dict) -> None:
    if not re.fullmatch(r'[0-9a-f]{40}', candidate):
        raise ValueError('Release evidence requires the full hexadecimal commit identity')
    if any(value != candidate for value in (identity.get('production_commit'),
                                            identity.get('fixture_commit'), summary.get('base'))):
        raise ValueError('Acceptance, fixture and candidate commit identities differ')
    if identity.get('working_tree_modified') is not False:
        raise ValueError('Acceptance did not establish a clean working tree')
    if (summary.get('source_stable') is not True or summary.get('failed') != []
            or type(summary.get('stages')) is not int or summary['stages'] <= 0):
        raise ValueError('Release requires successful, stable, nonempty acceptance results')
    if not before or before != after or before != candidate_inputs:
        raise ValueError('Accepted source inputs differ from the release candidate inputs')


def integrated_projects(repo: Path) -> list[str]:
    """Read the actual retained project population, not an old stage count."""
    workflow = (repo / '.github/workflows/audit-integrated.yml').read_text(encoding='utf-8-sig')
    projects = re.findall(r"@\{project='([A-Za-z0-9]+)';\s*suite='[^']+'\}", workflow)
    if not projects:
        raise ValueError('Integrated workflow project population is unavailable')
    if len(set(projects)) != len(projects):
        raise ValueError('Integrated workflow contains a duplicate project')
    for project in projects:
        if not (repo / f'Tools/{project}/{project}.csproj').is_file():
            raise ValueError('Registered regression project is missing: ' + project)
    return projects


def validate_complete_local_results(projects: list[str], summary: dict, results: list[dict]) -> None:
    """A matching SHA and focused green do not establish complete acceptance."""
    if not projects or len(set(projects)) != len(projects):
        raise ValueError('Complete local project population is empty or duplicated')
    expected = {'Host', 'Analyzers', 'SingularCompatibility'}
    expected.update(project + suffix for project in projects for suffix in ('-build', '-run'))
    if not isinstance(results, list) or any(not isinstance(row, dict) for row in results):
        raise ValueError('Complete local result population is unavailable')
    names = [row.get('stage') for row in results]
    optional = {'ObservationBoundary-extract'} if 'QuestObservationBoundaryRegressionTests' in projects else set()
    if (any(not isinstance(name, str) for name in names) or len(set(names)) != len(names)
            or set(names) - optional != expected or set(names) - expected - optional):
        raise ValueError('Complete local result population differs from the candidate workflow')
    if (type(summary.get('stages')) is not int or summary['stages'] != len(results)
            or summary.get('source_stable') is not True or summary.get('failed') != []):
        raise ValueError('Complete local stage count or successful source stability is inconsistent')
    if any(type(row.get('exit')) is not int or row['exit'] != 0 for row in results):
        raise ValueError('A complete local stage failed or has no authoritative exit code')
    order = {name: index for index, name in enumerate(names)}
    if names[0] != 'Host' or names[-1] != 'Analyzers':
        raise ValueError('Complete local host/analyzer execution order is inconsistent')
    if any(order[project + '-build'] >= order[project + '-run'] for project in projects):
        raise ValueError('A local regression ran before its current source was built')
    if ('QuestRecoveryRegressionTests' not in projects
            or order['SingularCompatibility'] <= order['QuestRecoveryRegressionTests-run']):
        raise ValueError('Actual compiled Singular compatibility execution is missing or out of order')
    if ('ObservationBoundary-extract' in order
            and order['ObservationBoundary-extract'] >= order['QuestObservationBoundaryRegressionTests-build']):
        raise ValueError('Pinned observation owners were extracted after their compilation')


def verify_complete_local_gate(repo: Path, gate: Path, candidate: str) -> dict:
    """Verify the population and log bytes before packaging an exact candidate."""
    def load(name: str):
        return json.loads((gate / name).read_text(encoding='utf-8-sig'))

    identity, summary = load('source-identity.json'), load('summary.json')
    before, after, results = load('source-before.json'), load('source-after.json'), load('results.json')
    current = source_identity(repo)
    if current != identity:
        # The legacy runner includes source_hashes in its identity receipt.
        if any(current[key] != identity.get(key) for key in current):
            raise ValueError('Current source identity differs from the complete local receipt')
    validate_release_gate(candidate, identity, summary, before, after, source_inputs(repo))
    projects = integrated_projects(repo)
    validate_complete_local_results(projects, summary, results)
    hashes = {}
    for row in results:
        stage = row['stage']
        command = row.get('command')
        if not isinstance(command, list) or not command or any(not isinstance(value, str) for value in command):
            raise ValueError('Stage command receipt is unavailable: ' + stage)
        if stage == 'Host' or stage.endswith('-build'):
            project = 'CopilotBuddy.csproj' if stage == 'Host' else f'Tools/{stage[:-6]}/{stage[:-6]}.csproj'
            if not all(value in command for value in ('build', project, '-p:Platform=x86', '-p:PlatformTarget=x86')):
                raise ValueError('Stage did not build its registered Windows x86 project: ' + stage)
        elif stage.endswith('-run') or stage == 'SingularCompatibility':
            project = 'QuestRecoveryRegressionTests' if stage == 'SingularCompatibility' else stage[:-4]
            if len(command) < 2 or Path(command[1]).name != project + '.dll':
                raise ValueError('Stage did not execute its registered regression binary: ' + stage)
            if stage == 'SingularCompatibility' and '--routine-compatibility' not in command:
                raise ValueError('Singular compatibility mode was not executed')
        for suffix, field in (('.log', 'log_sha256'), ('.stderr.log', 'stderr_sha256')):
            filename = stage + suffix
            path = gate / filename
            if path.is_symlink() or not path.resolve().is_relative_to(gate.resolve()):
                raise ValueError('Stage log escapes its retained gate: ' + filename)
            actual = file_sha256(path)
            if actual != row.get(field):
                raise ValueError('Stage log changed after execution: ' + filename)
            hashes[filename] = actual
    return {'source_commit': candidate, 'registered_projects': projects, 'actual_stages': len(results),
            'complete_result_population_verified': True, 'stage_log_sha256': hashes,
            'results_sha256': file_sha256(gate / 'results.json'), 'game_attached': False}


def write_json(path: Path, value: object) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    with path.open('x', encoding='utf-8') as stream:
        json.dump(value, stream, indent=2, ensure_ascii=True)
        stream.write('\n')


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--repo', type=Path, default=Path.cwd())
    parser.add_argument('--output', type=Path, required=True)
    parser.add_argument('--compare', type=Path)
    parser.add_argument('--allow-dirty', action='store_true')
    args = parser.parse_args()
    if args.output.exists():
        raise FileExistsError('Source snapshots are create-only')
    identity = source_identity(args.repo, args.allow_dirty)
    inputs = source_inputs(args.repo)
    snapshot = {'schema': 'complete-source-snapshot-335-v1', 'identity': identity, 'inputs': inputs,
                'created_utc': datetime.now(timezone.utc).isoformat()}
    if args.compare:
        previous = json.loads(args.compare.read_text(encoding='utf-8-sig'))
        if previous['identity'] != identity or previous['inputs'] != inputs:
            raise ValueError('Source changed during acceptance; do not publish a green release receipt')
        snapshot['compared_before_sha256'] = file_sha256(args.compare)
    write_json(args.output, snapshot)
    print(json.dumps({'commit': identity['production_commit'], 'source_inputs': len(inputs),
                      'working_tree_modified': identity['working_tree_modified'],
                      'comparison_passed': bool(args.compare), 'output': str(args.output)}))


if __name__ == '__main__':
    main()
