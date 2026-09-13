using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using Bots.Quest.QuestOrder;
using Styx.Logic.Pathing;
using Styx.Logic.POI;
using Styx.Logic.Questing.Recovery;
using Styx.WoWInternals.WoWObjects;
using WholesomeAQ;

internal static class RestOwnershipRegressionTests
{
    [ModuleInitializer]
    internal static void Run()
    {
        var cases = new List<(string Name, Action Test)>();
        foreach (string factory in new[] { "Pulse", "CreateLiveWorkSample", "TryRecoverFailedPickupTravel", "CapturePreDeathAttribution" })
        {
            string name = factory;
            cases.Add((name + " does not use area-rest status as an activity pause", () =>
            {
                var method = typeof(WholesomeAutoQuest).GetMethod(name,
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                    ?? throw new InvalidOperationException("Missing observed owner: " + name);
                Check(!Calls(method).Any(call => call.Name == "get_IsResting" && call.DeclaringType == typeof(WoWPlayer)),
                    "WoWPlayer.IsResting reads PLAYER_FLAGS bit 0x20 (area/rested-XP status), not Food/Drink or routine rest ownership");
                Check(Calls(method).Any(call => call.Name == "HasAura"), "actual aura observation must not be removed along with the wrong area flag");
            }));
        }
        cases.Add(("active owned pickup remains eligible", () => Check(Active(false, false), "ordinary owned work must be active")));
        cases.Add(("intentional rest still suspends pickup accounting", () => Check(!Active(true, false), "do not charge deliberate rest to the quest attempt")));
        cases.Add(("explicit user pause still suspends accounting", () => Check(!Active(false, true), "manual pause must remain authoritative")));
        cases.Add(("simultaneous rest and pause do not resume accounting", () => Check(!Active(true, true), "both suspension owners must be respected")));
        cases.Add(("stale attempt ownership cannot be made active by rest changes", () =>
            Check(!Active(false, false, owns: false) && !Active(true, false, owns: false), "rest changes must not override exact-generation ownership")));
        var failures = new List<string>();
        foreach (var test in cases)
        {
            try { test.Test(); Console.WriteLine("PASS rest ownership: " + test.Name); }
            catch (Exception error) { failures.Add(test.Name + ": " + error.Message); Console.Error.WriteLine("FAIL rest ownership: " + failures[^1]); }
        }
        Console.WriteLine($"Quest rest ownership: {cases.Count - failures.Count}/{cases.Count}. Four actual compiled call-site guards and five production pickup-policy controls; no game attached.");
        if (failures.Count != 0) throw new InvalidOperationException(string.Join(Environment.NewLine, failures));
    }

    private static bool Active(bool rest, bool pause, bool owns = true)
    {
        var location = new WoWPoint(20, 30, 0);
        var pickup = new ForcedQuestPickUp(867, "Pickup", 1001, "Giver", location, null);
        var key = QuestRecoveryKey.ForNpc(867, QuestRecoveryStage.Pickup, 1001);
        var poi = new BotPoi(location, PoiType.QuestPickUp) { Entry = 1001 };
        return WholesomeAutoQuest.IsPickupRecoveryActive(pickup, key, 7, owns, poi,
            inWorld: true, dead: false, ghost: false, onTaxi: false, onTransport: false,
            resting: rest, userPaused: pause, inCombat: false);
    }
    private static void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private static IEnumerable<MethodBase> Calls(MethodBase method)
    {
        var codes = typeof(OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static)
            .Select(field => (OpCode)field.GetValue(null)!).GroupBy(code => unchecked((ushort)code.Value))
            .ToDictionary(group => group.Key, group => group.First());
        byte[] bytes = method.GetMethodBody()?.GetILAsByteArray() ?? Array.Empty<byte>();
        for (int i = 0; i < bytes.Length;)
        {
            ushort key = bytes[i++]; if (key == 0xfe) key = (ushort)(0xfe00 | bytes[i++]);
            OpCode code = codes[key];
            if (code.OperandType == OperandType.InlineMethod)
            {
                int token = BitConverter.ToInt32(bytes, i); i += 4;
                yield return method.Module.ResolveMethod(token)!; continue;
            }
            i += code.OperandType switch
            {
                OperandType.InlineNone => 0,
                OperandType.ShortInlineBrTarget or OperandType.ShortInlineI or OperandType.ShortInlineVar => 1,
                OperandType.InlineVar => 2,
                OperandType.InlineI8 or OperandType.InlineR => 8,
                OperandType.InlineSwitch => 4 + 4 * BitConverter.ToInt32(bytes, i),
                _ => 4
            };
        }
    }
}
