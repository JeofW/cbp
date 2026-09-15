using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using Styx;
using Styx.Logic.Questing;
using Styx.WoWInternals;
using Styx.WoWInternals.WoWCache;
using Styx.WoWInternals.WoWObjects;
using WholesomeAQ;

// The build extracts the actual SellByQuality body and links the complete real
// QuestLog/PlayerQuest/snapshot owners. Only external bytes, cache observations,
// inventory and merchant dispatch are controlled; no client is attached.
internal static class SaleObservationRegressionTests
{
    private sealed class AssertionFailure(string message) : Exception(message) { }

    [ModuleInitializer]
    internal static void Run()
    {
        var tests = new List<(string Name, Action Test)>
        {
            ("stable hydrated acceptance still protects its required item", () =>
            {
                Accept(); Bot().RunSale();
                Check(MerchantFrame.Instance.Calls == 1 && MerchantFrame.Instance.Ids.Contains(600),
                    "stable accepted quest lost sale permission or required-item protection");
            }),
            ("readable empty log still allows ordinary selling", () =>
            {
                Bot().RunSale(); Check(MerchantFrame.Instance.Calls == 1, "readable empty control blocked");
            }),
            ("occupied unhydrated acceptance blocks selling", () =>
            {
                Accept(hydrate: false); Bot().RunSale(); NoSale();
            }),
            ("failed raw bytes cannot authorize an empty-log sale", () =>
            {
                ObjectManager.Wow!.FailRawLogReads = true; Bot().RunSale(); NoSale();
            }),
            ("short raw byte buffer is unavailable, not an empty log", () =>
            {
                ObjectManager.Wow!.ShortRawLogRead = true; Bot().RunSale(); NoSale();
            }),
            ("duplicate occupied IDs cannot authorize selling", () =>
            {
                Accept(); Accept(slot: 1); Bot().RunSale(); NoSale();
            }),
            ("invalid occupied uint identity cannot authorize selling", () =>
            {
                Accept(uint.MaxValue); Bot().RunSale(); NoSale();
            }),
            ("same-count replacement during hydration blocks selling", () =>
            {
                Accept(); StyxWoW.Cache.AfterLookup = _ => ObjectManager.Wow!.Slots[0].Id = 999;
                Bot().RunSale(); NoSale();
            }),
            ("same-count replacement after inventory observation blocks dispatch", () =>
                DuringInventory(() => ObjectManager.Wow!.Slots[0].Id = 999)),
            ("new acceptance after inventory observation blocks dispatch", () =>
                DuringInventory(() => Accept(999, slot: 1))),
            ("departure after inventory observation blocks dispatch", () =>
                DuringInventory(() => ObjectManager.Wow!.Slots[0].Id = 0)),
            ("changed completion flags after inventory observation block dispatch", () =>
                DuringInventory(() => ObjectManager.Wow!.Slots[0].Flags = WoWDescriptorQuestFlags.Completed)),
            ("changed objective progress after inventory observation blocks dispatch", () =>
                DuringInventory(() => ObjectManager.Wow!.Slots[0].ObjectivesDone[0]++)),
            ("raw read becomes unavailable before merchant dispatch", () =>
                DuringInventory(() => ObjectManager.Wow!.FailRawLogReads = true)),
            ("raw read becomes partial before merchant dispatch", () =>
                DuringInventory(() => ObjectManager.Wow!.ShortRawLogRead = true)),
            ("same wrapper with changed GUID blocks dispatch", () =>
                DuringInventory(() => StyxWoW.Me!.Guid++)),
            ("same wrapper with changed descriptor blocks dispatch", () =>
                DuringInventory(() => StyxWoW.Me!.Descriptor += 4096)),
            ("memory owner replacement blocks dispatch", () =>
                DuringInventory(() => { ObjectManager.Wow = new GreenMagic.Memory(); Accept(); })),
            ("player owner replacement blocks dispatch", () =>
                DuringInventory(() => StyxWoW.Me = new LocalPlayer())),
            ("world becomes unavailable before dispatch", () =>
                DuringInventory(() => ObjectManager.IsInGame = false)),
            ("invalid player before dispatch retains the old veto", () =>
                DuringInventory(() => StyxWoW.Me!.IsValid = false)),
            ("closed merchant before dispatch retains the old veto", () =>
                DuringInventory(() => MerchantFrame.Instance.IsVisible = false)),
            ("capture interruption propagates unchanged without a sale", () =>
            {
                Accept(); var error = new ThreadInterruptedException("controlled cache interruption");
                StyxWoW.Cache.Failure = error; SameException(() => Bot().RunSale(), error); NoSale();
            }),
            ("final raw revalidation cancellation propagates unchanged", () =>
            {
                Accept(); var error = new OperationCanceledException("controlled final raw cancellation");
                Consumable.OnFood = () => ObjectManager.Wow!.Failure = error;
                SameException(() => Bot().RunSale(), error); NoSale();
            }),
            ("ordinary metadata failure postpones selling", () =>
            {
                Accept(); StyxWoW.Cache.Failure = new TimeoutException("controlled cache timeout");
                // The legacy owner may propagate this ordinary cache failure; both
                // propagation and incomplete-observation deferral must prevent selling.
                try { Bot().RunSale(); } catch (TimeoutException) { }
                NoSale();
            }),
            ("metadata eviction alone does not discard already computed exclusions", () =>
            {
                Accept(); Consumable.OnFood = () => StyxWoW.Cache.Entries.Clear();
                Bot().RunSale();
                Check(MerchantFrame.Instance.Calls == 1 && MerchantFrame.Instance.Ids.Contains(600),
                    "unchanged raw log lost safe established exclusions after metadata eviction");
            }),
            ("closed merchant does not request quest observations", () =>
            {
                MerchantFrame.Instance.IsVisible = false; ObjectManager.Wow!.Failure = new Exception("must not read");
                Bot().RunSale(); NoSale(); Check(ObjectManager.Wow.ArrayReads == 0, "closed merchant read quest slots");
            }),
            ("disabled quality mask does not request quest observations", () =>
            {
                var bot = Bot(); bot._settings.SellWhite = false;
                ObjectManager.Wow!.Failure = new Exception("must not read");
                bot.RunSale(); NoSale(); Check(ObjectManager.Wow.ArrayReads == 0, "disabled sale read quest slots");
            })
        };
        int assertions = 0, unexpected = 0;
        foreach (var test in tests)
        {
            Reset();
            try { test.Test(); Console.WriteLine("PASS sale observation: " + test.Name); }
            catch (AssertionFailure error) { assertions++; Console.Error.WriteLine("FAIL sale observation assertion: " + test.Name + ": " + error); }
            catch (Exception error) { unexpected++; Console.Error.WriteLine("ERROR sale observation owner/fixture: " + test.Name + ": " + error); }
        }
        Reset();
        Console.WriteLine($"Sale observation scenarios: {tests.Count - assertions - unexpected}/{tests.Count}; assertions={assertions}; unexpected={unexpected}; actual sale/QuestLog/snapshot bodies; controlled external observations; no game attached.");
        if (assertions != 0 || unexpected != 0)
            throw new InvalidOperationException($"Sale observation regressions: assertions={assertions}; unexpected={unexpected}");
    }

    private static void Reset()
    {
        ObjectManager.Me = new LocalPlayer(); ObjectManager.Wow = new GreenMagic.Memory();
        ObjectManager.IsInGame = true; ObjectManager.Executor = null; StyxWoW.Cache = new WoWCache();
        QuestLog.GetCompletedQuestCacheStatusForIdentity(null!, null!);
        MerchantFrame.Instance = new MerchantFrame(); Consumable.OnFood = null;
    }
    private static void Accept(uint id = 867, bool hydrate = true, int slot = 0)
    {
        ObjectManager.Wow!.Slots[slot].Id = id;
        if (hydrate) StyxWoW.Cache.Entries[id] = new WoWCache.InfoBlock { Quest = new WoWCache.QuestCacheEntry { Id = id } };
    }
    private static WholesomeAutoQuest Bot()
    {
        var bot = new WholesomeAutoQuest();
        bot._dataLoader.Database!.Quests!.Add(new QuestEntry { Id = 867, Objectives = new() { new Objective { ItemId = 600 } } });
        return bot;
    }
    private static void DuringInventory(Action change)
    {
        Accept(); int calls = 0; Consumable.OnFood = () => { calls++; change(); };
        Bot().RunSale(); Check(calls == 1, "actual inventory boundary was not reached"); NoSale();
    }
    private static void NoSale() => Check(MerchantFrame.Instance.Calls == 0, "incomplete or changed observation authorized merchant dispatch");
    private static void Check(bool condition, string message) { if (!condition) throw new AssertionFailure(message); }
    private static void SameException(Action action, Exception expected)
    {
        try { action(); }
        catch (Exception actual) { Check(ReferenceEquals(actual, expected), "cancellation identity changed: " + actual); return; }
        throw new AssertionFailure("expected cancellation was not propagated");
    }
}
