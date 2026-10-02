"""Reproduce supported rest families from the pinned original 12340 Spell.dbc."""
import argparse
import hashlib
import json
from pathlib import Path
import struct

DBC_SHA256 = 'd5cce1a83550dcfa9eb2f0251dbb11fd24c272534b2b1a9b230924a44d817ab3'
NAMES = {'Food', 'Drink', 'Refreshment', 'Starfire Espresso'}


def derive(dbc):
    data = dbc.read_bytes()
    if hashlib.sha256(data).hexdigest() != DBC_SHA256:
        raise ValueError('Expected the pinned original build12340 enUS Spell.dbc')
    magic, count, fields, size, string_size = struct.unpack_from('<4s4I', data)
    if (magic, count, fields, size) != (b'WDBC', 49839, 234, 936):
        raise ValueError('Unexpected original-client table layout')
    strings = data[20 + count * size:]
    if len(strings) != string_size:
        raise ValueError('Truncated string table')
    rows = {row[0]: row for row in struct.iter_unpack('<234I', data[20:20 + count * size])}

    def name(row):
        offset = row[136]
        return strings[offset:strings.index(b'\0', offset)].decode('utf-8')

    def recovery(spell_id, seen=None):
        seen = set() if seen is None else seen
        if spell_id in seen:
            return set()
        seen.add(spell_id)
        row = rows[spell_id]
        result = {spell_id} if any(effect == 6 and aura in {20, 21, 84, 85}
                                  for effect, aura in zip(row[71:74], row[95:98])) else set()
        for effect, trigger in zip(row[71:74], row[116:119]):
            if effect == 64 and trigger:
                result.update(recovery(trigger, seen))
        return result

    records = [{'id': spell_id, 'name': name(row), 'aura_interrupt_flags': row[32],
                'effects': list(row[71:74]), 'auras': list(row[95:98]),
                'trigger_spells': list(row[116:119]), 'recovery_aura_ids': sorted(recovery(spell_id))}
               for spell_id, row in sorted(rows.items()) if name(row) in NAMES]
    facts = {'dbc_sha256': DBC_SHA256, 'records': records}
    lines = ['using System.Collections.Generic;', '', 'namespace Styx.Logic.Common;', '',
             '// Generated from original build12340 enUS Spell.dbc; exact source rows and',
             '// hash are retained in docs/audit/2026-10-02/rest-observation/spell-families.json.',
             '// These sets describe supported rest activity, not arbitrary unknown mechanics.',
             'internal static class RestSpellFamilies', '{']
    for family, names in [('Food', {'Food', 'Refreshment'}), ('Drink', {'Drink', 'Refreshment', 'Starfire Espresso'})]:
        ids = [r['id'] for r in records if r['name'] in names]
        lines.extend([f'    internal static readonly HashSet<int> {family} = new()', '    {'])
        for offset in range(0, len(ids), 12):
            lines.append('        ' + ', '.join(map(str, ids[offset:offset + 12])) + ',')
        lines.append('    };')
    lines.extend(['    // Original TriggerSpell effects are followed to named rest health/mana',
                  '    // auras. Persistent bonus effects (for example Mana Regeneration18194)',
                  '    // are not prerequisites for acknowledging the initial rest effect.',
                  '    internal static int[] RecoveryAuras(int id) => id switch', '    {'])
    for record in records:
        # The full trace remains in the facts; only named rest effects acknowledge rest.
        ids = [value for value in record['recovery_aura_ids'] if name(rows[value]) in NAMES]
        if ids:
            lines.append(f"        {record['id']} => new[] {{ " + ', '.join(map(str, ids)) + ' },')
    lines.extend(['        _ => System.Array.Empty<int>()', '    };', '}', ''])
    return facts, '\n'.join(lines)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--dbc', required=True, type=Path)
    parser.add_argument('--repo', type=Path, default=Path(__file__).resolve().parents[2])
    parser.add_argument('--write', action='store_true', help='Regenerate; default verifies existing artifacts')
    args = parser.parse_args()
    facts, source = derive(args.dbc)
    facts_path = args.repo / 'docs/audit/2026-10-02/rest-observation/spell-families.json'
    source_path = args.repo / 'Styx/Logic/Common/RestSpellFamilies.cs'
    if args.write:
        facts_path.parent.mkdir(parents=True, exist_ok=True)
        facts_path.write_text(json.dumps(facts, indent=2) + '\n', encoding='utf-8')
        source_path.write_text(source, encoding='utf-8')
    elif json.loads(facts_path.read_text(encoding='utf-8-sig')) != facts or source_path.read_text(encoding='utf-8-sig') != source:
        raise ValueError('Tracked rest family artifacts differ from the pinned original-client rows')
    print(json.dumps({'records': len(facts['records']), 'dbc_sha256': DBC_SHA256, 'verified': True}))


if __name__ == '__main__':
    main()
