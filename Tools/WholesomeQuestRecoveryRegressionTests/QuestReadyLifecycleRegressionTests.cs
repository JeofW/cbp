using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using WholesomeAQ;

// Executes the full production lifecycle reset and stopped Pulse, not an extracted
// helper. Only the previously recorded ready set is seeded. No client is attached.
internal static class QuestReadyLifecycleRegressionTests
{
    private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
    private sealed class AssertionFailure(string message) : Exception(message) { }
    private static readonly FieldInfo Ready = typeof(WholesomeAutoQuest).GetField("_lastReadyQuestIds", Hidden)!;
    private static readonly FieldInfo Stopped = typeof(WholesomeAutoQuest).GetField("_stopped", Hidden)!;
    private static void Check(bool ok, string reason) { if (!ok) throw new AssertionFailure(reason); }
    [ModuleInitializer]
    internal static void Run()
    {
        var cases = new List<(string Name, System.Action Run)>
        {
            ("lifecycle reset forgets the previous ready owner", () =>
            { var bot = new WholesomeAutoQuest(); Ready.SetValue(bot, new HashSet<int> { 867 }); bot.ResetRecoveryLifecycleState(); Check(Ready.GetValue(bot) == null, "Stop/Start reset retained a previous ready quest"); }),
            ("stopped Pulse cannot retain readiness history", () =>
            { var bot = new WholesomeAutoQuest(); Ready.SetValue(bot, new HashSet<int> { 867 }); Stopped.SetValue(bot, true); bot.Pulse(); Check(Ready.GetValue(bot) == null, "stopped Pulse retained previous readiness authority"); }),
            ("ordinary stopped Pulse remains harmless without a ready history", () =>
            { var bot = new WholesomeAutoQuest(); Stopped.SetValue(bot, true); bot.Pulse(); Check(Ready.GetValue(bot) == null, "stopped Pulse fabricated readiness"); })
        };
        int assertions = 0, unexpected = 0;
        foreach (var item in cases)
        {
            try { item.Run(); Console.WriteLine("PASS ready-lifecycle: " + item.Name); }
            catch (AssertionFailure error) { assertions++; Console.Error.WriteLine("FAIL ready-lifecycle assertion: " + item.Name + ": " + error.Message); }
            catch (Exception error) { unexpected++; Console.Error.WriteLine("ERROR ready-lifecycle fixture/owner: " + item.Name + ": " + error); }
        }
        Console.WriteLine($"Ready lifecycle scenarios: {cases.Count - assertions - unexpected}/{cases.Count}; assertions={assertions}; unexpected={unexpected}; full production reset and stopped Pulse; no game attached.");
        if (assertions + unexpected != 0) throw new InvalidOperationException("Ready lifecycle regressions remain.");
    }
}
