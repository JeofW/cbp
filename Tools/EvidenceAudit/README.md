# Private evidence audit tools

Read `docs/audit/2026-09-12/AUDIT.md` and `GRAPH.md` first. These tools reproduce the static/log evidence; they do not run the game or certify any quest, class, mesh, or transport route.

## Reproduce the pinned baseline

Use Python 3.11 or later in a virtual environment. Install `requirements.txt`, then run:

```sh
python -m unittest discover -s Tools/EvidenceAudit -p 'test_*.py' -v
mkdir ../cb-baseline
git archive 63112d57a373c1186cee880f98dab65245a214e1 | tar -x -C ../cb-baseline
python Tools/EvidenceAudit/run.py ../cb-baseline ../cb-evidence --source-commit 63112d57a373c1186cee880f98dab65245a214e1
```

Windows checkout may need `git config core.longpaths true`. Keep output outside the input tree. `--source-commit` must identify the actual input snapshot. Source hashes are included; curated source ranges and hypotheses must be reviewed again before analyzing a changed revision. The private `Audit evidence exports` workflow performs the pinned-baseline regeneration and retains its artifact for seven days.

## Outputs

- `runtime.json`: all captures, line counts, per-start lifecycle counts, threshold-censored latency distributions, conservative navigation buckets and exception episodes.
- `structure.json` / `.graphml`: C# syntax, ownership, calls to unresolved references, state writes and ambiguous event-like subscriptions. NOT a type-bound call graph.
- `causal-graph.html`: offline interactive directed overlay with saved queries, node/edge provenance and falsification checks; no external scripts or network calls.
- `causal-graph.json` / `.graphml`, `saved-queries.json`: eight feedback-loop hypotheses and three cross-cutting queries.
- `combined-graph.json` / `.graphml`: both layers, joined to the exact source components without upgrading inferred edges into proven runtime causes.
- `quests.json`: every global quest, spawn, relation and objective checked; all zone/global record differences retain JSON pointers. A missing record is not a blacklist or unreachable verdict.
- `singular-coverage.csv`: behavior-annotated discovery candidates, with runtime execution explicitly NOT_EXECUTED.
- `summary.json`, `SHA256SUMS`: scope/counts/limitations and output integrity hashes.

The initial source baseline has 62 logs, 218,114 physical log lines, 1,541 C# files, 4,335 quests and 106 zone files. Syntax extraction yields 50,245 nodes and 97,717 edges; adding the curated source-linked overlay yields 50,279 nodes and 97,779 edges. These are coverage counts, not unique bug counts.

The first 29 analyzer regressions were developed with failing cases; four additional graph/export integrity regressions bring the suite to 33. C# parser error nodes, historical deployed/source differences, unresolved references and missing live geometry remain visible. The graph does not pretend to cover semantic/dynamic binding, every configuration edge, or live acceptance.
