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
    start = src.index(marker); brace = src.index("{", start)
    depth = 0; i = brace; state = "code"
    while i < len(src):
        ch = src[i]; nxt = src[i+1] if i+1 < len(src) else ""
        if state == "code":
            if ch == '"': state = "string"
            elif ch == "'": state = "char"
            elif ch == "/" and nxt == "/": state = "line"; i += 1
            elif ch == "/" and nxt == "*": state = "block"; i += 1
            elif ch == "{": depth += 1
            elif ch == "}":
                depth -= 1
                if depth == 0: return src[start:i+1]
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

def line(rel: str, marker: str) -> str:
    rows = [row.strip() for row in text(rel).splitlines() if marker in row]
    if len(rows) != 1:
        raise RuntimeError(f"ambiguous line marker {rel}: {marker!r}")
    return rows[0]

extracts=[]
def take(rel, marker):
    value=member(rel, marker); extracts.append({"path":rel,"marker":marker,"sha256":sha256(value.encode()).hexdigest()}); return value

# The patrol admission member is expression-bodied. Copy it exactly up to its
# terminator, independently of the controlled actor/quest/cache test boundary.
patrol_source=text("Bots/Quest/QuestOrder/ForcedQuestTurnIn.cs")
patrol_marker="private bool SearchCurrent() =>"
if patrol_source.count(patrol_marker)!=1:
    raise RuntimeError("ambiguous actual patrol admission")
patrol_start=patrol_source.index(patrol_marker)
patrol_member=patrol_source[patrol_start:patrol_source.index(";",patrol_start)+1]
extracts.append({"path":"Bots/Quest/QuestOrder/ForcedQuestTurnIn.cs","marker":patrol_marker,"sha256":sha256(patrol_member.encode()).hexdigest()})

published_source=text("Bots/Quest/PublishedQuestRoot.cs")
extracts.append({"path":"Bots/Quest/PublishedQuestRoot.cs","marker":"<full-linked-source>","sha256":sha256(published_source.encode()).hexdigest()})
level_parts=[
    line("Bots/Grind/LevelBot.cs","private static CombatRoutine Routine =>"),
    take("Bots/Grind/LevelBot.cs","public static Composite CreateCombatBehavior()"),
    take("Bots/Grind/LevelBot.cs","private sealed class RoutineAdmissionGuard"),
    take("Bots/Grind/LevelBot.cs","private static Composite CreateOwnedPrePullBehavior()"),
    take("Bots/Grind/LevelBot.cs","private static Composite CreateOwnedGroundCombatBehavior()"),
    take("Bots/Grind/LevelBot.cs","private static bool IsPlayerOrPetInCombat()"),
    take("Bots/Grind/LevelBot.cs","private static bool CanPull()"),
]
quest_parts=[
    take("Bots/Quest/QuestBot.cs","private static Composite CreateTargetingBehavior()"),
    take("Bots/Quest/QuestBot.cs","internal static bool ShouldSuppressOpportunisticTargeting(PoiType poiType)"),
    take("Bots/Quest/QuestBot.cs","internal static bool ShouldSuppressOpportunisticTargeting(PoiType poiType, bool mounted)"),
]
for marker in ["internal static bool HasRequiredCombatTarget()", "private static Composite CreateRequiredTargetingBehavior()"]:
    if marker in text("Bots/Quest/QuestBot.cs"):
        quest_parts.append(take("Bots/Quest/QuestBot.cs", marker))
required_objective = "public override bool IsRequiredCombatTarget(WoWUnit unit)"
grind_required = take("Bots/Quest/Objectives/GrindObjective.cs", required_objective) if required_objective in text("Bots/Quest/Objectives/GrindObjective.cs") else "public override bool IsRequiredCombatTarget(WoWUnit unit)=>DonePrerequisites&&!IsCompleted&&IsMobObjective(unit);"
singular_parts=[
    take("runtime-snapshot/Routines/Singular wotlk/SingularRoutine.cs","private static bool IsMounted"),
    take("runtime-snapshot/Routines/Singular wotlk/SingularRoutine.cs","public bool CreateBehaviors()"),
    take("runtime-snapshot/Routines/Singular wotlk/SingularRoutine.cs","private bool EnsureComposite(bool error, BehaviorType type, out Composite composite)"),
    take("runtime-snapshot/Routines/Singular wotlk/SingularRoutine.cs","private static void StopBot(string reason)"),
    take("runtime-snapshot/Routines/Singular wotlk/SingularRoutine.cs","private class LockSelector : PrioritySelector"),
]
header="""#nullable disable
using System;using System.Collections.Generic;using System.Linq;
using CommonBehaviors.Actions;using CommonBehaviors.Decorators;using Levelbot.Actions.Combat;using Levelbot.Decorators.Combat;
using Singular.Dynamics;using Singular.Helpers;using Singular.Managers;using Singular.Settings;
using Styx;using Styx.Combat.CombatRoutine;using Styx.Helpers;using Styx.Logic;using Styx.Logic.BehaviorTree;using Styx.Logic.Combat;using Styx.Logic.Pathing;using Styx.Logic.POI;using Styx.WoWInternals;using Styx.WoWInternals.WoWObjects;using TreeSharp;
"""
generated=header+f"""
namespace Bots.Quest.Objectives{{public sealed partial class GrindObjective{{{grind_required}}}}}
namespace Bots.Quest{{public partial class QuestBot{{public static Func<PrioritySelector> RootFactory;public static PrioritySelector CreateRoot()=>RootFactory?.Invoke()??new PrioritySelector();{chr(10).join(quest_parts)}}}}}
namespace Bots.Grind{{public partial class LevelBot{{{chr(10).join(level_parts)}}}}}
namespace Bots.Quest.QuestOrder{{public partial class ForcedQuestTurnIn{{{patrol_member}}}}}
namespace Singular{{public partial class SingularRoutine:CombatRoutine{{
private Composite _combatBehavior,_combatBuffsBehavior,_healBehavior,_preCombatBuffsBehavior,_pullBehavior,_pullBuffsBehavior,_restBehavior;private WoWClass _myClass=WoWClass.Paladin;
private static LocalPlayer Me=>StyxWoW.Me;internal static event EventHandler<WoWContextEventArg> OnWoWContextChanged;internal static WoWContext LastWoWContext{{get;set;}}internal static WoWContext CurrentWoWContext=>WoWContext.Normal;
public override Composite CombatBehavior=>_combatBehavior;public override Composite CombatBuffBehavior=>_combatBuffsBehavior;public override Composite HealBehavior=>_healBehavior;public override Composite PreCombatBuffBehavior=>_preCombatBuffsBehavior;public override Composite PullBehavior=>_pullBehavior;public override Composite PullBuffBehavior=>_pullBuffsBehavior;public override Composite RestBehavior=>_restBehavior;
{chr(10).join(singular_parts)}
public sealed class WoWContextEventArg:EventArgs{{public WoWContextEventArg(WoWContext c,WoWContext p){{CurrentContext=c;PreviousContext=p;}}public readonly WoWContext CurrentContext,PreviousContext;}}
}}}}
"""
if len(sys.argv) != 3:
    raise SystemExit("usage: generate.py <generated-csharp> <extract-manifest>")
generated_path = Path(sys.argv[1]).resolve()
manifest_path = Path(sys.argv[2]).resolve()
generated_path.parent.mkdir(parents=True, exist_ok=True)
manifest_path.parent.mkdir(parents=True, exist_ok=True)
generated_path.write_text(generated,encoding="utf-8")
manifest_path.write_text(json.dumps(extracts,indent=2)+"\n",encoding="utf-8")
print(f"generated {len(extracts)} exact members")
