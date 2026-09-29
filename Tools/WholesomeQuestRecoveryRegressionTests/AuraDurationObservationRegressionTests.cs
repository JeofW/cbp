using System;
using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

// Complete tracked aura data/constructor/time properties and actual blessing
// selector. Controlled clock, spell-name/readiness and world/policy leaves.
// Build12340 UnitAura 6147C0 tests signed duration at +10h, not flag20h.
internal static class AuraDurationObservationRegressionTests
{
    [ModuleInitializer]
    internal static void Run()
    {
        string root = Root();
        var aura = CSharpSyntaxTree.ParseText(File.ReadAllText(Path.Combine(root,
            "Styx/Logic/Combat/WoWAura.cs"))).GetRoot().DescendantNodes()
            .OfType<ClassDeclarationSyntax>().Single(c => c.Identifier.ValueText == "WoWAura");
        var properties = new HashSet<string> { "SpellId", "CreatorGuid", "Flags", "Duration", "EndTime", "IsActive", "HasNoDuration", "TimeLeft", "TimeLeftMs" };
        var selected = aura.Members.OfType<PropertyDeclarationSyntax>()
            .Where(p => properties.Contains(p.Identifier.ValueText)).ToArray();
        if (selected.Length != properties.Count) throw new InvalidOperationException("Complete aura timer properties required");
        string declaration = aura.Members.OfType<StructDeclarationSyntax>().Single(s => s.Identifier.ValueText == "AuraInfo").ToString()
            + "\n" + aura.Members.OfType<EnumDeclarationSyntax>().Single(e => e.Identifier.ValueText == "AuraFlags").ToString()
            + "\n" + aura.Members.OfType<FieldDeclarationSyntax>().Single(f => f.Declaration.Variables.Any(v => v.Identifier.ValueText == "_data")).ToString()
            + "\n" + aura.Members.OfType<ConstructorDeclarationSyntax>().Single(c => c.ParameterList.Parameters.Count == 7).ToString()
            + "\n" + string.Join("\n", selected.Select(p => p.ToString()));
        var support = CSharpSyntaxTree.ParseText(File.ReadAllText(Path.Combine(root,
            "runtime-snapshot/Routines/Singular wotlk/ClassSpecific/Paladin/PaladinSupport.cs"))).GetRoot();
        var names = new HashSet<string> { "SelectNormalBlessing", "SupportAuras", "MatchesBlessing" };
        var methods = support.DescendantNodes().OfType<MethodDeclarationSyntax>()
            .Where(m => names.Contains(m.Identifier.ValueText)).ToArray();
        if (methods.Length != names.Count) throw new InvalidOperationException("Complete blessing selector required");
        string directory = Path.Combine(Path.GetTempPath(), "cb-aura-duration-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            File.WriteAllText(Path.Combine(directory, "Probe.cs"), Prefix +
                "public sealed class WoWAura { public string Name => World.Names[SpellId];\n" + declaration + "}\n" +
                "public static class SupportProbe {\n" + SupportLeaves +
                string.Join("\n", methods.Select(m => m.ToString())) + "}\n" + Cases);
            Type type = typeof(Styx.StyxWoW).Assembly.GetType("Styx.Loaders.SourceCompiler", true)!;
            object compiler = Activator.CreateInstance(type, new object[] { directory })!;
            var result = (CompilerResults)type.GetMethod("Compile")!.Invoke(compiler, null)!;
            var errors = result.Errors.Cast<CompilerError>().Where(e => !e.IsWarning).ToArray();
            if (errors.Length != 0) throw new InvalidOperationException(string.Join("; ", errors.Select(e => e.ToString())));
            var assembly = (Assembly)type.GetProperty("CompiledAssembly")!.GetValue(compiler)!;
            try { assembly.GetType("AuraDurationCases", true)!.GetMethod("Run")!.Invoke(null, null); }
            catch (TargetInvocationException e) when (e.InnerException != null)
            { ExceptionDispatchInfo.Capture(e.InnerException).Throw(); throw; }
        }
        finally { Directory.Delete(directory, true); }
    }

    private static string Root()
    {
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d != null; d = d.Parent)
            if (File.Exists(Path.Combine(d.FullName, "CopilotBuddy.csproj"))) return d.FullName;
        throw new InvalidOperationException("Tracked checkout required");
    }

    private const string Prefix = """
#nullable disable
using System;using System.Collections.Generic;using System.Linq;using System.Runtime.InteropServices;
public enum WoWClass {Paladin,Mage,Priest,Warlock,Warrior}
public enum TalentSpec {RetributionPaladin,HolyPaladin}
public enum WoWContext {Normal,Battlegrounds}
public enum PaladinBlessings {Auto,Kings,Might,Wisdom,Sanctuary}
public sealed class WoWPlayer {
 public ulong Guid;public WoWClass Class=WoWClass.Paladin;public bool IsMe,IsInParty,IsInRaid;public int MaxMana=100;
 public readonly List<WoWAura> Auras=new List<WoWAura>();
 public IEnumerable<WoWAura> GetAllAuras()=>Auras;
 public bool HasAura(string name)=>Auras.Any(a=>a.Name==name);
}
public sealed class Clock {public uint Now=1000;public int Reads;public uint PerformanceCounter(){Reads++;return Now;}}
public static class StyxWoW {public static Clock WoWClient=new Clock();public static WoWPlayer Me;}
public sealed class PaladinSettings {public PaladinBlessings Blessings;public bool UsePallyPowerAssignments;}
public sealed class SingularSettings {public static SingularSettings Instance=new SingularSettings();public PaladinSettings Paladin=new PaladinSettings();}
public static class TalentManager {public static TalentSpec CurrentSpec=TalentSpec.RetributionPaladin;}
public static class SingularRoutine {public static WoWContext CurrentWoWContext=WoWContext.Normal;}
public static class World {
 public static readonly Dictionary<int,string> Names=new Dictionary<int,string>();public static WoWPlayer Target;
 public static bool Ready=true,ContextValid=true,RecipientValid=true;
}
public static class SpellManager {
 public static bool HasSpell(string name)=>World.Ready;
 public static bool CanCast(string name,WoWPlayer player)=>World.Ready;
}
""";

    private const string SupportLeaves = """
 private enum PallyPowerReadStatus {Absent,Verified,Uncertain}
 private sealed class PallyPowerAssignment {public PallyPowerReadStatus Status;public string Blessing;}
 private static PallyPowerAssignment ReadPallyPowerAssignment(WoWPlayer player)=>new PallyPowerAssignment{Status=PallyPowerReadStatus.Absent};
 private static bool CanMaintainSupport()=>World.ContextValid;
 private static bool IsCurrentRecipient(WoWPlayer player,bool includeGroup)=>World.RecipientValid&&ReferenceEquals(player,World.Target);
 public static string Select(WoWPlayer player)=>SelectNormalBlessing(player);
""";

    private const string Cases = """
public static class AuraDurationCases {
 private sealed class Failure(string reason):Exception(reason){}
 private static int nextId;
 private static void Check(bool value,string reason){if(!value)throw new Failure(reason);}
 private static void Reset(){
  World.Names.Clear();World.Ready=World.ContextValid=World.RecipientValid=true;
  World.Target=new WoWPlayer{Guid=22};StyxWoW.Me=new WoWPlayer{Guid=7,IsMe=true};StyxWoW.WoWClient=new Clock();
  SingularSettings.Instance=new SingularSettings();TalentManager.CurrentSpec=TalentSpec.RetributionPaladin;
  SingularRoutine.CurrentWoWContext=WoWContext.Normal;
 }
 private static WoWAura Aura(string name,byte flags,uint duration,uint expiry,ulong owner=9){
  int id=++nextId;World.Names[id]=name;return new WoWAura(id,owner,(WoWAura.AuraFlags)flags,1,80,duration,expiry);
 }
 private static void Timer(WoWAura aura,uint duration,uint expiry,uint now){
  bool permanent=unchecked((int)duration)<=0; // actual signed JLE in the original API owner
  Check(aura.HasNoDuration==permanent,"duration-present flag replaced the native duration-field classification");
  uint expected=permanent?uint.MaxValue:now==0||expiry<=now?0:expiry-now;
  TimeSpan expectedSpan=permanent?TimeSpan.MaxValue:TimeSpan.FromMilliseconds(expected);
  Check(aura.TimeLeft==expectedSpan,"TimeLeft disagrees with duration/expiry observation");
  Check(aura.TimeLeftMs==expected,"TimeLeftMs disagrees with duration/expiry observation");
  Check(StyxWoW.WoWClient.Reads==(permanent?0:2),"timer reads changed or permanent aura queried the clock");
 }
 public static void Run(){
  int passed=0,assertions=0,unexpected=0,total=0;
  void Case(string name,Action body){total++;try{body();passed++;Console.WriteLine("PASS aura duration observation: "+name);}
   catch(Failure e){assertions++;Console.Error.WriteLine("FAIL aura duration observation: "+name+": "+e.Message);}
   catch(Exception e){unexpected++;Console.Error.WriteLine("ERROR aura duration observation: "+name+": "+e);}}
  foreach(byte flags in new byte[]{0,1,0x11,0x31,0x91,0xB1,0x71})
  foreach(uint duration in new uint[]{0,10000,uint.MaxValue})
   Case("field-not-flag/"+flags.ToString("X2")+"/"+duration,()=>{Reset();Timer(Aura("Probe",flags,duration,6000),duration,6000,1000);});
  foreach(byte flags in new byte[]{0x11,0x31})
  foreach(uint now in new uint[]{0,5999,6000,6001})
   Case("expiry-boundary/"+flags.ToString("X2")+"/"+now,()=>{Reset();StyxWoW.WoWClient.Now=now;Timer(Aura("Probe",flags,10000,6000),10000,6000,now);});
  foreach(byte flags in new byte[]{0x11,0x31})
  foreach(uint duration in new uint[]{1,0x7FFFFFFF,0x80000000})
   Case("native-signed-duration/"+flags.ToString("X2")+"/"+duration,()=>{Reset();Timer(Aura("Probe",flags,duration,6000),duration,6000,1000);});
  foreach(PaladinBlessings family in new[]{PaladinBlessings.Kings,PaladinBlessings.Might,PaladinBlessings.Wisdom,PaladinBlessings.Sanctuary})
  foreach(bool greater in new[]{false,true})
  foreach(ulong owner in new ulong[]{7,9})
  foreach(string time in new[]{"future","expired","permanent"}){
   Case("actual-blessing-selector/"+family+"/"+greater+"/"+owner+"/"+time,()=>{
    Reset();SingularSettings.Instance.Paladin.Blessings=family;string normal="Blessing of "+family;
    World.Target.Auras.Add(Aura(greater?"Greater "+normal:normal,time=="permanent"?(byte)0x11:(byte)0x31,
     time=="permanent"?0U:10000U,time=="future"?6000U:0U,owner));
    Check(SupportProbe.Select(World.Target)==(time=="expired"?normal:null),
     "expired timed coverage was retained or genuine permanent coverage was discarded by the actual selector");
   });
  }
  foreach(ulong owner in new ulong[]{0,9})
  foreach(string time in new[]{"future","expired","permanent"}){
   Case("actual-shout-coverage/"+owner+"/"+time,()=>{
    Reset();SingularSettings.Instance.Paladin.Blessings=PaladinBlessings.Might;
    World.Target.Auras.Add(Aura("Battle Shout",time=="permanent"?(byte)0x11:(byte)0x31,
     time=="permanent"?0U:10000U,time=="future"?6000U:0U,owner));
    Check(SupportProbe.Select(World.Target)==(time=="expired"?"Blessing of Might":null),
     "actual conservative Shout policy used inverted lifetime rather than current duration/expiry");
   });
  }
  Case("same-snapshot-expiry-progress",()=>{
   Reset();SingularSettings.Instance.Paladin.Blessings=PaladinBlessings.Might;
   World.Target.Auras.Add(Aura("Battle Shout",0x31,10000,6000));
   Check(SupportProbe.Select(World.Target)==null,"live coverage not retained");
   StyxWoW.WoWClient.Now=5999;Check(SupportProbe.Select(World.Target)==null,"coverage expired early");
   StyxWoW.WoWClient.Now=6000;Check(SupportProbe.Select(World.Target)=="Blessing of Might","expired snapshot permanently blocked valid Might");
  });
  foreach(string mode in new[]{"no-aura","inactive","not-ready","invalid-recipient","invalid-context"}){
   Case("preserved-admission/"+mode,()=>{
    Reset();SingularSettings.Instance.Paladin.Blessings=PaladinBlessings.Kings;
    if(mode=="inactive")World.Target.Auras.Add(Aura("Blessing of Kings",0,0,0));
    World.Ready=mode!="not-ready";World.RecipientValid=mode!="invalid-recipient";World.ContextValid=mode!="invalid-context";
    Check(SupportProbe.Select(World.Target)==(mode=="no-aura"||mode=="inactive"?"Blessing of Kings":null),"existing unrelated admission policy changed");
   });
  }
  Case("retained-duration-layout-and-enum-value",()=>{
   Check(Marshal.SizeOf(typeof(WoWAura.AuraInfo))==24&&Marshal.OffsetOf(typeof(WoWAura.AuraInfo),"Duration").ToInt32()==16
    &&Marshal.OffsetOf(typeof(WoWAura.AuraInfo),"EndTime").ToInt32()==20,"retained native duration/expiry field locations changed");
   Check((byte)WoWAura.AuraFlags.NoDuration==32,"legacy enum numeric compatibility was changed instead of the timer interpretation");
  });
  Console.WriteLine($"Aura duration observation scenarios: {passed}/{total}; assertions={assertions}; unexpected={unexpected}; complete tracked aura data/timer and actual blessing selector; native signed-duration/expiry vectors; clock/name/world/readiness controlled; no native/game/server execution or magnitude proof.");
  if(assertions+unexpected!=0)throw new InvalidOperationException("Aura duration observation regression");
 }
}
""";
}
