using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Reflection;
using System.IO;
using System.Security.Cryptography;
using System.Xml.Linq;
using Bots.Quest.Objectives;
using Bots.Quest.QuestOrder;
using Styx.Logic.Pathing;
using Styx.Logic.Profiles.Quest;
using Styx.Logic.Questing;
using Styx.Logic.Questing.Recovery;
using WholesomeAQ;

// Connects actual scheduling/profile/objective owners to the actual shared loot
// tree's slot actions, separate server observations and real inventory readers.
// Includes complete turn-in/reward/frame owners. Native world, collision and
// server responses remain controlled; no live-realm certificate is implied.
internal static class GroundCollectionAcknowledgementPipelineRegressionTests
{
    [ModuleInitializer]
    internal static void Run()
    {
        RunVariant(false);
        RunVariant(true);
    }
    private static void RunVariant(bool shippedModel)
    {
        using var dataScope = new QuestDataRepairPackRegressionTests.Fixture();
        using var f=new QuestInventorySnapshotRegressionTests.Fixture();
        f.NativeFixture.SetQuest(10161,"In Case of Emergency...",58,new int[4],new int[4],new[]{28116,0,0,0,0,0},new[]{30,0,0,0,0,0});
        var quest=new QuestEntry{Id=10161,Name="In Case of Emergency...",QuestLevel=58,MinLevel=58,
            MaxLevel=0,AllowableClasses=0,AllowableRaces=0,RequiredSkillID=0,RequiredSkillPoints=0,
            RequiredMinRepFaction=0,RequiredMinRepValue=0,RequiredMaxRepFaction=0,RequiredMaxRepValue=0,
            RequiredFactionValue1=0,RequiredFactionValue2=0,
            Objectives=Enumerable.Range(183394,4).Select((entry,index)=>new WholesomeAQ.QuestObjective{
                Type=WholesomeAQ.ObjectiveType.CollectFromGameObject,Index=index,GameObjectId=entry,
                GameObjectName="Zeppelin Debris",ItemId=28116,CollectCount=30}).ToList()};
        var db=new QuestDatabase{Quests=new(){quest},
            QuestGivers=new(){new(){QuestId=10161,GiverId=19367,GiverType=WholesomeAQ.QuestObjectType.Creature,GiverName="Screaming Screed Luckheed"}},
            QuestEnders=new(){new(){QuestId=10161,EnderId=19367,EnderType=WholesomeAQ.QuestObjectType.Creature,EnderName="Screaming Screed Luckheed"}},
            CreatureSpawns=new(){["19367"]=new(){new(){Map=530,X=12,Y=10,Z=10}}},
            GameObjectSpawns=Enumerable.Range(183394,4).ToDictionary(entry=>entry.ToString(),_=>new List<SpawnPoint>{new(){Map=530,X=10,Y=10,Z=10}})};
        double playerX=10,playerY=10,playerZ=10;
        var scanSettings=new WholesomeAQSettings();
        var scanOwner=new QuestScheduler(new DataLoader(dataScope.DataPath),new ProfileBuilder(),scanSettings);
        uint[] sources=Enumerable.Range(183394,4).Select(value=>(uint)value).ToArray();
        if(shippedModel)
        {
            string root=Root();
            string directory=Path.Combine(root,"runtime-snapshot/Bots/WholesomeAutoQuest-master/quest_data");
            db=new DataLoader(Path.Combine(directory,"quest_data.json")).Load();
            quest=db.Quests.Single(row=>row.Id==10161);
            Check(quest.Objectives.Count==1&&quest.Objectives[0].GameObjectId==183394
                &&quest.Objectives[0].ItemId==28116&&quest.Objectives[0].CollectCount==30,
                "actual shipped model changed; re-establish its source contract");
            // Keep the exact loaded quest, relation and geometry bytes; limit
            // scheduling population so unrelated pickup plans cannot mask it.
            db.Quests=new(){quest};
            sources=new uint[]{183394};
            var point=db.GameObjectSpawns["183394"].First(p=>p.Map==530);
            playerX=point.X;playerY=point.Y;playerZ=point.Z;
            foreach(string name in new[]{"quest_data.json","quest_data.repairs.json","quest_strategies.json","quest_knowledge_manifest.json"})
                Console.WriteLine("GROUND_SHIPPED_INPUT "+name+" sha256="+Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Path.Combine(directory,name)))).ToLowerInvariant());
        }
        QuestScheduleResult Schedule()
        {
            var inventory=(QuestInventorySnapshot)f.Capture();
            Check(inventory.IsComplete&&inventory.IsCurrent(),"complete inventory receipt required");
            bool accepted=f.Player.QuestLog.ContainsQuest(10161);
            QuestDescriptorData observed=default;
            if(accepted)Check(((Quest)f.NativeFixture.Quest).GetData(out observed),"actual quest descriptor is unavailable");
            Check(f.Player.QuestLog.TryGetAuthoritativeCompletedQuests(out var history),"completed history is unknown");
            return QuestScheduler.MaterializeSchedule(db,new QuestSchedulerSnapshot{
                UtcNow=new DateTime(2026,10,1,0,0,0,DateTimeKind.Utc),PlayerGuid=f.Player.Guid,
                PlayerLevel=58,PlayerRaceId=10,PlayerClassId=2,MapId=530,X=playerX,Y=playerY,Z=playerZ,
                HasCompleteQuestLog=true,HasAuthoritativeCompletions=true,CompletedQuestIds=history.ToArray(),
                AcceptedQuests=accepted?new[]{new QuestSchedulerAcceptedQuest{QuestId=10161,IsCompleted=observed.IsCompleted,
                    IsFailed=observed.IsFailed,ObjectiveCounts=observed.ObjectivesDone.Select(v=>(int)v).ToArray(),
                    NormalObjectiveIds=new int[4],NormalObjectiveRequiredCounts=new int[4]}}:Array.Empty<QuestSchedulerAcceptedQuest>(),
                CarriedItemCounts=inventory.ItemCounts,InventoryObservationStatus=inventory.Status,
                RawQuestStates=new Dictionary<uint,int>{[10161]=!accepted?0:observed.IsCompleted?1:3},
                SkillValues=new Dictionary<int,int>(),ReputationValues=new Dictionary<int,int>()},
                _=>new QuestRecoveryDecision{State=QuestRecoveryState.Eligible,MayAttempt=true},50,scanOwner.ScanThreshold,80,
                navigationAssessment:_=>new SpawnNavigationAssessment{IsKnownSafe=true,IsKnownReachable=true});
        }
        string Xml(QuestScheduleResult schedule)=>new ProfileBuilder().BuildProfileXml(schedule.Plan,db,"Controlled ground geometry","Fixture",58);
        var initial=Schedule();
        Check(initial.Plan.Any(p=>p.Quest.Id==10161&&p.Stage==QuestWorkStage.Objective),"scheduler did not admit the source-bound ground objective");
        string profile=Xml(initial);
        f.NativeFixture.LoadProfile(profile);
        var generated=XDocument.Parse(profile).Descendants("QuestOrder").Single().Descendants("Objective").ToArray();
        Check(generated.Length>0,"generated guarded profile contains no executable Objective node");
        // Retain the exact generated objective attributes. Its surrounding
        // accepted/not-completed guard is established by the actual snapshot
        // above; no hand-written item or count replaces the generated node.
        var nodes=OrderNodeCollection.FromXml(new XElement("QuestOrder",new XElement(generated[0])));
        var node=nodes.OfType<ObjectiveNode>().Single();
        using var owner=f.NativeFixture.CreateObjective(node);
        Check(owner.Objective is CollectItemObjective&&!owner.IsDone,"generated action is not incomplete actual collection");
        var collection=(CollectItemObjective)owner.Objective;
        _=owner.Branch;
        Check(Styx.StyxWoW.AreaManager.CurrentGrindArea.RequiresGroundInteraction,"generated ground objective did not install ground arrival semantics");
        var definition=collection.OverridedQuestInfo.FindCollectItem(28116);
        Check(definition.OverridedCollectFrom.Where(s=>s.Type==Styx.Logic.Profiles.Quest.CollectFromType.GameObject)
            .Select(s=>s.ID).OrderBy(id=>id).SequenceEqual(sources),"profile lost one of the exact object source alternatives");
        var before=QuestProgressObservation.Read(f.NativeFixture.Quest,(QuestInventorySnapshot)f.Capture());
        Check(before.Count>4&&before[4]==0,"profile creation or arrival fabricated carried stock");
        uint fields=0;int acknowledgements=0;
        using var loadedObject = new LoadedSourceObservation(collection);
        var acquired=new HashSet<ulong>();
        QuestLootHandoffRegressionTests.CollectionEntry=index=>sources[index%sources.Length];
        QuestLootHandoffRegressionTests.CollectionSourceAdmission=(entry,guid)=>
        {
            Check(!acquired.Contains(guid),"same GUID supplied a second collection acknowledgement");
            bool match=loadedObject.Matches(entry,guid);
            if(match)acquired.Add(guid);
            return match;
        };
        QuestLootHandoffRegressionTests.CollectionBeforeAcknowledgement=count=>
        {
            var observed=QuestProgressObservation.Read(f.NativeFixture.Quest,(QuestInventorySnapshot)f.Capture());
            Check(count==acknowledgements&&observed.Count>4&&observed[4]==count&&!owner.IsDone,
                "a loot action without its inventory reply fabricated progress or completion");
            Check(Schedule().Plan.Any(p=>p.Stage==QuestWorkStage.Objective),"unacknowledged loot removed the remaining objective");
        };
        QuestLootHandoffRegressionTests.CollectionAcknowledged=count=>
        {
            Check(count==acknowledgements+1,"duplicate or out-of-order loot receipt");
            // The test delivers this server observation separately, only after
            // the real owned slot action and two deliberate no-reply checks.
            if(count==1)fields=f.Item(23,28116,1).Fields;
            else f.Write32(fields+56,(uint)count);
            var inventory=(QuestInventorySnapshot)f.Capture();
            var progress=QuestProgressObservation.Read(f.NativeFixture.Quest,inventory);
            Check(inventory.IsComplete&&inventory.IsCurrent()&&progress.Count>4&&progress[4]==count,
                "acknowledged slot was not observed by the actual carried-item and quest progress readers");
            Check(owner.IsDone==(count==30),"partial stock prematurely completed the generated objective");
            if(count<30)Check(Schedule().Plan.Any(p=>p.Stage==QuestWorkStage.Objective),"partial stock suppressed remaining acquisition");
            acknowledgements++;
        };
        try{QuestLootHandoffRegressionTests.Run();}
        finally{QuestLootHandoffRegressionTests.CollectionAcknowledged=null;
            QuestLootHandoffRegressionTests.CollectionBeforeAcknowledgement=null;
            QuestLootHandoffRegressionTests.CollectionEntry=null;
            QuestLootHandoffRegressionTests.CollectionSourceAdmission=null;}
        Check(acknowledgements==30&&owner.IsDone,"not all thirty distinct owned actions reached the actual objective owner");
        Check(!Schedule().Plan.Any(p=>p.Stage==QuestWorkStage.TurnIn),"item quantity invented the separate server completion observation");
        f.NativeFixture.SetAccepted(true,complete:true);
        var completed=Schedule();
        // Real coordinates can put the ender outside the initial250-yard scan.
        // Execute the existing bounded expansion owner instead of silently
        // replacing source coordinates or using an unlimited fixture radius.
        for(int scan=0;completed.Plan.Count==0&&scanOwner.ScanThreshold<scanSettings.ScanMaxDistance&&scan<16;scan++)
        {
            Check(completed.Status.Contains("outside-scan-radius",StringComparison.Ordinal),
                "the empty completed plan is not explained by normal scan expansion: "+completed.Status);
            int previous=scanOwner.ScanThreshold;
            typeof(QuestScheduler).GetMethod("ApplyScanExpansionBeforeFallback",BindingFlags.Instance|BindingFlags.NonPublic)!
                .Invoke(scanOwner,new object[]{completed});
            Check(scanOwner.ScanThreshold>previous&&scanOwner.ScanThreshold<=scanSettings.ScanMaxDistance,
                "normal scan expansion did not remain bounded");
            Console.WriteLine("GROUND_ENDER_SCAN "+previous+"->"+scanOwner.ScanThreshold);
            completed=Schedule();
        }
        Check(completed.Plan.Any(p=>p.Stage==QuestWorkStage.TurnIn)&&!completed.Plan.Any(p=>p.Stage==QuestWorkStage.Objective),"server-completed state did not move scheduling to the ender: "+completed.Status);
        var turnin=XDocument.Parse(Xml(completed)).Descendants("TurnIn").Single(row=>(uint)row.Attribute("QuestId")! ==10161);
        using(var execution=new QuestTurnInExecutionRegressionTests.TurnInExecutionFixture())
        {
            execution.Execute(turnin,true,()=>{f.NativeFixture.SetAccepted(false);f.NativeFixture.SetHistory(new uint[]{10161});},
                ()=>Check(f.Player.QuestLog.ContainsQuest(10161)&&Schedule().Plan.Any(p=>p.Stage==QuestWorkStage.TurnIn),
                    "turn-in request fabricated completed quest history"));
        }
        Check(f.Player.QuestLog.TryGetAuthoritativeCompletedQuests(out var rewarded)&&rewarded.Contains(10161),
            "separate reward acknowledgement did not reach authoritative history");
        Check(!Schedule().Plan.Any(p=>p.Quest.Id==10161),"rewarded quest was scheduled again");
        Check(acquired.Count==30,"collection did not reacquire thirty distinct live candidates");
        Console.WriteLine("Acknowledged ground pipeline "+(shippedModel?"shipped10161 model":"four source alternatives")+": scheduler/profile -> actual source predicate/acquisition -> supported landing/dismount -> owned interaction/loot x30 -> separate delayed inventory replies -> partial/full objective -> separate server-ready -> ender travel/interaction -> actual reward selection/one submission -> separate history reply -> next scheduling. Native world, collision, live UI/server timing and realm completion remain UNPROVEN.");
    }
    private sealed class LoadedSourceObservation : IDisposable
    {
        private readonly IntPtr storage=Marshal.AllocHGlobal(4096);
        private readonly CollectItemObjective owner;
        internal LoadedSourceObservation(CollectItemObjective value)
        {
            owner=value;Marshal.Copy(new byte[4096],0,storage,4096);
            Marshal.WriteInt32(storage,8,unchecked((int)((uint)storage.ToInt32()+512)));
            Marshal.WriteInt32(storage,20,5);Marshal.WriteInt32(storage,512+68,1|(3<<8));
            Marshal.WriteInt32(storage,512+56,9);
        }
        internal bool Matches(uint entry,ulong guid)
        {
            Marshal.WriteInt64(storage,48,unchecked((long)guid));Marshal.WriteInt64(storage,512,unchecked((long)guid));
            Marshal.WriteInt32(storage,512+12,unchecked((int)entry));
            using(Styx.WoWInternals.ObjectManager.Wow!.TemporaryCacheState(false))
            {
                var live=new Styx.WoWInternals.WoWObjects.WoWGameObject(unchecked((uint)storage.ToInt32()));
                Check(!live.GetCachedInfo(out _),"unexpected optional gameobject cache record in discovery fixture");
                return (bool)typeof(CollectItemObjective).GetMethod("IsValidGameObjectTarget",BindingFlags.NonPublic|BindingFlags.Instance)!.Invoke(owner,new object[]{live})!;
            }
        }
        public void Dispose()=>Marshal.FreeHGlobal(storage);
    }
    private static string Root()
    {
        for(var directory=new DirectoryInfo(AppContext.BaseDirectory);directory!=null;directory=directory.Parent)
            if(File.Exists(Path.Combine(directory.FullName,"CopilotBuddy.csproj")))return directory.FullName;
        throw new InvalidOperationException("Tracked source checkout required");
    }
    private static void Check(bool value,string message){if(!value)throw new InvalidOperationException(message);}
}
