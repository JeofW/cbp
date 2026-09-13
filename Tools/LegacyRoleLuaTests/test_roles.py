"""Execute the role queries used by production C# under original-version Lua 5.1.

Only the client API observations are controlled. This does not attach a client or
certify every Lua command. Exact query extraction fails closed on source drift.
"""
import argparse
import hashlib
import json
import re
import subprocess
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
SITES = {
    'Styx/Helpers/WoWPlayerExtensions.cs': 3,
    'Styx/WoWInternals/WoWObjects/WoWPartyMember.cs': 1,
    'runtime-snapshot/Bots/CombatBot.cs': 1,
}
CASES = [
    ('tank', 'true, false, false', 'TANK', True, True),
    ('healer', 'false, true, false', 'HEALER', True, True),
    ('damage', 'false, false, true', 'DAMAGER', True, True),
    ('unassigned', 'false, false, false', 'NONE', True, True),
    ('nil flags', 'nil, nil, nil', 'NONE', True, True),
    ('tank precedence', 'true, true, true', 'TANK', True, True),
    ('healer precedence', 'false, true, true', 'HEALER', True, True),
    ('missing API', 'false, false, false', 'NONE', True, False),
    ('unit departed', 'true, false, false', 'NONE', False, True),
    ('modern role string is not a legacy flag', "'TANK', nil, nil", 'NONE', True, True),
]


def queries(path, expected_count):
    text = (ROOT / path).read_text(encoding='utf-8-sig')
    if 'LegacyGroupRoles.GetAssignedRole(' in text:
        if text.count('LegacyGroupRoles.GetAssignedRole(') != expected_count:
            raise AssertionError('Unexpected adapter count: ' + path)
        helper = (ROOT / 'Styx/Helpers/LegacyGroupRoles.cs').read_text()
        matches = re.findall(r'QueryTemplate\s*=\s*@"((?:[^"]|"")*)";', helper, re.S)
        if len(matches) != 1:
            raise AssertionError('Expected one actual verbatim query template')
        template = matches[0].replace('""', '"')
        token = 'player' if 'Extensions' in path else 'party1'
        return [template.replace('{0}', token)] * expected_count
    literals = re.findall(r'"((?:\\.|[^"\\])*)"', text)
    result = []
    for literal in literals:
        if 'return UnitGroupRolesAssigned(' not in literal:
            continue
        script = json.loads('"' + literal + '"')
        script = script.replace('{_unitId}', 'party1').replace('{0}', '1')
        result.append(script)
    if len(result) != expected_count:
        raise AssertionError('Unrecognized production role queries: ' + path)
    return result


def run(interpreter, output):
    version = subprocess.run([interpreter, '-v'], capture_output=True, text=True, check=True)
    if 'Lua 5.1' not in version.stdout + version.stderr:
        raise RuntimeError('This suite requires Lua 5.1, not retail Lua or a translated model')
    results = []
    for path, count in SITES.items():
        for index, query in enumerate(queries(path, count)):
            for name, flags, expected, exists, api in CASES:
                setup = "function UnitExists(unit) return " + str(exists).lower() + " end\n"
                if api:
                    setup += "function UnitGroupRolesAssigned(unit) return " + flags + " end\n"
                test = setup + 'local function actualQuery()\n' + query + '\nend\n'
                test += 'local value = actualQuery()\n'
                test += "assert(value == '" + expected + "', 'expected " + expected + " got '..tostring(value))\n"
                completed = subprocess.run([interpreter, '-'], input=test, capture_output=True, text=True, timeout=10)
                row = {'source': path, 'site': index, 'case': name, 'pass': completed.returncode == 0,
                       'query_sha256': hashlib.sha256(query.encode()).hexdigest(), 'error': completed.stderr.strip()}
                results.append(row)
                print(('PASS ' if row['pass'] else 'FAIL ') + path + ':' + str(index) + ' ' + name)
    output.mkdir(parents=True, exist_ok=True)
    (output / 'results.json').write_text(json.dumps(results, indent=2))
    passed = sum(row['pass'] for row in results)
    print(f'Original Lua role contracts: {passed}/{len(results)}. Controlled client API returns; no game attached.')
    return 0 if passed == len(results) else 1


if __name__ == '__main__':
    parser = argparse.ArgumentParser()
    parser.add_argument('--lua', default='lua5.1')
    parser.add_argument('--out', type=Path, required=True)
    args = parser.parse_args()
    raise SystemExit(run(args.lua, args.out))
