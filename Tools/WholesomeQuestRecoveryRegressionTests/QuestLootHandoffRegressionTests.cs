using System;
using System.CodeDom.Compiler;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

// Complete loot factory and quest opportunistic-target factory, real TreeSharp,
// POI decorators, target admission and existing target/POI action. Tests tick the
// actual handoff, acquisition and loot continuations. Frame observations and
// slot dispatch are controlled; they do not prove native response provenance.
internal static class QuestLootHandoffRegressionTests
{
    // Optional observation bridge for the separate integration test. Production
    // never sees this hook; the ordinary suite still executes every original case.
    internal static System.Action<int>? CollectionAcknowledged;
    internal static System.Action<int>? CollectionBeforeAcknowledgement;
    internal static Func<int, uint>? CollectionEntry;
    internal static Func<uint, ulong, bool>? CollectionSourceAdmission;
    [ModuleInitializer]
    internal static void Run()
    {
        const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
        string? root = null;
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d != null; d = d.Parent)
            if (File.Exists(Path.Combine(d.FullName, "CopilotBuddy.csproj"))) { root = d.FullName; break; }
        if (root == null) throw new InvalidOperationException("Tracked checkout required");
        var level = CSharpSyntaxTree.ParseText(File.ReadAllText(Path.Combine(root, "Bots/Grind/LevelBot.cs"))).GetRoot();
        var quest = CSharpSyntaxTree.ParseText(File.ReadAllText(Path.Combine(root, "Bots/Quest/QuestBot.cs"))).GetRoot();
        string combatObservation = CSharpSyntaxTree.ParseText(File.ReadAllText(Path.Combine(root,
            "Styx/WoWInternals/WoWObjects/LocalPlayer.cs"))).GetRoot().DescendantNodes()
            .OfType<PropertyDeclarationSyntax>().Single(property => property.Identifier.ValueText == "IsActuallyInCombat").ToString();
        string Methods(Microsoft.CodeAnalysis.SyntaxNode node, params string[] names) => string.Join("\n", node.DescendantNodes()
            .OfType<MethodDeclarationSyntax>().Where(method => names.Contains(method.Identifier.ValueText)).Select(method => method.ToString()));
        string guard = level.DescendantNodes().OfType<ClassDeclarationSyntax>().Single(c => c.Identifier.ValueText == "RoutineAdmissionGuard").ToString();
        string source = Prefix + "public static class LevelProbe {\n" + LevelLeaves +
            Methods(level, "CreateLootBehavior", "CanLoot", "CreateOwnedLootSelection", "CanSelectLoot", "IsLootPoi", "CreateOwnedLootInteraction", "CanBeginLoot", "LootAllItems", "IsPlayerOrPetInCombat") + guard +
            string.Join("\n", level.DescendantNodes().OfType<ClassDeclarationSyntax>().Where(c => c.Identifier.ValueText == "LootWorkObservation").Select(c => c.ToString())) + "}\n" +
            "public static class QuestProbe { public static Composite Build()=>CreateTargetingBehavior();\n" +
            Methods(quest, "CreateTargetingBehavior", "ShouldSuppressOpportunisticTargeting") + "}\n" +
            Boundary.Replace("/* ACTUAL_COMBAT_OBSERVATION */", combatObservation);
        string temp = Path.Combine(Path.GetTempPath(), "cb-quest-loot-handoff-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        bool logging = Styx.Helpers.Logging.FileLogging;
        try
        {
            Styx.Helpers.Logging.FileLogging = false;
            File.WriteAllText(Path.Combine(temp, "Probe.cs"), source);
            foreach (string path in new[] {
                "CommonBehaviors/Decorators/DecoratorIsPoiType.cs", "CommonBehaviors/Decorators/DecoratorIsNotPoiType.cs",
                "Bots/Grind/Levelbot/Decorators/Combat/DecoratorNeedToFindTarget.cs",
                "Bots/Grind/Levelbot/Actions/Combat/ActionSetTarget.cs",
                "CommonBehaviors/Actions/ActionSetPoi.cs", "CommonBehaviors/Actions/RetrieveBotPoiDelegate.cs",
                "CommonBehaviors/Actions/ActionClearPoi.cs", "CommonBehaviors/Actions/ActionIdle.cs",
                "CommonBehaviors/Actions/ActionAlwaysSucceed.cs", "CommonBehaviors/Actions/ActionDebugString.cs",
                "CommonBehaviors/Actions/DebugStringDelegate.cs", "CommonBehaviors/WaitLuaEvent.cs",
                "CommonBehaviors/Actions/ActionMoveToPoi.cs", "CommonBehaviors/Actions/NavigationAction.cs", "CommonBehaviors/Actions/GetPointDelegate.cs" })
                File.Copy(Path.Combine(root, path), Path.Combine(temp, Path.GetFileName(path)));
            string shared = Path.Combine(root, "CommonBehaviors/Actions/OwnedTargetHandoff.cs");
            if (File.Exists(shared)) File.Copy(shared, Path.Combine(temp, "OwnedTargetHandoff.cs"));
            string ground = Path.Combine(root, "CommonBehaviors/Actions/GroundLootApproach.cs");
            if (File.Exists(ground)) File.Copy(ground, Path.Combine(temp, "GroundLootApproach.cs"));
            Type compilerType = typeof(Styx.StyxWoW).Assembly.GetType("Styx.Loaders.SourceCompiler", true)!;
            object compiler = Activator.CreateInstance(compilerType, new object[] { temp })!;
            foreach (string path in ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator))
                compilerType.GetMethod("AddReference", flags)!.Invoke(compiler, new object[] { path });
            var result = (CompilerResults)compilerType.GetMethod("Compile", flags)!.Invoke(compiler, null)!;
            var errors = result.Errors.Cast<CompilerError>().Where(error => !error.IsWarning).ToArray();
            if (errors.Length != 0) throw new InvalidOperationException("Actual handoff compilation: " + string.Join("; ", errors.Select(error => error.ToString())));
            var assembly = (Assembly)compilerType.GetProperty("CompiledAssembly", flags)!.GetValue(compiler)!;
            assembly.GetType("HandoffCases", true)!.GetField("CollectionAcknowledged")!.SetValue(null, CollectionAcknowledged);
            assembly.GetType("HandoffCases", true)!.GetField("CollectionBeforeAcknowledgement")!.SetValue(null, CollectionBeforeAcknowledgement);
            assembly.GetType("HandoffCases", true)!.GetField("CollectionEntry")!.SetValue(null, CollectionEntry);
            assembly.GetType("HandoffCases", true)!.GetField("CollectionSourceAdmission")!.SetValue(null, CollectionSourceAdmission);
            try { assembly.GetType("HandoffCases", true)!.GetMethod("Run")!.Invoke(null, null); }
            catch (TargetInvocationException error) when (error.InnerException != null)
            { ExceptionDispatchInfo.Capture(error.InnerException).Throw(); throw; }
        }
        finally { Styx.Helpers.Logging.FileLogging = logging; Directory.Delete(temp, true); }
    }
    private const string Prefix = """
#nullable disable
using System;using System.Collections.Generic;using System.Linq;using System.Reflection;
using TreeSharp;using Styx;using Styx.Helpers;using Styx.Logic;using Styx.Logic.AreaManagement;
using Styx.Logic.BehaviorTree;using Styx.Logic.Combat;using Styx.Logic.Pathing;using Styx.Logic.POI;
using Styx.Logic.Profiles;using Styx.WoWInternals;using Styx.WoWInternals.WoWObjects;
using CommonBehaviors;using Styx.Logic.Inventory.Frames.LootFrame;
using FrameLock=Styx.WoWInternals.FrameLock;
using CommonBehaviors.Actions;using CommonBehaviors.Decorators;using Levelbot.Actions.Combat;using Levelbot.Decorators.Combat;
""";
    private const string LevelLeaves = """
 private static bool _lootEventsAttached=true;private static PoiType _lastLootPoiType;private static ulong _lastLootGuid;private static int _lootAttemptCount,_lootFailCount;
 private static void OnLootEvent(object sender,LuaEventArgs e){}
 private static void SleepForLag()=>HandoffCases.Event("lag");
""";
    private const string Boundary = """
public static class HandoffCases {
 sealed class Failure(string why):Exception(why){}
 public static LocalPlayer Actor;public static WoWUnit Selected;public static WoWObject Loot;public static BotPoi Original;
 public static int Targets,Publications,Clears,NavClears;public static bool Ack=true;public static string Stage;public static System.Action Callback;
 public static int Interactions,Slots,Closes,Stats,Moves;public static ulong FrameGuid;public static bool AutoCloseFinalSlot;public static readonly List<ulong> Blacklisted=new();
 public static int Descents,DescendStops,Dismounts,GroundMoves,Collected;public static bool GroundMode,SupportKnown=true,LineClear=true;public static float FloorZ=10;
 public static System.Action<int> CollectionAcknowledged;public static bool TrackCollection;
 public static System.Action<int> CollectionBeforeAcknowledgement;public static Func<int,uint> CollectionEntry;
 public static Func<uint,ulong,bool> CollectionSourceAdmission;public static ulong PendingCollectionGuid;
 static void Check(bool ok,string why){if(!ok)throw new Failure(why);}
 public static void Event(string stage){if(Stage==stage){var action=Callback;Stage=null;Callback=null;action?.Invoke();}}
 static void Reset(string owner){
  Stage=null;Callback=null;Targets=Publications=Clears=NavClears=0;Ack=true;
  Interactions=Slots=Closes=Stats=Moves=0;FrameGuid=0;AutoCloseFinalSlot=false;Blacklisted.Clear();Lua.Events.Handlers.Clear();
  Descents=DescendStops=Dismounts=GroundMoves=Collected=0;PendingCollectionGuid=0;GroundMode=TrackCollection=false;SupportKnown=LineClear=true;FloorZ=10;
  CharacterSettings.Instance=new CharacterSettings();
  StyxWoW.AreaManager.CurrentGrindArea=null;
  foreach(string field in new[]{"_lastLootGuid","_lootAttemptCount","_lootFailCount"}){var f=typeof(LevelProbe).GetField(field,BindingFlags.Static|BindingFlags.NonPublic);f.SetValue(null,Activator.CreateInstance(f.FieldType));}
  Actor=new LocalPlayer{Guid=1,IsMoving=true};StyxWoW.Me=Actor;WoWMovement.ActiveMover=Actor;
  ObjectManager.CachedUnits.Clear();ObjectManager.CachedUnits.Add(new WoWUnit{Guid=80,Aggro=true});
  Selected=new WoWUnit{Guid=2};Targeting.Instance=new Targeting{FirstUnit=Selected};Targeting.PullDistance=30;Targeting.PullDistanceSqr=900;
  Loot=new WoWUnit{Guid=3,IsAlive=false,IsHostile=false};LootTargeting.Instance=new LootTargeting{FirstObject=Loot};
  LootTargeting.SkinMobs=LootTargeting.HarvestHerbs=LootTargeting.HarvestMinerals=false;
  Original=new BotPoi(Loot,owner=="loot"?PoiType.Loot:PoiType.Hotspot);BotPoi.Seed(Original);
  Navigator.NavigationProvider=new object();ProfileManager.CurrentProfile=null;LevelbotSettings.Instance=new LevelbotSettings();
 }
 static IEnumerable<Composite> Walk(Composite item){yield return item;if(item is GroupComposite group)foreach(var child in group.Children.Where(c=>c!=null))foreach(var nested in Walk(child))yield return nested;}
 static Composite Build(string kind)=>kind=="quest"?QuestProbe.Build():Walk(LevelProbe.CreateLootBehavior()).OfType<DecoratorNeedToFindTarget>().Single();
 static RunStatus Tick(Composite tree){
  var errors=new List<string>();System.Action<Styx.Helpers.LogLevel,string> handler=(_,text)=>{if(text.Contains("Exception")||text.Contains("Object reference not set"))errors.Add(text);};
  var logged=typeof(Composite).Assembly.GetType("Styx.Helpers.Logging",true).GetEvent("OnMessageLogged");logged.AddEventHandler(null,handler);
  try{var result=tree.Tick(null);Check(errors.Count==0,"handoff swallowed exception: "+string.Join(";",errors));return result;}finally{logged.RemoveEventHandler(null,handler);}
 }
 static RunStatus Once(Composite tree){tree.Start(null);try{return Tick(tree);}finally{tree.Stop(null);}}
 static void Change(string kind){switch(kind){
  case "actor":StyxWoW.Me=new LocalPlayer{Guid=1,IsMoving=true};break;case "actor-guid":Actor.Guid++;break;
  case "dead":Actor.IsAlive=false;break;case "invalid":Actor.IsValid=false;break;case "map":Actor.MapId++;break;
  case "target":Targeting.Instance.FirstUnit=new WoWUnit{Guid=9};break;case "target-guid":Selected.Guid++;break;
  case "target-dead":Selected.IsAlive=false;break;case "target-invalid":Selected.IsValid=false;break;
  case "display":Actor.CurrentTarget=new WoWUnit{Guid=8};break;case "poi":BotPoi.Seed(new BotPoi(new WoWUnit{Guid=99,Location=new WoWPoint(500,10,10)},PoiType.Fly));break;
  case "poi-type":BotPoi.Current.Type=PoiType.Repair;break;case "provider":Navigator.NavigationProvider=new object();break;
  case "control":WoWMovement.ActiveMover=new WoWUnit{Guid=7};break;case "combat":Actor.Combat=true;break;
  case "cast":Actor.IsCasting=true;break;case "taxi":Actor.OnTaxi=true;break;case "transport":Actor.IsOnTransport=true;break;
  case "range":Selected.Range=80;break;case "not-hostile":Selected.IsHostile=false;break;
  default:throw new InvalidOperationException(kind);
 }}
 public static void Run(){
  var cases=new List<(string,System.Action)>();
  foreach(string owner in new[]{"loot","quest"}){
   string kind=owner;void Add(string name,System.Action test)=>cases.Add((kind+"/"+name,()=>{Reset(kind);test();}));
   Add("healthy exact acknowledgement",()=>{Check(Once(Build(kind))==RunStatus.Success&&Targets==1&&Publications==1&&BotPoi.Current.Type==PoiType.Kill&&BotPoi.Current.Guid==2,"healthy handoff changed");});
   Add("old unrelated target is not an acknowledgement",()=>{Ack=false;Actor.CurrentTarget=new WoWUnit{Guid=8};var tree=Build(kind);tree.Start(null);try{Check(Tick(tree)==RunStatus.Running&&Publications==0,"old target falsely acknowledged selection");Actor.CurrentTarget=Selected;Check(Tick(tree)==RunStatus.Success&&Publications==1&&BotPoi.Current.Guid==2,"later exact acknowledgement did not complete");}finally{tree.Stop(null);}});
   Add("missing acknowledgement stays pending",()=>{Ack=false;var tree=Build(kind);tree.Start(null);try{Check(Tick(tree)==RunStatus.Running&&Publications==0,"void target submission became acknowledgement");Actor.CurrentTarget=Selected;Check(Tick(tree)==RunStatus.Success&&Publications==1,"healthy delayed acknowledgement failed");}finally{tree.Stop(null);}});
   foreach(string mutation in new[]{"actor","actor-guid","dead","invalid","map","target","target-guid","target-dead","target-invalid","display","poi","poi-type","provider","control","combat","cast","taxi","transport","range","not-hostile"}){
    if(kind=="quest"&&mutation=="not-hostile")continue; // The retained quest target list may include attackable neutral objectives.
    string change=mutation;Add("wait revokes "+change,()=>{Ack=false;var tree=Build(kind);tree.Start(null);try{Check(Tick(tree)==RunStatus.Running,"handoff did not await target");Change(change);if(change!="display")StyxWoW.Me.CurrentTarget=Selected;var current=BotPoi.Current;Check(Tick(tree)==RunStatus.Failure&&Publications==0&&ReferenceEquals(BotPoi.Current,current),"revoked wait published a kill or retained stale handoff");}finally{tree.Stop(null);}});
   }
   foreach(string stage in new[]{"target","build","publish"})foreach(string mutation in new[]{"actor","map","target","target-guid","target-dead","display","poi","poi-type","provider","control","cast","range"}){
    string boundary=stage,change=mutation;Add(boundary+" revokes "+change,()=>{bool fired=false;Stage=boundary;Callback=()=>{fired=true;Change(change);};var result=Once(Build(kind));Check(fired,"callback not reached");Check(result==RunStatus.Failure&&Publications==(boundary=="publish"?1:0),"obsolete handoff published or reported success");if(change=="poi")Check(BotPoi.Current.Type==PoiType.Fly,"callback replacement was consumed");});
   }
   foreach(string mutation in new[]{"dead","invalid","target-dead","target-invalid","cast","taxi","transport","combat"}){string change=mutation;Add("initial "+change,()=>{Change(change);Check(Once(Build(kind))==RunStatus.Failure&&Targets==0&&Publications==0,"invalid initial handoff dispatched");});}
   Add("targeting service replacement revokes wait",()=>{Ack=false;var tree=Build(kind);tree.Start(null);try{Tick(tree);Targeting.Instance=new Targeting{FirstUnit=Selected};Actor.CurrentTarget=Selected;Check(Tick(tree)==RunStatus.Failure&&Publications==0,"new targeting service inherited a wait");}finally{tree.Stop(null);}});
   if(kind=="quest")Add("neutral admitted quest candidate remains usable",()=>{Selected.IsHostile=false;Check(Once(Build(kind))==RunStatus.Success&&Publications==1&&BotPoi.Current.Guid==Selected.Guid,"new shared handoff invented a hostile-only quest filter");});
  }
  foreach(PoiType type in new[]{PoiType.Fly,PoiType.Repair,PoiType.Sell,PoiType.Buy,PoiType.Mail,PoiType.Train,PoiType.QuestPickUp,PoiType.QuestTurnIn}){
   var intent=type;cases.Add(("loot acquisition preserves "+intent,()=>{Reset("loot");Original.Type=intent;Targeting.Instance.FirstUnit=null;Once(LevelProbe.CreateLootBehavior());Check(Publications==0&&ReferenceEquals(BotPoi.Current,Original),"opportunistic loot consumed service/quest intent");}));
   cases.Add(("quest targeting preserves "+intent,()=>{Reset("quest");Original.Type=intent;Once(QuestProbe.Build());Check(Targets==0&&Publications==0,"opportunistic quest targeting consumed travel");}));
  }
  foreach(string mode in new[]{"loot","skin","herb","mineral"}){string variant=mode;cases.Add(("healthy acquisition "+variant,()=>{Reset("loot");Original.Type=PoiType.None;Targeting.Instance.FirstUnit=null;
   if(variant=="skin"){((WoWUnit)Loot).CanSkin=true;LootTargeting.SkinMobs=true;}
   if(variant=="herb"||variant=="mineral"){Loot=new WoWGameObject{Guid=4,IsHerb=variant=="herb",IsMineral=variant=="mineral"};LootTargeting.Instance.FirstObject=Loot;LootTargeting.HarvestHerbs=LootTargeting.HarvestMinerals=true;}
   Once(LevelProbe.CreateLootBehavior());Check(Publications==1&&BotPoi.Current.Guid==Loot.Guid&&BotPoi.Current.Type==(variant=="skin"?PoiType.Skin:variant=="loot"?PoiType.Loot:PoiType.Harvest),"healthy acquisition priority changed");}));}
  AddLootCompletionCases(cases);
  AddLootAdmissionCases(cases);
  AddCombatObservationCases(cases);
  AddUpstreamLootCases(cases);
  AddGroundLootCases(cases);
  if(CollectionAcknowledged!=null)cases=cases.Where(item=>item.Item1=="ground collection/thirty distinct objects require thirty actual interaction and loot dispatches").ToList();
  int passed=0,assertions=0,unexpected=0;foreach(var item in cases){try{item.Item2();passed++;Console.WriteLine("PASS quest loot handoff: "+item.Item1);}catch(Failure e){assertions++;Console.Error.WriteLine("FAIL quest loot handoff: "+item.Item1+": "+e.Message);}catch(Exception e){unexpected++;Console.Error.WriteLine("ERROR quest loot handoff: "+item.Item1+": "+e);}}
  Console.WriteLine($"Quest loot handoff scenarios: {passed}/{cases.Count}; assertions={assertions}; unexpected={unexpected}; actual caller factories/admission/POI actions and real TreeSharp; controlled world/target callbacks; no loot-frame or native acknowledgement provenance.");
  if(assertions+unexpected!=0)throw new InvalidOperationException("Quest loot handoff regression");
 }
 static void AddGroundLootCases(List<(string,System.Action)> cases){
  void Add(string name,System.Action body)=>cases.Add(("ground collection/"+name,()=>{Reset("loot");Targeting.Instance.FirstUnit=null;Actor.IsMoving=false;GroundMode=true;Loot=new WoWGameObject{Guid=3,Entry=183394};LootTargeting.Instance.FirstObject=Loot;Original=new BotPoi(Loot,PoiType.Loot);BotPoi.Seed(Original);Publications=0;body();}));
  void Open(){FrameGuid=Loot.Guid;Lua.Events.Fire("LOOT_OPENED");}
  Add("mounted ground object waits for a dismount observation",()=>{Actor.Mounted=true;var tree=LevelProbe.CreateLootBehavior();tree.Start(null);try{
   Check(Tick(tree)==RunStatus.Running&&Dismounts==1&&Interactions==0,"mounted object interacted before observed dismount");
   Check(Tick(tree)==RunStatus.Running&&Dismounts==1&&Interactions==0,"pending dismount was spammed or became interaction permission");
   Actor.Mounted=false;Check(Tick(tree)==RunStatus.Running&&Interactions==1,"acknowledged dismount did not reach interaction");
   Open();Tick(tree);Check(Collected==1&&Stats==0,"ground loot did not produce item receipt or was counted as a dead mob");
  }finally{tree.Stop(null);}});
  Add("vertical arrival descends before an out-of-range travel branch",()=>{Actor.Mounted=true;Actor.IsFlying=true;Actor.Location=new WoWPoint(10,10,40);Loot.WithinInteractRange=false;
   var tree=LevelProbe.CreateLootBehavior();tree.Start(null);try{
    Check(Tick(tree)==RunStatus.Running&&Descents==1&&Moves==0&&Interactions==0,"out-of-range flight dispatch starved ground descent");
    Check(Dismounts==0,"flying actor was dismounted");Actor.Location=new WoWPoint(10,10,10);Actor.IsFlying=false;Loot.WithinInteractRange=true;
    Check(Tick(tree)==RunStatus.Running&&DescendStops>0&&Dismounts==1&&Interactions==0,"landing was not separated from dismount acknowledgement");
    Actor.Mounted=false;Tick(tree);Check(Interactions==1,"landed dismounted collection never interacted");Open();Tick(tree);Check(Collected==1,"loot dispatch failed to produce the controlled item observation");
   }finally{tree.Stop(null);}});
  foreach(string hazard in new[]{"support-unknown","wrong-floor","line-of-sight","falling","movement-unknown","zero-range","blacklisted"}){string mode=hazard;
   Add("approach refuses "+mode,()=>{Actor.Mounted=true;Actor.IsFlying=true;Actor.Location=new WoWPoint(10,10,40);Loot.WithinInteractRange=false;
    if(mode=="support-unknown")SupportKnown=false;if(mode=="wrong-floor")FloorZ=30;if(mode=="line-of-sight")LineClear=false;
    if(mode=="falling"){Actor.IsFlying=false;Actor.Falling=true;}if(mode=="movement-unknown")Actor.MovementKnown=false;if(mode=="zero-range")Loot.InteractRange=0;
    if(mode=="blacklisted")Blacklisted.Add(Loot.Guid);
    Once(LevelProbe.CreateLootBehavior());Check(Descents==0&&Dismounts==0&&Interactions==0,"unsafe or unknown observation authorized descent/dismount/interaction");
   });
  }
  foreach(string change in new[]{"actor","actor-guid","map","dead","invalid","combat","control","poi","poi-type","loot-object","loot-despawn","transport","provider"}){string mode=change;
   Add("owned descent releases on "+mode,()=>{Actor.Mounted=true;Actor.IsFlying=true;Actor.Location=new WoWPoint(10,10,40);Loot.WithinInteractRange=false;
    var tree=LevelProbe.CreateLootBehavior();tree.Start(null);try{Check(Tick(tree)==RunStatus.Running&&Descents==1,"descent did not begin");ChangeLoot(mode);var replacement=BotPoi.Current;Tick(tree);
     Check(Interactions==0&&Dismounts==0&&ReferenceEquals(replacement,BotPoi.Current),"revoked descent consumed replacement work");
    }finally{tree.Stop(null);}
   });
  }
  foreach(string hazard in new[]{"support-unknown","wrong-floor","line-of-sight","movement-unknown","falling","zero-range"}){string mode=hazard;
   Add("changed landing observation releases descent: "+mode,()=>{Actor.Mounted=true;Actor.IsFlying=true;Actor.Location=new WoWPoint(10,10,40);Loot.WithinInteractRange=false;
    var tree=LevelProbe.CreateLootBehavior();tree.Start(null);try{Check(Tick(tree)==RunStatus.Running&&Descents==1,"descent control failed");
     if(mode=="support-unknown")SupportKnown=false;if(mode=="wrong-floor")FloorZ=30;if(mode=="line-of-sight")LineClear=false;if(mode=="movement-unknown")Actor.MovementKnown=false;if(mode=="falling")Actor.Falling=true;if(mode=="zero-range")Loot.InteractRange=0;
     Tick(tree);Check(DescendStops==1&&!Actor.MovementInfo.IsDescending&&Interactions==0&&Dismounts==0,"lost landing evidence left descent input active");
    }finally{tree.Stop(null);}
   });
  }
  Add("object origin above its support does not cancel valid final descent",()=>{Actor.Mounted=true;Actor.IsFlying=true;Actor.Location=new WoWPoint(10,10,14);Loot.Location=new WoWPoint(10,10,13);Original.Location=Loot.Location;Loot.WithinInteractRange=false;
   var tree=LevelProbe.CreateLootBehavior();tree.Start(null);try{Check(Tick(tree)==RunStatus.Running&&Descents==1,"supported landing control failed");Actor.Location=new WoWPoint(10,10,11);Tick(tree);Check(Moves==0&&Actor.MovementInfo.IsDescending,"valid landing below an object's origin restarted flight before reaching support");}finally{tree.Stop(null);}});
  Add("ground navigation dispatch is rate bounded without observed movement",()=>{Loot.Location=new WoWPoint(20,10,10);Loot.WithinInteractRange=false;var tree=LevelProbe.CreateLootBehavior();tree.Start(null);try{Tick(tree);Tick(tree);Check(GroundMoves==1,"unchanged position flooded ground path requests");}finally{tree.Stop(null);}});
  foreach(string hazard in new[]{"falling","moving","movement-unknown","range","object-location"}){string mode=hazard;
   Add("final sight callback cannot donate stale interaction permission: "+mode,()=>{int reads=0;System.Action mutation=null;mutation=()=>{if(++reads<2){Stage="ground-los";Callback=mutation;return;}if(mode=="falling")Actor.Falling=true;if(mode=="moving")Actor.IsMoving=true;if(mode=="movement-unknown")Actor.MovementKnown=false;if(mode=="range")Loot.WithinInteractRange=false;if(mode=="object-location")Loot.Location=new WoWPoint(12,10,10);};Stage="ground-los";Callback=mutation;
    Once(LevelProbe.CreateLootBehavior());Check(reads==2&&Interactions==0,"final collision observation bypassed fresh movement/range/position checks");
   });
  }
  Add("stopping a pending descent releases its input",()=>{Actor.Mounted=true;Actor.IsFlying=true;Actor.Location=new WoWPoint(10,10,40);Loot.WithinInteractRange=false;
   var tree=LevelProbe.CreateLootBehavior();tree.Start(null);Check(Tick(tree)==RunStatus.Running&&Descents==1,"descent did not begin");tree.Stop(null);Check(DescendStops==1&&!Actor.MovementInfo.IsDescending,"stopped tree left its descent input held");});
  Add("expired approach performs a bounded rescan instead of endless Running",()=>{Actor.Mounted=true;Actor.IsFlying=true;Actor.Location=new WoWPoint(10,10,40);Loot.WithinInteractRange=false;
   var tree=LevelProbe.CreateLootBehavior();tree.Start(null);try{Tick(tree);var approach=Walk(tree).FirstOrDefault(c=>c.GetType().Name=="GroundLootApproach");Check(approach!=null,"no bounded ground approach owner exists");
    approach.GetType().GetField("_startedUtc",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(approach,DateTime.UtcNow.AddMinutes(-5));
    Check(Tick(tree)!=RunStatus.Running&&Clears==1&&Blacklisted.Contains(Loot.Guid)&&DescendStops>0,"expired ground approach held input or failed to release its owned POI");
   }finally{tree.Stop(null);}});
  Add("ground approach does not remount a nearby object",()=>{Loot.Location=new WoWPoint(20,10,10);Loot.WithinInteractRange=false;var tree=LevelProbe.CreateLootBehavior();tree.Start(null);try{
   Tick(tree);Check(GroundMoves==1&&Moves==0&&Dismounts==0&&Interactions==0,"ground collection returned to flying-mount travel");
   Actor.Location=Loot.Location;Loot.WithinInteractRange=true;Tick(tree);Check(Interactions==1,"real ground arrival failed to interact");
  }finally{tree.Stop(null);}});
  Add("a remount during interaction setup revokes the dispatch",()=>{Actor.IsMoving=true;Stage="stop";Callback=()=>Actor.Mounted=true;Once(LevelProbe.CreateLootBehavior());Check(Interactions==0,"late mount state was ignored by the interaction gate");});
  Add("an object consumed before interaction is not used",()=>{Actor.IsMoving=true;Stage="stop";Callback=()=>((WoWGameObject)Loot).CanLoot=false;Once(LevelProbe.CreateLootBehavior());Check(Interactions==0,"consumed object was interacted after setup");});
  Add("full inventory does not issue collection actions",()=>{ProfileManager.CurrentProfile=new Profile();Actor.FreeNormalBagSlots=0;Actor.Mounted=true;Once(LevelProbe.CreateLootBehavior());Check(Interactions==0&&Dismounts==0&&Moves==0&&Collected==0,"full inventory was treated as an acquisition receipt");});
  Add("foreign loot window cannot advance the item counter",()=>{var tree=LevelProbe.CreateLootBehavior();tree.Start(null);try{Tick(tree);Check(Interactions==1,"healthy attempt missing");FrameGuid=99;Lua.Events.Fire("LOOT_OPENED");Tick(tree);Check(Collected==0&&Slots==0,"foreign frame donated quest item progress");}finally{tree.Stop(null);}});
  Add("an unacknowledged interaction cannot advance collection",()=>{var tree=LevelProbe.CreateLootBehavior();tree.Start(null);try{Tick(tree);Check(Interactions==1,"healthy attempt missing");foreach(var wait in Walk(tree).OfType<Wait>())typeof(Wait).GetField("End",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(wait,DateTime.MinValue);Tick(tree);Check(Collected==0&&Slots==0,"missing LOOT_OPENED donated item credit");}finally{tree.Stop(null);}});
  Add("an immediate owned loot event is captured before interaction returns",()=>{AutoCloseFinalSlot=true;Stage="interact";Callback=Open;var tree=LevelProbe.CreateLootBehavior();tree.Start(null);try{
   Check(Tick(tree)==RunStatus.Success&&Interactions==1&&Collected==1&&Clears==1,"native interaction completed before the event listener was attached");
  }finally{tree.Stop(null);}});
  Add("ground opening can outlast the corpse acknowledgement budget",()=>{AutoCloseFinalSlot=true;var tree=LevelProbe.CreateLootBehavior();tree.Start(null);try{
   Check(Tick(tree)==RunStatus.Running&&Interactions==1,"ground opening did not begin");
   Actor.IsCasting=true;Check(Tick(tree)==RunStatus.Running&&Interactions==1&&Collected==0,"opening cast was interrupted or counted as loot");
   var wait=Walk(tree).OfType<WaitLuaEvent>().Single();
   Check(wait.Timeout.TotalSeconds<=10,"ground acknowledgement lost its finite upper bound");
   // Five and a quarter seconds of elapsed opening latency, without a real sleep.
   typeof(Wait).GetField("End",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(wait,DateTime.Now.AddSeconds(wait.Timeout.TotalSeconds-5.25));
   Actor.IsCasting=false;Open();Check(Tick(tree)==RunStatus.Success&&Collected==1&&Blacklisted.Count==0,"valid delayed ground loot was discarded at the shorter corpse deadline");
  }finally{tree.Stop(null);}});
  foreach(bool aerial in new[]{false,true}){bool flying=aerial;Add("static hotspot without a live object awaits respawn, flying="+flying,()=>{
   Actor.Mounted=Actor.IsFlying=flying;if(flying)Actor.Location=new WoWPoint(10,10,40);
   Original=new BotPoi(PoiType.None);BotPoi.Seed(Original);LootTargeting.Instance.FirstObject=null;
   StyxWoW.AreaManager.CurrentGrindArea=new GrindArea{CurrentHotSpot=new Hotspot{Position=new WoWPoint(10,10,10)}};
   var tree=LevelProbe.CreateLootBehavior();Once(tree);Once(tree);
   Check(Interactions==0&&Publications==0&&Dismounts==0&&Blacklisted.Count==0,"static coordinates invented a live source or an unsafe dismount");
   LootTargeting.Instance.FirstObject=Loot;Once(tree);
   Check(Publications==1&&BotPoi.Current.Guid==Loot.Guid,"respawn observation did not re-enter actual acquisition");
  });}
  Add("thirty distinct objects require thirty actual interaction and loot dispatches",()=>{AutoCloseFinalSlot=true;TrackCollection=true;var tree=LevelProbe.CreateLootBehavior();
   for(int index=0;index<30;index++){
    Loot=new WoWGameObject{Guid=(ulong)(1000+index),Entry=CollectionEntry?.Invoke(index)??(uint)(183394+index%4)};
    Check(CollectionSourceAdmission?.Invoke(Loot.Entry,Loot.Guid)??true,"generated source predicate rejected the live candidate");
    LootTargeting.Instance.FirstObject=Loot;Original=new BotPoi(PoiType.None);BotPoi.Seed(Original);FrameGuid=0;
    int published=Publications;Once(tree);Check(Publications==published+1&&BotPoi.Current.Guid==Loot.Guid&&BotPoi.Current.Type==PoiType.Loot,"live candidate was not acquired by the actual shared owner");
    Original=BotPoi.Current;
    int prior=Collected;
    if(index==0){Actor.Mounted=true;Actor.IsFlying=true;Actor.Location=new WoWPoint(10,10,40);Loot.WithinInteractRange=false;}
    tree.Start(null);try{
     if(index==0){Check(Tick(tree)==RunStatus.Running&&Descents==1&&Collected==prior&&Interactions==0,"aerial approach fabricated interaction or count");Actor.Location=new WoWPoint(10,10,10);Actor.IsFlying=false;Loot.WithinInteractRange=true;Check(Tick(tree)==RunStatus.Running&&Dismounts==1&&Collected==prior&&Interactions==0,"ground arrival bypassed dismount acknowledgement");Actor.Mounted=false;}
     Check(Tick(tree)==RunStatus.Running&&Collected==prior,"source acquisition or interaction fabricated progress");
     int issued=Interactions;Check(Tick(tree)==RunStatus.Running&&Interactions==issued&&Collected==prior,"waiting repeated the same GUID interaction or invented progress");
     Open();Check(Tick(tree)==RunStatus.Success&&Collected==prior&&PendingCollectionGuid==Loot.Guid,"loot slot dispatch was mistaken for an item acknowledgement");
     // A delayed or denied inventory update is a separate observation. Prove
     // that two no-reply observations cannot finish the actual parent objective.
     CollectionBeforeAcknowledgement?.Invoke(prior);CollectionBeforeAcknowledgement?.Invoke(prior);
     PendingCollectionGuid=0;Collected++;CollectionAcknowledged?.Invoke(Collected);
     Check(Collected==prior+1,"separate owned inventory acknowledgement advanced the wrong quantity");
    }finally{tree.Stop(null);}
   }
   Check(Collected==30&&Interactions==30&&Stats==0,"complete collection bypassed actions, duplicated GUID credit or inflated mob statistics");
  });
 }
 static void AddLootCompletionCases(List<(string,System.Action)> cases){
  void Add(string name,System.Action body)=>cases.Add(("loot completion/"+name,()=>{Reset("loot");Targeting.Instance.FirstUnit=null;Actor.IsMoving=false;body();}));
  Composite Begin(){var tree=LevelProbe.CreateLootBehavior();tree.Start(null);Check(Tick(tree)==RunStatus.Running&&Interactions==1,"loot did not submit one interaction and await an event");return tree;}
  void Open(){FrameGuid=Loot.Guid;Lua.Events.Fire("LOOT_OPENED");}
  Add("healthy observed event keeps final cleanup",()=>{var tree=Begin();try{Open();Check(Tick(tree)==RunStatus.Success&&Slots==2&&Closes==1&&Stats==1&&Clears==1&&BotPoi.Current.Type==PoiType.None,"healthy loot completion changed");}finally{tree.Stop(null);}});
  Add("client closing the exact frame after the final slot completes",()=>{AutoCloseFinalSlot=true;var tree=Begin();try{Open();Check(Tick(tree)==RunStatus.Success&&Slots==2&&Closes==0&&Stats==1&&Clears==1&&Blacklisted.Count==0,"normal final-slot closure was treated as a timeout or touched a closed frame");}finally{tree.Stop(null);}});
  Add("replacement frame after the final slot is never closed",()=>{var tree=Begin();try{Open();Stage="slot";Callback=()=>FrameGuid=99;Tick(tree);Check(Slots==1&&Closes==0&&Stats==0,"foreign frame inherited slot or close authority");}finally{tree.Stop(null);}});
  Add("timeout is not a looted mob",()=>{var tree=Begin();try{foreach(var waiter in Walk(tree).OfType<Wait>())typeof(Wait).GetField("End",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(waiter,DateTime.MinValue);Tick(tree);Check(Slots==0&&Closes==0&&Stats==0,"missing event became successful loot statistics/close");}finally{tree.Stop(null);}});
  foreach(string frame in new[]{"missing","foreign"}){string observation=frame;Add("event with "+observation+" frame",()=>{var tree=Begin();try{FrameGuid=observation=="missing"?0UL:99UL;Lua.Events.Fire("LOOT_OPENED");Tick(tree);Check(Slots==0&&Closes==0&&Stats==0,"event borrowed missing/foreign frame authority");}finally{tree.Stop(null);}});}
  Add("pre-existing frame is left alone",()=>{FrameGuid=99;Once(LevelProbe.CreateLootBehavior());Check(Interactions==0&&Slots==0&&Closes==0&&Stats==0&&Blacklisted.Count==0,"pre-existing frame was adopted or replaced");});
  foreach(string mode in new[]{"actor","actor-guid","dead","invalid","map","poi","poi-type","provider","control","combat","taxi","transport","loot-guid","loot-object","loot-despawn"}){
   string change=mode;Add("pending event revokes "+change,()=>{var tree=Begin();try{ChangeLoot(change);var replacement=BotPoi.Current;Open();Tick(tree);Check(Slots==0&&Closes==0&&Stats==0&&Blacklisted.Count==0&&Clears==0&&ReferenceEquals(BotPoi.Current,replacement),"revoked pending loot continued or consumed replacement");}finally{tree.Stop(null);}});
  }
  foreach(string stage in new[]{"loot-log","frame-lock","slot","close","stats"})foreach(string mode in new[]{"actor","map","poi","poi-type","loot-object"}){
   string boundary=stage,change=mode;Add(boundary+" callback revokes "+change,()=>{var tree=Begin();try{Open();bool fired=false;Stage=boundary;Callback=()=>{fired=true;ChangeLoot(change);};Tick(tree);Check(fired,"loot callback was not reached");int permittedSlots=boundary=="slot"?1:boundary=="close"||boundary=="stats"?2:0;Check(Slots==permittedSlots&&Closes==(boundary=="close"||boundary=="stats"?1:0)&&Stats==(boundary=="stats"?1:0)&&Clears==0&&Blacklisted.Count==0,"revoked synchronous loot continued across callback");}finally{tree.Stop(null);}});
  }
  foreach(string stage in new[]{"stop","lag","interact"})foreach(string mode in new[]{"actor","map","poi","loot-object"}){
   string boundary=stage,change=mode;Add(boundary+" setup revokes "+change,()=>{Actor.IsMoving=true;bool fired=false;Stage=boundary;Callback=()=>{fired=true;ChangeLoot(change);};Once(LevelProbe.CreateLootBehavior());Check(fired&&Interactions==(boundary=="interact"?1:0)&&Slots==0&&Closes==0&&Stats==0&&Clears==0&&Blacklisted.Count==0,"revoked setup dispatched later loot work");});
  }
  Add("range changes after stopping deny interaction",()=>{Actor.IsMoving=true;Stage="stop";Callback=()=>((WoWUnit)Loot).WithinLootRange=false;Once(LevelProbe.CreateLootBehavior());Check(Interactions==0&&Slots==0&&Stats==0&&Blacklisted.Count==0,"out-of-range corpse reached Interact after movement setup");});
 }
 static void ChangeLoot(string mode){if(mode=="loot-guid")Loot.Guid++;else if(mode=="loot-object")Original.AsObject=new WoWUnit{Guid=3,IsAlive=false};else if(mode=="loot-despawn")Original.AsObject=null;else Change(mode);}
 static void AddLootAdmissionCases(List<(string,System.Action)> cases){
  void Add(string name,System.Action body)=>cases.Add(("loot admission/"+name,()=>{Reset("loot");Targeting.Instance.FirstUnit=null;Actor.IsMoving=false;body();}));
  foreach(string stage in new[]{"build","publish"})foreach(string mode in new[]{"actor","map","poi","poi-type","provider","loot-guid","loot-replaced","loot-invalid","combat","cast","taxi"}){
   string boundary=stage,change=mode;Add(boundary+" acquisition revokes "+change,()=>{
    Original.Type=PoiType.None;bool fired=false;Stage=boundary;Callback=()=>{fired=true;if(change=="loot-replaced")LootTargeting.Instance.FirstObject=new WoWUnit{Guid=8};else if(change=="loot-invalid")Loot.IsValid=false;else ChangeLoot(change);};
    var result=Once(LevelProbe.CreateLootBehavior());Check(fired,"acquisition callback not reached");Check(result==RunStatus.Failure&&Publications==(boundary=="publish"?1:0),"revoked loot selection published or certified success");if(change=="poi")Check(BotPoi.Current.Type==PoiType.Fly,"new flight work was replaced");
   });
  }
  foreach(string mode in new[]{"dead","invalid","taxi","transport","cast","loot-invalid","loot-zero"}){string change=mode;Add("initial acquisition "+change,()=>{
   Original.Type=PoiType.None;if(change=="loot-invalid")Loot.IsValid=false;else if(change=="loot-zero")Loot.Guid=0;else ChangeLoot(change);Once(LevelProbe.CreateLootBehavior());Check(Publications==0,"unavailable candidate or actor acquired loot work");
  });}
  foreach(string phase in new[]{"attempt-log","blacklist"})foreach(string mode in new[]{"actor","map","poi","poi-type","loot-object"}){
   string boundary=phase,change=mode;Add("retry "+boundary+" revokes "+change,()=>{
    typeof(LevelProbe).GetField("_lastLootGuid",BindingFlags.Static|BindingFlags.NonPublic).SetValue(null,Loot.Guid);
    typeof(LevelProbe).GetField("_lastLootPoiType",BindingFlags.Static|BindingFlags.NonPublic).SetValue(null,PoiType.Loot);
    typeof(LevelProbe).GetField("_lootAttemptCount",BindingFlags.Static|BindingFlags.NonPublic).SetValue(null,4);
    typeof(LevelProbe).GetField("_lootFailCount",BindingFlags.Static|BindingFlags.NonPublic).SetValue(null,1);
    bool fired=false;Stage=boundary;Callback=()=>{fired=true;ChangeLoot(change);};Once(LevelProbe.CreateLootBehavior());
    Check(fired&&Blacklisted.Count==(boundary=="blacklist"?1:0)&&Clears==0&&Interactions==0,"revoked retry blacklisted/cleared replacement work");
   });
  }
  foreach(string mode in new[]{"actor","map","poi","poi-type","reappeared"}){string change=mode;Add("missing-object log revokes "+change,()=>{
   Original.AsObject=null;bool fired=false;Stage="missing-log";Callback=()=>{fired=true;if(change=="reappeared")Original.AsObject=Loot;else ChangeLoot(change);};
   Once(LevelProbe.CreateLootBehavior());Check(fired&&Blacklisted.Count==0&&Clears==0,"missing-object diagnostic consumed a changed owner/object");
  });}
  foreach(string mode in new[]{"actor","map","poi","poi-type","loot-object"}){string change=mode;Add("range predicate revokes movement "+change,()=>{
   ((WoWUnit)Loot).WithinLootRange=false;bool fired=false;Stage="loot-range";Callback=()=>{fired=true;ChangeLoot(change);};
   Once(LevelProbe.CreateLootBehavior());Check(fired&&Moves==0&&Interactions==0&&Clears==0&&Blacklisted.Count==0,"travel action adopted work replaced by the range predicate");
  });}
  Add("healthy out-of-range travel",()=>{((WoWUnit)Loot).WithinLootRange=false;Check(Once(LevelProbe.CreateLootBehavior())==RunStatus.Success&&Moves==1&&Interactions==0,"healthy loot travel stopped dispatching");});
 }
 static void AddCombatObservationCases(List<(string,System.Action)> cases){
  foreach(bool pet in new[]{false,true})foreach(bool empty in new[]{false,true})foreach(string stage in new[]{"acquisition","interaction","pending","slot","travel"}){
   bool petCombat=pet,noUnits=empty;string boundary=stage;
   cases.Add(("raw combat/"+(pet?"pet":"actor")+"/"+(empty?"empty-cache":"no-aggro")+"/"+boundary,()=>{
    Reset("loot");Targeting.Instance.FirstUnit=null;Actor.IsMoving=false;
    void StartCombat(){if(petCombat)Actor.Pet=new WoWUnit{Guid=90,IsAlive=true,Combat=true};else Actor.Combat=true;ObjectManager.CachedUnits.Clear();if(!noUnits)ObjectManager.CachedUnits.Add(new WoWUnit{Guid=80,Aggro=false});Check(!Actor.IsActuallyInCombat,"actual cache-dependent observation did not exercise its gap");}
    if(boundary=="acquisition"||boundary=="interaction"){
     if(boundary=="acquisition")Original.Type=PoiType.None;StartCombat();Once(LevelProbe.CreateLootBehavior());
     Check(Publications==0&&Interactions==0&&Moves==0&&Slots==0,"cached aggro absence admitted loot during raw combat");return;
    }
    if(boundary=="travel"){
     ((WoWUnit)Loot).WithinLootRange=false;bool fired=false;Stage="loot-range";Callback=()=>{fired=true;StartCombat();};Once(LevelProbe.CreateLootBehavior());
     Check(fired&&Moves==0&&Interactions==0&&Clears==0,"range callback released raw combat to travel");return;
    }
    var tree=LevelProbe.CreateLootBehavior();tree.Start(null);try{
     Check(Tick(tree)==RunStatus.Running&&Interactions==1,"initial loot did not wait for event");
     if(boundary=="pending")StartCombat();else{Stage="slot";Callback=StartCombat;}
     FrameGuid=Loot.Guid;Lua.Events.Fire("LOOT_OPENED");Tick(tree);
     Check(Slots==(boundary=="slot"?1:0)&&Closes==0&&Stats==0&&Clears==0&&Blacklisted.Count==0,"raw combat continuation consumed loot after cached enemies disappeared");
    }finally{tree.Stop(null);}
   }));
  }
  foreach(bool empty in new[]{false,true}){bool noUnits=empty;cases.Add(("raw combat/ordinary out-of-combat control/"+empty,()=>{
   Reset("loot");Targeting.Instance.FirstUnit=null;Original.Type=PoiType.None;ObjectManager.CachedUnits.Clear();if(!noUnits)ObjectManager.CachedUnits.Add(new WoWUnit{Guid=80,Aggro=true});
   Check(Once(LevelProbe.CreateLootBehavior())==RunStatus.Success&&Publications==1,"actual absence of actor/pet combat incorrectly blocks healthy loot");
  }));}
 }
 static void AddUpstreamLootCases(List<(string,System.Action)> cases){
  void Add(string name,System.Action test)=>cases.Add(("upstream loot/"+name,()=>{Reset("loot");Targeting.Instance.FirstUnit=null;Actor.IsMoving=false;test();}));
  Composite Begin(){var tree=LevelProbe.CreateLootBehavior();tree.Start(null);Check(Tick(tree)==RunStatus.Running&&Interactions==1,"owned attempt failed to submit and wait");return tree;}
  void Open(){FrameGuid=Loot.Guid;Lua.Events.Fire("LOOT_OPENED");}
  Add("bounded owned attempt bypasses unrelated interaction throttle",()=>{Loot.TimerReady=false;var tree=Begin();try{Check(Tick(tree)==RunStatus.Running&&Interactions==1,"waiting operation repeated its interaction");}finally{tree.Stop(null);}});
  foreach(PoiType type in new[]{PoiType.Loot,PoiType.Skin,PoiType.Harvest}){
   var kind=type;Add("frame deadline "+kind,()=>{
    if(kind==PoiType.Harvest){Loot=new WoWGameObject{Guid=3,IsHerb=true};Original.AsObject=Loot;LootTargeting.Instance.FirstObject=Loot;}
    Original.Type=kind;var tree=Begin();try{Check(Walk(tree).OfType<WaitLuaEvent>().Single().Timeout==TimeSpan.FromSeconds(kind==PoiType.Harvest?10:3),"wrong bounded frame wait");}finally{tree.Stop(null);}
   });
  }
  Add("skinning completion does not wait to skin the same corpse again",()=>{
   Original.Type=PoiType.Skin;CharacterSettings.Instance.SkinMobs=true;var tree=Begin();try{Open();Check(Tick(tree)==RunStatus.Success&&Clears==1&&Stats==0&&Interactions==1,"completed skin operation entered a second post-loot skin wait");}finally{tree.Stop(null);}
  });
  Add("exact maximum skinnable level remains eligible",()=>{
   CharacterSettings.Instance.SkinMobs=true;((WoWUnit)Loot).Level=Actor.CanSkinLevel;var tree=Begin();try{
    Open();Check(Tick(tree)==RunStatus.Running&&Clears==0&&Stats==0&&Interactions==1,"equal skinnable level skipped the readiness wait");
    ((WoWUnit)Loot).CanSkin=true;Check(Tick(tree)==RunStatus.Success&&Clears==1&&Stats==1&&Interactions==1,"ready skinning did not release normal loot completion");
   }finally{tree.Stop(null);}
  });
  Add("skin readiness has a two-second bounded wait",()=>{
   CharacterSettings.Instance.SkinMobs=true;var tree=Begin();try{Open();Check(Tick(tree)==RunStatus.Running,"readiness wait did not start");
    var pending=Walk(tree).OfType<WaitContinue>().Single(wait=>wait is not WaitLuaEvent&&wait.LastStatus==RunStatus.Running);
    Check(pending.Timeout==TimeSpan.FromSeconds(2),"old five-second skin wait retained");
   }finally{tree.Stop(null);}
  });
  foreach(string change in new[]{"actor","map","poi","combat","loot-object"}){string mutation=change;Add("skin readiness revokes "+mutation,()=>{
   CharacterSettings.Instance.SkinMobs=true;var tree=Begin();try{Open();Check(Tick(tree)==RunStatus.Running,"readiness wait did not start");ChangeLoot(mutation);((WoWUnit)Loot).CanSkin=true;
    Tick(tree);Check(Stats==0&&Clears==0&&Interactions==1&&Blacklisted.Count==0,"skin readiness consumed a revoked owner");
   }finally{tree.Stop(null);}
  });}
 }
}
public class ItemInfo {public int UniqueCount,BeginQuestId;}
/* Controlled observed world. */ namespace Styx.WoWInternals.WoWObjects {
 public class WoWObject {public ulong Guid;public uint Entry=70;public bool IsValid=true,TimerReady=true,IsDisabled;public string Name="controlled";public WoWPoint Location=new(10,10,10);public float InteractRange=4;public bool WithinInteractRange=true;public WoWUnit ToUnit()=>this as WoWUnit;public WoWGameObject ToGameObject()=>this as WoWGameObject;public void Interact()=>Interact(false);public void Interact(bool ignoreTimer){if(!ignoreTimer&&!TimerReady)return;HandoffCases.Interactions++;HandoffCases.Event("interact");}}
 public class WoWUnit:WoWObject {public bool IsAlive=true,IsHostile=true,IsPlayer,IsMoving,Combat,CanSkin,CanLoot=true,Aggro;private bool withinLootRange=true;public bool WithinLootRange{get{bool observed=withinLootRange;HandoffCases.Event("loot-range");return observed;}set{withinLootRange=value;}}public bool Dead=>!IsAlive;public uint FactionId=1;public int Level=10,Race,Class;public WoWUnit OwnedByUnit,CurrentTarget;public double Range=5;public double Distance=>Range;public double DistanceSqr=>Range*Range;public double MyAggroRange=>15;public bool InLineOfSpellSight=true;public ulong CurrentTargetGuid=>CurrentTarget?.Guid??0;public bool GotTarget=>CurrentTarget!=null;public WoWCreatureSkinType SkinType=>WoWCreatureSkinType.Leather;
  public void Target(){HandoffCases.Targets++;if(HandoffCases.Ack)StyxWoW.Me.CurrentTarget=this;HandoffCases.Event("target");}public void ClearTarget(){CurrentTarget=null;}
 }
 public class WoWGameObject:WoWObject {public bool IsHerb,IsMineral,CanLoot=true;}
 public class WoWItem:WoWObject {public global::ItemInfo ItemInfo=new();}
 public class LocalPlayer:WoWUnit {public List<WoWItem> CarriedItems=new();public uint MapId,FreeBagSlots=100,FreeNormalBagSlots=100;public bool Mounted,IsCasting,IsOnTransport,OnTaxi,IsFlying,Falling,MovementKnown=true;public ShapeshiftForm Shapeshift;public int ChanneledCastingSpellId,CanSkinLevel=450;/* ACTUAL_COMBAT_OBSERVATION */ public WoWUnit Pet;public bool PetInCombat=>Pet?.Combat==true;public bool GotAlivePet=>Pet?.IsAlive==true;public MoveState MovementInfo=new();public bool TryGetMovementState(out uint flags,out ulong transport){flags=(IsFlying?0x02000000u:0u)|(Falling?0x2000u:0u);transport=IsOnTransport?5UL:0UL;return MovementKnown;}}
 public class MoveState{public bool IsDescending;}
}
/* Controlled runtime. */ namespace Styx {public enum ShapeshiftForm{Normal,FlightForm,EpicFlightForm}public static class StyxWoW{public static LocalPlayer Me;public static AreaManager AreaManager=new();public static void ResetAfk(){} }}
/* Controlled runtime. */ namespace Styx.WoWInternals {public class LuaEventArgs:EventArgs{}public static class ObjectManager{public static LocalPlayer Me=>StyxWoW.Me;public static readonly List<WoWUnit> CachedUnits=new();}public sealed class FrameLock:IDisposable{public FrameLock(){HandoffCases.Event("frame-lock");}public void Dispose(){}}public static class Lua{public static void DoString(string code){if(code!="CloseLoot();")throw new InvalidOperationException("unexpected Lua");HandoffCases.Closes++;HandoffCases.Event("close");}public static class Events{public static readonly List<(string Name,System.Action<object,LuaEventArgs> Handler)> Handlers=new();public static void AttachEvent(string name,System.Action<object,LuaEventArgs> handler){Handlers.Add((name,handler));}public static void DetachEvent(string name,System.Action<object,LuaEventArgs> handler){Handlers.RemoveAll(value=>value.Name==name&&value.Handler==handler);}public static void Fire(string name){foreach(var item in Handlers.Where(value=>value.Name==name).ToArray())item.Handler(null,new LuaEventArgs());}}}public static class WoWMovement{public static WoWUnit ActiveMover;public enum MovementDirection{Descend}public static void Move(MovementDirection value){HandoffCases.Descents++;StyxWoW.Me.MovementInfo.IsDescending=true;HandoffCases.Event("descend");}public static void MoveStop(params MovementDirection[] value){if(StyxWoW.Me.MovementInfo.IsDescending){HandoffCases.DescendStops++;StyxWoW.Me.MovementInfo.IsDescending=false;}StyxWoW.Me.IsMoving=false;HandoffCases.Event("stop");}}}
/* Controlled admission observations. */ namespace Styx.Logic.AreaManagement {public class Hotspot{public WoWPoint Position;}public class GrindArea{public Hotspot CurrentHotSpot;public List<int> MobIDs=new(),Factions=new();public int TargetMinLevel,TargetMaxLevel=int.MaxValue;}public class AreaManager{public GrindArea CurrentGrindArea;}}
/* Controlled profile. */ namespace Styx.Logic.Profiles {public class Profile{public int MinFreeBagSlots;public List<uint> Factions=new();}public static class ProfileManager{public static Profile CurrentProfile;}}
/* Controlled target registries. */ namespace Styx.Logic {public class Targeting{public static Targeting Instance=new();public WoWUnit FirstUnit;public bool KillBetweenHotspots;public static double PullDistance=30,PullDistanceSqr=900,CollectionRange=100;}public class LootTargeting{public static LootTargeting Instance=new();public WoWObject FirstObject;public static bool SkinMobs,HarvestHerbs,HarvestMinerals;}public static class Battlegrounds{public static bool IsInsideBattleground=>false;}public static class Blacklist{public static bool Contains(ulong guid)=>HandoffCases.Blacklisted.Contains(guid);public static void Add(ulong guid,TimeSpan duration){HandoffCases.Blacklisted.Add(guid);HandoffCases.Event("blacklist");}}public static class Mount{public static void Dismount(string reason){HandoffCases.Dismounts++;HandoffCases.Event("dismount");}}}
/* Controlled POI effects. */ namespace Styx.Logic.POI {public enum PoiType{None,Kill,Loot,Skin,Harvest,Sell,Repair,Train,Buy,Mail,Fly,Hotspot,Quest,QuestPickUp,QuestTurnIn}public class BotPoi{static BotPoi current;public PoiType Type;public ulong Guid;public uint Entry;public WoWObject AsObject;public WoWPoint Location;public BotPoi(PoiType type){Type=type;}public BotPoi(WoWObject obj,PoiType type){Type=type;Guid=obj.Guid;Entry=obj.Entry;AsObject=obj;Location=obj.Location;HandoffCases.Event("build");}public static BotPoi Current{get=>current;set{current=value;HandoffCases.Publications++;HandoffCases.Event("publish");}}public static void Seed(BotPoi value)=>current=value;public static void Clear(string reason){HandoffCases.Clears++;current=new BotPoi(PoiType.None);}}}
/* Controlled diagnostics/settings. */ namespace Styx.Helpers {public static class Logging{public static void Write(string text,params object[] args){if(text.StartsWith("Looting "))HandoffCases.Event("loot-log");else if(text.StartsWith("Blacklisting lootable to avoid"))HandoffCases.Event("attempt-log");else if(text.StartsWith("[LB] Loot object"))HandoffCases.Event("missing-log");}public static void WriteDebug(string text,params object[] args){}public static void WriteDiagnostic(string text,params object[] args){}}public class LevelbotSettings{public static LevelbotSettings Instance=new();public bool GroundMountFarmingMode;}public class CharacterSettings{public static CharacterSettings Instance=new();public bool SkinMobs,NinjaSkin;}}
/* Controlled status. */ namespace Styx.Logic.BehaviorTree {public static class TreeRoot{public static string StatusText{set{HandoffCases.Event("status");}}}}
/* Controlled navigation. */ namespace Styx.Logic.Pathing {public static class Navigator{public static object NavigationProvider;public static void Clear(){HandoffCases.NavClears++;HandoffCases.Event("nav-clear");}public static MoveResult MoveTo(WoWPoint point){HandoffCases.GroundMoves++;HandoffCases.Event("ground-move");return MoveResult.Moved;}public static RunStatus GetRunStatusFromMoveResult(MoveResult value)=>RunStatus.Failure;}public static class Flightor{public static void MoveTo(WoWPoint point){HandoffCases.Moves++;HandoffCases.Event("move");}}}
/* Controlled collision observations; the production owner interprets them. */ namespace Styx.WoWInternals.World {public static class GameWorld {public enum CGWorldFrameHitFlags{HitTestGroundAndStructures}public static bool TraceLine(WoWPoint from,WoWPoint to,CGWorldFrameHitFlags flags,out WoWPoint hit){HandoffCases.Event("ground-trace");hit=new WoWPoint(from.X,from.Y,HandoffCases.FloorZ);return HandoffCases.SupportKnown;}public static bool IsInLineOfSight(WoWPoint from,WoWPoint to){HandoffCases.Event("ground-los");return HandoffCases.LineClear;}}}
/* Controlled statistics. */ namespace Styx.Logic.Combat {public static class GameStats{public static void LootedMob(){HandoffCases.Stats++;HandoffCases.Event("stats");}}}
/* Controlled frame observation and slot dispatch. */ namespace Styx.Logic.Inventory.Frames.LootFrame {public class LootFrame{public static readonly LootFrame Instance=new();public ulong LootingObjectGuid=>HandoffCases.FrameGuid;public bool IsVisible=>LootingObjectGuid!=0;public int LootItems=>2;public uint GetItemId(int slot)=>HandoffCases.GroundMode&&slot==0?28116u:(uint)(100+slot);public void Loot(int slot){if(HandoffCases.GroundMode&&slot==0&&LootingObjectGuid==HandoffCases.Loot.Guid&&!HandoffCases.Actor.Mounted&&!HandoffCases.Actor.IsFlying){if(HandoffCases.TrackCollection)HandoffCases.PendingCollectionGuid=LootingObjectGuid;else HandoffCases.Collected++;}HandoffCases.Slots++;HandoffCases.Event("slot");if(HandoffCases.AutoCloseFinalSlot&&(HandoffCases.GroundMode?slot==1:HandoffCases.Slots==2))HandoffCases.FrameGuid=0;}}}
""";
}
