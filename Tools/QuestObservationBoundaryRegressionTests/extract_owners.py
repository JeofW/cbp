"""Prepare a pinned, source-extracted W42 baseline fixture; never change the repo.

Requires a local Git checkout containing the specified commit. No network, LFS,
checkout/reset, remote writes, native calls, or package downloads are performed.
This extracts selected real methods, NOT the full host. Read README.md for limits.
"""
from __future__ import annotations

import argparse
import hashlib
import json
import re
import subprocess
from pathlib import Path

BASE = "0e2c092b23514ffd530a9d14d0270a9b1dcca0d7"
SOURCE_PATHS = {
    "log": "Styx/Logic/Questing/QuestLog.cs",
    "quest": "Styx/Logic/Questing/PlayerQuest.cs",
    "sale": "runtime-snapshot/Bots/WholesomeAutoQuest-master/WholesomeAutoQuest.cs",
    "protection": "Styx/Logic/Profiles/ProtectedItemsManager.cs",
    "sets": "Styx/Helpers/DualHashSet.cs",
    "pairs": "Styx/Helpers/ValuePair.cs",
}
# These four blob identities were read directly from GitHub during this slice.
KNOWN_BLOBS = {
    "log": "cdceb09dd4c3f2cfdff00beb761c6dbc5807a850",
    "quest": "a13d62739f7c29863977ed38feac2eb1e4be2996",
    "sale": "db70754f12bd9a73839bddd0d24a46aa33ed829a",
    "protection": "86e4e0a51d902be26491ef2179107322768eb787",
}
LOG_METHODS = [
    "public List<PlayerQuest> GetAllQuests()",
    "public PlayerQuest GetQuest(uint index)",
    "public uint GetQuestId(uint index)",
    "private uint GetQuestIdAtIndex(uint index)",
    "public bool ContainsQuest(uint questId)",
    "public PlayerQuest GetQuestById(uint questId)",
    "public QuestCompletionSnapshot GetQuestCompletionSnapshot(uint questId)",
    "internal static QuestCompletionSnapshot ResolveQuestCompletionSnapshot(",
    "internal static QuestCompletionState ResolveQuestCompletionState(",
]


def git_blob_sha(data: bytes) -> str:
    header = b"blob " + str(len(data)).encode("ascii") + b"\0"
    return hashlib.sha1(header + data).hexdigest()


def _code_mask(source: str) -> str:
    """Keep positions; blank comments and ordinary/verbatim string/char literals.

    C# raw strings are rejected instead of silently misparsed. This is a bounded
    lexical extractor, not a C# parser. A future syntax change requires review.
    """
    out = list(source)
    i, n = 0, len(source)
    while i < n:
        start = i
        if source.startswith('//', i):
            end = source.find('\n', i + 2)
            i = n if end < 0 else end
        elif source.startswith('/*', i):
            end = source.find('*/', i + 2)
            if end < 0:
                raise ValueError('Unterminated block comment')
            i = end + 2
        elif source.startswith('"""', i):
            raise ValueError('Raw string syntax requires an explicit extractor update')
        elif source[i] in ('"', "'"):
            quote = source[i]
            verbatim = quote == '"' and i > 0 and (
                source[i - 1] == '@' or source[max(0, i - 2):i] == '@$')
            i += 1
            while i < n:
                if source[i] == quote:
                    if verbatim and i + 1 < n and source[i + 1] == quote:
                        i += 2
                        continue
                    i += 1
                    break
                if not verbatim and source[i] == '\\':
                    i += 2
                else:
                    i += 1
            else:
                raise ValueError('Unterminated string or character literal')
        else:
            i += 1
            continue
        for pos in range(start, min(i, n)):
            if out[pos] not in '\r\n':
                out[pos] = ' '
    return ''.join(out)


def extract_block(source: str, signature: str) -> str:
    """Return one exact declaration/body, rejecting missing or ambiguous matches."""
    masked = _code_mask(source)
    matches = list(re.finditer(re.escape(signature), masked))
    if len(matches) != 1:
        raise ValueError(f'Expected exactly one code declaration for {signature!r}; got {len(matches)}')
    start = matches[0].start()
    opening = masked.find('{', matches[0].end())
    if opening < 0:
        raise ValueError(f'Missing body for {signature!r}')
    prefix = masked[matches[0].end():opening]
    if '=>' in prefix or ';' in prefix:
        raise ValueError('Expression-bodied/abstract declarations require explicit extraction support')
    depth = 0
    for pos in range(opening, len(masked)):
        if masked[pos] == '{':
            depth += 1
        elif masked[pos] == '}':
            depth -= 1
            if depth == 0:
                return source[start:pos + 1]
    raise ValueError(f'Unbalanced declaration {signature!r}')


def git(repo: Path, *args: str) -> bytes:
    result = subprocess.run(['git', '-C', str(repo), *args], capture_output=True, check=False)
    if result.returncode:
        message = result.stderr.decode('utf-8', errors='replace').strip()
        raise RuntimeError(f'Git read failed: {message}')
    return result.stdout


def generate(repo: Path, ref: str, out: Path) -> dict:
    if not re.fullmatch(r'[0-9a-fA-F]{40}', ref):
        raise ValueError('--ref must be a full immutable 40-character commit SHA')
    commit = git(repo, 'rev-parse', '--verify', f'{ref}^{{commit}}').decode().strip()
    if out.exists() and any(out.iterdir()):
        raise FileExistsError(f'Refusing to overwrite non-empty output: {out}')
    source, manifest = {}, {'source_commit': commit, 'kind': 'source-extraction-only',
                             'csharp_executed': False, 'game_attached': False, 'files': {}}
    for key, path in SOURCE_PATHS.items():
        raw = git(repo, 'show', f'{commit}:{path}')
        blob = git_blob_sha(raw)
        if commit == BASE and key in KNOWN_BLOBS and blob != KNOWN_BLOBS[key]:
            raise ValueError(f'Pinned baseline blob mismatch: {path}')
        source[key] = raw.decode('utf-8-sig')
        manifest['files'][path] = {'git_blob': blob, 'sha256': hashlib.sha256(raw).hexdigest(),
                                    'bytes': len(raw)}
    header = ('#nullable disable\nusing System; using System.Collections.Generic; '
              'using System.Collections.ObjectModel; using System.Linq; '
              'using Styx; using Styx.Logic.Profiles; using Styx.WoWInternals; '
              'using Styx.WoWInternals.WoWCache;\n')
    types = '\n\n'.join(extract_block(source['log'], sig) for sig in [
        'public enum QuestCompletionState', 'public readonly struct QuestCompletionSnapshot'])
    methods = '\n\n'.join(extract_block(source['log'], sig) for sig in LOG_METHODS)
    quest_methods = '\n\n'.join(extract_block(source['quest'], sig) for sig in [
        'protected PlayerQuest(WoWCache.QuestCacheEntry entry)',
        'internal static new PlayerQuest FromId(uint id)'])
    sale = extract_block(source['sale'], 'private void SellByQuality()')
    generated = {
        'QuestLogOwner.g.cs': header + '\nnamespace Styx.Logic.Questing {\n' + types +
            '\npublic partial class QuestLog {\n' + methods + '\n}\n}\n',
        'PlayerQuestOwner.g.cs': header + '\nnamespace Styx.Logic.Questing {\n'
            'public partial class PlayerQuest : Quest {\n' + quest_methods + '\n}\n}\n',
        'SaleOwner.g.cs': header + '\nnamespace WholesomeAQ {\n'
            'public partial class WholesomeAutoQuest {\n' + sale + '\n}\n}\n',
        'ProtectedItemsManager.cs': source['protection'],
        'DualHashSet.cs': source['sets'],
        'ValuePair.cs': source['pairs'],
    }
    manifest['generated_sha256'] = {name: hashlib.sha256(text.encode('utf-8')).hexdigest()
                                    for name, text in generated.items()}
    manifest['extracted_log_signatures'] = LOG_METHODS
    out.mkdir(parents=True, exist_ok=True)
    for name, text in generated.items():
        (out / name).write_text(text, encoding='utf-8', newline='\n')
    (out / 'source-manifest.json').write_text(json.dumps(manifest, indent=2) + '\n', encoding='utf-8')
    return manifest


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--repo', type=Path, required=True)
    parser.add_argument('--ref', default=BASE)
    parser.add_argument('--out', type=Path, required=True)
    args = parser.parse_args()
    try:
        result = generate(args.repo.resolve(), args.ref, args.out.resolve())
    except (OSError, ValueError, RuntimeError) as error:
        parser.exit(2, f'Extraction failed: {error}\n')
    print(json.dumps({'source_commit': result['source_commit'],
                      'generated_files': len(result['generated_sha256']),
                      'csharp_executed': False}, indent=2))


if __name__ == '__main__':
    main()
