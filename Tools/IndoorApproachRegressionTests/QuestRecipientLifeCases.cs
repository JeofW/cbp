using CommonBehaviors.Actions;
using Styx.Logic.Pathing;
using Styx.Logic.POI;
using TreeSharp;

// The pinned 3.3.5 source gives Scout Jyoba a questgiver flag and permanent
// feign-death (29266), whose script sets UNIT_DYNFLAG_DEAD. The host therefore
// observes IsAlive=false without making the quest interaction invalid.
internal static class QuestRecipientLifeCases
{
    internal static void Run(Action<string, Action<ActionMoveToPoi>> test)
    {
        static void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
        static void Ground()
        {
            World.Actor.Position = new(100, 10, 0); World.Actor.Flags = 0; World.Actor.MountedValue = false;
            World.Target.Position = new(130, 10, 0); World.Target.Outdoors = true;
            World.Target.IsAlive = false; World.Target.IsQuestGiver = true; BotPoi.Current.Position = World.Target.Position;
        }
        foreach (PoiType type in new[] { PoiType.QuestPickUp, PoiType.QuestTurnIn })
            test("source-selected dead questgiver can complete its approach/" + type, action =>
            {
                Ground(); BotPoi.Current.Type = type;
                Check(action.Tick(null!) == RunStatus.Running && World.Walks.Count == 1,
                    "questgiver's dead presentation prevented its owned approach");
                World.Actor.Position = World.Target.Position.Add(-1, 0, 0);
                Check(action.Tick(null!) == RunStatus.Success && GroundTransition.TryInteractWith(World.Target)
                    && World.Interactions.SequenceEqual(new[] { World.Target.Guid }),
                    "observed dead questgiver did not reach exact-recipient interaction");
            });
        foreach (string change in new[] { "quest-flag", "alive", "guid", "base", "subject", "poi-type" })
            test("dead questgiver's final admission revokes changed identity/" + change, action =>
            {
                Ground(); World.Actor.Position = World.Target.Position.Add(-1, 0, 0); bool reached = false;
                World.Callback = stage =>
                {
                    if (stage != "interaction-prepare") return;
                    reached = true; World.Callback = null;
                    switch (change)
                    {
                        case "quest-flag": World.Target.IsQuestGiver = false; break;
                        case "alive": World.Target.IsAlive = true; break;
                        case "guid": World.Target.Guid++; break;
                        case "base": World.Target.BaseAddress++; break;
                        case "subject": BotPoi.Current.Object = new Styx.WoWInternals.WoWObjects.WoWUnit
                            { Guid = World.Target.Guid, BaseAddress = World.Target.BaseAddress, IsQuestGiver = true, IsAlive = false }; break;
                        case "poi-type": BotPoi.Current.Type = PoiType.Kill; break;
                    }
                };
                Check(!GroundTransition.TryInteractWith(World.Target) && reached && World.Interactions.Count == 0,
                    "changed dead-recipient admission was submitted or never exercised final entry");
            });
        foreach (PoiType type in new[] { PoiType.QuestPickUp, PoiType.QuestTurnIn, PoiType.Sell, PoiType.Repair, PoiType.Kill })
            test("ordinary corpse cannot borrow dead questgiver permission/" + type, action =>
            {
                Ground(); BotPoi.Current.Type = type; World.Target.IsQuestGiver = false;
                World.Actor.Position = World.Target.Position.Add(-1, 0, 0);
                Check(!GroundTransition.TryInteractWith(World.Target) && World.Interactions.Count == 0,
                    "ordinary corpse was admitted as an observed questgiver");
            });
        test("living questgiver death revokes the retained journey", action =>
        {
            Ground(); World.Target.IsAlive = true;
            Check(action.Tick(null!) == RunStatus.Running && World.Walks.Count == 1, "living control did not start");
            World.Target.IsAlive = false;
            Check(action.Tick(null!) == RunStatus.Failure && World.Interactions.Count == 0,
                "a dead successor inherited the living recipient's route");
        });
    }
}
