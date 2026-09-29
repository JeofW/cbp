using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Styx.Logic.Profiles.Quest;

internal static class QuestCollectedItemLifecycleRegressionTests
{
    private sealed class Failure(string message) : Exception(message) { }
    [ModuleInitializer]
    internal static void Run()
    {
        var cases = new (string Name, bool Accepted, bool Failed, int Count, bool Expected)[]
        {
            ("empty inventory stays incomplete", true, false, 0, false),
            ("partial stack stays incomplete", true, false, 2, false),
            ("full stack acknowledges an accepted quest", true, false, 3, true),
            ("failed quest cannot acknowledge retained items", true, true, 3, false),
            ("abandoned quest cannot acknowledge retained items", false, false, 3, false),
            ("fresh nonfailed state can acknowledge items", true, false, 4, true)
        };
        int passed = 0, assertions = 0, unexpected = 0;
        using var fixture = new QuestDatasetObservationFixture();
        foreach (var item in cases)
        {
            try
            {
                fixture.SetQuest(867, "Controlled carried-item lifecycle", 60, new int[4], new int[4], new[] { 960010, 0, 0, 0, 0, 0 }, new[] { 3, 0, 0, 0, 0, 0 });
                var owner = fixture.CreateObjective(new ObjectiveNode(867, ObjectiveType.CollectItem, 960010, "", 3, 0));
                fixture.SetInventory(new Dictionary<int, long> { [960010] = item.Count });
                fixture.SetAccepted(item.Accepted, failed: item.Failed);
                if (owner.IsDone != item.Expected) throw new Failure("incorrect collection completion for accepted=" + item.Accepted + "; failed=" + item.Failed);
                passed++; Console.WriteLine("PASS collection lifecycle: " + item.Name);
            }
            catch (Failure error) { assertions++; Console.Error.WriteLine("FAIL collection lifecycle: " + item.Name + ": " + error.Message); }
            catch (Exception error) { unexpected++; Console.Error.WriteLine("ERROR collection lifecycle: " + item.Name + ": " + error); }
            finally { fixture.ReleaseOwners(); }
        }
        Console.WriteLine($"Collection lifecycle scenarios: {passed}/{cases.Length}; assertions={assertions}; unexpected={unexpected}; actual inventory and behavior owner.");
        if (assertions + unexpected != 0) throw new InvalidOperationException("Collected item lifecycle regression");
    }
}
