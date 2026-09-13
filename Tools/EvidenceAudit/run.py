"""Regenerate private audit evidence from an explicit repository snapshot."""
import argparse
import hashlib
import json
from pathlib import Path
import csv
import graph
import quests
import runtime
import structure


def dump(path, value):
    path.write_text(json.dumps(value, indent=2, ensure_ascii=True), encoding='utf-8')


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('root', type=Path)
    parser.add_argument('out', type=Path)
    parser.add_argument('--source-commit', required=True,
                        help='Commit of the audited source snapshot, not the analyzer checkout')
    args = parser.parse_args()
    root, out = args.root.resolve(), args.out.resolve()
    if not (root / 'CODEX_AUDIT_PROMPT.md').is_file():
        parser.error('Root is not a CopilotBuddy audit source snapshot')
    if out == root or root in out.parents:
        parser.error('Output must be outside the audited tree to avoid recursive self-audits')
    out.mkdir(parents=True, exist_ok=True)
    graph.BASELINE = args.source_commit
    logs = runtime.analyze_directory(root)
    dump(out / 'runtime.json', logs)
    syntax = structure.extract_repository(root)
    syntax['source_commit'] = args.source_commit
    dump(out / 'structure.json', syntax)
    structure.write_graphml(syntax, out / 'structure.graphml')
    overlay = graph.build(root)
    dump(out / 'causal-graph.json', overlay)
    structure.write_graphml(overlay, out / 'causal-graph.graphml')
    graph.write_html(overlay, out / 'causal-graph.html')
    dump(out / 'saved-queries.json', overlay['saved_queries'])
    combined = dict(syntax)
    nodes = {n['id']: n for n in syntax['nodes']}
    edges = list(syntax['edges']) + overlay['edges']
    for node in overlay['nodes']:
        nodes[node['id']] = node
        source = node.get('source')
        if source:
            target = 'file:' + source['path']
            nodes.setdefault(target, dict(id=target, type='component', label=source['path'],
                                          sha256=source['sha256'], community='evidence'))
            edges.append(dict(source=node['id'], target=target, type='documented_in',
                              classification='EXTRACTED', confidence=1.0, provenance=[source]))
    combined.update(nodes=list(nodes.values()), edges=edges, saved_queries=overlay['saved_queries'])
    structure.validate_graph(combined)
    dump(out / 'combined-graph.json', combined)
    structure.write_graphml(combined, out / 'combined-graph.graphml')
    data_path = root / 'runtime-snapshot/Bots/WholesomeAutoQuest-master/quest_data/quest_data.json'
    raw = data_path.read_bytes()
    data = json.loads(raw)
    quest_result = quests.audit_database(data)
    zone_files = {p.relative_to(root).as_posix(): json.loads(p.read_bytes())
                  for p in sorted(data_path.parent.glob('Zone_*.json'))}
    quest_result['zones'] = quests.compare_zones(data, zone_files)
    quest_result['source'] = dict(path=data_path.relative_to(root).as_posix(),
                                  sha256=hashlib.sha256(raw).hexdigest(), commit=args.source_commit)
    dump(out / 'quests.json', quest_result)
    behaviors = [n for n in syntax['nodes'] if n['type'] == 'method'
                 and n.get('community', '').startswith('Singular/') and 'Behavior(' in n.get('attributes', '')]
    with (out / 'singular-coverage.csv').open('w', newline='', encoding='utf-8') as stream:
        writer = csv.writer(stream)
        writer.writerow(['community', 'method', 'path', 'start_line', 'attributes', 'runtime_status'])
        for node in behaviors:
            writer.writerow([node['community'], node['label'], node['source']['path'],
                             node['source']['start_line'], node['attributes'], 'NOT_EXECUTED'])
    summary = dict(source_commit=args.source_commit, log_files=len(logs['captures']),
                   physical_log_lines=sum(c['line_count'] for c in logs['captures']),
                   csharp_files=sum(syntax['source_files_by_community'].values()),
                   syntax_nodes=len(syntax['nodes']), syntax_edges=len(syntax['edges']),
                   combined_nodes=len(combined['nodes']), combined_edges=len(combined['edges']),
                   quest_count=quest_result['quest_count'], zone_files=len(zone_files),
                   zone_differences=len(quest_result['zones']['differences']),
                   limitations=['Syntax references are not type-bound callees.',
                                'Warning percentiles are not all-tick percentiles.',
                                'Missing quest records are not unreachable verdicts.',
                                'No live game, mesh geometry, or all-spec acceptance is implied.'])
    dump(out / 'summary.json', summary)
    files = sorted(p for p in out.iterdir() if p.is_file() and p.name != 'SHA256SUMS')
    (out / 'SHA256SUMS').write_text(''.join(hashlib.sha256(p.read_bytes()).hexdigest() + '  ' + p.name + '\n'
                                          for p in files), encoding='utf-8')
    print(json.dumps(summary, indent=2))


if __name__ == '__main__':
    main()
