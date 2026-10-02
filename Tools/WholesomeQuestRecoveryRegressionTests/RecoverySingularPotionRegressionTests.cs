using System;
using System.CodeDom.Compiler;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

internal static class RecoverySingularPotionRegressionTests
{
    [ModuleInitializer]
    internal static void Run()
    {
        string? root=null;for(var d=new DirectoryInfo(AppContext.BaseDirectory);d!=null;d=d.Parent)
            if(File.Exists(Path.Combine(d.FullName,"CopilotBuddy.csproj"))){root=d.FullName;break;}
        if(root==null)throw new InvalidOperationException("Tracked source required");
        var syntax=CSharpSyntaxTree.ParseText(File.ReadAllText(Path.Combine(root,"runtime-snapshot/Routines/Singular wotlk/Helpers/Item.cs"))).GetRoot();
        var methods=syntax.DescendantNodes().OfType<MethodDeclarationSyntax>().Where(m=>m.Identifier.ValueText is "CreateUsePotionAndHealthstone" or "FindFirstUsableItemBySpell").ToArray();
        if(methods.Length!=2)throw new InvalidOperationException("Complete candidate-selection and potion factory owners required");
        string folder=Path.Combine(Path.GetTempPath(),"cb-recovery-singular-item-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(folder);
        try
        {
            File.WriteAllText(Path.Combine(folder,"Owner.cs"),Prefix+string.Join("\n",methods.Select(m=>m.ToFullString()))+"}}\n"+Boundary);
            Type compilerType=typeof(Styx.StyxWoW).Assembly.GetType("Styx.Loaders.SourceCompiler",true)!;
            object compiler=Activator.CreateInstance(compilerType,new object[]{folder})!;
            foreach(string reference in ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator))compilerType.GetMethod("AddReference",BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic)!.Invoke(compiler,new object[]{reference});
            var result=(CompilerResults)compilerType.GetMethod("Compile")!.Invoke(compiler,null)!;
            var errors=result.Errors.Cast<CompilerError>().Where(e=>!e.IsWarning).ToArray();if(errors.Length>0)throw new InvalidOperationException(string.Join("; ",errors.Select(e=>e.ToString())));
            var assembly=(Assembly)compilerType.GetProperty("CompiledAssembly")!.GetValue(compiler)!;
            try{assembly.GetType("RecoveryItemCases",true)!.GetMethod("Run")!.Invoke(null,null);}catch(TargetInvocationException e)when(e.InnerException!=null){ExceptionDispatchInfo.Capture(e.InnerException).Throw();throw;}
        }
        finally{Directory.Delete(folder,true);}
    }
    private const string Prefix="""
using System;using System.Collections.Generic;using System.Linq;using TreeSharp;using Styx;using Styx.Logic.Combat;using Styx.WoWInternals.WoWObjects;using Action=TreeSharp.Action;
/* Actual runtime factories follow. */ namespace Singular.Helpers {public static class Item {
""";
    private const string Boundary="""
public static class RecoveryItemCases {
 private sealed class Failure(string text):Exception(text){}
 public static readonly List<string> Messages=new();public static readonly List<string> Calls=new();
 public static int Native;public static bool PendingHeal,PendingItem,Reject,Cancel,Unknown;
 public static void Submit(WoWItem item){Native++;}
 public static void Run(){int passed=0,failed=0,unexpected=0,total=0;
  void Case(string name,System.Action body){total++;Reset();try{body();passed++;}catch(Failure e){failed++;Console.Error.WriteLine("FAIL Singular recovery item: "+name+": "+e.Message);}catch(Exception e){unexpected++;Console.Error.WriteLine("ERROR Singular recovery item: "+name+": "+e);}}
  Composite Factory()=>Singular.Helpers.Item.CreateUsePotionAndHealthstone(35,30);
  Case("health potion enters shared health admission",()=>{Tick(Factory());Check(Native==1&&Calls.SequenceEqual(new[]{"health"}),"health item bypassed shared admission");});
  Case("healthstone follows the same owner",()=>{StyxWoW.Me.CarriedItems[0].ItemSpells[0].ActualSpell.Name="Healthstone";Tick(Factory());Check(Native==1&&Calls.SequenceEqual(new[]{"health"}),"healthstone bypassed shared admission");});
  Case("pending self heal blocks health item",()=>{PendingHeal=true;Check(Tick(Factory())==RunStatus.Failure&&Native==0,"pending heal did not prevent item use");});
  Case("pending item blocks repeated pulse",()=>{Tick(Factory());Tick(Factory());Check(Native==1&&Calls.Count==2,"repeated item action ignored acknowledgement owner");});
  Case("blocked health permits independent mana recovery",()=>{PendingHeal=true;StyxWoW.Me.ManaPercent=20;StyxWoW.Me.CarriedItems.Add(Potion("Restore Mana"));Tick(Factory());Check(Native==1&&Calls.SequenceEqual(new[]{"health","mana"}),"health conflict starved independent mana candidate");});
  Case("one accepted item does not spend the other resource",()=>{StyxWoW.Me.ManaPercent=20;StyxWoW.Me.CarriedItems.Add(Potion("Restore Mana"));Tick(Factory());Check(Native==1&&Calls.SequenceEqual(new[]{"health"}),"accepted health item fell through to mana");});
  Case("mana-only admission remains identified",()=>{StyxWoW.Me.HealthPercent=100;StyxWoW.Me.ManaPercent=20;StyxWoW.Me.CarriedItems.Clear();StyxWoW.Me.CarriedItems.Add(Potion("Restore Mana"));Tick(Factory());Check(Native==1&&Calls.SequenceEqual(new[]{"mana"}),"mana resource semantics changed");});
  Case("known thresholds are preserved",()=>{StyxWoW.Me.HealthPercent=35;StyxWoW.Me.ManaPercent=30;Check(Tick(Factory())==RunStatus.Failure&&Native==0&&Calls.Count==0,"threshold boundary changed");});
  Case("rejected native action falls through",()=>{Reject=true;bool lower=false;Tick(new PrioritySelector(Factory(),new TreeSharp.Action(_=>{lower=true;return RunStatus.Success;})));Check(lower&&Native==0,"rejected item starved lower-priority action");});
  Case("unavailable candidate observation is nonfatal deferral",()=>{Unknown=true;Check(Tick(Factory())==RunStatus.Failure&&Native==0,"unknown item observation reached native dispatch");});
  Case("cancelled shared admission propagates",()=>{Cancel=true;bool cancelled=false;try{Tick(Factory());}catch(OperationCanceledException){cancelled=true;}Check(cancelled&&Native==0,"cancellation was swallowed or action dispatched");});
  Case("successful submission is not logged as acknowledged use",()=>{Tick(Factory());Check(Messages.Any(m=>m.Contains("Submitted"))&&!Messages.Any(m=>m.StartsWith("Used ")),"item submission has no distinct pending log");});
  Case("missing candidate remains ordinary fallback",()=>{StyxWoW.Me.CarriedItems.Clear();Check(Tick(Factory())==RunStatus.Failure&&Native==0,"missing candidate acquired a reservation");});
  Console.WriteLine($"Singular potion admission cases: {passed}/{total}; assertions={failed}; unexpected={unexpected}; actual complete selector/factory, real TreeSharp, controlled shared-coordinator boundary; no game.");
  if(failed+unexpected!=0)throw new InvalidOperationException("Singular potion admission regressions failed");
 }
 private static WoWItem Potion(string name)=>new WoWItem{ItemSpells=new(){new Effect{ActualSpell=new EffectSpell{Name=name}}}};
 private static void Reset(){Messages.Clear();Calls.Clear();Native=0;PendingHeal=PendingItem=Reject=Cancel=Unknown=false;StyxWoW.Me=new LocalPlayer();StyxWoW.Me.CarriedItems.Add(Potion("Healing Potion"));}
 private static RunStatus Tick(Composite c){c.Start(null!);try{int n=0;RunStatus s;while((s=c.Tick(null!))==RunStatus.Running)if(++n>10)throw new Failure("unbounded item behavior");return s;}finally{c.Stop(null!);}}
 private static void Check(bool value,string message){if(!value)throw new Failure(message);}
}
/* Controlled world and item observations. */ namespace Styx.WoWInternals.WoWObjects {
 public sealed class LocalPlayer {public uint Level=80;public double HealthPercent=20,ManaPercent=100;public List<WoWItem> CarriedItems=new();}
 public sealed class ItemInfo {public uint RequiredLevel=1;public int Level=1;}
 public sealed class EffectSpell {public string Name;}
 public sealed class Effect {public bool IsValid=true;public EffectSpell ActualSpell;}
 public sealed class WoWItem {public string Name="Potion";public ItemInfo ItemInfo=new();public bool Usable=true;public List<Effect> ItemSpells=new();public float Cooldown{get{if(RecoveryItemCases.Unknown)throw new Styx.Helpers.ObservationUnavailableException("item","controlled missing cooldown");return 0;}}public void UseContainerItem()=>RecoveryItemCases.Submit(this);}
}
/* Controlled current actor. */ namespace Styx {public static class StyxWoW {public static LocalPlayer Me;}}
/* Shared ledger/adapter is exercised independently, native dispatch leaf controlled here. */ namespace Styx.Logic.Combat {public static class RecoveryActions {
 public static bool TryUseConsumable(WoWItem item,bool health,bool mana,string owner){RecoveryItemCases.Calls.Add(health?"health":"mana");if(RecoveryItemCases.Cancel)throw new OperationCanceledException("owned cancellation");if(RecoveryItemCases.Reject||RecoveryItemCases.PendingItem||health&&RecoveryItemCases.PendingHeal)return false;RecoveryItemCases.Submit(item);RecoveryItemCases.PendingItem=true;return true;}
}}
/* Real item factory controls sequence/fallback; no real lag required here. */ namespace Singular.Helpers {
 public static class Logger {public static void Write(string s)=>RecoveryItemCases.Messages.Add(s);}
 public static class Common {public static Composite CreateWaitForLagDuration()=>new TreeSharp.Action(_=>RunStatus.Success);}
}
""";
}
