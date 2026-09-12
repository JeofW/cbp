# CopilotBuddy audit context

## Purpose

This repository is the working CopilotBuddy fork and includes the source,
regression tests, a runtime-content snapshot, and captured runtime logs. Use
these materials together when reviewing architecture, reliability, failure
recovery, and maintainability.

## Runtime environment

- World of Warcraft 3.3.5a, build 12340
- C# / .NET 10, WPF, x86
- Behavior-tree-driven bot bases and runtime-compiled extensions
- Detour/Recast navigation through `Navigation.dll`

## Evidence map

- Core application source: repository root, `Styx/`, `Bots/`, `TreeSharp/`,
  `Tripper/`, and `UI/`
- Regression harnesses: `Tools/`
- Installed bot bases, including Wholesome Auto Quester:
  `runtime-snapshot/Bots/`
- Installed plugins and routines: `runtime-snapshot/Plugins/` and
  `runtime-snapshot/Routines/`
- Profiles and scripted behaviors: `runtime-snapshot/Default Profiles/`,
  `runtime-snapshot/Quest Behaviors/`, and
  `runtime-snapshot/Dungeon Scripts/`
- Runtime configuration: `runtime-snapshot/Settings/`,
  `runtime-snapshot/Data/`, and the configuration files directly under
  `runtime-snapshot/`
- Captured execution evidence: `runtime-logs/`
- Packaged build: `output.zip`

## Suggested audit focus

1. Identify architectural boundaries, overly coupled components, and unclear
   ownership of state.
2. Trace the main pulse, behavior-tree, quest, combat, navigation, vendor, and
   recovery flows from entry point to side effects.
3. Correlate warnings, exceptions, stalls, and recovery actions in
   `runtime-logs/` with their source paths.
4. Review thread affinity, cancellation, timing, retry limits, and state-reset
   behavior.
5. Assess whether the regression harnesses cover the highest-risk runtime
   paths and recommend specific missing tests.
6. Separate confirmed defects from design risks and rank findings by impact,
   likelihood, and remediation effort.

## Supplying known problems

Put the current known problems and desired audit questions in the ChatGPT
prompt so the reviewer receives the latest priorities. Keep durable facts,
reproduction steps, and accepted findings in this file or a linked issue so
future audits do not depend on one chat history.

## Deliberately excluded

- `mmaps/`: multi-gigabyte generated navigation tiles
- local SDK/tool caches and build intermediates
- duplicate backup trees
