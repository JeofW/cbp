using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

// Complete coordinator, ledger, parsers, Lua generators and the existing actual
// spell-observation owner/public target-remapping method. Native dispatch and
// authoritative world replies are controlled external leaves in this fixture.
internal static class RecoveryActionAdapterRegressionTests
{
    [ModuleInitializer]
    internal static void Run()
    {
        string root=Root();
        string[] names={"RecoveryActions.cs","RecoveryActionLedger.cs","RecoveryActionEvidence.cs","RecoveryActionLua.cs"};
        if(!File.Exists(Path.Combine(root,"Styx/Logic/Combat/RecoveryActions.cs")))
            throw new InvalidOperationException("Shared runtime recovery coordinator is absent; actual potion conflict red is retained separately.");
        var manager=CSharpSyntaxTree.ParseText(File.ReadAllText(Path.Combine(root,"Styx/Logic/Combat/SpellManager.cs"))).GetRoot();
        string context=manager.DescendantNodes().OfType<ClassDeclarationSyntax>().Single(c=>c.Identifier.ValueText=="SpellObservationContext").ToFullString();
        string capture=manager.DescendantNodes().OfType<MethodDeclarationSyntax>().Single(m=>m.Identifier.ValueText=="CaptureSpellObservation").ToFullString();
        string cast=manager.DescendantNodes().OfType<MethodDeclarationSyntax>().Single(m=>m.Identifier.ValueText=="Cast"&&m.ParameterList.Parameters.Count==2&&m.ParameterList.Parameters[0].Type!.ToString()=="WoWSpell").ToFullString();
        string spellOwner=ManagerPrefix+context+capture+cast+ManagerSuffix;
        var trees=names.Select(name=>CSharpSyntaxTree.ParseText(File.ReadAllText(Path.Combine(root,"Styx/Logic/Combat",name)))).ToList();
        trees.Add(CSharpSyntaxTree.ParseText(File.ReadAllText(Path.Combine(root,"Styx/Logic/Common/RestSpellFamilies.cs"))));
        trees.Add(CSharpSyntaxTree.ParseText(File.ReadAllText(Path.Combine(root,"Styx/Helpers/ObservationUnavailableException.cs"))));
        trees.Add(CSharpSyntaxTree.ParseText(File.ReadAllText(Path.Combine(root,"Styx/InvalidProcessException.cs"))));
        trees.Add(CSharpSyntaxTree.ParseText(File.ReadAllText(Path.Combine(root,"Styx/InvalidExecutorException.cs"))));
        trees.Add(CSharpSyntaxTree.ParseText(File.ReadAllText(Path.Combine(root,"Styx/Logic/Combat/WoWSpellEffectType.cs"))));
        trees.Add(CSharpSyntaxTree.ParseText(Boundary+spellOwner+Cases));
        var refs=((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator).Select(p=>MetadataReference.CreateFromFile(p));
        var compilation=CSharpCompilation.Create("RecoveryAdapter_"+Guid.NewGuid().ToString("N"),trees,refs,new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using var bytes=new MemoryStream();var result=compilation.Emit(bytes);
        if(!result.Success)throw new InvalidOperationException(string.Join("; ",result.Diagnostics.Where(d=>d.Severity==DiagnosticSeverity.Error)));
        var assembly=Assembly.Load(bytes.ToArray());
        try{assembly.GetType("Styx.Logic.Combat.RecoveryAdapterCases",true)!.GetMethod("Run")!.Invoke(null,null);}
        catch(TargetInvocationException error)when(error.InnerException!=null){ExceptionDispatchInfo.Capture(error.InnerException).Throw();throw;}
    }
    private static string Root(){for(var d=new DirectoryInfo(AppContext.BaseDirectory);d!=null;d=d.Parent)if(File.Exists(Path.Combine(d.FullName,"CopilotBuddy.csproj")))return d.FullName;throw new InvalidOperationException("Tracked source required");}
    private const string Boundary="""
using System;using System.Collections.Generic;using System.Linq;using System.Reflection;using System.Globalization;using System.Text.RegularExpressions;
using Styx;using Styx.Helpers;using Styx.Logic.Combat;using Styx.Logic.BehaviorTree;using Styx.WoWInternals;using Styx.WoWInternals.WoWObjects;
/* controlled client boundary */ namespace Styx {
 public static class StyxWoW {public static LocalPlayer Me;}
 public static class BotEvents {public static event Action<EventArgs> OnBotStopped;public static void Stop()=>OnBotStopped?.Invoke(EventArgs.Empty);}
}
/* controlled diagnostic boundary */ namespace Styx.Helpers {
 public static class Logging {public static void WriteDiagnostic(string s){State.Messages.Add(s);var cb=State.OnLog;State.OnLog=null;cb?.Invoke();}}
 static class ObservationFailureDiagnostics {internal static void Report(ObservationUnavailableException error,string consumer=null){State.ObservationFailures++;}}
}
/* controlled session boundary */ namespace Styx.Logic.BehaviorTree {
 public static class TreeRoot {public static bool IsRunning=true,CurrentThreadIsBotThread=true;public static object Current=new(),RunIdentity=new();public static void VerifyPulseOwner(object bot,bool worker){if(worker&&(!IsRunning||!ReferenceEquals(bot,Current)))throw new OperationCanceledException("controlled Stop");}}
}
/* controlled profile boundary */ namespace Styx.Logic.Profiles {public static class ProfileManager {public static object CurrentProfile=new();}}
/* controlled world boundary */ namespace Styx.WoWInternals.WoWObjects {
 public class WoWUnit {public bool IsValid=true,IsAlive=true;public ulong Guid=1;public uint BaseAddress=4096,MapId;public WoWUnit CurrentTarget;public string Name="Player";public bool TryGetAllAuras(out WoWAuraCollection a,string consumer=""){if(State.AuraMetadataUnknown){a=null;return false;}return TryGetRawAuras(out a,consumer);}public bool TryGetRawAuras(out WoWAuraCollection a,string consumer=""){if(State.AuraUnknown){a=null;return false;}a=new WoWAuraCollection();a.AddRange(State.Auras.Where(x=>x.Target==Guid).Select(x=>new WoWAura{SpellId=x.Spell,CreatorGuid=x.Actor}));return true;}}
 public sealed class LocalPlayer:WoWUnit {}
 public sealed class ItemInfo {public int[] SpellId=new[]{301,0,0,0,0};}
 public sealed class WoWItem {public ulong Guid=2,OwnerGuid=1;public uint Entry=123,BaseAddress=8192;public bool IsValid=true,Usable=true;public ItemInfo ItemInfo=new();public string Name="Potion";
  public bool IsCooldownReady=>State.CooldownKnown&&State.CooldownDuration==0;
  public bool TryUseContainerItem(){var cb=State.BeforeNative;State.BeforeNative=null;cb?.Invoke();string script="use-container-"+Guid;if(!RecoveryActions.BindContainerRequest(Guid,Entry,script))return false;if(State.NoNative)return false;if(!RecoveryActions.BeforeLuaSubmission(script))return false;if(State.GuardRefused){RecoveryActions.ObserveContainerReply(script,false);return false;}State.ItemCalls++;if(State.NativeFailure!=null)throw State.NativeFailure;return !State.UncertainReply;}
 }
}
/* controlled transport boundary */ namespace Styx.WoWInternals {
 public sealed class Memory {public IntPtr ProcessHandle=new IntPtr(1);}
 public static class ObjectManager {public static Memory Wow=new();public static object Executor=new();public static bool IsInGame=true;}
 public static class Lua {
  public static List<string> GetObservedReturnValues(string script){State.Queries++;var cb=State.OnQuery;State.OnQuery=null;cb?.Invoke();if(State.QueryUnknown)throw new ObservationUnavailableException("test-query","controlled unavailable observation");
   string token=Regex.Match(script,@"local token='([0-9a-f]+)'").Groups[1].Value;string id=Regex.Match(script,@"local id=(\d+)").Groups[1].Value;string time=(State.Now/1000d).ToString(CultureInfo.InvariantCulture);string actor="0x"+StyxWoW.Me.Guid.ToString("x16");
   if(script.Contains("GetSpellInfo(id)")){int spell=int.Parse(id);return new(){"recovery-spell",id,actor,State.Spells[spell].Name,State.BadRank?null:"Rank 1",State.Instant?"0":"2500",time};}
   if(script.Contains("GetItemCount(id,false)")){if(!State.CooldownKnown)return new(){"unavailable","cooldown missing"};return new(){"recovery-item",id,actor,State.ItemCount.ToString(),State.CooldownStart.ToString(CultureInfo.InvariantCulture),State.CooldownDuration.ToString(CultureInfo.InvariantCulture),"1",time};}
   if(script.Contains("CreateFrame('Frame')")){State.CollectorToken=token;return new(){"recovery-events",token,time,State.Events.Count.ToString(),"0","0"};}
   if(script.Contains("local after=")){long after=long.Parse(Regex.Match(script,@"local after=(\d+)").Groups[1].Value);var events=State.Events.Where(e=>e.Sequence>after).Take(5).ToArray();var values=new List<string>{"recovery-events",token,time,State.Events.Count.ToString(),State.Lost.ToString(),events.Length.ToString()};foreach(var e in events)values.AddRange(new[]{e.Sequence.ToString(),e.ClientTime.ToString(CultureInfo.InvariantCulture),e.Kind,e.SpellName,e.SpellRank,e.CastId.ToString(),"0x"+e.SourceGuid.ToString("x16"),e.TargetGuid==0?"":"0x"+e.TargetGuid.ToString("x16"),e.SpellId.ToString(),e.Amount.ToString(CultureInfo.InvariantCulture),e.Overheal.ToString(CultureInfo.InvariantCulture)});return values;}
   if(script.Contains("recovery-disposed"))return new(){"recovery-disposed","ok"};throw new InvalidOperationException("Unexpected observed query");
  }
 }
}
/* controlled rest admission (full owner tested separately) */ namespace Styx.Logic.Common {public static class Rest {public static string GetAdmissionDenial(LocalPlayer player,bool requireStationary=true,bool allowQueries=true){if(State.Prepared&&allowQueries)State.NestedPreparedQueries++;return State.RestDenial;}}}
/* controlled metadata boundary */ namespace Styx.Logic.Combat {
 static class Environment {internal static long TickCount64=>State.Now;}
 public static class RoutineManager {public static object Current=new();}
 public sealed class SpellEffect {public WoWSpellEffectType EffectType;}
 public sealed class WoWAura {public int SpellId;public ulong CreatorGuid;}
 public sealed class WoWAuraCollection:List<WoWAura> {}
 public sealed class WoWSpell {public int Id;public string Name;public bool IsValid=true;public uint AttributesEx;public SpellEffect[] SpellEffects=new[]{new SpellEffect(),new SpellEffect(),new SpellEffect()};public static WoWSpell FromId(int id)=>State.Spells.TryGetValue(id,out var s)?s:null;}
 static class State {
  internal static long Now=1000,Lost;internal static int SpellCalls,ItemCalls,Queries,ObservationFailures;internal static ulong NativeTarget;
  internal static bool QueryUnknown,CooldownKnown=true,AuraUnknown,AuraMetadataUnknown,NoNative,UncertainReply,GuardRefused,BadRank,Instant;
  internal static long ItemCount=4;internal static double CooldownStart,CooldownDuration;internal static string CollectorToken;
  internal static Action OnQuery,BeforeNative,OnLog;internal static Exception NativeFailure;internal static string RestDenial;internal static bool Prepared;internal static int NestedPreparedQueries;
  internal static readonly Dictionary<int,WoWSpell> Spells=new();internal static readonly List<RecoveryEvent> Events=new();internal static readonly List<(int Spell,ulong Actor,ulong Target)> Auras=new();internal static readonly List<string> Messages=new();
  internal static void Add(string kind,int spell=101,long counter=42,ulong source=1,ulong target=1){Events.Add(new RecoveryEvent(Events.Count+1,Now/1000d,kind,kind=="HEAL"?"":Spells[spell].Name,kind=="HEAL"?"":"Rank 1",kind=="HEAL"?0:counter,source,kind=="HEAL"?target:0,kind=="HEAL"?spell:0,kind=="HEAL"?500:0,0));}
  internal static WoWSpell Spell(int id,string name,WoWSpellEffectType effect){var s=new WoWSpell{Id=id,Name=name};s.SpellEffects[0].EffectType=effect;Spells[id]=s;return s;}
  internal static void Reset(){TreeRoot.IsRunning=false;RecoveryActions.Pulse();Now=1000;Lost=0;SpellCalls=ItemCalls=Queries=ObservationFailures=0;NativeTarget=0;QueryUnknown=AuraUnknown=NoNative=UncertainReply=GuardRefused=BadRank=Instant=false;CooldownKnown=true;ItemCount=4;CooldownStart=CooldownDuration=0;CollectorToken=null;OnQuery=BeforeNative=OnLog=null;NativeFailure=null;Events.Clear();Auras.Clear();Messages.Clear();Spells.Clear();StyxWoW.Me=new LocalPlayer();StyxWoW.Me.CurrentTarget=new WoWUnit{Guid=9,BaseAddress=12288};ObjectManager.Wow=new();ObjectManager.Executor=new();TreeRoot.Current=new();TreeRoot.RunIdentity=new();TreeRoot.IsRunning=true;RoutineManager.Current=new();Styx.Logic.Profiles.ProfileManager.CurrentProfile=new();SpellManager.Spells.Clear();foreach(var s in new[]{Spell(101,"Holy Light",WoWSpellEffectType.Heal),Spell(102,"Flash of Light",WoWSpellEffectType.Heal),Spell(201,"Divine Protection",WoWSpellEffectType.ApplyAura),Spell(202,"Combo Buff",WoWSpellEffectType.ApplyAura),Spell(401,"Damage",WoWSpellEffectType.SchoolDamage)})SpellManager.Spells[s.Name]=s;Spells[202].AttributesEx=0x10;Spell(301,"Healing Potion",WoWSpellEffectType.Heal);Spell(302,"Restore Mana",WoWSpellEffectType.Energize);Spell(303,"Rejuvenation",WoWSpellEffectType.Heal);}
 }
}
""";
    private const string ManagerPrefix="""
/* actual captured owner follows */ namespace Styx.Logic.Combat {public static class SpellManager {
 public static Dictionary<string,WoWSpell> Spells=new(StringComparer.OrdinalIgnoreCase);
 private static readonly object _cooldownSync=new();private static long _cooldownEpoch;
""";
    private const string ManagerSuffix="""
 private static bool TryCastSpellById(int id,ulong target){var cb=State.BeforeNative;State.BeforeNative=null;cb?.Invoke();if(State.NoNative)return false;if(!RecoveryActions.BeforeSpellSubmission(id,target))return false;State.SpellCalls++;State.NativeTarget=target;if(State.NativeFailure!=null)throw State.NativeFailure;return !State.UncertainReply;}
}}
""";
    private const string Cases="""
/* executable scenarios */ namespace Styx.Logic.Combat {public static class RecoveryAdapterCases {
 private sealed class Failure(string message):Exception(message){}
 private static void Check(bool value,string message){if(!value)throw new Failure(message);}
 private static bool Heal(string name="Holy Light",WoWUnit target=null)=>RecoveryActions.TryCast(name,target??StyxWoW.Me,true,false,"test-heal");
 private static bool Aura(string name="Divine Protection")=>RecoveryActions.TryCast(name,StyxWoW.Me,false,true,"test-aura");
 private static WoWItem Potion(bool mana=false,bool dual=false)=>new WoWItem{ItemInfo=new ItemInfo{SpellId=new[]{dual?303:mana?302:301,0,0,0,0}}};
 private static bool Use(bool mana=false,bool dual=false)=>RecoveryActions.TryUseConsumable(Potion(mana,dual),!mana,mana,"test-item");
 private static bool RestItem(bool drink=false,Func<bool> admission=null){int id=drink?430:433;State.Spell(id,drink?"Drink":"Food",WoWSpellEffectType.ApplyAura);var item=new WoWItem{ItemInfo=new(){SpellId=new[]{id,0,0,0,0}}};return RecoveryActions.TryUseRestConsumable(item,!drink,drink,"rest",admission);}
 public static void Run(){int total=0,passed=0,failed=0,unexpected=0;
  void Case(string name,Action body){total++;State.Reset();State.AuraMetadataUnknown=false;State.RestDenial=null;State.Prepared=false;State.NestedPreparedQueries=0;try{body();passed++;}catch(Failure error){failed++;Console.Error.WriteLine("FAIL recovery adapter: "+name+": "+error.Message);}catch(Exception error){unexpected++;Console.Error.WriteLine("ERROR recovery adapter: "+name+": "+error);}}
  Case("submitted self heal reserves health",()=>{Check(Heal(),"healthy heal was not submitted");Check(!Use()&&State.SpellCalls==1&&State.ItemCalls==0,"pending self heal spent a health item");});
  Case("rest food requires new owned aura and consumed stack",()=>{Check(RestItem(),"food did not submit");State.ItemCount--;State.Auras.Add((433,1,1));State.Now+=500;RecoveryActions.Pulse();Check(Heal(),"consumed food with owned effect never acknowledged");});
  Case("rest drink is independent of pending self heal",()=>{Check(Heal()&&RestItem(true)&&State.ItemCalls==1,"mana-only rest blocked behind health");});
  Case("rest fresh denial before native request",()=>{State.OnQuery=()=>State.RestDenial="combat";Check(!RestItem()&&State.ItemCalls==0,"combat starting during item query did not revoke rest");});
  Case("rest denied context avoids item queries",()=>{State.RestDenial="moving";Check(!RestItem()&&State.Queries==0&&State.ItemCalls==0,"unsafe rest crossed item observation or dispatch boundary");});
  Case("rest native reentry checks current safety",()=>{State.BeforeNative=()=>State.RestDenial="mounted";Check(!RestItem()&&State.ItemCalls==0,"mount change before native entry ignored");});
  Case("rest prepared item request does not query another native observation",()=>{State.BeforeNative=()=>State.Prepared=true;Check(RestItem()&&State.ItemCalls==1&&State.NestedPreparedQueries==0,"native rest safety queried inside prepared container request");});
  Case("rest configured owner denial avoids queries",()=>Check(!RestItem(admission:()=>false)&&State.Queries==0&&State.ItemCalls==0,"revoked configured owner queried or used item"));
  Case("rest configured owner replaced during query",()=>{bool current=true;State.OnQuery=()=>current=false;Check(!RestItem(admission:()=>current)&&State.ItemCalls==0,"obsolete configured owner dispatched item");});
  Case("rest configured owner replaced at native entry",()=>{bool current=true;State.BeforeNative=()=>current=false;Check(!RestItem(admission:()=>current)&&State.ItemCalls==0,"native entry borrowed obsolete configured owner");});
  Case("rest submitted request alone remains pending",()=>{Check(RestItem(),"food did not submit");State.Now+=500;RecoveryActions.Pulse();Check(!Heal(),"submission acknowledged food");});
  Case("rest quantity alone remains pending",()=>{RestItem();State.ItemCount--;State.Now+=500;RecoveryActions.Pulse();Check(!Heal(),"consumed food lacks aura effect");});
  Case("rest aura alone remains pending",()=>{RestItem();State.Auras.Add((433,1,1));State.Now+=500;RecoveryActions.Pulse();Check(!Heal(),"aura without inventory change acknowledged item");});
  Case("rest foreign aura cannot acknowledge",()=>{RestItem();State.ItemCount--;State.Auras.Add((433,9,1));State.Now+=500;RecoveryActions.Pulse();Check(!Heal(),"foreign aura acknowledged local food");});
  Case("rest raw unknown keeps pending",()=>{RestItem();State.ItemCount--;State.Auras.Add((433,1,1));State.AuraUnknown=true;State.Now+=500;RecoveryActions.Pulse();Check(!Heal(),"raw UNKNOWN acknowledged food");});
  Case("rest unrelated metadata does not hide owned effect",()=>{RestItem();State.ItemCount--;State.Auras.Add((433,1,1));State.Auras.Add((61988,1,1));State.AuraMetadataUnknown=true;State.Now+=500;RecoveryActions.Pulse();Check(Heal(),"unrelated metadata hid owned rest effect");});
  Case("rest preexisting aura is not a new effect",()=>{State.Auras.Add((433,1,1));Check(!RestItem()&&State.ItemCalls==0,"already resting actor consumed duplicate item");});
  Case("different self heal also conflicts",()=>{Heal();Check(!Heal("Flash of Light")&&State.SpellCalls==1,"pending heal was doubled by another spell");});
  Case("submission result does not prove healing",()=>{Heal();State.Now+=3008;RecoveryActions.Pulse();Check(!Use(),"submitted cast was considered landed");});
  Case("unknown post-dispatch result retains reservation",()=>{State.UncertainReply=true;Check(!Heal(),"uncertain return claimed submission success");State.UncertainReply=false;Check(!Use()&&State.ItemCalls==0,"uncertain dispatched heal released health reservation");});
  Case("known absence of native entry releases preparation",()=>{State.NoNative=true;Check(!Heal(),"rejected preparation claimed native submission");State.NoNative=false;Check(Use()&&State.ItemCalls==1,"unsubmitted preparation poisoned next action");});
  Case("ordinary failure after native entry remains pending",()=>{State.NativeFailure=new InvalidOperationException("unknown native result");Check(!Heal(),"failed native return claimed success");State.NativeFailure=null;Check(!Use(),"failed native result was treated unsubmitted");});
  Case("other recipient does not reserve our health",()=>{Check(Heal(target:StyxWoW.Me.CurrentTarget)&&Use(),"group heal blocked independent self recovery");});
  Case("known mana-only item is independent of healing",()=>{Heal();Check(Use(true)&&State.ItemCalls==1,"health heal blocked mana-only recovery");});
  Case("dual-resource mana candidate respects pending healing",()=>{Heal();Check(!Use(true,true)&&State.ItemCalls==0,"dual-resource item bypassed health arbitration");});
  Case("health item reserves against another self heal",()=>{Check(Use(),"item did not submit");Check(!Heal()&&State.SpellCalls==0,"pending health item was doubled by a heal");});
  Case("two resource owners cannot submit overlapping items",()=>{Use();Check(!Use(true)&&State.ItemCalls==1,"health and mana item owners both submitted");});
  Case("matching counter and effect acknowledge a heal",()=>{Heal();State.Add("START");State.Add("SUCCEEDED");State.Add("HEAL");State.Now+=500;RecoveryActions.Pulse();Check(Use()&&State.ItemCalls==1,"complete known heal did not release health reservation");});
  Case("cast success alone is not an effect",()=>{Heal();State.Add("START");State.Add("SUCCEEDED");State.Now+=500;RecoveryActions.Pulse();Check(!Use(),"cast success alone released recovery");});
  Case("late instant success cannot become a new heal",()=>{State.Instant=true;Heal();State.Add("SUCCEEDED");State.Add("HEAL");State.Now+=500;RecoveryActions.Pulse();Check(!Use(),"late instant event was assigned to current request");});
  foreach(string wrong in new[]{"spell","target","actor","counter"})Case("unrelated acknowledgement / "+wrong,()=>{Heal();State.Add("START");State.Add("SUCCEEDED",counter:wrong=="counter"?99:42);State.Add("HEAL",spell:wrong=="spell"?102:101,source:wrong=="actor"?9UL:1UL,target:wrong=="target"?9UL:1UL);State.Now+=500;RecoveryActions.Pulse();Check(!Use(),"unrelated event cleared an in-flight action");});
  Case("matching interruption releases recovery",()=>{Heal();State.Add("START");State.Add("INTERRUPTED");State.Now+=500;RecoveryActions.Pulse();Check(Use(),"known interrupted heal retained reservation");});
  Case("observed event loss does not imply a heal outcome",()=>{Heal();State.Add("START");State.Add("SUCCEEDED");State.Add("HEAL");State.Lost=1;State.Now+=500;RecoveryActions.Pulse();Check(!Use(),"incomplete event coverage was promoted to completion");});
  Case("defensive interval beyond old prevention stays pending",()=>{Check(Aura(),"defensive did not submit");State.Now+=3008;Check(!Aura()&&State.SpellCalls==1,"Divine Protection submitted twice without acknowledgement");});
  Case("known defensive aura is authoritative acknowledgement",()=>{Aura();State.Auras.Add((201,1,1));State.Now+=500;RecoveryActions.Pulse();Check(!Aura()&&State.SpellCalls==1,"already active defensive was resubmitted");});
  Case("unrelated unavailable metadata does not hide exact defensive absence",()=>{State.AuraMetadataUnknown=true;State.Auras.Add((61988,1,1));Check(Aura()&&State.SpellCalls==1,"unrelated missing row blocked a known exact-ID absence");});
  Case("unrelated unavailable metadata does not hide owned defensive acknowledgement",()=>{Check(Aura(),"defensive did not submit");State.AuraMetadataUnknown=true;State.Auras.Add((61988,1,1));State.Auras.Add((201,1,1));State.Now+=500;RecoveryActions.Pulse();Check(State.Messages.Any(m=>m.Contains("state=Acknowledged")),"owned exact aura was hidden by unrelated metadata");});
  Case("metadata-only unknown does not weaken defensive caster ownership",()=>{Check(Aura(),"defensive did not submit");State.AuraMetadataUnknown=true;State.Auras.Add((61988,1,1));State.Auras.Add((201,9,1));State.Now+=500;RecoveryActions.Pulse();Check(!State.Messages.Any(m=>m.Contains("state=Acknowledged")),"foreign caster borrowed ownership through partial metadata");});
  Case("unknown aura coverage never authorizes a defensive",()=>{State.AuraUnknown=true;Check(!Aura()&&State.SpellCalls==0,"unknown aura coverage became absence");});
  Case("existing aura from another caster is not absent coverage",()=>{State.Auras.Add((201,9,1));Check(!Aura()&&State.SpellCalls==0,"other-caster coverage authorized an unnecessary duplicate");});
  Case("existing aura with unavailable caster is not absence",()=>{State.Auras.Add((201,0,1));Check(!Aura()&&State.SpellCalls==0,"unavailable aura caster became absence");});
  Case("another caster cannot acknowledge our pending defensive",()=>{Aura();State.Auras.Add((201,9,1));State.Now+=500;RecoveryActions.Pulse();Check(!State.Messages.Any(m=>m.Contains("state=Acknowledged")),"another caster acknowledged our submission");});
  Case("unknown aura after dispatch keeps the defensive pending",()=>{Aura();State.AuraUnknown=true;State.Now+=3008;Check(!Aura()&&State.SpellCalls==1,"unknown defensive outcome authorized a duplicate");});
  Case("combo native target and aura recipient remain distinct",()=>{Check(Aura("Combo Buff")&&State.NativeTarget==9,"combo native target remapping was broken");State.Auras.Add((202,1,1));State.Now+=500;RecoveryActions.Pulse();Check(!Aura("Combo Buff")&&State.SpellCalls==1,"self aura was attributed to the enemy target");});
  foreach(string partial in new[]{"quantity","cooldown"})Case("partial consumable acknowledgement / "+partial,()=>{Use();if(partial=="quantity")State.ItemCount--;else{State.CooldownStart=1;State.CooldownDuration=120;}State.Now+=500;RecoveryActions.Pulse();Check(!Heal(),"partial item outcome released expected recovery");});
  Case("consumption alone does not prove health recovery landed",()=>{Use();State.ItemCount--;State.CooldownStart=1;State.CooldownDuration=120;State.Now+=500;RecoveryActions.Pulse();Check(!Heal(),"consumed item released health recovery before its effect");});
  Case("consumable quantity cooldown and owned heal acknowledge recovery",()=>{Use();State.ItemCount--;State.CooldownStart=1;State.CooldownDuration=120;State.Add("HEAL",301);State.Now+=500;RecoveryActions.Pulse();Check(Heal(),"complete item outcome did not release recovery");});
  Case("known native item guard rejection releases reservation",()=>{State.GuardRefused=true;Check(!Use()&&State.ItemCalls==0,"known guard rejection claimed use");State.GuardRefused=false;Check(Heal(),"known unexecuted item poisoned health recovery");});
  Case("unknown item cooldown remains nonfatal",()=>{State.CooldownKnown=false;Check(!Use()&&State.ItemCalls==0,"unknown item cooldown authorized use");State.CooldownKnown=true;Check(Use(),"later known observation did not recover");});
  Case("unknown positive item effect is not mana-only",()=>{Heal();var item=Potion(true);item.ItemInfo.SpellId[1]=999;Check(!RecoveryActions.TryUseConsumable(item,false,true,"unknown-effect")&&State.ItemCalls==0,"incomplete effects bypassed health reservation");});
  Case("unlearned spell cannot be inferred from level",()=>Check(!Heal("Unlearned")&&State.SpellCalls==0,"missing trained spell submitted"));
  Case("ID helper retains learned heal identity",()=>{Check(RecoveryActions.TryCast(101,StyxWoW.Me,true,false,"id-heal")&&!Use()&&State.SpellCalls==1,"ID heal bypassed shared recovery ownership");});
  Case("ID helper rejects unlearned and invalid spell identities",()=>{State.Spell(999,"Unlearned",WoWSpellEffectType.Heal);foreach(int id in new[]{0,-1,999})Check(!RecoveryActions.TryCast(id,StyxWoW.Me,true,false,"id-heal"),"ID helper borrowed unlearned metadata");Check(State.SpellCalls==0,"invalid ID reached native dispatch");});
  Case("ordinary damage remains available during health recovery",()=>{Heal();Check(RecoveryActions.TryCast("Damage",StyxWoW.Me.CurrentTarget,false,false,"damage")&&State.SpellCalls==2,"health reservation starved ordinary damage");});
  Case("ordinary resurrection keeps the dead recipient eligible",()=>{var target=StyxWoW.Me.CurrentTarget;target.IsAlive=false;var spell=State.Spell(402,"Resurrection",WoWSpellEffectType.Resurrect);SpellManager.Spells[spell.Name]=spell;Check(RecoveryActions.TryCast(spell.Name,target,false,false,"resurrection")&&State.NativeTarget==9,"health arbitration rejected a distinct resurrection action");});
  Case("ordinary healing cannot target an observed corpse",()=>{var target=StyxWoW.Me.CurrentTarget;target.IsAlive=false;Check(!Heal(target:target)&&State.SpellCalls==0,"dead recipient admitted ordinary healing");});
  Case("metadata query failure is controlled admission",()=>{State.QueryUnknown=true;Check(!Heal()&&!Use(),"unavailable observations authorized action");State.QueryUnknown=false;Check(Heal(),"known query did not recover");});
  Case("missing spell rank is unavailable",()=>{State.BadRank=true;Check(!Heal()&&State.SpellCalls==0,"unknown spell rank became empty/known");});
  foreach(string change in new[]{"actor","run","map","profile","routine","memory"})Case("owner changes before native entry / "+change,()=>{State.BeforeNative=()=>Change(change);bool submitted=false;try{submitted=Heal();}catch(OperationCanceledException){}Check(!submitted&&State.SpellCalls==0,"revoked action reached native entry");});
  foreach(string change in new[]{"actor","run","map","profile","routine","memory"})Case("new owner does not inherit old reservations / "+change,()=>{Heal();Change(change);Check(Use(),"new owner inherited stale action state");});
  Case("Stop and restart discard owned transient actions",()=>{Heal();TreeRoot.IsRunning=false;BotEvents.Stop();RecoveryActions.Pulse();TreeRoot.RunIdentity=new();TreeRoot.IsRunning=true;Check(Use(),"restart inherited an old heal");});
  Case("death releases recovery state without blocking death handler",()=>{Heal();StyxWoW.Me.IsAlive=false;RecoveryActions.Pulse();Check(!Use(),"dead actor submitted recovery");StyxWoW.Me.IsAlive=true;Check(Use(),"resurrection inherited pending heal");});
  Case("bounded unacknowledged action can retry with diagnosis",()=>{Heal();State.Now+=10001;RecoveryActions.Pulse();Check(Use()&&State.Messages.Any(m=>m.Contains("TimedOut")),"unacknowledged action either blocked forever or lost diagnosis");});
  Case("blocked repeated heal admission does not reread Lua every pulse",()=>{Heal();int queries=State.Queries;for(int i=0;i<20;i++)Check(!Heal(),"pending heal allowed another submission");Check(State.Queries==queries&&State.SpellCalls==1,"pending heal repeatedly crossed the native observation boundary");});
  Case("blocked repeated health item admission does not reread Lua every pulse",()=>{Heal();int queries=State.Queries;for(int i=0;i<20;i++)Check(!Use(),"pending heal allowed an item submission");Check(State.Queries==queries&&State.ItemCalls==0,"conflicting item repeatedly crossed the native observation boundary");});
  Case("blocked repeated defensive does not reread Lua metadata",()=>{Aura();int queries=State.Queries;for(int i=0;i<20;i++)Check(!Aura(),"pending defensive resubmitted");Check(State.Queries==queries,"pending defensive repeated metadata reads");});
  Case("native duplicate bypass is denied while scoped action is pending",()=>{Heal();Check(!RecoveryActions.BeforeSpellSubmission(101,1),"raw same-spell duplicate bypassed reservation");});
  foreach(string replacement in new[]{"executor","memory-same-handle","bot"})Case("stale raw duplicate owner does not suppress successor / "+replacement,()=>{Check(Heal(),"initial recovery submission failed");if(replacement=="executor")ObjectManager.Executor=new();else if(replacement=="memory-same-handle")ObjectManager.Wow=new();else TreeRoot.Current=new();Check(SpellManager.Cast(State.Spells[101],StyxWoW.Me)&&State.SpellCalls==2,"stale recovery owner suppressed a current raw native action");});
  Case("stale raw check cannot clear reentrant successor",()=>{Check(Heal(),"initial recovery submission failed");State.BeforeNative=()=>{ObjectManager.Executor=new();Check(RecoveryActions.TryCast("Flash of Light",StyxWoW.Me,true,false,"reentrant-successor"),"reentrant successor did not acquire current owner");};Check(SpellManager.Cast(State.Spells[101],StyxWoW.Me)&&State.SpellCalls==3,"outer raw action did not survive stale-owner replacement");Check(!SpellManager.Cast(State.Spells[102],StyxWoW.Me)&&State.SpellCalls==3,"stale predecessor cleanup erased the reentrant successor reservation");});
  Case("raw combo duplicate retains its native target ownership",()=>{Aura("Combo Buff");Check(!RecoveryActions.BeforeSpellSubmission(202,9),"native enemy target bypassed a pending self-aura reservation");});
  Case("aura arriving during metadata read prevents duplicate native entry",()=>{State.OnQuery=()=>State.Auras.Add((201,1,1));Check(!Aura()&&State.SpellCalls==0,"newly observed aura was ignored after metadata yielded");});
  Case("replaced aura recipient cannot acknowledge an older action",()=>{var target=StyxWoW.Me.CurrentTarget;Check(RecoveryActions.TryCast("Divine Protection",target,false,true,"group-aura"),"group aura control failed");target.Guid=10;State.Auras.Add((201,1,10));State.Now+=500;RecoveryActions.Pulse();Check(!State.Messages.Any(m=>m.Contains("state=Acknowledged")),"replacement recipient acknowledged an older target's action");});
  foreach(bool wrapped in new[]{false,true})Case("native cancellation preserves exact signal / "+wrapped,()=>{var signal=new OperationCanceledException("native cancellation");State.NativeFailure=wrapped?new TargetInvocationException(signal):signal;Exception caught=null;try{Heal();}catch(Exception error){caught=error;}Check(ReferenceEquals(caught,signal),"native cancellation was swallowed or changed");});
  foreach(bool wrapped in new[]{false,true})foreach(bool process in new[]{false,true})Case("fatal ownership failure preserves exact signal / "+process+" / "+wrapped,()=>{Exception signal=process?new InvalidProcessException("process lost"):new InvalidExecutorException("executor lost");State.NativeFailure=wrapped?new TargetInvocationException(signal):signal;Exception caught=null;try{Heal();}catch(Exception error){caught=error;}Check(ReferenceEquals(caught,signal),"fatal ownership loss was swallowed or changed");});
  Case("ordinary diagnostic subscriber failure is nonfatal",()=>{State.OnLog=()=>throw new InvalidOperationException("log subscriber");Check(Heal(),"diagnostic subscriber prevented a valid action");});
  Console.WriteLine($"Recovery runtime adapter cases: {passed}/{total}; assertions={failed}; unexpected={unexpected}; complete coordinator and shared owners with controlled native/world replies; no game.");
  if(failed+unexpected!=0)throw new InvalidOperationException("Recovery runtime adapter regressions failed");
 }
 private static void Change(string what){if(what=="actor")StyxWoW.Me=new LocalPlayer{CurrentTarget=new WoWUnit{Guid=9,BaseAddress=12288}};else if(what=="run")TreeRoot.RunIdentity=new();else if(what=="map")StyxWoW.Me.MapId=530;else if(what=="profile")Styx.Logic.Profiles.ProfileManager.CurrentProfile=new();else if(what=="routine")RoutineManager.Current=new();else if(what=="memory")ObjectManager.Wow=new();}
}}
""";
}
