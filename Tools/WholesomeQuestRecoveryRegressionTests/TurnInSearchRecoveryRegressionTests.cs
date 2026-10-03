using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using Bots.Quest.QuestOrder;
using Styx.Logic.Pathing;
using Styx.Logic.POI;
using Styx.Logic.Profiles.Quest;
using Styx.Logic.Questing.Recovery;
using WholesomeAQ;
using QuestObjectType = Styx.Logic.Profiles.Quest.QuestObjectType;

internal static class TurnInSearchRecoveryRegressionTests
{
    [ModuleInitializer] internal static void Run()
    {
        const BindingFlags Hidden=BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.Public;
        string temp=Path.Combine(Path.GetTempPath(),"cb-turnin-search-recovery-"+Guid.NewGuid().ToString("N"));
        var manager=QuestRecoveryManager.Instance;
        var oldOrder=QuestOrder.Instance;var oldPoi=BotPoi.Current;
        try
        {
            manager.Configure(new QuestRecoveryEnvironment(temp,"Test","MissingNpc","data","core","nav"));
            var position=new WoWPoint(100,100,100);
            var behavior=new ForcedQuestTurnIn(10161,"Quest",19367,"NPC",position,QuestObjectType.Npc);
            var order=new QuestOrder{CurrentBehavior=behavior};
            var poi=new BotPoi(new TurnInNode(position,19367,"NPC",QuestObjectType.Npc,10161,"Quest"));BotPoi.Current=poi;
            var searchType=typeof(ForcedQuestTurnIn).Assembly.GetType("Styx.Logic.Questing.QuestRelationSearch")!;
            object search=Activator.CreateInstance(searchType,Hidden,null,new object[]{530U,10161U,19367U,position},null)!;
            searchType.GetMethod("Next",Hidden)!.Invoke(search,new object[]{100.0,position});
            searchType.GetMethod("Next",Hidden)!.Invoke(search,new object[]{131.0,position});
            typeof(ForcedQuestTurnIn).GetField("relationSearch",Hidden)!.SetValue(behavior,search);
            var bot=new WholesomeAutoQuest();typeof(WholesomeAutoQuest).GetField("_stopped",Hidden)!.SetValue(bot,false);
            var owners=(WholesomeAttemptOwnership)typeof(WholesomeAutoQuest).GetField("_attemptOwnership",Hidden)!.GetValue(bot)!;
            var gate=(RefreshGate)typeof(WholesomeAutoQuest).GetField("_refreshGate",Hidden)!.GetValue(bot)!;
            var key=QuestRecoveryKey.ForNpc(10161,QuestRecoveryStage.TurnIn,19367);
            var claim=manager.TryBeginAttempt(key,new QuestRecoveryContext());owners.Begin(behavior,key,claim);
            var recover=typeof(WholesomeAutoQuest).GetMethod("TryDeferExhaustedTurnIn",Hidden);
            bool Run()=>recover!=null&&(bool)recover.Invoke(bot,new object[]{behavior,new QuestRecoveryContext()})!;
            bool accepted=Run();
            if(!accepted||manager.OwnsAttempt(key,claim.AttemptGeneration)||owners.TryGet(behavior,out _))
                throw new InvalidOperationException("an exhausted absent-NPC turn-in did not release its exact attempt for other work");
            var record=manager.GetEntries().Single(r=>r.Key.Equals(key));
            if(record.State!=QuestRecoveryState.CoolingDown||record.EpisodeCount!=0||!gate.Begin().HasValue||Run())
                throw new InvalidOperationException("missing-NPC deferral duplicated a retry or consumed quest failure authority");
            var successor=new ForcedQuestTurnIn(10161,"Quest",19367,"NPC",position,QuestObjectType.Npc);order.CurrentBehavior=successor;
            var replacement=new BotPoi(new TurnInNode(position,19367,"NPC",QuestObjectType.Npc,10161,"Quest"));BotPoi.Current=replacement;
            if(Run()||!ReferenceEquals(BotPoi.Current,replacement))throw new InvalidOperationException("old absence cleared successor work");
            Console.WriteLine("Turn-in search recovery: exact active attempt released once, neutral cooldown, one refresh, successor preserved; no game.");
        }
        finally
        {
            manager.Flush();typeof(QuestOrder).GetProperty("Instance")!.SetValue(null,oldOrder);BotPoi.Current=oldPoi;
            if(Directory.Exists(temp))Directory.Delete(temp,true);
        }
    }
}
