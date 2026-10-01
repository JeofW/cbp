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

        internal void Execute(XElement generated, bool choices, System.Action acknowledge, System.Action beforeAcknowledgement)
        {
            uint quest = (uint)generated.Attribute("QuestId")!, ender = (uint)generated.Attribute("TurnInId")!;
            Check(quest == 10161 && ender == 19367, "The source-bound fixture must consume the generated quest/ender identity");
            ExecuteBound(generated, choices, quest, ender, acknowledge, beforeAcknowledgement);
        }

        internal void ExecuteBound(XElement generated, bool choices, uint expectedQuest, uint expectedEnder,
            System.Action acknowledge, System.Action beforeAcknowledgement, uint expectedMap = 530)
        {
            uint quest = (uint)generated.Attribute("QuestId")!, ender = (uint)generated.Attribute("TurnInId")!;
            Check(quest != 0 && ender != 0 && quest == expectedQuest && ender == expectedEnder,
                "Generated turn-in differs from the independently supplied source identity");
            ExecutedRequests.Clear();
            _session?.Dispose();
            _session = _lua.BeginSession(UiSetup + "\nchoiceCount=" + (choices ? "3" : "0") + "\nshownQuest=" + quest + "\n");
            _observe.SetValue(null, new Func<string, List<string>>(Observe));
            Call("Reset", quest, ender, (float)generated.Attribute("X")!, (float)generated.Attribute("Y")!, (float)generated.Attribute("Z")!, expectedMap);
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
            Call("Tick");
            Check(ReadInt("Interactions") == 1 && ReadInt("CompletionRequests") == 0, "ender interaction skipped the waiting boundary");
            Check(!(bool)Call("IsDone")!, "ender interaction was treated as quest completion");
            Observe("gossip=true"); // Deliberately separate native interaction acknowledgement.
            Call("Tick");
            Check(ReadInt("CompletionRequests") == 1, "one reward operation must issue exactly one completion request; actual=" + ReadInt("CompletionRequests"));
            Check(ReadInt("SelectedChoice") == (choices ? 3 : 0), "real reward selection did not consume the observed choice set");
            Check(!(bool)Call("IsDone")!, "button submission fabricated a server turn-in acknowledgement");
            beforeAcknowledgement();
            // Only after the actual turn-in request is verified does the test
            // driver supply the server reply, and only then update parent memory.
            Observe("accepted=false; qvisible=false; reward=false; completed=true");
            acknowledge();
            for (int tick = 0; tick < 6 && ReadInt("Clears") == 0; tick++) Call("Tick");
            Check((bool)Call("IsDone")! && ReadInt("Clears") == 1, "acknowledged turn-in did not release its work");
            Check(ReadInt("CompletionRequests") == 1, "cleanup repeated an already acknowledged reward action");
            Check(ReadInt("Interactions") == 1, "turn-in re-interacted after its own frame opened");
            Call("Stop");
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
public static class Logging {public static void Write(string format,params object[] args){}public static void WriteDebug(string format,params object[] args){}public static void WriteException(Exception error)=>TurnInState.Errors.Add(error);}
public static class TurnInState {
 public static int Moves,Interactions,Clears; public static readonly List<Exception> Errors=new();
 public static uint Quest=10161;public static WoWUnit Npc;
 public static int ReadInt(string name)=>name switch {
  "Moves"=>Moves,"Interactions"=>Interactions,"Clears"=>Clears,"Map"=>(int)StyxWoW.Me.MapId,
  "CompletionRequests"=>Lua.GetReturnVal<int>("return requests",0),
  "SelectedChoice"=>Lua.GetReturnVal<int>("return QuestInfoFrame.itemChoice",0),_=>throw new InvalidOperationException(name)};
}
public static class TurnInDriver {
 static ForcedQuestTurnIn owner; static Composite tree;
 public static void Reset(uint quest,uint ender,float x,float y,float z,uint map){Stop();TurnInState.Moves=TurnInState.Interactions=TurnInState.Clears=0;TurnInState.Errors.Clear();TurnInState.Quest=quest;
  StyxWoW.Me=new LocalPlayer{Guid=1,MapId=map,Location=new WoWPoint(x-30,y,z)};WoWMovement.ActiveMover=StyxWoW.Me;
  TurnInState.Npc=new WoWUnit{Guid=77,Entry=ender,Location=new WoWPoint(x,y,z)};BotPoi.Current=new BotPoi(PoiType.None);
  owner=new ForcedQuestTurnIn(quest,"In Case of Emergency...",ender,"Screed",new WoWPoint(x,y,z),QuestObjectType.Npc);tree=owner.Branch;tree.Start(null);
 }
 public static string Tick(){if(tree.LastStatus.HasValue&&tree.LastStatus!=RunStatus.Running)tree.Start(null);var result=tree.Tick(null);if(TurnInState.Errors.Count>0)throw new InvalidOperationException("Production tree swallowed an error",TurnInState.Errors[0]);return result.ToString();}
 public static void Arrive(){StyxWoW.Me.Location=TurnInState.Npc.Location;}
 public static bool IsDone()=>owner.IsDone;
 public static void Stop(){tree?.Stop(null);owner?.Dispose();tree=null;owner=null;}
}
/* Controlled memory boundary. */ namespace GreenMagic {public class Memory {public T Read<T>(uint address){uint value=address==12637788U?Lua.GetReturnVal<uint>("return shownQuest",0):0U;return (T)(object)value;}}}
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
 public class WoWObject {public ulong Guid;public uint Entry;public WoWPoint Location;public bool IsValid=true;
  public bool WithinInteractRange=>Location.DistanceSqr(StyxWoW.Me.Location)<16;public WoWUnit ToUnit()=>this as WoWUnit;
  public void Interact(){TurnInState.Interactions++;} }
 public class WoWUnit:WoWObject {public bool IsAlive=true;public void Target(){StyxWoW.Me.CurrentTarget=this;}}
 public class LocalPlayer:WoWUnit {public uint MapId;public bool OnTaxi,IsOnTransport,IsCasting,IsMoving;public uint ChanneledCastingSpellId;
  public WoWUnit CurrentTarget;public bool GotTarget=>CurrentTarget!=null;public ulong CurrentTargetGuid=>CurrentTarget?.Guid??0;public ObservedQuestLog QuestLog=new();
  public void ClearTarget(){CurrentTarget=null;}public bool CanEquipItem(ItemInfo item)=>false; }
 public class ObservedQuestLog {public bool ContainsQuest(uint quest)=>quest==TurnInState.Quest&&Lua.GetReturnVal<int>("return accepted and 1 or 0",0)==1;
  public Styx.Logic.Questing.PlayerQuest GetQuestById(uint quest)=>ContainsQuest(quest)?new():null;}
 public class ItemInfo {public uint Id;public string Name=>"Observed reward "+Id;public int SellPrice=>Id==25979?30:Id==25980?20:10;public static ItemInfo FromId(uint id)=>new(){Id=id};}
 public class ItemStats{public ItemStats(string link){}}
}
/* Controlled logging boundary. */ namespace Styx.Helpers {public static class Logging {public static void Write(string format,params object[] args){}public static void WriteDebug(string format,params object[] args){}public static void WriteDiagnostic(string format,params object[] args){}public static void WriteException(Exception e)=>TurnInState.Errors.Add(e);}}
/* Controlled status sink. */ namespace Styx.Logic.BehaviorTree {public static class TreeRoot{public static string GoalText{get;set;}}}
/* Source-bound observed quest metadata. */ namespace Styx.Logic.Questing {
 public class Quest {public string Name=>"In Case of Emergency...";public Data InternalInfo=new();public static Quest FromId(uint id)=>id==TurnInState.Quest?new():null;public class Data {public int[] RewardChoiceItem=>Lua.GetReturnVal<int>("return choiceCount",0)==3?new[]{25981,25980,25979}:Array.Empty<int>();}}
 public class PlayerQuest {public bool IsCompleted=>Lua.GetReturnVal<int>("return ready and 1 or 0",0)==1;}
}
/* Controlled owner-facing frame handles. */ namespace Bots.Quest { public static class QuestManager {public static Styx.Logic.Inventory.Frames.Quest.QuestFrame QuestFrame=>Styx.Logic.Inventory.Frames.Quest.QuestFrame.Instance;public static Styx.Logic.Inventory.Frames.Gossip.GossipFrame GossipFrame=>Styx.Logic.Inventory.Frames.Gossip.GossipFrame.Instance;}}
/* Controlled gossip observation and dispatch. */ namespace Styx.Logic.Inventory.Frames.Gossip {
 public class GossipQuestEntry {public int Id,Index;}
 public class GossipFrame:Styx.Logic.Inventory.Frames.Frame {public static readonly GossipFrame Instance=new();public GossipFrame():base("GossipFrame"){}
  public List<GossipQuestEntry> ActiveQuests=>new(){new(){Id=(int)TurnInState.Quest,Index=1}};
  public void SelectActiveQuest(int index){if(index!=1)throw new InvalidOperationException("Wrong active quest index");Lua.DoString("gossip=false;qvisible=true");}
  public void Close()=>Lua.DoString("CloseGossip()");}
}
/* Controlled observed reward values. */ namespace Styx.Logic.Inventory {
 public class WeightSetEx {public static readonly WeightSetEx CurrentWeightSet=new();public float EvaluateItem(ItemInfo item,ItemStats stats)=>item.SellPrice;}
 public static class ConsumableVendorPolicy {public static uint ParseItemId(string link){var match=System.Text.RegularExpressions.Regex.Match(link??"",@"item:(\d+)");return match.Success?uint.Parse(match.Groups[1].Value):0;}}
}
/* Controlled published work handle. */ namespace Styx.Logic.POI {
 public enum PoiType{None,Harvest,Skin,Loot,Kill,QuestTurnIn}
 public class BotPoi {public static BotPoi Current;public PoiType Type;public uint Entry;public ulong Guid;public WoWPoint Location;public TurnInNode AsTurnIn;
  public WoWObject AsObject=>Type==PoiType.QuestTurnIn&&TurnInState.Npc?.Entry==Entry?TurnInState.Npc:null;
  public BotPoi(PoiType type){Type=type;}public BotPoi(TurnInNode node){Type=PoiType.QuestTurnIn;AsTurnIn=node;Entry=node.TurnInId;Guid=TurnInState.Npc.Guid;Location=node.TurnInLocation;}
  public static void Clear(string reason){TurnInState.Clears++;Current=new BotPoi(PoiType.None);}}
}
/* Controlled navigation dispatch. */ namespace Styx.Logic.Pathing {
 public static class Navigator {public static object NavigationProvider=new();public static MoveResult MoveTo(WoWPoint destination){TurnInState.Moves++;return MoveResult.Moved;}public static RunStatus GetRunStatusFromMoveResult(MoveResult result)=>RunStatus.Success;}
 public static class Flightor {public static void MoveTo(WoWPoint destination){TurnInState.Moves++;}}
}
""";
}
