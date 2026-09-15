"""Explicitly scope seven historical leaf-substitution fixtures to the schedule gate.

The original source files and every oracle remain intact. This is a declared
fixture-topology migration, NOT a claim of source-equivalent full-root testing.
Actual root/publication suites are deliberately excluded. Both red and repair
runs must use these same generated fixtures and retain the migration manifest.
"""
import hashlib

HELPER = 'LegacyScheduleGateFixture.cs'
REPLACEMENTS = {
    'Program.cs': '(TreeSharp.GroupComposite)bot.Root',
    'QuestSchedulerRawPublicationRegressionTests.cs': '(GroupComposite)Bot.Root',
    'QuestPublicationAcceptanceRegressionTests.cs': '(GroupComposite)Bot.Root',
    'QuestPublicationRegressionTests.cs': '(GroupComposite)bot.Root',
    'QuestScanEntryRegressionTests.cs': '(GroupComposite)bot.Root',
    'QuestScanFailureRegressionTests.cs': '(GroupComposite)bot.Root',
    'QuestScanRevocationRegressionTests.cs': '(GroupComposite)bot.Root',
}


def replacement(expression: str) -> str:
    receiver = 'Bot' if expression.endswith('Bot.Root') else 'bot'
    return f'LegacyScheduleGateFixture.Create({receiver})'


def isolate(sources: dict) -> tuple[dict, list]:
    result = dict(sources)
    if HELPER not in result:
        return result, []
    records = []
    for name, old in REPLACEMENTS.items():
        if name not in result:
            raise ValueError('Missing explicitly scoped schedule-gate fixture: ' + name)
        raw, before = result[name]
        if before.count(old) != 1:
            raise ValueError('Changed/duplicate schedule-gate fixture contract: ' + name)
        after = before.replace(old, replacement(old), 1)
        result[name] = raw, after
        records.append({
            'file': name, 'changes': 1,
            'before_sha256': hashlib.sha256(before.encode()).hexdigest(),
            'after_sha256': hashlib.sha256(after.encode()).hexdigest(),
            'from': old, 'to': replacement(old),
            'scope': 'isolated production schedule policy and execution gate; not full root',
        })
    return result, records
