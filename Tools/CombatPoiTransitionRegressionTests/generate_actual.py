from __future__ import annotations

from hashlib import sha256
from pathlib import Path
import json
import sys

HERE = Path(__file__).resolve().parent
SOURCE = HERE.parents[1]


def text(rel: str) -> str:
    return (SOURCE / rel).read_text(encoding="utf-8-sig")


def member(rel: str, marker: str) -> str:
    src = text(rel)
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
            if ch == '"': state = "string"
            elif ch == "'": state = "char"
            elif ch == "/" and nxt == "/": state = "line"; i += 1
            elif ch == "/" and nxt == "*": state = "block"; i += 1
            elif ch == "{": depth += 1
            elif ch == "}":
                depth -= 1
                if depth == 0: return src[start:i + 1]
        elif state == "string":
            if ch == "\\": i += 1
            elif ch == '"': state = "code"
        elif state == "char":
            if ch == "\\": i += 1
            elif ch == "'": state = "code"
        elif state == "line" and ch == "\n": state = "code"
        elif state == "block" and ch == "*" and nxt == "/": state = "code"; i += 1
        i += 1
    raise RuntimeError(f"unclosed member {rel}: {marker}")


if len(sys.argv) != 3:
    raise SystemExit("usage: generate_actual.py <generated-csharp> <manifest-json>")

specs = [
    ("Styx/Logic/Pathing/Navigator.cs", "internal static void InvalidatePoiRoute()", "Navigator"),
    ("Styx/Logic/Pathing/Flightor.cs", "internal static void InvalidateRouteContext()", "Flightor"),
    ("Styx/Logic/Pathing/MeshNavigator.cs", "internal void InvalidateRouteContext()", "MeshNavigator"),
]
parts = []
manifest = {"extracts": [], "move_to_core_lease_fences": [], "linked_sources": {}}
for rel, marker, owner in specs:
    body = member(rel, marker)
    parts.append((owner, body))
    manifest["extracts"].append({"path": rel, "marker": marker, "sha256": sha256(body.encode()).hexdigest()})

flight = text("Styx/Logic/Pathing/Flightor.cs")
for required in (
    "routeLease != null ? routeLease() : BotPoi.CurrentGeneration == poiGeneration",
    "_pathRouteLease != null || _pathPoiGeneration != poiGeneration",
    "ContextChanged(_pathPoi, _pathPoiGeneration, _pathRouteLease, _pathProfile, _pathProvider)",
):
    if required not in flight:
        raise RuntimeError("missing expected Flightor route-lease fence: " + required)
    manifest["move_to_core_lease_fences"].append({"text": required, "sha256": sha256(required.encode()).hexdigest()})

for rel in (
    "Styx/Logic/POI/BotPoi.cs",
    "Styx/Logic/POI/PoiType.cs",
    "Styx/Logic/Combat/MountedCombatTransition.cs",
    "Styx/Logic/MountedTravelProgress.cs",
    "Styx/Logic/Pathing/GroundTransition.cs",
    "Styx/Logic/Pathing/GroundTransitionContext.cs",
    "Styx/Logic/Pathing/GroundTransitionMachine.cs",
    "Styx/Logic/Pathing/GroundTransitionRuntime.cs",
    "Styx/Logic/Pathing/GroundApproachGeometry.cs",
    "Styx/Logic/Pathing/GroundApproachQueries.cs",
    "Styx/Logic/Pathing/MeshNavigator.RequestOwnership.cs",
    "Styx/Logic/Pathing/Navigator.cs",
    "Styx/Logic/Pathing/Flightor.cs",
    "Styx/Logic/Pathing/MeshNavigator.cs",
):
    manifest["linked_sources"][rel] = sha256(text(rel).encode()).hexdigest()

generated = "#nullable enable\nusing System;\nusing Styx;\nusing Styx.Logic.POI;\nusing Styx.Logic.Profiles;\nusing Styx.WoWInternals;\nusing Styx.WoWInternals.WoWObjects;\nnamespace Styx.Logic.Pathing\n{\n"
for owner, body in parts:
    prefix = "public static partial class" if owner in ("Navigator", "Flightor") else "public partial class"
    generated += f"{prefix} {owner}\n{{\n{body}\n}}\n"
generated += "}\n"

out = Path(sys.argv[1]).resolve()
receipt = Path(sys.argv[2]).resolve()
out.parent.mkdir(parents=True, exist_ok=True)
receipt.parent.mkdir(parents=True, exist_ok=True)
out.write_text(generated, encoding="utf-8")
receipt.write_text(json.dumps(manifest, indent=2) + "\n", encoding="utf-8")
print("generated exact Navigator/Flightor/Mesh invalidation members; verified Flightor lease-or-generation fences")
