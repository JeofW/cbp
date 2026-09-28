using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using System.Xml.Linq;
using Bots.Quest.Actions;
using Bots.Quest.QuestOrder;
using Styx;
using Styx.Logic.Pathing;
using Styx.Logic.Profiles.Quest;

// Actual parsed movement node -> actual executor factory -> actual forced owner.
// No routine, world, navigation, process, or native entry point is executed.
internal static class ForcedMoveMaterializationRegressionTests
{
    private sealed class Failure(string why) : Exception(why) { }
    [ModuleInitializer]
    internal static void Run()
    {
        var cases = new List<(string Name, Action Run)>();
        foreach (NavType? mode in new NavType?[] { null, NavType.Run, NavType.Fly })
        foreach (uint quest in new uint[] { 0, 123 })
        foreach (float precision in new[] { 1.5f, 4f })
        {
            NavType? nav = mode; uint questId = quest; float radius = precision;
            cases.Add(($"{nav?.ToString() ?? "Auto"}/{questId}/{radius}", () =>
            {
                var xml = new XElement("MoveTo", new XAttribute("X", -20), new XAttribute("Y", 30),
                    new XAttribute("Z", 40), new XAttribute("Name", "controlled movement"),
                    new XAttribute("Precision", radius), new XAttribute("QuestId", questId));
                if (nav.HasValue) xml.Add(new XAttribute("Nav", nav.Value));
                var node = MoveToNode.FromXml(xml);
                if (node.NavType != nav) throw new Failure("Actual parser changed the requested navigation mode");
                var previousOrder = QuestOrder.Instance;
                try
                {
                    var executor = new ForcedBehaviorExecutor(new QuestOrder());
                    var factory = typeof(ForcedBehaviorExecutor).GetMethod("CreateForcedBehavior", BindingFlags.Instance | BindingFlags.NonPublic)!;
                    ForcedMoveTo behavior;
                    try { behavior = (ForcedMoveTo)factory.Invoke(executor, new object[] { node })!; }
                    catch (TargetInvocationException error) when (error.InnerException != null)
                    { ExceptionDispatchInfo.Capture(error.InnerException).Throw(); throw; }
                    if (behavior.NavType != nav || behavior.Location != node.Location || behavior.LocationName != node.LocationName
                        || behavior.Precision != radius || behavior.QuestId != questId)
                        throw new Failure("Executor discarded declared movement mode or changed waypoint/precision/quest identity");
                }
                finally
                {
                    // The real order constructor publishes its singleton. A test
                    // must not leave that publication for surrounding suites.
                    typeof(QuestOrder).GetProperty("Instance")!.SetValue(null, previousOrder);
                }
            }));
        }
        int passed = 0, assertions = 0, unexpected = 0;
        foreach (var test in cases)
        {
            try { test.Run(); passed++; Console.WriteLine("PASS forced materialization: " + test.Name); }
            catch (Failure error) { assertions++; Console.Error.WriteLine("FAIL forced materialization: " + test.Name + ": " + error.Message); }
            catch (Exception error) { unexpected++; Console.Error.WriteLine("ERROR forced materialization: " + test.Name + ": " + error); }
        }
        Console.WriteLine($"Forced materialization scenarios: {passed}/{cases.Count}; assertions={assertions}; unexpected={unexpected}; actual parser/executor/forced owner; no game or navigation execution.");
        if (assertions + unexpected != 0) throw new InvalidOperationException("Forced materialization regression");
    }
}
