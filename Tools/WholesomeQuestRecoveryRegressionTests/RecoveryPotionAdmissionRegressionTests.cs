using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

// Entire enabled plugin and its policy execute. The coordinator, current-world
// and native item submission replies are controlled API boundaries; the shared
// ledger/event and actual dispatch markers have separate owner regressions.
internal static class RecoveryPotionAdmissionRegressionTests
{
    [ModuleInitializer]
    internal static void Run()
    {
        string root=Root();
        string Class(string path,string name)=>CSharpSyntaxTree.ParseText(File.ReadAllText(Path.Combine(root,path))).GetRoot()
            .DescendantNodes().OfType<ClassDeclarationSyntax>().Single(c=>c.Identifier.ValueText==name).ToFullString();
        string source=Prefix+Class("runtime-snapshot/Plugins/DrinkPotions/DrinkPotions.cs","DrinkPotions")
            +Class("runtime-snapshot/Plugins/DrinkPotions/PotionUsePolicy.cs","PotionUsePolicy")+Boundary+"}";
        var refs=((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator)
            .Append(typeof(Styx.Helpers.ObservationUnavailableException).Assembly.Location).Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(path=>MetadataReference.CreateFromFile(path));
        var compilation=CSharpCompilation.Create("RecoveryPotion_"+Guid.NewGuid().ToString("N"),
            new[]{CSharpSyntaxTree.ParseText(source)},refs,new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using var bytes=new MemoryStream();var result=compilation.Emit(bytes);
        if(!result.Success)throw new InvalidOperationException(string.Join("; ",result.Diagnostics.Where(d=>d.Severity==DiagnosticSeverity.Error)));
        var assembly=Assembly.Load(bytes.ToArray());
        try{assembly.GetType("RecoveryPotionProbe.Cases",true)!.GetMethod("Run")!.Invoke(null,null);}
        catch(TargetInvocationException error)when(error.InnerException!=null){ExceptionDispatchInfo.Capture(error.InnerException).Throw();throw;}
    }
    private static string Root(){for(var dir=new DirectoryInfo(AppContext.BaseDirectory);dir!=null;dir=dir.Parent)if(File.Exists(Path.Combine(dir.FullName,"CopilotBuddy.csproj")))return dir.FullName;throw new InvalidOperationException("Tracked source required");}
    private const string Prefix="""
#nullable disable
using System;using System.Collections.Generic;using System.Linq;using System.Reflection;using System.Drawing;using Styx.Helpers;
using DateTime=RecoveryPotionProbe.TestClock; namespace RecoveryPotionProbe {
""";
    private const string Boundary="""
public abstract class HBPlugin {public virtual void OnEnable(){}public virtual void OnDisable(){}public virtual void Pulse(){}public abstract string Name{get;}public abstract string Author{get;}public abstract Version Version{get;}public abstract bool WantButton{get;}public abstract string ButtonText{get;}}
public enum WoWPowerType{Mana,Rage}
public sealed class LocalPlayer {public ulong Guid=1;public uint Level=80;public bool IsValid=true,IsAlive=true,Combat=true;public bool Dead,IsGhost,IsOnTransport,OnTaxi,Stunned,Mounted,IsFlying;public Map CurrentMap=new();public double HealthPercent=20,ManaPercent=20;public WoWPowerType PowerType=WoWPowerType.Mana;public List<WoWItem> CarriedItems=new();public bool CanUseItem(ItemInfo info)=>true;}
public sealed class Map {public bool IsInstance;}
public sealed class Bot {public string Name="Wholesome";}
public static class BotManager {public static Bot Current=new();}
public static class StyxWoW {public static LocalPlayer Me;}
public static class ObjectManager {public static bool IsInGame=true;public static LocalPlayer Me=>StyxWoW.Me;}
public static class TreeRoot {public static bool IsRunning=true;}
public sealed class ItemInfo {public int RequiredLevel=1,Level=1;}
public sealed class EffectSpell {public string Name;}
public sealed class ItemEffect {public int SpellId;public bool IsValid=true;public EffectSpell Spell;public EffectSpell ActualSpell=>Spell;}
public sealed class WoWItem {
 public uint Entry=33447;public ulong Guid=2,OwnerGuid=1;public bool IsValid=true,Usable=true;public ItemInfo ItemInfo=new();public string Name="Test recovery";
 public List<ItemEffect> ItemSpells=new();
 public float Cooldown {get{var action=State.OnRead;State.OnRead=null;action?.Invoke();if(!State.KnownReady)throw new ObservationUnavailableException("item-cooldown","controlled unavailable cooldown");return 0;}}
 public bool IsCooldownReady {get{var action=State.OnRead;State.OnRead=null;action?.Invoke();return State.KnownReady;}}
 public void UseContainerItem(){State.DirectBypasses++;TryUseContainerItem();}
 public bool TryUseContainerItem(){State.Submissions++;State.LastItem=Guid;return State.SubmitAccepted;}
}
public static class RecoveryActions {
 public static void ReportDeferral(Exception error,string owner){Styx.Logic.Combat.RecoveryActions.RethrowControlFlow(error);State.Messages.Add("Deferred "+owner);}
 public static bool TryUseConsumable(WoWItem item,bool health,bool mana,string owner){
  State.CoordinatorCalls++;if(!TreeRoot.IsRunning)throw new OperationCanceledException("explicit Stop");
  if(item==null||StyxWoW.Me==null||item.OwnerGuid!=StyxWoW.Me.Guid||!item.IsValid||!item.IsCooldownReady)return false;
  if(State.PendingItem||(health&&State.PendingHeal))return false;
  bool accepted=item.TryUseContainerItem();if(accepted)State.PendingItem=true;return accepted;
 }
}
public static class State {
 public static long Now=10000;public static bool KnownReady=true,PendingHeal,PendingItem,SubmitAccepted=true;public static int Submissions,DirectBypasses,CoordinatorCalls;public static ulong LastItem;public static Action OnRead;
 public static readonly List<string> Messages=new();
}
public sealed class WaitTimer {private long until;private readonly long span;public WaitTimer(TimeSpan value){span=(long)value.TotalMilliseconds;}public bool IsFinished=>State.Now>=until;public void Reset(){until=State.Now+span;}}
public readonly struct TestClock {private readonly long value;public TestClock(long ticks){value=ticks;}public static TestClock UtcNow=>new(State.Now);public static TestClock MinValue=>new(long.MinValue);public TestClock AddSeconds(double seconds)=>new(value+(long)(seconds*1000));public static bool operator >=(TestClock a,TestClock b)=>a.value>=b.value;public static bool operator <=(TestClock a,TestClock b)=>a.value<=b.value;}
public static class Colors {public static readonly object DarkBlue=new();}
public static class Logging {
 public static void Write(object color,string text,params object[] args)=>State.Messages.Add(string.Format(text,args));
 public static void WriteDiagnostic(object color,string text,params object[] args)=>State.Messages.Add(string.Format(text,args));
 public static void WriteDebug(string text,params object[] args)=>State.Messages.Add(string.Format(text,args));
}
public static class Cases {
 private sealed class Failure(string message):Exception(message){}
 private static void Check(bool value,string message){if(!value)throw new Failure(message);}
 private static DrinkPotions Reset(bool health=true,bool mana=false){
  State.Now=10000;State.KnownReady=State.SubmitAccepted=true;State.PendingHeal=State.PendingItem=false;State.Submissions=State.DirectBypasses=State.CoordinatorCalls=0;State.LastItem=0;State.OnRead=null;State.Messages.Clear();TreeRoot.IsRunning=true;ObjectManager.IsInGame=true;
  StyxWoW.Me=new LocalPlayer{HealthPercent=health?20:100,ManaPercent=mana?20:100};
  var item=new WoWItem();if(health)item.ItemSpells.Add(new ItemEffect{SpellId=1,Spell=new EffectSpell{Name="Healing Potion"}});if(mana)item.ItemSpells.Add(new ItemEffect{SpellId=2,Spell=new EffectSpell{Name="Restore Mana"}});StyxWoW.Me.CarriedItems.Add(item);
  var plugin=new DrinkPotions();plugin.OnEnable();State.Now+=2001;return plugin;
 }
 public static void Run(){int total=0,passed=0,failed=0,unexpected=0;
  void Case(string name,Action action){total++;try{action();passed++;Console.WriteLine("PASS recovery potion: "+name);}catch(Failure error){failed++;Console.Error.WriteLine("FAIL recovery potion: "+name+": "+error.Message);}catch(Exception error){unexpected++;Console.Error.WriteLine("ERROR recovery potion: "+name+": "+error);}}
  Case("healthy health-potion submission retained",()=>{var plugin=Reset();plugin.Pulse();Check(State.Submissions==1,"eligible health item was not submitted");});
  Case("healthy mana-only submission retained",()=>{var plugin=Reset(false,true);plugin.Pulse();Check(State.Submissions==1,"eligible mana item was not submitted");});
  Case("configured thresholds remain authoritative",()=>{var plugin=Reset();StyxWoW.Me.HealthPercent=51;plugin.Pulse();Check(State.Submissions==0,"health above existing default threshold used an item");});
  Case("pending self-heal reserves health recovery",()=>{var plugin=Reset();State.PendingHeal=true;plugin.Pulse();Check(State.Submissions==0,"health potion overlapped an in-flight heal");});
  Case("pending heal does not block unrelated mana-only recovery",()=>{var plugin=Reset(false,true);State.PendingHeal=true;plugin.Pulse();Check(State.Submissions==1,"health reservation starved unrelated mana recovery");});
  Case("dual-resource item submitted once",()=>{var plugin=Reset(true,true);plugin.Pulse();Check(State.Submissions==1,"same health/mana item was submitted twice in one pulse");});
  Case("two resource branches share an item-in-flight admission",()=>{var plugin=Reset(true,true);StyxWoW.Me.CarriedItems[0].ItemSpells.RemoveAt(1);StyxWoW.Me.CarriedItems.Add(new WoWItem{Guid=3,ItemSpells=new(){new ItemEffect{SpellId=2,Spell=new EffectSpell{Name="Restore Mana"}}}});plugin.Pulse();Check(State.Submissions==1,"independent resource branches submitted conflicting items");});
  Case("submitted item is not declared used",()=>{var plugin=Reset();plugin.Pulse();Check(!State.Messages.Any(s=>s.Contains("Used ")),"local submission was logged as acknowledged consumption");});
  Case("rejected item is not declared used",()=>{var plugin=Reset();State.SubmitAccepted=false;plugin.Pulse();Check(!State.Messages.Any(s=>s.Contains("Used ")),"failed item use was logged as consumption");});
  Case("unknown cooldown defers candidate",()=>{var plugin=Reset();State.KnownReady=false;bool escaped=false;try{plugin.Pulse();}catch(ObservationUnavailableException){escaped=true;}Check(!escaped&&State.Submissions==0,"unknown cooldown was treated ready or escaped optional admission");});
  Case("replacement owner cannot consume stale carried item",()=>{var plugin=Reset();State.OnRead=()=>StyxWoW.Me=new LocalPlayer{Guid=4,HealthPercent=20,ManaPercent=100};plugin.Pulse();Check(State.Submissions==0,"old candidate was submitted after actor replacement");});
  Case("Stop during admission propagates",()=>{var plugin=Reset();State.OnRead=()=>TreeRoot.IsRunning=false;bool stopped=false;try{plugin.Pulse();}catch(OperationCanceledException){stopped=true;}Check(stopped&&State.Submissions==0,"explicit Stop was swallowed or allowed item use");});
  Case("later pulse defers until item acknowledgement",()=>{var plugin=Reset();plugin.Pulse();State.Now+=3000;plugin.Pulse();Check(State.Submissions==1,"timer expiry ignored the pending item");});
  Case("acknowledged item permits a fresh eligible action",()=>{var plugin=Reset();plugin.Pulse();State.PendingItem=false;State.Now+=3000;plugin.Pulse();Check(State.Submissions==2,"acknowledgement failed to release the next action");});
  Case("all item submissions pass through shared ownership",()=>{var plugin=Reset();plugin.Pulse();Check(State.DirectBypasses==0&&State.CoordinatorCalls==1,"plugin bypassed shared action ownership");});
  Console.WriteLine($"Recovery potion admission cases: {passed}/{total}; assertions={failed}; unexpected={unexpected}; actual plugin/policy, controlled shared admission/native replies; no item use or game.");
  if(failed+unexpected!=0)throw new InvalidOperationException("Recovery potion admission regressions failed");
 }
}
""";
}
