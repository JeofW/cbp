"""Deterministic, read-only analysis of captured logs.

Percentiles describe emitted warning samples, NOT the uncensored pulse population.
Spatial clusters are conservative investigation buckets, NOT proven unique failures.
All references retain original one-based source lines and capture-local timestamps.
"""
from collections import Counter, defaultdict
import math
import re

PATTERNS = ('Slow bot root', 'Slow plugin', 'Exception', 'NullReferenceException',
            'Building spell book', '[Nav] MoveTo failed', 'partial=True', 'We are stuck',
            'Blackspot', 'Mount-up request cancelled', 'PathGenerationFailed',
            'Slow path generation', 'ThreadInterruptedException')
STAMP = re.compile(r'^\[(\d{2}):(\d{2}):(\d{2})\.(\d{3})\]')
NUMBER = r'[-+]?(?:\d+(?:\.\d*)?|\.\d+)'
POINT = r'\(\s*(' + NUMBER + r'),\s*(' + NUMBER + r'),\s*(' + NUMBER + r')\s*\)'
ROOT = re.compile(r'Slow bot root: (\d+)ms bot=(.*?) poi=(\S+) combat=(\S+)')
PLUGIN = re.compile(r'Slow plugin: name=(.*?) elapsed=(\d+)ms')
PATH = re.compile(r'Slow path generation: (\d+)ms')
SHARED = re.compile(r'Slow shared pulse: (.*)')
EXCEPTION = re.compile(r'\b(?:[\w]+\.)*([\w]*Exception)\b')
FRAME = re.compile(r'\bat ([\w.`+<>]+)\(')


def parse_records(text):
    records, current = [], None
    previous, day, segment = None, 0, 0
    for number, line in enumerate(text.splitlines(), 1):
        match = STAMP.match(line.lstrip('\ufeff'))
        if match and all(int(x) < limit for x, limit in zip(match.groups()[:3], (24, 60, 60))):
            h, m, s, ms = map(int, match.groups())
            value = ((h * 60 + m) * 60 + s) * 1000 + ms
            if previous is not None and value < previous:
                if previous - value > 12 * 3600 * 1000:
                    day += 1
                else:
                    segment += 1
                    day = 0
            previous = value
            current = dict(line=number, end_line=number, timestamp=match.group(0)[1:-1],
                           ms=value + day * 86400000, clock_segment=segment, text=line)
            records.append(current)
        elif current is None:
            current = dict(line=number, end_line=number, timestamp=None, ms=None,
                           clock_segment=segment, text=line)
            records.append(current)
        else:
            current['text'] += '\n' + line
            current['end_line'] = number
    return records


def point_after(text, prefixes):
    for prefix in prefixes:
        match = re.search(prefix + r'\s*' + POINT, text)
        if match:
            return [float(x) for x in match.groups()]
    return None


def distribution(values):
    if not values:
        return {'n': 0}
    ordered = sorted(values)
    if not all(math.isfinite(v) and v >= 0 for v in ordered):
        raise ValueError('Duration samples must be finite and nonnegative')
    def percentile(p):
        index = (len(ordered) - 1) * p
        low = math.floor(index)
        high = math.ceil(index)
        return round(ordered[low] + (ordered[high] - ordered[low]) * (index - low), 3)
    return dict(n=len(ordered), p50=percentile(.5), p95=percentile(.95),
                p99=percentile(.99), max=ordered[-1])


def analyze_text(text, name):
    lines = text.splitlines()
    result = dict(capture=name, line_count=len(lines), record_count=0,
                  raw_line_matches={p: sum(p in s for s in lines) for p in PATTERNS},
                  blackspot_case_insensitive_lines=sum('blackspot' in s.lower() for s in lines),
                  skipped_path_node_lines=sum(bool(re.search(r'Skipped \d+ path nodes', s)) for s in lines),
                  runs=[], samples=defaultdict(list), navigation=[], exceptions=[],
                  clock_segments=0, untimed_records=0)
    run_number, poi = 0, None
    run = dict(run=0, start_line=1, start_time=None, spellbook_builds=0,
               spellmanager_initializations=0, lua_subscription_messages=0)
    records = parse_records(text)
    result['record_count'] = len(records)
    result['clock_segments'] = len({r['clock_segment'] for r in records})
    exception_open = {}
    for record in records:
        message = record['text']
        if 'Starting the bot.' in message:
            if run_number or run['spellbook_builds'] or run['spellmanager_initializations']:
                result['runs'].append(run)
            run_number += 1
            poi = None
            run = dict(run=run_number, start_line=record['line'], start_time=record['timestamp'],
                       spellbook_builds=0, spellmanager_initializations=0, lua_subscription_messages=0)
        run['spellbook_builds'] += message.count('Building spell book')
        run['spellmanager_initializations'] += message.count('[SpellManager] Initialize')
        run['lua_subscription_messages'] += message.count('Subscribed to LEARNED_SPELL_IN_TAB, ACTIVE_TALENT_GROUP_CHANGED')
        if record['ms'] is None:
            result['untimed_records'] += 1
        poi_match = re.search(r'Changed POI to: Type: (\w+)', message)
        if poi_match:
            poi = poi_match.group(1)
        root = ROOT.search(message)
        if root:
            duration, bot, kind, combat = root.groups()
            result['samples'][f'root:{bot}:{kind}:{combat}'].append(int(duration))
            result['samples']['root:all'].append(int(duration))
        plugin = PLUGIN.search(message)
        if plugin:
            result['samples']['plugin:' + plugin[1]].append(int(plugin[2]))
        path = PATH.search(message)
        if path:
            result['samples']['path:all'].append(int(path[1]))
        shared = SHARED.search(message)
        if shared:
            for subsystem, duration in re.findall(r'(\w+)=(\d+)', shared[1]):
                result['samples']['shared:' + subsystem].append(int(duration))
        signature = None
        if '[Nav] MoveTo failed' in message:
            signature = 'partial_path' if 'partial=True' in message else 'path_failure'
        elif 'We are stuck' in message:
            signature = 'stuck'
        elif 'Exhausted partial path remained stationary' in message:
            signature = 'partial_recovery'
        elif 'Ground movement made no progress' in message:
            signature = 'commanded_no_progress'
        elif 'Could not generate path from' in message:
            signature = 'native_path_failure'
        if signature:
            map_match = re.search(r'\bmap[= ](\d+)', message)
            event = {k: record[k] for k in ('line', 'end_line', 'timestamp', 'ms', 'clock_segment')}
            event.update(capture=name, run=run_number, signature=signature, poi=poi,
                         map=int(map_match[1]) if map_match else None,
                         location=point_after(message, (r'from=', r'loc:', r'at', r'from')),
                         destination=point_after(message, (r'to=', r'toward', r'destination=', r'to')))
            for field in ('pathCount', 'pathIndex'):
                match = re.search(field + r'=(\d+)', message)
                event[field] = int(match[1]) if match else None
            event['partial'] = True if 'partial=True' in message else False if 'partial=False' in message else None
            result['navigation'].append(event)
        error = EXCEPTION.search(message)
        if error:
            frames = FRAME.findall(message)
            key = (run_number, record['clock_segment'], error[1], tuple(frames[:3]))
            prior = exception_open.get(key)
            now = record['ms']
            if prior is not None and now is not None and prior['last_ms'] is not None and 0 <= now - prior['last_ms'] <= 5000:
                prior.update(last_line=record['end_line'], last_ms=now)
                prior['records'] += 1
            else:
                prior = dict(capture=name, run=run_number, type=error[1], frames=frames[:3],
                             first_line=record['line'], last_line=record['end_line'],
                             first_timestamp=record['timestamp'], last_ms=now, records=1)
                result['exceptions'].append(prior)
                exception_open[key] = prior
    if run_number or run['spellbook_builds'] or run['spellmanager_initializations']:
        result['runs'].append(run)
    result['samples'] = dict(result['samples'])
    return result


def cluster_navigation(events, radius=30, window_ms=30000):
    """Fixed-anchor 3D spatial buckets; no transitive spatial chaining or assumed maps.

    Missing location, timestamp or map yields a singleton marked incomplete. Destination
    equality is tolerant within five game yards, with unknown destinations kept distinct
    from known ones. Signatures can share a bucket but remain separately counted.
    """
    if radius <= 0 or window_ms <= 0:
        raise ValueError('Clustering thresholds must be positive')
    clusters = []
    for event in events:
        found = None
        complete = all(event.get(k) is not None for k in ('location', 'ms', 'map'))
        if complete:
            for cluster in reversed(clusters):
                same_context = all(event.get(k) == cluster.get(k) for k in ('capture', 'run', 'clock_segment', 'map', 'poi'))
                dest, other = event.get('destination'), cluster.get('destination')
                same_dest = dest is None and other is None or dest is not None and other is not None and math.dist(dest, other) <= 5
                if (same_context and same_dest and cluster['spatially_resolved'] and
                    0 <= event['ms'] - cluster['last_ms'] <= window_ms and
                    math.dist(event['location'], cluster['anchor']) <= radius):
                    found = cluster
                    break
        if found is None:
            found = {k: event.get(k) for k in ('capture', 'run', 'clock_segment', 'map', 'poi', 'destination')}
            found.update(id=f'nav-cluster-{len(clusters) + 1}', anchor=event.get('location'),
                         first_line=event['line'], last_line=event['line'], last_ms=event.get('ms'),
                         spatially_resolved=complete, event_count=0, signatures={}, event_lines=[])
            clusters.append(found)
        found['event_count'] += 1
        found['signatures'][event['signature']] = found['signatures'].get(event['signature'], 0) + 1
        found['event_lines'].append(event['line'])
        found['last_line'], found['last_ms'] = event['line'], event.get('ms')
    return clusters


def analyze_directory(root):
    import hashlib
    captures, samples, totals = [], defaultdict(list), Counter()
    for path in sorted((root / 'runtime-logs').rglob('*')):
        if not path.is_file():
            continue
        data = path.read_bytes()
        capture = analyze_text(data.decode('utf-8-sig', errors='replace'), path.relative_to(root).as_posix())
        capture.update(bytes=len(data), sha256=hashlib.sha256(data).hexdigest(),
                       decoding_replacements=data.decode('utf-8-sig', errors='replace').count('\ufffd'))
        for key, values in capture.pop('samples').items():
            samples[key].extend(values)
        totals.update(capture['raw_line_matches'])
        captures.append(capture)
    events = [event for capture in captures for event in capture['navigation']]
    return dict(schema_version=1, measurement_population='emitted slow warnings; threshold-censored, not all ticks',
                percentile_method='linear interpolation at (n-1)*p',
                clustering='fixed 3D anchor 30y; 30s quiet gap; map/run/POI/destination; unresolved singleton',
                captures=captures, raw_line_matches=dict(totals),
                metrics={key: distribution(values) for key, values in sorted(samples.items())},
                navigation_clusters=cluster_navigation(events))
