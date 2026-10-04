using System.CodeDom.Compiler;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using System.Text;
using System.Xml.Linq;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

// Complete turn-in, reward selection, Frame and QuestFrame owners execute with
// real TreeSharp and stock Lua5.1. Only world/server/UI observations, native
// movement/interaction and item pricing are controlled. A click is never a
// server receipt: the driver explicitly delivers that observation afterwards.
internal static class QuestTurnInExecutionRegressionTests
{
    [ModuleInitializer]
    internal static void Run()
    {
        using var fixture = new TurnInExecutionFixture();
        int cases = fixture.VerifyButtonBoundary();
        cases += fixture.VerifyHandoffBoundaries();
        cases += fixture.VerifyReviewBoundaries();
        cases += fixture.VerifyApproachBoundaries();
        cases += fixture.VerifyMissingNpcBoundaries();
        cases += fixture.VerifyTransitBoundaries();
        foreach (bool choices in new[] { false, true })
        {
            int acknowledgements = 0;
            fixture.Execute(new XElement("TurnIn", new XAttribute("QuestId", 10161),
                new XAttribute("QuestName", "In Case of Emergency..."), new XAttribute("TurnInId", 19367),
                new XAttribute("TurnInType", "Npc"), new XAttribute("X", 12), new XAttribute("Y", 10), new XAttribute("Z", 10)),
                choices, () => acknowledgements++, () => Check(acknowledgements == 0, "request fabricated reward acknowledgement"));
            Check(acknowledgements == 1, "exactly one separate server acknowledgement is required");
            cases++;
        }
        foreach (bool choices in new[] { false, true })
        {
            int acknowledgements = 0;
            fixture.Execute(new XElement("TurnIn", new XAttribute("QuestId", 10161),
                new XAttribute("QuestName", "In Case of Emergency..."), new XAttribute("TurnInId", 19367),
                new XAttribute("TurnInType", "Npc"), new XAttribute("X", 12), new XAttribute("Y", 10), new XAttribute("Z", 10)),
                choices, () => acknowledgements++, () => Check(acknowledgements == 0, "request fabricated reward acknowledgement"),
                delayHistory: true);
            Check(acknowledgements == 1, "delayed history must acknowledge exactly once");
            cases++;
        }
        fixture.Execute(new XElement("TurnIn", new XAttribute("QuestId", 10161),
            new XAttribute("QuestName", "In Case of Emergency..."), new XAttribute("TurnInId", 19367),
            new XAttribute("TurnInType", "Npc"), new XAttribute("X", 12), new XAttribute("Y", 10), new XAttribute("Z", 10)),
            true, () => { }, () => { }, residualLoot: true);
        cases++;
        Console.WriteLine($"Turn-in execution scenarios: {cases}/{cases}; complete turn-in/reward/frame owners, actual Lua5.1, explicit delayed server observations; no client connection.");
    }

    internal sealed class TurnInExecutionFixture : IDisposable
    {
        private readonly Assembly _assembly;
        private readonly RewardLua51Boundary.StockLua51 _lua;
        private readonly Type _state;
        private readonly Type _driver;
        private readonly Func<string, uint> _loadSize;
        private readonly FieldInfo _observe;
        private readonly string _root;
        private RewardLua51Boundary.StockLua51.Session? _session;
        internal readonly List<string> ExecutedRequests = new();

        internal TurnInExecutionFixture()
        {
            _root = Root();
            string temporary = Path.Combine(Path.GetTempPath(), "cb-turnin-execution-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(temporary);
            try
            {
                string[] files = {
                    "Bots/Quest/QuestOrder/ForcedBehavior.cs", "Bots/Quest/QuestOrder/ForcedQuestTurnIn.cs",
                    "Styx/Logic/Questing/QuestRelationSearch.cs",
                    "Styx/Logic/Questing/QuestTurnInCompletion.cs", "Bots/Quest/QuestLootHandoff.cs",
                    "Bots/Quest/Actions/ActionSelectQuest.cs", "Bots/Quest/Actions/ActionSelectReward.cs",
                    "Styx/Logic/Inventory/Frames/Frame.cs", "Styx/Logic/Inventory/Frames/Quest/QuestFrame.cs",
                    "CommonBehaviors/Actions/ActionSleep.cs", "CommonBehaviors/Actions/ActionMoveStop.cs",
                    "CommonBehaviors/Actions/ActionSetPoi.cs", "CommonBehaviors/Actions/RetrieveBotPoiDelegate.cs",
                    "CommonBehaviors/Actions/ActionClearPoi.cs", "CommonBehaviors/Actions/ActionMoveToPoi.cs",
                    "CommonBehaviors/Actions/NavigationAction.cs", "CommonBehaviors/Actions/GetPointDelegate.cs",
                    "CommonBehaviors/Decorators/DecoratorIsPoiType.cs", "CommonBehaviors/Decorators/DecoratorIsNotPoiType.cs"
                };
                foreach (string name in files) File.Copy(Path.Combine(_root, name), Path.Combine(temporary, Path.GetFileName(name)));
                File.WriteAllText(Path.Combine(temporary, "ControlledWorld.cs"), ControlledWorld);
                // This dynamic assembly owns its controlled LocalPlayer type.
                // Compile the exact production mount observation against that
                // type instead of binding the host assembly's different player.
                // The complete mounted transition is covered by the dedicated
                // integration suite; no travel/attack decision is stubbed here.
                var mounted = CSharpSyntaxTree.ParseText(File.ReadAllText(Path.Combine(_root,
                    "Styx/Logic/Combat/MountedCombatTransition.cs"))).GetRoot().DescendantNodes()
                    .OfType<MethodDeclarationSyntax>().Single(method => method.Identifier.ValueText == "IsMountedOrFlying");
                File.WriteAllText(Path.Combine(temporary, "MountedObservation.cs"),
                    "#nullable disable\nusing Styx.WoWInternals.WoWObjects;\nnamespace Styx.Logic.Combat {public static class MountedCombatTransition {"
                    + mounted.ToFullString() + "}}");
                var pickup = CSharpSyntaxTree.ParseText(File.ReadAllText(Path.Combine(_root,"Bots/Quest/QuestOrder/ForcedQuestPickUp.cs"))).GetRoot().DescendantNodes().OfType<ClassDeclarationSyntax>().Single(c=>c.Identifier.ValueText=="ForcedQuestPickUp");
                string pickupMembers=string.Join("\n",pickup.Members.Where(m=>m is FieldDeclarationSyntax || m is MethodDeclarationSyntax method && (method.Identifier.ValueText is "HandleQuestFrame" or "ResetMismatchTracking" or "RecordPickupDecision" or "CreateBehavior" or "CreatePickupPoi" or "InteractWithQuestGiver" or "BeginInteractionCycle" || method.Identifier.ValueText.StartsWith("CompleteObserved"))).Select(m=>m.ToString()));
                // Keep the production run/session namespace: extracted reward
                // members must use the same controlled TreeRoot as the full
                // TurnIn owner and the reentrant replacement cases below.
                File.WriteAllText(Path.Combine(temporary,"PickupDialog.cs"),"#nullable disable\nusing System;using System.Linq;using System.Collections.Generic;using TreeSharp;using Styx;using Styx.WoWInternals;using Styx.WoWInternals.WoWObjects;using Styx.Logic.BehaviorTree;using Styx.Logic.Questing;using Styx.Logic.Questing.Recovery;using Styx.Logic.Inventory.Frames;using Styx.Logic.Inventory.Frames.Gossip;using Styx.Logic.Inventory.Frames.Quest;using Styx.Logic.Pathing;using Styx.Logic.POI;using Styx.Logic.Profiles.Quest;using CommonBehaviors.Actions;using CommonBehaviors.Decorators;\nnamespace Bots.Quest.QuestOrder {public partial class ForcedQuestPickUp {"+pickupMembers+"public RunStatus TickDialog()=>HandleQuestFrame(null);}}");
                RewardLua51Boundary.WriteManagedBridge(temporary, File.ReadAllText(Path.Combine(_root, "Styx/WoWInternals/Lua.cs")));
                Type compilerType = typeof(Styx.StyxWoW).Assembly.GetType("Styx.Loaders.SourceCompiler", true)!;
                object compiler = Activator.CreateInstance(compilerType, new object[] { temporary })!;
                foreach (string name in ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator))
                    compilerType.GetMethod("AddReference")!.Invoke(compiler, new object[] { name });
                var result = (CompilerResults)compilerType.GetMethod("Compile")!.Invoke(compiler, null)!;
                string[] errors = result.Errors.Cast<CompilerError>().Where(e => !e.IsWarning).Select(e => e.ToString()).ToArray();
                if (errors.Length > 0) throw new InvalidOperationException("Complete turn-in fixture compilation: " + string.Join("; ", errors));
                _assembly = (Assembly)compilerType.GetProperty("CompiledAssembly")!.GetValue(compiler)!;
            }
            finally { Directory.Delete(temporary, true); }
            _state = _assembly.GetType("TurnInState", true)!;
            _driver = _assembly.GetType("TurnInDriver", true)!;
            Type bridge = _assembly.GetType("RewardRecordedBridge", true)!;
            _loadSize = (Func<string, uint>)bridge.GetMethod("LoadSize")!.CreateDelegate(typeof(Func<string, uint>));
            _observe = bridge.GetField("Observe")!;
            _lua = new RewardLua51Boundary.StockLua51(_root);
        }

        internal void Execute(XElement generated, bool choices, System.Action acknowledge, System.Action beforeAcknowledgement,
            bool delayHistory = false, System.Action? afterDeparture = null, bool residualLoot = false)
        {
            uint quest = (uint)generated.Attribute("QuestId")!, ender = (uint)generated.Attribute("TurnInId")!;
            Check(quest == 10161 && ender == 19367, "The source-bound fixture must consume the generated quest/ender identity");
            ExecuteBound(generated, choices, quest, ender, acknowledge, beforeAcknowledgement,
                delayHistory: delayHistory, afterDeparture: afterDeparture, residualLoot: residualLoot);
        }

        internal void ExecuteBound(XElement generated, bool choices, uint expectedQuest, uint expectedEnder,
            System.Action acknowledge, System.Action beforeAcknowledgement, uint expectedMap = 530,
            bool delayHistory = false, System.Action? afterDeparture = null, bool residualLoot = false)
        {
            uint quest = (uint)generated.Attribute("QuestId")!, ender = (uint)generated.Attribute("TurnInId")!;
            Check(quest != 0 && ender != 0 && quest == expectedQuest && ender == expectedEnder,
                "Generated turn-in differs from the independently supplied source identity");
            ExecutedRequests.Clear();
            _session?.Dispose();
            _session = _lua.BeginSession(UiSetup + "\nchoiceCount=" + (choices ? "3" : "0") + "\nshownQuest=" + quest + "\n");
            _observe.SetValue(null, new Func<string, List<string>>(Observe));
            Call("Reset", quest, ender, (float)generated.Attribute("X")!, (float)generated.Attribute("Y")!, (float)generated.Attribute("Z")!, expectedMap);
            if (residualLoot) Call("ResidualLoot");
            Check(ReadInt("Map") == expectedMap, "Turn-in fixture did not preserve the independently supplied source map");
            // Installing a new POI legitimately consumes the first selector
            // pulse. Advance a bounded number of real ticks to travel, keeping
            // the no-interaction/no-reward assertions throughout setup.
            for (int tick = 0; tick < 3 && ReadInt("Moves") == 0; tick++)
            {
                Call("Tick");
                Check(ReadInt("Interactions") == 0 && ReadInt("CompletionRequests") == 0,
                    "POI publication or travel fabricated an interaction/reward action");
            }
            Check(ReadInt("Moves") == 1 && ReadInt("Interactions") == 0, "turn-in travel did not dispatch without interaction");
            Check(ReadInt("CompletionRequests") == 0, "travel fabricated a reward request");
            Call("Arrive");
            for (int tick = 0; tick < 3 && ReadInt("Interactions") == 0; tick++) Call("Tick");
            Check(ReadInt("Interactions") == 1 && ReadInt("CompletionRequests") == 0, "ender interaction skipped the waiting boundary");
            Check(!(bool)Call("IsDone")!, "ender interaction was treated as quest completion");
            Observe("gossip=true"); // Deliberately separate native interaction acknowledgement.
            Call("Tick");
            Check(ReadInt("CompletionRequests") == 1, "one reward operation must issue exactly one completion request; actual=" + ReadInt("CompletionRequests"));
            Check(ReadInt("SelectedChoice") == (choices ? 3 : 0), "real reward selection did not consume the observed choice set");
            Check(!(bool)Call("IsDone")!, "button submission fabricated a server turn-in acknowledgement");
            beforeAcknowledgement();
            if (delayHistory)
            {
                // Live October 2 ordering: the accepted quest disappeared before
                // the one-minute completed-history cache had been refreshed.
                Observe("accepted=false; qvisible=false; reward=false; completed=false");
                afterDeparture?.Invoke();
                Check(!(bool)Call("IsDone")!, "accepted-log departure with stale history completed TurnIn");
                for (int tick = 0; tick < 3; tick++) Call("Tick");
                Check(ReadInt("CompletionRequests") == 1 && ReadInt("Interactions") == 1,
                    "pending completion confirmation repeated reward or giver interaction");
            }
            // Only after the actual turn-in request is verified does the test
            // driver supply the server reply, and only then update parent memory.
            int cleanupBaseline = ReadInt("Clears");
            Observe("accepted=false; qvisible=false; reward=false; completed=true");
            acknowledge();
            for (int tick = 0; tick < 6 && ReadInt("Clears") == cleanupBaseline; tick++) Call("Tick");
            Check((bool)Call("IsDone")! && ReadInt("Clears") == cleanupBaseline + 1, "acknowledged turn-in did not release its work");
            Check(ReadInt("CompletionRequests") == 1, "cleanup repeated an already acknowledged reward action");
            Check(ReadInt("Interactions") == 1, "turn-in re-interacted after its own frame opened");
            Call("Stop");
        }

        internal int VerifyReviewBoundaries()
        {
            _session?.Dispose(); _session = _lua.BeginSession(UiSetup);
            _observe.SetValue(null, new Func<string, List<string>>(Observe));
            return (int)Call("ReviewCases")!;
        }

        internal int VerifyHandoffBoundaries()
        {
            _session?.Dispose(); _session = _lua.BeginSession(UiSetup);
            _observe.SetValue(null, new Func<string, List<string>>(Observe));
            return (int)Call("HandoffCases")!;
        }

        internal int VerifyApproachBoundaries()
        {
            _session?.Dispose(); _session = _lua.BeginSession(UiSetup);
            _observe.SetValue(null, new Func<string, List<string>>(Observe));
            return (int)Call("ApproachCases")!;
        }

        internal int VerifyButtonBoundary()
        {
            var samples = new (string Name, string State, int Continue, int Reward)[] {
                ("reward panel submits once", "qvisible=true;reward=true;choiceCount=0", 0, 1),
                ("progress panel advances once", "qvisible=true;reward=false;choiceCount=0", 1, 0),
                ("closed dialog", "qvisible=false;reward=true;choiceCount=0", 0, 0),
                ("disabled reward", "qvisible=true;reward=true;choiceCount=0;rewardEnabled=false", 0, 0),
                ("disabled continue", "qvisible=true;reward=false;continueEnabled=false", 0, 0),
                ("missing reward button", "qvisible=true;reward=true;QuestFrameCompleteQuestButton=nil", 0, 0),
                ("missing dialog", "QuestFrame=nil;reward=true;choiceCount=0", 0, 0),
                ("selected reward remains exact", "qvisible=true;reward=true;choiceCount=3;QuestInfoFrame.itemChoice=2", 0, 1),
                ("choice absent cannot complete", "qvisible=true;reward=true;choiceCount=3;QuestInfoFrame.itemChoice=0", 0, 0)
            };
            var errors = new List<string>();
            foreach (var sample in samples)
            {
                _session?.Dispose();
                _session = _lua.BeginSession(UiSetup + "\n" + sample.State);
                _observe.SetValue(null, new Func<string, List<string>>(Observe));
                try
                {
                    var frameType = _assembly.GetType("Styx.Logic.Inventory.Frames.Quest.QuestFrame", true)!;
                    frameType.GetMethod("CompleteQuest")!.Invoke(frameType.GetField("Instance")!.GetValue(null), null);
                    int progress = int.Parse(Observe("return progressRequests")[0]);
                    int rewards = int.Parse(Observe("return requests")[0]);
                    Check(progress == sample.Continue && rewards == sample.Reward,
                        $"expected progress/reward={sample.Continue}/{sample.Reward}, got {progress}/{rewards}");
                }
                catch (Exception error) { errors.Add(sample.Name + ": " + (error.InnerException ?? error).Message); }
            }
            Check(errors.Count == 0, "Quest frame submission boundaries: " + string.Join("; ", errors));
            return samples.Length;
        }

        internal int VerifyMissingNpcBoundaries()
        {
            _session?.Dispose(); _session = _lua.BeginSession(UiSetup);
            _observe.SetValue(null, new Func<string, List<string>>(Observe));
            return (int)Call("MissingNpcCases")!;
        }

        internal int VerifyTransitBoundaries()
        {
            _session?.Dispose(); _session = _lua.BeginSession(UiSetup);
            _observe.SetValue(null, new Func<string, List<string>>(Observe));
            return (int)Call("TransitCases")!;
        }

        private List<string> Observe(string script)
        {
            var result = _session!.Execute(script, _loadSize(script));
            if (result.Load != 0 || result.Call != 0) throw new InvalidOperationException("Actual UI Lua failed: " + result.Error);
            ExecutedRequests.Add(script);
            return result.Values;
        }
        private int ReadInt(string name) => (int)_state.GetMethod("ReadInt")!.Invoke(null, new object[] { name })!;
        private object? Call(string method, params object[] args)
        {
            try { return _driver.GetMethod(method)!.Invoke(null, args); }
            catch (TargetInvocationException error) when (error.InnerException != null)
            { ExceptionDispatchInfo.Capture(error.InnerException).Throw(); throw; }
        }
        public void Dispose()
        {
            try { Call("Stop"); }
            finally { _observe.SetValue(null, null); _session?.Dispose(); _lua.Dispose(); }
        }
    }

    private static string Root()
    {
        for (var path = new DirectoryInfo(AppContext.BaseDirectory); path != null; path = path.Parent)
            if (File.Exists(Path.Combine(path.FullName, "CopilotBuddy.csproj"))) return path.FullName;
        throw new InvalidOperationException("Actual source checkout required");
    }
    private static void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); }

    private const string UiSetup = """
clicks=0; qvisible=false; gossip=false; ready=true; reward=false; accepted=true; completed=false
choiceCount=3; shownQuest=10161; requests=0; progressRequests=0; rewardEnabled=true;continueEnabled=true
QuestFrame={IsVisible=function() return qvisible end,IsShown=function() return qvisible end}
GossipFrame={IsVisible=function() return gossip end}
QuestTitleButton1={IsVisible=function() return false end}
QuestFrameRewardPanel={IsShown=function() return qvisible and reward end}
QuestInfoFrame={chooseItems=true,questLog=false,itemChoice=0}
QuestFrameCompleteButton={}
function QuestFrameCompleteButton:IsVisible() return qvisible and ready and not reward end
function QuestFrameCompleteButton:IsEnabled() return continueEnabled and 1 or nil end
function QuestFrameCompleteButton:Click() if self:IsVisible() and self:IsEnabled() then CompleteQuest() end end
QuestFrameCompleteQuestButton={}
function QuestFrameCompleteQuestButton:IsVisible() return qvisible and reward end
function QuestFrameCompleteQuestButton:IsEnabled() return rewardEnabled and 1 or nil end
function QuestFrameCompleteQuestButton:Click()
 if self:IsVisible() and self:IsEnabled() and (choiceCount==0 or QuestInfoFrame.itemChoice>0) then requests=requests+1 end
end
function CompleteQuest() progressRequests=progressRequests+1;if qvisible and ready and not reward then reward=true end end
function CloseQuest() qvisible=false end
function CloseGossip() gossip=false end
function GetNumQuestChoices() return choiceCount end
function GetTitleText() return "Controlled reward" end
QuestFrameAcceptButton={IsVisible=function() return false end}
local ids={25981,25980,25979}
function GetQuestItemLink(kind,i) if kind~='choice' or not ids[i] then error('invalid live reward slot') end return '|Hitem:'..ids[i]..':0:0:0:0:0:0:0|h[Controlled price]|h' end
function GetQuestItemInfo(kind,i) if kind~='choice' or not ids[i] then error('invalid reward metadata') end return 'Observed',nil,1 end
for i=1,3 do
 local b={type='choice',id=i}
 function b:IsShown() return qvisible and reward and self.id<=choiceCount end
 function b:GetID() return self.id end
 function b:Click() clicks=clicks+1;QuestInfoFrame.itemChoice=self.id end
 _G['QuestInfoItem'..i]=b
end
""";

    private const string ControlledWorld = """
#nullable disable
using System;using System.Linq;using System.Collections.Generic;using System.Globalization;
using TreeSharp;using Styx;using Styx.WoWInternals;using Styx.WoWInternals.WoWObjects;
using Styx.Logic.POI;using Styx.Logic.Profiles.Quest;using Styx.Logic.Pathing;using Bots.Quest.QuestOrder;
public static class Logging {public static void Write(string format,params object[] args){}public static void WriteDebug(string format,params object[] args){}public static void WriteDiagnostic(string format,params object[] args)=>TurnInState.Log(format,args);public static void WriteException(Exception error)=>TurnInState.Errors.Add(error);}
public static class TurnInState {
 public static int Moves,Interactions,Clears; public static readonly List<Exception> Errors=new();
 public static System.Action<string> LogCallback;public static void Log(string format,object[] args){LogCallback?.Invoke(string.Format(format,args));}
 public static uint Quest=10161;public static WoWUnit Npc;public static bool NeedQuestItem,Mounted,Flying;public static bool Sight=true;
 public static int ReadInt(string name)=>name switch {
  "Moves"=>Moves,"Interactions"=>Interactions,"Clears"=>Clears,"Map"=>(int)StyxWoW.Me.MapId,
  "CompletionRequests"=>Lua.GetReturnVal<int>("return requests",0),
  "SelectedChoice"=>Lua.GetReturnVal<int>("return QuestInfoFrame.itemChoice",0),_=>throw new InvalidOperationException(name)};
}
public static class TurnInDriver {
 static ForcedQuestTurnIn owner; static Composite tree;
 public static int TransitCases(){
  Reset(10286,20159,-689.583f,4167.8f,58.5228f,530);Tick();Arrive();TurnInState.Npc=null;Tick();
  var flags=System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic;
  var search=typeof(ForcedQuestTurnIn).GetField("relationSearch",flags).GetValue(owner);
  search.GetType().GetField("_started",flags).SetValue(search,Environment.TickCount64/1000.0-3);
  GroundTransition.Created=GroundTransition.Cancelled=0;Tick();
  if(GroundTransition.Created!=1)throw new InvalidOperationException("patrol did not create its first travel owner");
  for(int i=0;i<5;i++){StyxWoW.Me.Location=GroundTransition.LastDestination;Tick();}
  if(GroundTransition.Created!=1||GroundTransition.Cancelled!=0)throw new InvalidOperationException("patrol waypoint cancelled and recreated its travel owner");
  Stop();Console.WriteLine("PASS complete turn-in transit: one owner across six waypoints");return 1;
 }
 public static void Reset(uint quest,uint ender,float x,float y,float z,uint map){Stop();TurnInState.Moves=TurnInState.Interactions=TurnInState.Clears=0;TurnInState.Errors.Clear();TurnInState.Quest=quest;TurnInState.NeedQuestItem=false;BotPoi.ResidualObject=null;Styx.Logic.Inventory.Frames.LootFrame.LootFrame.Instance.LootingObjectGuid=0;Styx.Logic.Profiles.ProfileManager.CurrentProfileSnapshot=new object();
  TurnInState.Mounted=TurnInState.Flying=false;TurnInState.Sight=true;
  StyxWoW.Me=new LocalPlayer{Guid=1,MapId=map,Location=new WoWPoint(x-30,y,z)};WoWMovement.ActiveMover=StyxWoW.Me;
  TurnInState.Npc=new WoWUnit{Guid=77,Entry=ender,Location=new WoWPoint(x,y,z)};BotPoi.Current=new BotPoi(PoiType.None);
  Styx.Logic.BehaviorTree.TreeRoot.IsRunning=true;Styx.Logic.BehaviorTree.TreeRoot.RunIdentity=new object();
  owner=new ForcedQuestTurnIn(quest,"In Case of Emergency...",ender,"Screed",new WoWPoint(x,y,z),QuestObjectType.Npc);Bots.Quest.QuestState.Instance.Order.CurrentBehavior=owner;Bots.Quest.QuestState.Instance.Order.CurrentNode=new TurnInNode(new WoWPoint(x,y,z),ender,"Screed",QuestObjectType.Npc,quest,"In Case of Emergency...");tree=owner.Branch;tree.Start(null);
 }
 public static string Tick(){if(tree.LastStatus.HasValue&&tree.LastStatus!=RunStatus.Running)tree.Start(null);var result=tree.Tick(null);if(TurnInState.Errors.Count>0)throw new InvalidOperationException("Production tree swallowed an error",TurnInState.Errors[0]);return result.ToString();}
 public static int ApproachCases(){
  int count=0;var failures=new List<string>();
  foreach(bool pickup in new[]{false,true})foreach(string condition in new[]{"mounted","flying","wall"}){
   string name=(pickup?"Pickup":"TurnIn")+" close NPC / "+condition;
   Reset(10161,19367,12,10,10,530);Lua.DoString("qvisible=false;gossip=false;requests=0;accepted=true;completed=false");
   ForcedQuestPickUp pick=null;
   try{
    if(pickup){tree.Stop(null);pick=new ForcedQuestPickUp{QuestId=10161,GiverId=19367,GiverType=QuestObjectType.Npc,GiverLocation=TurnInState.Npc.Location,GiverName="Controlled giver"};Bots.Quest.QuestState.Instance.Order.CurrentBehavior=pick;Bots.Quest.QuestState.Instance.Order.CurrentNode=new PickUpNode(TurnInState.Npc.Location,19367,"Controlled giver",QuestObjectType.Npc,10161,"Controlled pickup");BotPoi.Current=new BotPoi((PickUpNode)Bots.Quest.QuestState.Instance.Order.CurrentNode);tree=pick.Branch;tree.Start(null);}
    else Tick();
    Arrive();TurnInState.Mounted=condition=="mounted"||condition=="flying";TurnInState.Flying=condition=="flying";TurnInState.Sight=condition!="wall";
    for(int i=0;i<2;i++)Tick();
    if(TurnInState.Interactions!=0||TurnInState.Moves==0)throw new InvalidOperationException("nearby unsafe subject bypassed the ground approach; interactions="+TurnInState.Interactions+" moves="+TurnInState.Moves);
    TurnInState.Mounted=TurnInState.Flying=false;TurnInState.Sight=true;
    for(int i=0;i<4&&TurnInState.Interactions==0;i++)Tick();
    if(TurnInState.Interactions!=1||TurnInState.ReadInt("CompletionRequests")!=0)throw new InvalidOperationException("observed approach did not admit exactly one interaction before UI acknowledgement");
    count++;Console.WriteLine("PASS quest approach: "+name);
   }catch(Exception error){failures.Add(name+": "+error.Message);Console.Error.WriteLine("FAIL quest approach: "+failures.Last());}finally{Stop();pick?.Dispose();}
  }
  if(failures.Count>0)throw new InvalidOperationException("Quest approach regressions: "+count+"/"+(count+failures.Count)+"; "+string.Join("; ",failures));return count;
 }
 public static int HandoffCases(){
  int count=0;
  void Check(bool good,string reason){if(!good)throw new InvalidOperationException("Handoff: "+reason);}
  void Case(string name,System.Action action){Reset(10161,19367,12,10,10,530);BotPoi.ResidualObject=new WoWUnit{Guid=88,Entry=123,Location=new WoWPoint(10,10,10)};BotPoi.Current=new BotPoi(PoiType.Loot){Guid=88};try{action();count++;Console.WriteLine("PASS mandatory loot handoff: "+name);}finally{Stop();}}
  void Age(long milliseconds){var type=typeof(Bots.Quest.QuestLootHandoff);var value=type.GetField("drain",System.Reflection.BindingFlags.Static|System.Reflection.BindingFlags.NonPublic).GetValue(null);value.GetType().GetField("Started",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).SetValue(value,Environment.TickCount64-milliseconds);}
  bool CanRun()=>Bots.Quest.QuestLootHandoff.CanRunMandatory(owner);
  Case("ordinary corpse gets bounded grace and remains requeueable",()=>{Check(!CanRun(),"fresh corpse skipped grace");Age(5000);Check(CanRun()&&BotPoi.Current.Type==PoiType.None,"incidental corpse exceeded grace");Check(TurnInState.Interactions==0,"handoff fabricated loot interaction");});
  Case("known required quest source receives longer bounded drain",()=>{TurnInState.NeedQuestItem=true;((WoWUnit)BotPoi.ResidualObject).QuestItems=new uint[]{123};Check(!CanRun(),"required loot skipped");Age(5000);Check(!CanRun(),"required loot used incidental budget");Age(11000);Check(CanRun(),"required loot starved mandatory stage indefinitely");});
  Case("completion during corpse ownership shortens required drain",()=>{TurnInState.NeedQuestItem=true;((WoWUnit)BotPoi.ResidualObject).QuestItems=new uint[]{123};Check(!CanRun(),"required loot skipped");Age(5000);TurnInState.NeedQuestItem=false;Check(CanRun(),"completed objective kept required-loot priority");});
  Case("delayed lootability cannot repeatedly restart stage budget",()=>{Check(!CanRun(),"initial grace missing");for(int i=0;i<3;i++)Check(!CanRun(),"grace ended early");Age(5000);Check(CanRun(),"unchanged corpse restarted budget");});
  Case("existing matching loot window is closed once after drain and acknowledged",()=>{var frame=Styx.Logic.Inventory.Frames.LootFrame.LootFrame.Instance;frame.LootingObjectGuid=88;Check(!CanRun(),"open frame preempted early");Age(5000);Check(!CanRun()&&frame.LootingObjectGuid==0,"close request treated as observed handoff");Check(CanRun(),"observed closed frame did not yield");});
  Case("another window is never closed by old corpse handoff",()=>{var frame=Styx.Logic.Inventory.Frames.LootFrame.LootFrame.Instance;frame.LootingObjectGuid=99;Check(!CanRun(),"fresh grace skipped");Age(5000);Check(CanRun()&&frame.LootingObjectGuid==99,"foreign loot window closed");});
  Case("new combat prevents residual POI mutation",()=>{Check(!CanRun(),"missing grace");Age(5000);StyxWoW.Me.Combat=true;Check(!CanRun()&&BotPoi.Current.Type==PoiType.Loot,"combat owner was overwritten");});
  foreach(bool flightForm in new[]{false,true}){var form=flightForm;Case("mounted mandatory travel survives incidental combat, flightForm="+form,()=>{
   BotPoi.Current=new BotPoi((TurnInNode)Bots.Quest.QuestState.Instance.Order.CurrentNode);
   StyxWoW.Me.Combat=true;TurnInState.Mounted=!form;TurnInState.Flying=form;
   Check(CanRun(),"selected mounted quest journey lost admission to incidental combat");
   Check(TurnInState.Interactions==0&&TurnInState.ReadInt("CompletionRequests")==0,"travel admission fabricated interaction or quest completion");
   TurnInState.Mounted=TurnInState.Flying=false;
   Check(!CanRun(),"observed unmount did not yield mandatory work to combat");
  });}
  Case("stale generation cannot drain replacement corpse",()=>{Check(!CanRun(),"missing grace");Age(5000);BotPoi.CurrentGeneration++;Check(!CanRun()&&BotPoi.Current.Type==PoiType.Loot,"replacement inherited expired timer");});
  Case("profile replacement starts independent grace",()=>{Check(!CanRun(),"missing grace");Age(5000);Styx.Logic.Profiles.ProfileManager.CurrentProfileSnapshot=new object();Check(!CanRun(),"profile replacement inherited timer");});
  Case("same player wrapper base replacement starts independent grace",()=>{Check(!CanRun(),"missing grace");Age(5000);StyxWoW.Me.BaseAddress+=4096;Check(!CanRun()&&BotPoi.Current.Type==PoiType.Loot,"same-wrapper base replacement inherited expired timer");});
  Case("same memory wrapper process replacement starts independent grace",()=>{Check(!CanRun(),"missing grace");Age(5000);ObjectManager.Wow.ProcessId++;Check(!CanRun()&&BotPoi.Current.Type==PoiType.Loot,"same-memory process replacement inherited expired timer");});
  Case("cancelled selected stage cannot clear an old corpse",()=>{Check(!CanRun(),"missing grace");Age(5000);Bots.Quest.QuestState.Instance.Order.CurrentNode=null;Bots.Quest.QuestState.Instance.Order.CurrentBehavior=null;Check(!CanRun()&&BotPoi.Current.Type==PoiType.Loot,"cancelled owner cleared POI");});
  Case("Stop revokes pending handoff",()=>{Check(!CanRun(),"missing grace");Age(5000);Styx.Logic.BehaviorTree.TreeRoot.IsRunning=false;Check(!CanRun()&&BotPoi.Current.Type==PoiType.Loot,"stopped owner cleared POI");});
  foreach(var kind in new[]{PoiType.Skin,PoiType.Harvest}){var selected=kind;Case(selected+" also yields bounded incidental work",()=>{BotPoi.Current.Type=selected;Check(!CanRun(),"initial grace missing");Age(5000);Check(CanRun(),"incidental work remained veto");});}
  Case("root loot gate revokes running child at deadline",()=>{int ticks=0;var gate=new Bots.Quest.QuestLootHandoff(new TreeSharp.Action(_=>{ticks++;return RunStatus.Running;}));gate.Start(null);try{Check(gate.Tick(null)==RunStatus.Running&&ticks==1,"loot child did not run in grace");Age(5000);Check(gate.Tick(null)==RunStatus.Failure&&ticks==1,"expired running loot child continued");}finally{gate.Stop(null);}});
  Case("selected mandatory stage cannot acquire new incidental loot",()=>{BotPoi.Current=new BotPoi(PoiType.None);int ticks=0;var gate=new Bots.Quest.QuestLootHandoff(new TreeSharp.Action(_=>{ticks++;return RunStatus.Success;}));gate.Start(null);try{Check(gate.Tick(null)==RunStatus.Failure&&ticks==0,"mandatory stage acquired fresh incidental loot");}finally{gate.Stop(null);}});
  return count;
 }
 public static int MissingNpcCases(){
  Reset(10161,19367,12,10,10,530);Lua.DoString("qvisible=false;gossip=false;requests=0;accepted=true;completed=false");
  try {
   Tick();Arrive();var npc=TurnInState.Npc;var poi=BotPoi.Current;TurnInState.Npc=null;
   if(Tick()!="Running"||!ReferenceEquals(BotPoi.Current,poi)||owner.IsDone||TurnInState.Interactions!=0)
    throw new InvalidOperationException("missing turn-in NPC at its stored endpoint fell through instead of retaining bounded search ownership");
   TurnInState.Npc=npc;
   for(int i=0;i<8&&TurnInState.Interactions==0;i++)Tick();
   if(TurnInState.Interactions!=1||TurnInState.ReadInt("CompletionRequests")!=0)
    throw new InvalidOperationException("a returning NPC did not reacquire exact interaction without inventing turn-in completion");
   Console.WriteLine("PASS missing turn-in: absent NPC retains ownership and returning NPC resumes exact interaction");return 1;
  } finally {Stop();}
 }
 public static int ReviewCases(){
  int count=0;var failures=new List<string>();
  void Check(bool good,string why){if(!good)throw new InvalidOperationException(why);}
  void Case(string name,System.Action test){Reset(10161,19367,12,10,10,530);Lua.DoString("accepted=true;completed=false;shownQuest=10161;qvisible=true;reward=true;choiceCount=0;requests=0");try{test();count++;Console.WriteLine("PASS turn-in review: "+name);}catch(Exception error){failures.Add(name+": "+error.Message);Console.Error.WriteLine("FAIL turn-in review: "+failures.Last());}finally{TurnInState.LogCallback=null;Stop();}}
  foreach(string mode in new[]{"run","actor","cancel","shown-quest","profile"}){var mutation=mode;Case("submission callback revokes "+mutation,()=>{
   TurnInState.LogCallback=message=>{if(!message.Contains("state=Submitted"))return;TurnInState.LogCallback=null;if(mutation=="run")Styx.Logic.BehaviorTree.TreeRoot.RunIdentity=new object();if(mutation=="actor")StyxWoW.Me=new LocalPlayer{Guid=2};if(mutation=="cancel")Styx.Logic.Questing.QuestTurnInCompletion.Find(10161).Cancel(owner);if(mutation=="shown-quest")Lua.DoString("shownQuest=9349");if(mutation=="profile")Styx.Logic.Profiles.ProfileManager.CurrentProfileSnapshot=new object();};
   typeof(ForcedQuestTurnIn).GetMethod("CompleteQuest",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).Invoke(owner,new object[]{null});Check(TurnInState.ReadInt("CompletionRequests")==0,"obsolete owner submitted reward");
  });}
  Case("yield diagnostic cannot clear replacement POI",()=>{BotPoi.ResidualObject=new WoWUnit{Guid=88,Entry=123};BotPoi.Current=new BotPoi(PoiType.Loot){Guid=88};Check(!Bots.Quest.QuestLootHandoff.CanRunMandatory(owner),"grace absent");var d=typeof(Bots.Quest.QuestLootHandoff).GetField("drain",System.Reflection.BindingFlags.Static|System.Reflection.BindingFlags.NonPublic).GetValue(null);d.GetType().GetField("Started",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).SetValue(d,Environment.TickCount64-5000);var replacement=new BotPoi(PoiType.Kill){Guid=999};TurnInState.LogCallback=message=>{if(message.Contains("[QuestLootHandoff] yielding")){TurnInState.LogCallback=null;BotPoi.Current=replacement;BotPoi.CurrentGeneration++;}};Check(!Bots.Quest.QuestLootHandoff.CanRunMandatory(owner)&&ReferenceEquals(BotPoi.Current,replacement),"obsolete drain consumed replacement POI");});
  Case("same NPC Pickup reward obtains confirmation fence and submits once",()=>{var pickup=new ForcedQuestPickUp{QuestId=9349,QuestName="Other legitimate pickup",GiverId=19367};pickup.TickDialog();Check(TurnInState.ReadInt("CompletionRequests")==1,"known completed other quest was not submitted");var record=Styx.Logic.Questing.QuestTurnInCompletion.Find(10161);Check(record!=null&&record.BlocksPickup,"alternate reward path omitted per-quest fence");pickup.TickDialog();Check(TurnInState.ReadInt("CompletionRequests")==1,"pending alternate reward repeated submission");Lua.DoString("accepted=false;qvisible=false;reward=false");Check(record.Observe()!=Styx.Logic.Questing.QuestTurnInCompletionState.Confirmed&&record.BlocksPickup,"departure fabricated alternate reward completion");Lua.DoString("completed=true");Check(record.Observe()==Styx.Logic.Questing.QuestTurnInCompletionState.Confirmed,"fresh alternate reward history not acknowledged");});
  Case("repeatable TurnIn reward is once-owned while outcome is unknown",()=>{Styx.Logic.Questing.QuestTurnInCompletion.SetRepeatableQuestIds(new uint[]{10161});typeof(ForcedQuestTurnIn).GetMethod("CompleteQuest",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).Invoke(owner,new object[]{null});typeof(ForcedQuestTurnIn).GetMethod("CompleteQuest",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).Invoke(owner,new object[]{null});Check(TurnInState.ReadInt("CompletionRequests")==1,"repeatable TurnIn repeated reward while outcome remained unknown");});
  Case("repeatable alternate Pickup reward is once-owned while outcome is unknown",()=>{Styx.Logic.Questing.QuestTurnInCompletion.SetRepeatableQuestIds(new uint[]{10161});var pickup=new ForcedQuestPickUp{QuestId=9349,QuestName="Other legitimate pickup",GiverId=19367};pickup.TickDialog();pickup.TickDialog();Check(TurnInState.ReadInt("CompletionRequests")==1,"repeatable alternate reward repeated while outcome remained unknown");});
  Case("alternate Pickup submission callback revokes profile replacement",()=>{var pickup=new ForcedQuestPickUp{QuestId=9349,QuestName="Other legitimate pickup",GiverId=19367};TurnInState.LogCallback=message=>{if(!message.Contains("state=Submitted"))return;TurnInState.LogCallback=null;Styx.Logic.Profiles.ProfileManager.CurrentProfileSnapshot=new object();};pickup.TickDialog();Check(TurnInState.ReadInt("CompletionRequests")==0,"obsolete alternate reward owner submitted after profile replacement");});
  if(failures.Count>0)throw new InvalidOperationException("Turn-in review regressions: "+count+"/"+(count+failures.Count)+"; "+string.Join("; ",failures));return count;
 }
 public static void ResidualLoot(){BotPoi.Current=new BotPoi(PoiType.Loot);}
 public static void Arrive(){StyxWoW.Me.Location=TurnInState.Npc.Location;}
 public static bool IsDone()=>owner.IsDone;
 public static void Stop(){tree?.Stop(null);owner?.Dispose();tree=null;owner=null;}
}
/* Controlled memory boundary. */ namespace GreenMagic {public class Memory {public int ProcessId=1;public T Read<T>(uint address){uint value=address==12637788U?Lua.GetReturnVal<uint>("return shownQuest",0):0U;return (T)(object)value;}}}
/* Controlled world/time boundary. */ namespace Styx {public static class StyxWoW {public static LocalPlayer Me;public static bool IsInGame=>true;public static void Sleep(int milliseconds){}public static void ResetAfk(){}}}
/* Controlled transport boundary. */ namespace Styx.WoWInternals {
 public static class ObjectManager {public static LocalPlayer Me=>StyxWoW.Me;public static GreenMagic.Memory Wow=new();}
 public static class Lua {
  public static T GetReturnVal<T>(string code,uint index)=>RewardRecordedBridge.GetReturnVal<T>(code,index);
  public static void DoString(string code,params object[] args)=>RewardRecordedBridge.Observe(args.Length==0?code:string.Format(CultureInfo.InvariantCulture,code,args));
 }
 public static class WoWMovement {public static WoWUnit ActiveMover;public static void MoveStop(){StyxWoW.Me.IsMoving=false;}}
}
/* Controlled loaded-world observations. */ namespace Styx.WoWInternals.WoWObjects {
 public class WoWObject {public uint BaseAddress=4096;public ulong Guid;public uint Entry;public WoWPoint Location;public bool IsValid=true;
  public bool WithinInteractRange=>Location.DistanceSqr(StyxWoW.Me.Location)<16;public WoWUnit ToUnit()=>this as WoWUnit;
  public void Interact(){TurnInState.Interactions++;} }
 public class WoWUnit:WoWObject {public bool IsAlive=true;public uint[] QuestItems=Array.Empty<uint>();public bool GetCachedInfo(out CacheInfo info){info=new(){QuestItems=QuestItems};return true;}public void Target(){StyxWoW.Me.CurrentTarget=this;}}
 public class CacheInfo {public uint[] QuestItems=Array.Empty<uint>();}
 public class WoWGameObject:WoWObject {public uint[] QuestItems=Array.Empty<uint>();public bool GetCachedInfo(out CacheInfo info){info=new(){QuestItems=QuestItems};return true;}}
 public class WoWItem:WoWObject {}
 public class LocalPlayer:WoWUnit {public uint MapId;public bool Combat;public bool OnTaxi,IsOnTransport,IsCasting,IsMoving;public uint ChanneledCastingSpellId;
  public bool Mounted=>TurnInState.Mounted;public ShapeshiftForm Shapeshift=>TurnInState.Flying?ShapeshiftForm.FlightForm:(ShapeshiftForm)0;
  public string Name="turnin-fixture",RealmName="realm";
  public WoWUnit CurrentTarget;public bool GotTarget=>CurrentTarget!=null;public ulong CurrentTargetGuid=>CurrentTarget?.Guid??0;public Styx.Logic.Questing.QuestLog QuestLog=new();
  public List<WoWItem> CarriedItems=new();
  public void ClearTarget(){CurrentTarget=null;}public bool CanEquipItem(ItemInfo item)=>false; }
 public class ItemInfo {public uint Id;public string Name=>"Observed reward "+Id;public int SellPrice=>Id==25979?30:Id==25980?20:10;public static ItemInfo FromId(uint id)=>new(){Id=id};}
 public class ItemStats{public ItemStats(string link){}}
}
/* Controlled logging boundary. */ namespace Styx.Helpers {public static class Logging {public static void Write(string format,params object[] args){}public static void WriteDebug(string format,params object[] args){}public static void WriteDiagnostic(string format,params object[] args)=>TurnInState.Log(format,args);public static void WriteException(Exception e)=>TurnInState.Errors.Add(e);}}
/* Controlled status sink. */ namespace Styx.Logic.BehaviorTree {public static class TreeRoot{public static string GoalText{get;set;}public static bool IsRunning=true;public static object RunIdentity=new object();}}
/* Shared optional-observation boundary, independently tested by recovery adapter tests. */ namespace Styx.Logic.Combat {public static class RecoveryActions {public static void ReportDeferral(Exception error,string owner){if(error is OperationCanceledException||error is System.Threading.ThreadInterruptedException)throw error;}}}
/* Source-bound observed quest metadata. */ namespace Styx.Logic.Questing {
 public enum QuestCompletionState {Unknown,KnownIncomplete,KnownComplete}
 public enum CompletedQuestCacheStatus {Unknown,Valid,RefreshFailed}
 public sealed class CompletedQuestHistoryObservation {
  public string Identity="turnin-fixture\u001frealm";public long Generation,Revision=1;public DateTime ObservedUtc=DateTime.UtcNow.AddMinutes(-1);
  public CompletedQuestCacheStatus Status=CompletedQuestCacheStatus.Valid;public List<uint> QuestIds=new();
  public bool IsAuthoritativeAfter(long generation)=>Status==CompletedQuestCacheStatus.Valid&&Generation>=generation;
 }
 public class QuestLogSnapshot {public bool IsIdentityComplete=true,IsComplete=true;public List<uint> AcceptedQuestIds=new(),ReadyQuestIds=new(),FailedQuestIds=new();public List<PlayerQuest> Quests=new();}
 public class QuestLog {
  public static void InvalidateCompletedQuestHistory(){var log=ObjectManager.Me?.QuestLog;if(log!=null){log.requested++;log.history.Status=CompletedQuestCacheStatus.Unknown;}}
  long requested;CompletedQuestHistoryObservation history=new();
  public QuestCompletionState GetQuestCompletionState(uint quest)=>ContainsQuest(quest)&&Lua.GetReturnVal<int>("return ready and 1 or 0",0)==1?QuestCompletionState.KnownComplete:QuestCompletionState.KnownIncomplete;
  public bool ContainsQuest(uint quest)=>quest==TurnInState.Quest&&Lua.GetReturnVal<int>("return accepted and 1 or 0",0)==1;
  public PlayerQuest GetQuestById(uint quest)=>ContainsQuest(quest)?new():null;
  public QuestLogSnapshot CaptureSnapshot()=>new(){AcceptedQuestIds=ContainsQuest(TurnInState.Quest)?new(){TurnInState.Quest}:new(),Quests=new(){new PlayerQuest{CollectItemIds=TurnInState.NeedQuestItem?new[]{123}:Array.Empty<int>()}},ReadyQuestIds=TurnInState.NeedQuestItem?new():new(){TurnInState.Quest}};
  public bool IsSnapshotCurrent(QuestLogSnapshot snapshot)=>snapshot.IsIdentityComplete;
  public long RequestCompletedQuestHistoryRefresh()=>++requested;
  public CompletedQuestHistoryObservation CaptureCompletedQuestHistory(){
   if(Lua.GetReturnVal<int>("return completed and 1 or 0",0)==1&&requested>history.Generation)
    history=new(){Generation=requested,Revision=history.Revision+1,ObservedUtc=DateTime.UtcNow,QuestIds=new(){TurnInState.Quest}};
   return history;
  }
 }
 public class Quest {public bool IsDaily,IsWeekly;public string Name=>"In Case of Emergency...";public Data InternalInfo=new();public static Quest FromId(uint id)=>id==TurnInState.Quest?new():null;public class Data {public int[] RewardChoiceItem=>Lua.GetReturnVal<int>("return choiceCount",0)==3?new[]{25981,25980,25979}:Array.Empty<int>();}}
 // Daily descriptor acknowledgement is exercised using real allocated original
 // descriptors by QuestTurnInCompletionRegressionTests. This UI-only boundary
 // does not supply a positive daily observation to any reward decision.
 public sealed class QuestDailySnapshot {public IReadOnlyCollection<uint> QuestIds=>null;public ulong PlayerGuid=>0;public bool IsCurrent()=>false;public static QuestDailySnapshot Capture(LocalPlayer player)=>new();}
 public class PlayerQuest {public uint Id=>TurnInState.Quest;public int[] CollectItemIds=Array.Empty<int>();public bool IsCompleted=>Lua.GetReturnVal<int>("return ready and 1 or 0",0)==1;}
}
/* Controlled owner-facing frame handles. */ namespace Bots.Quest { public static class QuestManager {public static Styx.Logic.Inventory.Frames.Quest.QuestFrame QuestFrame=>Styx.Logic.Inventory.Frames.Quest.QuestFrame.Instance;public static Styx.Logic.Inventory.Frames.Gossip.GossipFrame GossipFrame=>Styx.Logic.Inventory.Frames.Gossip.GossipFrame.Instance;}}
/* Controlled gossip observation and dispatch. */ namespace Styx.Logic.Inventory.Frames.Gossip {
 public class GossipQuestEntry {public int Id,Index;}
 public class GossipFrame:Styx.Logic.Inventory.Frames.Frame {public static readonly GossipFrame Instance=new();public GossipFrame():base("GossipFrame"){}
  public List<GossipQuestEntry> AvailableQuests=>new();
  public List<GossipQuestEntry> ActiveQuests=>new(){new(){Id=(int)TurnInState.Quest,Index=1}};
  public void SelectActiveQuest(int index){if(index!=1)throw new InvalidOperationException("Wrong active quest index");Lua.DoString("gossip=false;qvisible=true");}
  public void Close()=>Lua.DoString("CloseGossip()");}
}
/* Controlled observed reward values. */ namespace Styx.Logic.Inventory {
 public class WeightSetEx {public static readonly WeightSetEx CurrentWeightSet=new();public float EvaluateItem(ItemInfo item,ItemStats stats)=>item.SellPrice;}
 public static class ConsumableVendorPolicy {public static uint ParseItemId(string link){var match=System.Text.RegularExpressions.Regex.Match(link??"",@"item:(\d+)");return match.Success?uint.Parse(match.Groups[1].Value):0;}}
}
/* Controlled published work handle. */ namespace Styx.Logic.POI {
 public enum PoiType{None,Harvest,Skin,Loot,Kill,QuestTurnIn,QuestPickUp,Buy,Sell,Repair,Train,Mail,Fly,InnKeeper}
 public class BotPoi {public static BotPoi Current;public static long CurrentGeneration;public static WoWObject ResidualObject;public PoiType Type;public uint Entry;public ulong Guid;public WoWPoint Location;public TurnInNode AsTurnIn;
  public WoWObject AsObject=>(Type==PoiType.QuestTurnIn||Type==PoiType.QuestPickUp)&&TurnInState.Npc?.Entry==Entry?TurnInState.Npc:Type is PoiType.Loot or PoiType.Skin or PoiType.Harvest?ResidualObject:null;
  public BotPoi(PoiType type){Type=type;}public BotPoi(TurnInNode node){Type=PoiType.QuestTurnIn;AsTurnIn=node;Entry=node.TurnInId;Guid=TurnInState.Npc.Guid;Location=node.TurnInLocation;}
  public BotPoi(PickUpNode node){Type=PoiType.QuestPickUp;Entry=node.GiverId;Guid=TurnInState.Npc.Guid;Location=node.GiverLocation;}
  public static void Clear(string reason){TurnInState.Clears++;Current=new BotPoi(PoiType.None);}}
}
/* Controlled selected work and profile observations. */ namespace Bots.Quest {public class QuestState {public static QuestState Instance=new();public Bots.Quest.QuestOrder.QuestOrder Order=new();}}
/* Controlled conditional order observations and unused pickup type. */ namespace Bots.Quest.QuestOrder {
public class QuestOrder {public static QuestOrder Instance=>Bots.Quest.QuestState.Instance.Order;public object CurrentNode;public ForcedBehavior CurrentBehavior;}
public class ForcedIf:ForcedBehavior {public object IfNode;public QuestOrder ActiveOrder;public override bool IsDone=>false;protected override Composite CreateBehavior()=>null;}
public class ForcedWhile:ForcedBehavior {public object WhileNode;public QuestOrder ActiveOrder;public override bool IsDone=>false;protected override Composite CreateBehavior()=>null;}
// The production pickup tree and native-interaction caller are included above.
// Item/gossip preparation leaves are controlled here; reward ownership remains
// actual production, and ground geometry/acknowledgement has its own integration suite.
public partial class ForcedQuestPickUp:ForcedBehavior {
 public uint QuestId,GiverId;public string QuestName,GiverName;public QuestObjectType? GiverType;public WoWPoint GiverLocation;public bool PickupUnavailable;public override bool IsDone=>false;
 private static LocalPlayer Me=>ObjectManager.Me;private bool ShouldSetPoi(object context)=>false;
 private RunStatus UseQuestItem(WoWItem item)=>RunStatus.Running;private RunStatus CloseFrames(object context)=>RunStatus.Success;
 private bool IsQuestFrameVisible(object context)=>Styx.Logic.Inventory.Frames.Quest.QuestFrame.Instance.IsVisible;
 private bool IsGossipOrQuestListVisible(object context)=>false;private RunStatus SelectAvailableQuest(object context)=>RunStatus.Running;
 private RunStatus ClearTarget(object context)=>RunStatus.Success;
}}
/* Controlled profile epoch. */ namespace Styx.Logic.Profiles {public static class ProfileManager {public static object CurrentProfileSnapshot=new();}}
/* Controlled loot window observation. */ namespace Styx.Logic.Inventory.Frames.LootFrame {public class LootFrame {public static LootFrame Instance=new();public ulong LootingObjectGuid;public void Close(){LootingObjectGuid=0;}}}
/* Controlled navigation dispatch. */ namespace Styx.Logic.Pathing {
 public static class Navigator {public static object NavigationProvider=new();public static MoveResult MoveTo(WoWPoint destination){TurnInState.Moves++;return MoveResult.Moved;}public static RunStatus GetRunStatusFromMoveResult(MoveResult result)=>RunStatus.Success;}
 public static class Flightor {public static void MoveTo(WoWPoint destination){TurnInState.Moves++;}}
 public enum GroundTransitionPurpose{Interaction,Combat,Transit}
 public enum GroundTransitionState{Pending,Ready,Unavailable,Revoked}
 public sealed class GroundTransition{
  public static int Created,Cancelled;public static WoWPoint LastDestination;
  public GroundTransition(GroundTransitionPurpose purpose){Created++;}
  public GroundTransitionState TickTransit(WoWPoint destination,double distance,Func<bool> admitted){if(!double.IsFinite(distance)||distance<destination.Distance(StyxWoW.Me.Location))throw new InvalidOperationException("patrol omitted the remaining journey cost");return Tick(destination,null,admitted);}
  public static bool CanInteractWith(WoWObject subject,Func<bool> admitted=null)=>subject!=null&&!TurnInState.Mounted&&!TurnInState.Flying&&TurnInState.Sight&&subject.WithinInteractRange&&(admitted==null||admitted());
  public static bool TryInteractWith(WoWObject subject,Func<bool> admitted=null,bool ignoreTimer=false){if(!CanInteractWith(subject,admitted))return false;subject.Interact();return admitted==null||admitted();}
  public GroundTransitionState Tick(WoWPoint destination,WoWObject subject,Func<bool> admitted){if(!admitted())return GroundTransitionState.Revoked;LastDestination=destination;if(CanInteractWith(subject,admitted))return GroundTransitionState.Ready;TurnInState.Moves++;return GroundTransitionState.Pending;}
  public void Cancel(){Cancelled++;}
 }
}
""";
}
