"""Generate post-initialization test entries; never change production or saved fixtures."""
import argparse
import hashlib
import json
from pathlib import Path
import re

PROJECTS = {'QuestLogObservationRegressionTests', 'WholesomeQuestRecoveryRegressionTests'}
INITIALIZER = re.compile(r'\[ModuleInitializer\](?=\s*internal\s+static\s+void\s+Run\s*\(\s*\))')
WHOLESOME_ENTRY = 'var utcNow = new DateTime(2026, 9, 3, 0, 0, 0, DateTimeKind.Utc);\n'


def digest(data: bytes) -> str:
    return hashlib.sha256(data).hexdigest()


def normalize(project: Path, output: Path) -> dict:
    project, output = project.resolve(), output.resolve()
    if project.name not in PROJECTS:
        raise ValueError('Unsupported test project; production projects are forbidden')
    if not output.is_relative_to(project / 'obj') or output == project / 'obj':
        raise ValueError('Generated output must be a dedicated directory below project/obj')
    sources = {}
    groups = []
    for path in sorted(project.glob('*.cs')):
        raw = path.read_bytes()
        text = raw.decode('utf-8-sig').replace('\r\n', '\n')
        changed = text
        if '[ModuleInitializer]' in text:
            classes = re.findall(r'\binternal\s+static\s+class\s+(\w+)', text)
            if len(classes) != 1 or classes[0] != path.stem or len(INITIALIZER.findall(text)) != 1 or text.count('[ModuleInitializer]') != 1:
                raise ValueError('Unrecognized initializer contract in ' + path.name)
            if re.search(r'^\s*namespace\b', text, re.MULTILINE):
                raise ValueError('Namespaced initializer requires explicit review: ' + path.name)
            changed = INITIALIZER.sub('', text)
            groups.append(classes[0])
        sources[path.name] = (raw, changed)
    if not groups or 'Program.cs' not in sources:
        raise ValueError('Expected test entry and initialized groups were not found')
    raw, entry = sources['Program.cs']
    call = '\nW42PostInitializationGroups.Run();\n'
    if 'W42PostInitializationGroups' in entry:
        raise ValueError('Entry was already normalized; refusing duplicate execution')
    if project.name == 'WholesomeQuestRecoveryRegressionTests':
        if entry.count(WHOLESOME_ENTRY) != 1:
            raise ValueError('Unrecognized Wholesome entry point')
        entry = entry.replace(WHOLESOME_ENTRY, WHOLESOME_ENTRY + call, 1)
    else:
        if not re.search(r'Environment\.ExitCode\s*=.*;\s*$', entry):
            raise ValueError('Unrecognized observation entry point')
        entry += call
    sources['Program.cs'] = (raw, entry)
    driver = ['// Generated test-only group entry. Each failure remains a failing exit code.',
              'internal static class W42PostInitializationGroups', '{', '    internal static void Run()', '    {']
    for group in groups:
        driver += [f'        global::System.Console.WriteLine("BEGIN aggregate group: {group}");',
                   f'        try {{ {group}.Run(); global::System.Console.WriteLine("PASS aggregate group: {group}"); }}',
                   '        catch (global::System.Exception error)', '        {',
                   '            global::System.Environment.ExitCode = 1;',
                   f'            global::System.Console.Error.WriteLine("FAIL aggregate group: {group}: " + error);',
                   '        }']
    driver += ['    }', '}', '']
    generated_driver = '\n'.join(driver).encode()
    output.mkdir(parents=True, exist_ok=True)
    for stale in output.glob('*.cs'):
        stale.unlink()
    files = []
    for name, (raw, text) in sources.items():
        data = text.encode('utf-8')
        (output / name).write_bytes(data)
        files.append({'file': name, 'original_sha256': digest(raw), 'generated_sha256': digest(data)})
    (output / 'W42PostInitializationGroups.cs').write_bytes(generated_driver)
    manifest = {'schema': 1, 'project': project.name, 'groups': groups, 'files': files,
                'driver_sha256': digest(generated_driver), 'production_modified': False}
    (output / 'normalization-manifest.json').write_text(json.dumps(manifest, indent=2) + '\n', encoding='utf-8')
    return manifest


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--project', type=Path, required=True)
    parser.add_argument('--out', type=Path, required=True)
    arguments = parser.parse_args()
    result = normalize(arguments.project, arguments.out)
    print('Normalized test groups:', ', '.join(result['groups']))
