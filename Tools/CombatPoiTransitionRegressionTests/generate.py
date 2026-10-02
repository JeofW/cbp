from __future__ import annotations

from hashlib import sha256
from pathlib import Path
import json
import sys

HERE = Path(__file__).resolve().parent
SOURCE = HERE.parents[1]


def source_text(rel: str) -> str:
    return (SOURCE / rel).read_text(encoding="utf-8-sig")


def member(rel: str, marker: str) -> str:
    src = source_text(rel)
    if src.count(marker) != 1:
        raise RuntimeError(f"ambiguous member marker {rel}: {marker!r}")
    start = src.index(marker)
    brace = src.index("{", start)
    depth = 0
    state = "code"
    i = brace
    while i < len(src):
        ch = src[i]
        nxt = src[i + 1] if i + 1 < len(src) else ""
        if state == "code":
            if ch == '"':
                state = "string"
            elif ch == "'":
                state = "char"
            elif ch == "/" and nxt == "/":
                state = "line"
                i += 1
            elif ch == "/" and nxt == "*":
                state = "block"
                i += 1
            elif ch == "{":
                depth += 1
            elif ch == "}":
                depth -= 1
                if depth == 0:
                    return src[start:i + 1]
        elif state == "string":
            if ch == "\\":
                i += 1
            elif ch == '"':
                state = "code"
        elif state == "char":
            if ch == "\\":
                i += 1
            elif ch == "'":
                state = "code"
        elif state == "line" and ch == "\n":
            state = "code"
        elif state == "block" and ch == "*" and nxt == "/":
            state = "code"
            i += 1
        i += 1
    raise RuntimeError(f"unclosed member {rel}: {marker}")


if len(sys.argv) != 3:
    raise SystemExit("usage: generate.py <generated-csharp> <manifest-json>")

rel = "Styx/Logic/Pathing/Navigator.cs"
marker = "internal static void InvalidatePoiRoute()"
body = member(rel, marker)
generated = f"""#nullable enable
namespace Styx.Logic.Pathing
{{
    public static partial class Navigator
    {{
{body}
    }}
}}
"""

generated_path = Path(sys.argv[1]).resolve()
manifest_path = Path(sys.argv[2]).resolve()
generated_path.parent.mkdir(parents=True, exist_ok=True)
manifest_path.parent.mkdir(parents=True, exist_ok=True)
generated_path.write_text(generated, encoding="utf-8")
manifest = {
    "extracts": [
        {
            "path": rel,
            "marker": marker,
            "sha256": sha256(body.encode()).hexdigest(),
        }
    ],
    "linked_sources": {
        rel: sha256(source_text(rel).encode()).hexdigest(),
        "Styx/Logic/POI/BotPoi.cs": sha256(source_text("Styx/Logic/POI/BotPoi.cs").encode()).hexdigest(),
        "Styx/Logic/Combat/MountedCombatTransition.cs": sha256(source_text("Styx/Logic/Combat/MountedCombatTransition.cs").encode()).hexdigest(),
        "Bots/Grind/Levelbot/Actions/Combat/ActionPull.cs": sha256(source_text("Bots/Grind/Levelbot/Actions/Combat/ActionPull.cs").encode()).hexdigest(),
        "Bots/Grind/Levelbot/Actions/Combat/ActionSetTarget.cs": sha256(source_text("Bots/Grind/Levelbot/Actions/Combat/ActionSetTarget.cs").encode()).hexdigest(),
    },
}
manifest_path.write_text(json.dumps(manifest, indent=2) + "\n", encoding="utf-8")
print("generated exact Navigator.InvalidatePoiRoute production member")
