# Dungeon Scripts — WotLK 3.3.5a

Dungeon scripts for CopilotBuddy's DungeonBuddy bot. They tell the bot how to handle a
specific dungeon: where the entrance is, which targets to ignore or prioritise, and what to
do during each boss fight.

Scripts are **compiled at runtime** by the bot (Roslyn). They are not part of the main
assembly — drop a `.cs` file in the right folder and it is picked up on the next start.

## Anatomy of a script

```csharp
public class UtgardeKeep : Dungeon
{
    public override uint DungeonId { get { return 202; } }

    public override WoWPoint Entrance     { get { return new WoWPoint(1235.02, -4860.00, 41.24); } }
    public override WoWPoint ExitLocation { get { return new WoWPoint(144.45, -88.97, 12.55); } }

    public override void RemoveTargetsFilter(List<WoWObject> units) { }
    public override void IncludeTargetsFilter(List<WoWObject> incoming, HashSet<WoWObject> outgoing) { }
    public override void WeighTargetsFilter(List<Targeting.TargetPriority> units) { }

    [ObjectHandler(186611, "Glowing Anvil", ObjectRange = 120)]
    public Composite GlowingAnvilHandler() { ... }

    [EncounterHandler(23954, "Ingvar the Plunderer")]
    public Composite IngvarThePlundererEncounter() { ... }
}
```

| Member | Meaning |
|---|---|
| `DungeonId` | Row ID in **`LFGDungeons.dbc`** of the 3.3.5a client. This is how the bot matches a script to the dungeon you queued for. |
| `Entrance` | Where the bot walks to enter. Needed for LFG and solo farm. |
| `ExitLocation` | Where the bot walks after the run. |
| `RemoveTargetsFilter` | Drop units the bot must not pull. |
| `IncludeTargetsFilter` | Force units into the target list. |
| `WeighTargetsFilter` | Adjust target priority scores. |
| `[EncounterHandler(entry, name)]` | Behaviour tree that runs while that boss is engaged. `entry` is the **creature_template entry**. |
| `[ObjectHandler(entry, name)]` | Behaviour tree for a game object. `entry` is the **gameobject_template entry**. |

`Composite` factories follow the TreeSharp lifecycle: `Start()` once, `Tick()` per pulse,
`Stop()` once. Never call `Start()` every tick.

## Hard rules

**WotLK 3.3.5a only.** Several of these scripts came from a bot generation that targeted
Cataclysm and later. Dungeons revamped in Cataclysm — Deadmines, Shadowfang Keep, Stormwind
Stockade, Razorfen — have completely different bosses there. A Cataclysm creature entry will
simply never match anything, and the handler is dead code.

**`DungeonId` must be unique.** `DungeonManager.RegisterInstance` keeps the first script
registered for an ID and silently discards the rest — no warning, no log. Two scripts sharing
an ID means one of them never runs.

**Verify every ID against real data**, not from memory:

```sql
-- creature entry -> name
SELECT entry, name FROM creature_template WHERE entry = 23954;

-- gameobject entry -> name
SELECT entry, name FROM gameobject_template WHERE entry = 186611;

-- the real bosses of a map (Deadmines = 36, Shadowfang Keep = 33, Stockade = 34)
SELECT ct.entry, ct.name, ct.rank
FROM creature_template ct JOIN creature c ON c.id = ct.entry
WHERE c.map = 36 AND ct.rank >= 1
GROUP BY ct.entry, ct.name, ct.rank ORDER BY ct.rank DESC;
```

`DungeonId` values come from `LFGDungeons.dbc` in the 3.3.5a client, not from a wiki — later
expansions renumbered that file.

## Contributing

Pick something from the list below, write the script, test it in-game, open a pull request.
One dungeon per pull request. State which client build and which server core you tested on.

71 of the 93 scripts handle at least one boss. What is left, easiest first:

### Straightforward — bosses have no mechanic the routine cannot handle

| Script | State |
|---|---|
| Lower Blackrock Spire | entrance and exit set, no boss handler |
| Scholomance | entrance and exit set, no boss handler |
| Magisters' Terrace, normal and heroic | empty |

### Needs one real behaviour

| Script | Why |
|---|---|
| The Forge of Souls, normal and heroic | Bronjahm pulls to the middle of the room, Devourer of Souls mirrors damage |
| The Arcatraz, normal and heroic | Warden Mellichar summons waves that must be picked up in order |
| Opening of the Dark Portal, normal and heroic | eighteen portal waves to clear while Medivh is kept alive |
| Pit of Saron, normal and heroic | timed escape from Scourgelord Tyrannus at the end |

### Hard — the bot has to drive a vehicle or follow a scripted sequence

| Script | Why |
|---|---|
| Trial of the Champion, normal and heroic | opens on a mounted joust, fought from a vehicle |
| Halls of Reflection, normal and heroic | ten defence waves, then a timed escape from the Lich King |
| The Oculus, normal and heroic | the whole dungeon is fought from drakes |

### Must be rewritten for WotLK

These dungeons were revamped in Cataclysm. The scripts here handle the *revamped* bosses,
which do not exist on a 3.3.5a server, so nothing in them ever fires. They have to be
written from scratch against the WotLK bosses.

| Script | Real 3.3.5a bosses |
|---|---|
| Deadmines | Rhahk'Zor 644, Sneed's Shredder 642, Gilnid 1763, Mr. Smite 646, Captain Greenskin 647, Edwin VanCleef 639 |
| Shadowfang Keep | Rethilgore 3914, Razorclaw the Butcher 3886, Baron Silverlaine 3887, Commander Springvale 4278, Odo the Blindwatcher 4279, Fenrus the Devourer 4274, Wolf Master Nandos 3927, Archmage Arugal 4275 |

Shadowfang Keep already handles Baron Silverlaine and Commander Springvale correctly; the
other three handlers are Cataclysm bosses and the six listed above are missing.

Stormwind Stockade had the same problem and has been rewritten — use it as the model.

### How much a script actually needs

A boss handler does not have to be clever. The combat routine already fights the boss — the
handler only exists to add what the routine cannot know: move out of an effect, face away
from the group, ignore a summon. When a boss needs nothing special, this is enough:

```csharp
[EncounterHandler(23953, "Prince Keleseth")]
public Composite PrinceKelesethEncounter()
{
    WoWUnit boss = null;
    return new PrioritySelector(ctx => boss = ctx as WoWUnit);
}
```

So for most dungeons a working script is `DungeonId` + `Entrance` + `ExitLocation` + one
minimal handler per boss. Only fights with a hard requirement — vehicles, escort waves, a
timed escape — need real behaviour trees.
