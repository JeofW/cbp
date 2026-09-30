"""Run the actual 4,335-quest and vetted-strategy owners on Windows/x86.

The fixture supplies explicitly controlled observations from the pinned TC335
reference. This proves reproducible bot behavior, never actual realm completion.
"""
from __future__ import annotations

import argparse
from datetime import datetime, timezone
import gzip
import json
import os
from pathlib import Path, PurePosixPath
import subprocess
import time

from primary_closure_335 import classification_partition, validate_closure_inputs, validate_strategy_results, validate_availability_result
from release_evidence_335 import file_sha256, source_identity, source_inputs, write_json

EVIDENCE = Path('docs/audit/2026-09-29/wholesome-primary-closure')
KNOWLEDGE = Path('runtime-snapshot/Bots/WholesomeAutoQuest-master/quest_data')


def read_json(path: Path):
    return json.loads(path.read_text(encoding='utf-8-sig'))


def read_rows(path: Path):
    raw = gzip.decompress(path.read_bytes()) if path.suffix == '.gz' else path.read_bytes()
    return [json.loads(line) for line in raw.decode('utf-8-sig').splitlines() if line.strip()]


def closure_evidence_directory(repo: Path, knowledge_manifest: dict) -> Path:
    value = knowledge_manifest.get('closure_evidence_directory', EVIDENCE.as_posix())
    if (not isinstance(value, str) or not value.startswith('docs/audit/') or '\\' in value
            or ':' in value or '..' in PurePosixPath(value).parts):
        raise ValueError('The closure evidence directory must be a retained repository audit path')
    candidate = (repo / value).resolve()
    if not candidate.is_relative_to(repo.resolve() / 'docs/audit'):
        raise ValueError('The closure evidence directory resolves outside the repository audit root')
    return candidate


def run(args) -> None:
    repo = args.repo.resolve()
    if os.name != 'nt':
        raise RuntimeError('The actual quest owners require Windows and the verified x86 desktop runtime')
    args.output.mkdir(parents=True, exist_ok=False)
    output = args.output.resolve()
    identity = source_identity(repo, args.allow_dirty)
    before = source_inputs(repo)
    write_json(output / 'source-identity.json', identity)
    write_json(output / 'source-before.json', before)
    knowledge = repo / KNOWLEDGE
    evidence = closure_evidence_directory(repo, read_json(knowledge / 'quest_knowledge_manifest.json'))
    fixture = read_json(evidence / 'closure-fixture-manifest.json')
    for name, expected in fixture['knowledge_sha256'].items():
        if Path(name).name != name or file_sha256(knowledge / name) != expected:
            raise ValueError('Shipped knowledge differs from the reviewed fixture: ' + name)
    observations = gzip.decompress((evidence / 'simulation-observations.jsonl.gz').read_bytes())
    (output / 'observations.jsonl').write_bytes(observations)
    if file_sha256(output / 'observations.jsonl') != fixture['observations_sha256']:
        raise ValueError('Controlled observation fixture hash differs')
    environment = dict(os.environ)
    environment.pop('CB_QUEST_SIM_LIMIT', None)
    environment.update(CB_QUEST_SIM_DATASET=str(knowledge / 'quest_data.json'),
                       CB_QUEST_SIM_OBSERVATIONS=str(output / 'observations.jsonl'),
                       CB_QUEST_SIM_OUTPUT=str(output / 'quest-simulation.jsonl'),
                       CB_QUEST_SIM_USE_DATA_LOADER='1',
                       CB_QUEST_STRATEGY_SIM_OUTPUT=str(output / 'strategy-simulation.jsonl'),
                       DOTNET_TieredCompilation='0', COMPlus_TieredCompilation='0')
    results = []

    def command(stage, command_line):
        started = time.monotonic()
        with (output / (stage + '.stdout.log')).open('xb') as stdout, (output / (stage + '.stderr.log')).open('xb') as stderr:
            process = subprocess.run(command_line, cwd=repo, env=environment, stdout=stdout, stderr=stderr,
                                     timeout=900, creationflags=subprocess.CREATE_NO_WINDOW)
        result = {'stage': stage, 'command': command_line, 'exit': process.returncode,
                  'elapsed_seconds': round(time.monotonic() - started, 3)}
        results.append(result)
        print(json.dumps(result), flush=True)
        if process.returncode:
            raise RuntimeError('Quest closure stage failed: ' + stage)

    failure = None
    try:
        project = 'PostMergeAuditRegressionTests'
        command('build', ['dotnet', 'build', f'Tools/{project}/{project}.csproj', '-c', 'Release',
                          '-p:Platform=x86', '-p:PlatformTarget=x86', '-v', 'quiet'])
        assemblies = [path for path in (repo / 'Tools' / project / 'bin').rglob(project + '.dll')
                      if not any(part in ('ref', 'refint') for part in path.parts)]
        if len(assemblies) != 1:
            raise RuntimeError('The focused owner assembly is ambiguous: ' + str(assemblies))
        command('dataset', [str(args.runtime.resolve()), str(assemblies[0]), 'QuestDatasetSimulationRegressionTests'])
        command('strategies', [str(args.runtime.resolve()), str(assemblies[0]), 'QuestVettedStrategyPipelineRegressionTests'])
        data = read_json(knowledge / 'quest_data.json')
        ledger = read_rows(evidence / 'quest-ledger.jsonl.gz')
        rows = read_rows(output / 'quest-simulation.jsonl')
        _, _, simulations = validate_closure_inputs(ledger, data['Quests'], rows)
        if any(row.get('failed_cases', 1) != 0 or row.get('passed_cases', 0) <= 0 for row in rows):
            raise ValueError('Every quest must have a successful nonempty simulation receipt')
        strategies = validate_strategy_results(read_rows(output / 'strategy-simulation.jsonl'), simulations,
                                               fixture['knowledge_sha256']['quest_strategies.json'])
        declared = {recipe['QuestId'] for recipe in read_json(knowledge / 'quest_strategies.json')['Recipes']}
        if set(strategies) != declared or set(strategies) != set(fixture['strategy_quest_ids']):
            raise ValueError('Every declared strategy needs its own exact lifecycle receipt')
        if any(row.get('pipeline_status') != 'PASS' or row.get('failed_cases', 1) != 0 for row in strategies.values()):
            raise ValueError('A vetted strategy lifecycle did not pass')
        loaded_model = read_json(output / 'quest-simulation.jsonl.effective-model.json')
        availability = loaded_model.get('QuestAvailabilityConditions', [])
        declared_availability = read_json(knowledge / 'quest_data.repairs.json').get('QuestAvailabilityConditions', [])
        if availability != declared_availability:
            raise ValueError('Loaded availability contracts differ from the shipped repair pack')
        for contract in availability:
            if not validate_availability_result(contract, simulations[contract['QuestId']]):
                raise ValueError('Every availability contract requires actual status and publication owner cases')
        ids = classification_partition(ledger, [quest['Id'] for quest in data['Quests']])
        counts = {category: len(values) for category, values in ids.items()}
        if counts != fixture['classification_counts']:
            raise ValueError('The six classification counts differ from the exact ledger')
        outputs = {'simulation_sha256': 'quest-simulation.jsonl',
                   'effective_model_sha256': 'quest-simulation.jsonl.effective-model.json',
                   'dependency_metadata_sha256': 'quest-simulation.jsonl.dependency-metadata.json',
                   'strategy_results_sha256': 'strategy-simulation.jsonl'}
        output_hashes = {key: file_sha256(output / name) for key, name in outputs.items()}
        for key, actual in output_hashes.items():
            if actual != fixture['expected_output_sha256'][key]:
                raise ValueError('Current owner output differs from the reviewed evidence: ' + key)
        verification = {'quest_count': len(rows), 'classification_counts': counts,
                        'simulation_checks': sum(row['passed_cases'] for row in rows),
                        'strategy_checks': sum(row['passed_cases'] for row in strategies.values()),
                        'strategy_quest_ids': sorted(strategies), 'failed_cases': 0,
                        'availability_condition_quests': len(availability),
                        'availability_condition_checks': sum(simulations[contract['QuestId']]['availability_condition_validation']['passed_cases'] for contract in availability),
                        'output_sha256': output_hashes, 'live_completion_proven': False,
                        'claim_limit': 'Actual owners under controlled source-bound observations; no game attachment.'}
        write_json(output / 'verification.json', verification)
        results.append({'stage': 'evidence-validation', 'exit': 0})
    except Exception as error:
        failure = error
        results.append({'stage': 'failure', 'exit': 1, 'error': str(error)})
    after = source_inputs(repo)
    write_json(output / 'source-after.json', after)
    if source_identity(repo, args.allow_dirty) != identity or after != before:
        failure = failure or RuntimeError('Candidate inputs changed during the quest closure run')
        results.append({'stage': 'source-stability', 'exit': 1})
    summary = {'base': identity['production_commit'], 'source_stable': before == after,
               'stages': len(results), 'failed': [row['stage'] for row in results if row['exit'] != 0],
               'source_inputs': len(before), 'working_tree_modified': identity['working_tree_modified'],
               'game_attached': False, 'architecture': 'x86', 'completed_utc': datetime.now(timezone.utc).isoformat()}
    write_json(output / 'results.json', results)
    write_json(output / 'summary.json', summary)
    print(json.dumps(summary, indent=2), flush=True)
    if failure:
        raise failure


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--repo', type=Path, default=Path.cwd())
    parser.add_argument('--runtime', type=Path, required=True)
    parser.add_argument('--output', type=Path, required=True)
    parser.add_argument('--allow-dirty', action='store_true', help='Diagnostic iteration only; not release acceptance')
    run(parser.parse_args())


if __name__ == '__main__':
    main()
