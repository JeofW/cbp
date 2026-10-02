using System.Reflection;
using System.Threading;
using System.Runtime.CompilerServices;
using Bots.Quest;
using Bots.Quest.QuestOrder;
using Styx.Logic.BehaviorTree;
using Styx.Logic.Pathing;
using Styx.Logic.POI;
using Styx.Logic.Profiles.Quest;
using TreeSharp;

internal static class QuestMandatoryLootHandoffRegressionTests
{
    private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
    [ModuleInitializer]
    internal static void Run()
    {
        int count = 0;
        foreach (string nesting in new[] { "direct", "If", "While" })
        foreach (bool pickup in new[] { false, true })
        foreach (PoiType kind in new[] { PoiType.Loot, PoiType.Harvest, PoiType.Skin })
        {
            using var world = new QuestDatasetObservationFixture("Mandatory Handoff Fixture " + Guid.NewGuid().ToString("N"));
            world.LoadProfile("<HBProfile><Name>Mandatory stage fixture</Name><MinLevel>1</MinLevel><MaxLevel>80</MaxLevel><QuestOrder /></HBProfile>");
            world.SetQuest(10161, "Controlled quest", 58, new int[4], new int[4], new int[6], new int[6]);
            world.SetAccepted(!pickup, complete: !pickup);
            world.SetAlive();
            // This case owns a stale corpse with no open loot window. The
            // fixture's read-only self-process is not a WoW UI: leaving the
            // fixed client address unseeded can read unrelated mapped bytes
            // after other aggregate groups load assemblies at that address.
            // Supply the explicit closed-window observation through the real
            // memory reader's fixture cache; production handoff is unchanged.
            var cache = (ThreadLocal<System.Collections.Generic.Dictionary<IntPtr, byte[]>>)
                typeof(QuestDatasetObservationFixture).GetField("cache", Hidden)!.GetValue(world)!;
            cache.Value![new IntPtr(12560600)] = BitConverter.GetBytes(0UL);
            Check(Styx.Logic.Inventory.Frames.LootFrame.LootFrame.Instance.LootingObjectGuid == 0,
                "closed loot-window fixture observation was not current");
            var order = QuestState.Instance.Order;
            var priorNodes = order.Nodes; var priorBehavior = order.CurrentBehavior;
            var priorPoi = BotPoi.Current;
            var thread = typeof(TreeRoot).GetField("_workerThread", Hidden)!;
            var priorThread = thread.GetValue(null); var priorState = TreeRoot.State;
            var destination = new WoWPoint(50, 10, 10);
            ForcedBehavior owner = pickup
                ? new ForcedQuestPickUp(10161, "Controlled quest", 19367, "Controlled giver", destination, QuestObjectType.Npc)
                : new ForcedQuestTurnIn(10161, "Controlled quest", 19367, "Controlled ender", destination, QuestObjectType.Npc);
            var node = pickup
                ? (OrderNode)new PickUpNode(destination, 19367, "Controlled giver", QuestObjectType.Npc, 10161, "Controlled quest")
                : new TurnInNode(destination, 19367, "Controlled ender", QuestObjectType.Npc, 10161, "Controlled quest");
            ForcedBehavior execution = owner;
            try
            {
                thread.SetValue(null, Thread.CurrentThread);
                typeof(TreeRoot).GetProperty("State", Hidden)!.SetValue(null, TreeRootState.Running);
                order.Nodes = new OrderNodeCollection { node }; order.CurrentBehavior = owner;
                if (nesting == "If")
                {
                    var container = new IfNode(() => true, new[] { node }); execution = new ForcedIf(container);
                    order.Nodes = new OrderNodeCollection { container }; order.CurrentBehavior = execution;
                    execution.OnStart();
                }
                else if (nesting == "While")
                {
                    var container = new WhileNode(() => true, new[] { node }); execution = new ForcedWhile(container);
                    order.Nodes = new OrderNodeCollection { container }; order.CurrentBehavior = execution;
                }
                BotPoi.Current = new BotPoi(kind) { Guid = 884422, Entry = 16863, Location = new WoWPoint(10, 10, 10) };
                var branch = execution.Branch;
                branch.Start(null);
                branch.Tick(null); // Actual forced owner must relinquish the stale corpse and publish its own typed POI.
                Check(BotPoi.Current.Type == (pickup ? PoiType.QuestPickUp : PoiType.QuestTurnIn),
                    $"{nesting}/{owner.GetType().Name} remained blocked by stale {kind}; "
                    + $"running={TreeRoot.IsRunning}; alive={world.Player.IsAlive}; combat={world.Player.Combat}; "
                    + $"frame={Styx.Logic.Inventory.Frames.LootFrame.LootFrame.Instance.LootingObjectGuid}; "
                    + $"subject={BotPoi.Current.AsObject?.Guid}; stageMatches={ReferenceEquals(typeof(QuestLootHandoff).GetMethod("Stage", Hidden)!.Invoke(null, null), owner)}; branch={branch.LastStatus}");
                Check(BotPoi.Current.Entry == 19367, "handoff published a different giver");
                Check(world.Player.QuestLog.ContainsQuest(10161) == !pickup,
                    "POI handoff fabricated quest acceptance or completion");
                count++;
                Console.WriteLine($"PASS mandatory actual owner: {nesting}/{(pickup ? "Pickup" : "TurnIn")} after {kind}");
            }
            finally
            {
                execution.ExistingBranch?.Stop(null); execution.Dispose(); owner.Dispose();
                order.Nodes = priorNodes; order.CurrentBehavior = priorBehavior; BotPoi.Current = priorPoi;
                thread.SetValue(null, priorThread);
                typeof(TreeRoot).GetProperty("State", Hidden)!.SetValue(null, priorState);
            }
        }
        Console.WriteLine($"Mandatory actual-owner handoff scenarios: {count}/{count}; actual ForcedPickup/TurnIn, POI and quest-memory owners; no native request or completion invented.");
    }
    private static void Check(bool good, string message) { if (!good) throw new InvalidOperationException(message); }
}
