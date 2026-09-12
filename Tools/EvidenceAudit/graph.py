"""Curated directed hypothesis overlay; does not upgrade adjacency into causation."""
import argparse
import hashlib
import json
from pathlib import Path
from structure import validate_graph, write_graphml

BASELINE = '63112d57a373c1186cee880f98dab65245a214e1'
SOURCES = {
 'intent': ('Styx/Logic/Pathing/Navigator.cs', 470, 500),
 'partial': ('Styx/Logic/Pathing/MeshNavigator.cs', 875, 896),
 'lift': ('Styx/Logic/Pathing/MeshNavigator.cs', 1611, 1765),
 'stuck': ('Styx/Logic/Pathing/StuckHandler.cs', 1, 321),
 'movement': ('Styx/WoWInternals/WoWMovement.cs', 554, 558),
 'stop': ('Styx/Logic/BehaviorTree/TreeRoot.cs', 614, 696),
 'spell': ('Styx/Logic/Combat/SpellManager.cs', 1083, 1121),
 'start': ('Styx/Logic/BehaviorTree/TreeRoot.cs', 116, 144),
 'ret': ('runtime-snapshot/Routines/Singular wotlk/ClassSpecific/Paladin/Retribution.cs', 65, 280),
 'cast': ('runtime-snapshot/Routines/Singular wotlk/Helpers/Spell.cs', 255, 335),
 'quest': ('runtime-snapshot/Bots/WholesomeAutoQuest-master/WholesomeAutoQuest.cs', 1600, 1787),
 'selection': ('runtime-snapshot/Bots/WholesomeAutoQuest-master/QuestSchedulingPolicy.cs', 1, 131),
 'assessment': ('runtime-snapshot/Bots/WholesomeAutoQuest-master/QuestScheduler.cs', 990, 1110),
 'profile': ('runtime-snapshot/Bots/WholesomeAutoQuest-master/ProfileBuilder.cs', 1, 210),
 'fingerprint': ('runtime-snapshot/Bots/WholesomeAutoQuest-master/DataLoader.cs', 116, 128),
 'plugin': ('runtime-snapshot/Plugins/AutoEquip2/AutoEquip.cs', 61, 190),
 'boarding': ('Styx/Logic/Pathing/ElevatorTransitController.cs', 107, 146),
 'log': ('runtime-logs/2026-09-12_1701_45740.log', 21758, 21980),
 'distance': ('Styx/WoWInternals/WoWObjects/WoWObject.cs', 203, 234),
 'build': ('CopilotBuddy.csproj', 39, 54),
}
LOOPS = {
 'thunder-bluff': (['intent', 'partial', 'lift', 'stuck', 'intent'],
  'Wrong-floor partial travel repeats without a validated lift exit.',
  'Capture live transform, both horizontal gates, landing samples and route IDs; a validated ramp or an unavailable exit falsifies radius-only attribution.'),
 'mount-buff-recovery': (['stuck', 'movement', 'ret', 'cast', 'stuck'],
  'Recovery dismount, buff casts and mount cancellation may extend stationarity.',
  'Replay with travel maintenance suppressed, holding route and native latency constant; compare command ownership and progress.'),
 'spell-lifecycle': (['start', 'spell', 'start'],
  'Initialize adds a start handler while already executing the start event; later starts invoke accumulated handlers.',
  'Count invocation-list size and refreshes across 13 starts. A stable one-handler/one-refresh result contradicts the growth explanation.'),
 'pulse-latency': (['plugin', 'movement', 'stuck', 'partial', 'plugin'],
  'Synchronous work may delay progress sampling and recovery may add more work.',
  'Collect every tick and sample timestamps; replay artificial plugin delay. Existing speed-based stuck gates may reject a latency-only explanation.'),
 'blackspot-replan': (['stuck', 'partial', 'intent', 'stuck'],
  'A recovery blackspot may alter later paths and produce direction reversal.',
  'Record blackspot version/provenance, route revisions and positions; replay with fixed geometry. Co-occurrence alone is insufficient.'),
 'lookahead-corner': (['intent', 'lift', 'partial', 'stuck', 'intent'],
  'Look-ahead or shortcutting may select a segment crossing unsupported or colliding geometry.',
  'Replay the same corridor and skip candidates with collision and ground-support checks; no unsafe accepted segment falsifies this hypothesis.'),
 'poi-floor': (['selection', 'assessment', 'intent', 'partial', 'selection'],
  'Geometric endpoint ordering may repeatedly choose a costly or inaccessible floor.',
  'Compare complete approach/wait/ride/exit/onward cost and selection failure history; partial paths already retain unknown reachability.'),
 'movement-ownership': (['quest', 'intent', 'stuck', 'movement', 'cast', 'quest'],
  'Independent movement and action producers may cancel or preempt each other.',
  'Add intent/generation IDs and command-owner events; show conflicting commands in one interval, then replay with arbitration.'),
}


def build(root):
    nodes, edges = {}, []
    def source(key):
        path, first, last = SOURCES[key]
        raw = (root / path).read_bytes()
        if len(raw.decode('utf-8-sig', errors='replace').splitlines()) < last:
            raise ValueError('Invalid evidence range: ' + path)
        return {'path': path, 'start_line': first, 'end_line': last,
                'sha256': hashlib.sha256(raw).hexdigest(), 'commit': BASELINE}
    for key in SOURCES:
        nodes[key] = {'id': key, 'type': 'log_event' if key == 'log' else 'component',
                      'label': key, 'source': source(key), 'community': key}
    for name, (members, reason, falsifier) in LOOPS.items():
        ident = 'hypothesis:' + name
        nodes[ident] = {'id': ident, 'type': 'hypothesis', 'label': name,
                        'status': 'requires_reproduction', 'reason': reason, 'falsification': falsifier}
        for left, right in zip(members, members[1:]):
            edges.append({'source': left, 'target': right, 'type': 'contributes_to',
                          'classification': 'INFERRED', 'confidence': .85 if name == 'spell-lifecycle' else .4,
                          'provenance': [source(left), source(right)], 'loop': name,
                          'reason': reason, 'falsification': falsifier})
        edges.append({'source': ident, 'target': members[0], 'type': 'requires_reproduction',
                      'classification': 'INFERRED', 'confidence': 1,
                      'provenance': [source(members[0])], 'reason': reason, 'falsification': falsifier})
    for ident, label, target, evidence, reason, falsifier in [
        ('49-yards', '49-yard log does not prove horizontal gate rejection', 'lift', 'distance',
         'Logged Distance is 3D; the preference gate uses 2D and animated location.', 'Capture both distance definitions and transforms simultaneously.'),
        ('duration-sleep', 'Timed movement queues a deadline rather than sleeping', 'movement', 'movement',
         'Current duration overload schedules a stop after initiating the native move.', 'Inspect the compiled overload and native wait stack in the deployed build.'),
        ('partial-unreachable', 'Partial paths are not automatically declared unreachable', 'selection', 'assessment',
         'AssessMeshNavigation deliberately preserves unknown reachability for a partial path.', 'Trace how that unknown value is consumed by every selector.'),
        ('quest-transport', 'Quest sampling already has transport suspension context', 'quest', 'quest',
         'Transport and attempt-generation state participate in progress sampling.', 'Replay waits and ensure suspension reaches the evaluator without counting failure.')]:
        node = 'counter-evidence:' + ident
        nodes[node] = {'id': node, 'type': 'counter_evidence', 'label': label}
        edges.append({'source': node, 'target': target, 'type': 'contradicts',
                      'classification': 'INFERRED', 'confidence': .95,
                      'provenance': [source(evidence)], 'reason': reason, 'falsification': falsifier})
    graph = {'schema_version': 1, 'directed': True, 'baseline': BASELINE,
             'scope': 'Curated hypothesis overlay; merge with structure.json for syntax graph. Not a type-bound call graph.',
             'nodes': list(nodes.values()), 'edges': edges,
             'saved_queries': {key: {'loop': key, 'seeds': members} for key, (members, _, _) in LOOPS.items()}}
    graph['saved_queries'].update({'cliff-safety': {'seeds': ['boarding', 'stuck', 'lift']},
                                  'wholesome-ownership': {'seeds': ['quest', 'selection', 'profile', 'fingerprint']},
                                  'retribution-decisions': {'seeds': ['ret', 'cast', 'spell']}})
    validate_graph(graph)
    return graph


def write_html(graph, path):
    payload = json.dumps(graph, ensure_ascii=True).replace('<', '\\u003c')
    template = Path(__file__).with_name('viewer.html').read_text(encoding='utf-8')
    path.write_text(template.replace('PAYLOAD', payload), encoding='utf-8')


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('root', type=Path)
    parser.add_argument('out', type=Path)
    args = parser.parse_args()
    graph = build(args.root.resolve()); args.out.mkdir(parents=True, exist_ok=True)
    (args.out / 'causal-graph.json').write_text(json.dumps(graph, indent=2), encoding='utf-8')
    write_graphml(graph, args.out / 'causal-graph.graphml')
    write_html(graph, args.out / 'causal-graph.html')
    (args.out / 'saved-queries.json').write_text(json.dumps(graph['saved_queries'], indent=2), encoding='utf-8')
