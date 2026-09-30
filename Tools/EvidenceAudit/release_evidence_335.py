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
