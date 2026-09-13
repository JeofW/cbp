using System.Runtime.CompilerServices;
using Styx;
using Styx.Logic.Questing;
using Styx.WoWInternals;
using Styx.WoWInternals.WoWObjects;
using Styx.WoWInternals.WoWCache;
using WholesomeAQ;

internal static class ReadyObservationRegressionTests
{
    private static void Check(bool ok,string reason){if(!ok)throw new InvalidOperationException(reason);}
    private static WholesomeAutoQuest Ready()
    {
        ObjectManager.Me=new();ObjectManager.Wow=new();ObjectManager.IsInGame=true;ObjectManager.Executor=null;StyxWoW.Cache=new();
        ObjectManager.Wow.Slots[0].Id=867;ObjectManager.Wow.Slots[0].Flags=WoWDescriptorQuestFlags.Completed;
        StyxWoW.Cache.Entries[867]=new WoWCache.InfoBlock{Quest=new WoWCache.QuestCacheEntry{Id=867}};
        var bot=new WholesomeAutoQuest();bot.RunReady();Check(bot.Refreshes==0,"first observation invented a departure");return bot;
    }
    [ModuleInitializer]
    internal static void Run()
    {
        var tests=new List<(string,Action)>();
        tests.Add(("missing metadata is not a turn-in",()=>{var b=Ready();StyxWoW.Cache.Entries.Clear();b.RunReady();Check(b.Refreshes==0,"cache miss generated a false turn-in refresh");}));
        tests.Add(("still accepted but no longer ready is not a departure",()=>{var b=Ready();ObjectManager.Wow!.Slots[0].Flags=WoWDescriptorQuestFlags.None;b.RunReady();Check(b.Refreshes==0,"readiness regression became an accepted-log departure");}));
        tests.Add(("genuine raw departure requests one refresh",()=>{var b=Ready();ObjectManager.Wow!.Slots[0].Id=0;b.RunReady();b.RunReady();Check(b.Refreshes==1,"raw departure lost or repeated");}));
        tests.Add(("new player owner cannot inherit prior ready quest identities",()=>{var b=Ready();StyxWoW.Me=new LocalPlayer();ObjectManager.Wow!.Slots[0].Id=0;b.RunReady();Check(b.Refreshes==0,"new owner inherited old ready-log history");}));
        tests.Add(("hydration after a gap does not synthesize a turn-in",()=>{var b=Ready();StyxWoW.Cache.Entries.Clear();b.RunReady();StyxWoW.Cache.Entries[867]=new(){Quest=new(){Id=867}};b.RunReady();Check(b.Refreshes==0,"hydration gap fabricated departure");}));
        int failed=0;
        foreach(var t in tests){try{t.Item2();Console.WriteLine("PASS ready observation: "+t.Item1);}catch(Exception e){failed++;Console.Error.WriteLine("FAIL ready observation: "+t.Item1+": "+e.Message);}}
        Console.WriteLine($"Ready-log observation scenarios: {tests.Count-failed}/{tests.Count}; actual production block, full quest owners; no client attached.");
        if(failed>0)throw new InvalidOperationException("Ready-log observation regressions: "+failed);
    }
}
