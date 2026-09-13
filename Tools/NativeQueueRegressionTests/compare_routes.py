"""Compare native candidate outputs exactly; report paired warm timings without a flaky gate."""
from __future__ import annotations
import json
import pathlib
import statistics
import sys

FIELDS = ('route', 'repeat', 'requested_start', 'requested_end', 'succeeded', 'partial',
          'status', 'resource_limited', 'out_of_nodes', 'complete_to_requested',
          'fail_step', 'raw_native_fail_step', 'count', 'points', 'flags', 'areas', 'polygons')

def compare(root: pathlib.Path) -> dict:
    names = ('baseline-1', 'indexed-1', 'indexed-2', 'baseline-2')
    runs = {}
    for name in names:
        rows = json.loads((root / name / 'routes.json').read_text(encoding='utf-8-sig'))
        if len(rows) != 20: raise ValueError(f'{name}: expected twenty complete query records')
        keys = [(row['route'], row['repeat']) for row in rows]
        if len(set(keys)) != 20: raise ValueError(f'{name}: duplicate route/repeat identity')
        if any(len(row['points']) != row['count'] for row in rows): raise ValueError(f'{name}: invalid point counts')
        runs[name] = rows
    reference = [{key: row[key] for key in FIELDS} for row in runs[names[0]]]
    for name in names[1:]:
        actual = [{key: row[key] for key in FIELDS} for row in runs[name]]
        if actual != reference: raise ValueError(f'{name}: native path semantics changed; inspect raw route records')
    timings = []
    for route in sorted({row['route'] for row in runs[names[0]]}):
        values = {}
        for variant in ('baseline', 'indexed'):
            samples = [row['native_call_ms'] for name, rows in runs.items() if name.startswith(variant)
                       for row in rows if row['route'] == route and row['repeat'] > 0]
            values[variant] = {'warm_samples': samples, 'median_ms': statistics.median(samples)}
        timings.append({'route': route, **values, 'median_speed_ratio':
                        values['baseline']['median_ms'] / values['indexed']['median_ms']
                        if values['indexed']['median_ms'] else None})
    return {'semantic_records_identical': 80, 'comparisons_to_reference': 60,
            'game_attached': False, 'order': names, 'timings': timings,
            'scope': 'Same fixtures, filter, assets, native budget and managed host. Not all-map equivalence or live acceptance.'}

if __name__ == '__main__':
    root = pathlib.Path(sys.argv[1])
    result = compare(root)
    (root / 'queue-comparison.json').write_text(json.dumps(result, indent=2))
    print(json.dumps(result, indent=2))
