using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Styx;
using Styx.Logic.Questing;
using Styx.WoWInternals;
using Styx.WoWInternals.WoWCache;
using Styx.WoWInternals.WoWObjects;
using WholesomeAQ;

// Executes the extracted actual readiness owner and full QuestLog/PlayerQuest.
// Memory/cache/world inputs are controlled; no fake snapshot or readiness policy.
internal static class RawReadyRegressionTests
{
    private sealed class AssertionFailure(string message) : Exception(message) { }
    private static void Check(bool condition, string reason)
    { if (!condition) throw new AssertionFailure(reason); }
    private static WholesomeAutoQuest Ready()
    {
        ObjectManager.Me = new(); ObjectManager.Wow = new(); ObjectManager.IsInGame = true;
        ObjectManager.Executor = null; StyxWoW.Cache = new();
        Put(0, 867); var bot = new WholesomeAutoQuest(); bot.RunReady();
        Check(bot.Refreshes == 0, "fixture's first observation fabricated a departure");
        return bot;
    }
    private static void Put(int slot, uint id)
    {
        ObjectManager.Wow!.Slots[slot].Id = id;
        ObjectManager.Wow.Slots[slot].Flags = WoWDescriptorQuestFlags.Completed;
        if (id != 0) StyxWoW.Cache.Entries[id] = new WoWCache.InfoBlock { Quest = new() { Id = id } };
    }
    [ModuleInitializer]
    internal static void Run()
    {
        var tests = new List<(string Name, Action Test)>
        {
            ("accepted readiness regression does not request a departure refresh", () =>
            { var b=Ready(); ObjectManager.Wow!.Slots[0].Flags=WoWDescriptorQuestFlags.None; b.RunReady(); Check(b.Refreshes==0,"readiness loss was treated as log departure"); }),
            ("metadata eviction preserves a still-accepted ready identity", () =>
            { var b=Ready(); StyxWoW.Cache.Entries.Clear(); b.RunReady(); Check(b.Refreshes==0,"metadata eviction was treated as log departure"); }),
            ("raw departure queues exactly one refresh", () =>
            { var b=Ready(); Put(0,0); b.RunReady(); b.RunReady(); Check(b.Refreshes==1,"genuine departure was lost or repeated"); }),
            ("multiple raw departures queue one refresh", () =>
            { var b=Ready(); Put(1,999); b.RunReady(); Put(0,0); Put(1,0); b.RunReady(); b.RunReady(); Check(b.Refreshes==1,"departure batch was lost or duplicated"); }),
            ("same-count replacement is a departure when ownership is unchanged", () =>
            { var b=Ready(); Put(0,999); b.RunReady(); b.RunReady(); Check(b.Refreshes==1,"equal quest counts hid the departed ready ID"); }),
            ("descriptor replacement cannot inherit ready history", () =>
            { var b=Ready(); ObjectManager.Me!.Descriptor+=4096; Put(0,0); b.RunReady(); Check(b.Refreshes==0,"new descriptor borrowed previous ready history"); }),
            ("raw GUID replacement cannot inherit ready history", () =>
            { var b=Ready(); ObjectManager.Me!.Guid++; Put(0,0); b.RunReady(); Check(b.Refreshes==0,"new raw GUID borrowed previous ready history"); }),
            ("memory owner replacement cannot inherit ready history", () =>
            { var b=Ready(); ObjectManager.Wow=new(); b.RunReady(); Check(b.Refreshes==0,"new memory owner borrowed previous ready history"); }),
            ("same-name player replacement cannot inherit ready history", () =>
            { var b=Ready(); ObjectManager.Me=new LocalPlayer(); Put(0,0); b.RunReady(); Check(b.Refreshes==0,"new same-name player borrowed ready history"); }),
            ("failed raw reads do not mean an empty log and break history continuity", () =>
            { var b=Ready(); ObjectManager.Wow!.FailRawLogReads=true; b.RunReady(); Check(b.Refreshes==0,"failed reads fabricated empty-log departure"); ObjectManager.Wow.FailRawLogReads=false; Put(0,0); b.RunReady(); Check(b.Refreshes==0,"unknown interval retained departure authority"); }),
            ("short raw read breaks history continuity", () =>
            { var b=Ready(); ObjectManager.Wow!.ShortRawLogRead=true; b.RunReady(); ObjectManager.Wow.ShortRawLogRead=false; Put(0,0); b.RunReady(); Check(b.Refreshes==0,"partial raw bytes retained departure authority"); }),
            ("duplicate occupied IDs break history continuity", () =>
            { var b=Ready(); Put(1,867); b.RunReady(); Put(0,0); Put(1,0); b.RunReady(); Check(b.Refreshes==0,"duplicate log retained departure authority"); }),
            ("invalid occupied ID is unknown, not departure permission", () =>
            { var b=Ready(); ObjectManager.Wow!.Slots[0].Id=uint.MaxValue; b.RunReady(); Check(b.Refreshes==0,"invalid raw identity fabricated departure"); }),
            ("unavailable world observation breaks history continuity", () =>
            { var b=Ready(); ObjectManager.IsInGame=false; b.RunReady(); ObjectManager.IsInGame=true; Put(0,0); b.RunReady(); Check(b.Refreshes==0,"world gap retained departure authority"); }),
            ("metadata failure is not a raw departure", () =>
            { var b=Ready(); var error=new InvalidOperationException("controlled cache failure"); StyxWoW.Cache.Failure=error; try { b.RunReady(); } catch(Exception e) when(ReferenceEquals(e,error)) { throw new AssertionFailure("optional metadata failure escaped the readiness owner"); } Check(b.Refreshes==0,"cache exception fabricated departure"); }),
            ("cancellation retains its exact exception identity", () =>
            { var b=Ready(); var error=new OperationCanceledException("controlled ready cancellation"); StyxWoW.Cache.Failure=error; try { b.RunReady(); throw new AssertionFailure("cancellation did not propagate"); } catch(Exception e) when(ReferenceEquals(e,error)) { } })
        };
        int assertions=0, unexpected=0;
        foreach(var t in tests)
        {
            try { t.Test(); Console.WriteLine("PASS raw-ready: "+t.Name); }
            catch(AssertionFailure e) { assertions++; Console.Error.WriteLine("FAIL raw-ready assertion: "+t.Name+": "+e.Message); }
            catch(Exception e) { unexpected++; Console.Error.WriteLine("ERROR raw-ready fixture/owner: "+t.Name+": "+e); }
        }
        Console.WriteLine($"Raw-ready scenarios: {tests.Count-assertions-unexpected}/{tests.Count}; assertions={assertions}; unexpected={unexpected}; actual extracted readiness and QuestLog owners; no game attached.");
        if(assertions+unexpected!=0) throw new InvalidOperationException("Raw-ready regressions: "+assertions+" assertions; "+unexpected+" unexpected");
    }
}
