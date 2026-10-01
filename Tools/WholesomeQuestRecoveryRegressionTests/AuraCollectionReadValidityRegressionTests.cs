using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

// Complete tracked aura collection, Memory typed/byte/raw/cache chain and
// blessing selector. Only external transfers, world, clock and spell lookup
// are controlled. No process is opened and no client/native code is executed.
internal static class AuraCollectionReadValidityRegressionTests
{
    [ModuleInitializer]
    internal static void Run()
    {
        string root = Root();
        SyntaxNode Parse(string path) => CSharpSyntaxTree.ParseText(File.ReadAllText(Path.Combine(root, path))).GetRoot();
        var memory = Parse("GreenMagic/Memory.cs").DescendantNodes().OfType<ClassDeclarationSyntax>()
            .Single(c => c.Identifier.ValueText == "Memory");
        var memoryMethods = memory.Members.OfType<MethodDeclarationSyntax>().Where(m =>
            m.Identifier.ValueText is "Read" or "ReadInternal" ||
            m.Identifier.ValueText == "ReadRawMemory" && !m.Modifiers.Any(t => t.ValueText == "static") ||
            m.Identifier.ValueText == "ReadBytes" && m.ParameterList.Parameters[0].Type!.ToString() == "uint" &&
            (m.ParameterList.Parameters.Count == 3 && m.ParameterList.Parameters[1].Type!.ToString() == "void*" ||
             m.ParameterList.Parameters.Count == 2 && m.ParameterList.Parameters[1].Type!.ToString() == "int")).ToArray();
        if (memoryMethods.Length != 5) throw new InvalidOperationException("Complete typed/byte/raw read chain required");
        var fields = memory.Members.OfType<FieldDeclarationSyntax>().Where(f => f.Declaration.Variables.Any(v =>
            v.Identifier.ValueText is "_cache" or "_cacheEnabled")).ToArray();
        if (fields.Length != 2) throw new InvalidOperationException("Actual Memory cache fields required");
        var fastSize = Parse("GreenMagic/FastSize.cs").DescendantNodes().OfType<ClassDeclarationSyntax>()
            .Single(c => c.Identifier.ValueText == "FastSize");
        var unit = Parse("Styx/WoWInternals/WoWObjects/WoWUnit.cs");
        var unitNames = new HashSet<string> { "IsPlausibleAuraCount", "GetAllAuras", "UnavailableAuraObservation" };
        var unitMethods = unit.DescendantNodes().OfType<MethodDeclarationSyntax>().Where(m => unitNames.Contains(m.Identifier.ValueText)).ToArray();
        if (!unitMethods.Any(m => m.Identifier.ValueText == "GetAllAuras") || !unitMethods.Any(m => m.Identifier.ValueText == "IsPlausibleAuraCount"))
            throw new InvalidOperationException("Actual collection owner required");
        var aura = Parse("Styx/Logic/Combat/WoWAura.cs").DescendantNodes().OfType<ClassDeclarationSyntax>()
            .Single(c => c.Identifier.ValueText == "WoWAura");
        var auraProperties = new HashSet<string> { "SpellId", "CreatorGuid", "Flags", "Duration", "EndTime", "IsActive", "HasNoDuration", "TimeLeft", "Spell", "Name", "MetadataFailure" };
        string auraMembers = string.Join("\n", aura.Members.Where(m =>
            m is StructDeclarationSyntax || m is EnumDeclarationSyntax || m is FieldDeclarationSyntax ||
            m is ConstructorDeclarationSyntax c && c.ParameterList.Parameters.Count == 1 ||
            m is PropertyDeclarationSyntax p && auraProperties.Contains(p.Identifier.ValueText)).Select(m => m.ToString()));
        var supportNames = new HashSet<string> { "SelectNormalBlessing", "SupportAuras", "MatchesBlessing" };
        string support = string.Join("\n", Parse("runtime-snapshot/Routines/Singular wotlk/ClassSpecific/Paladin/PaladinSupport.cs")
            .DescendantNodes().OfType<MethodDeclarationSyntax>().Where(m => supportNames.Contains(m.Identifier.ValueText)).Select(m => m.ToString()));
        string source = Prefix + fastSize + "\npublic sealed class Memory {\n" +
            string.Join("\n", fields.Select(f => f.ToString())) + MemoryLeaves +
            string.Join("\n", memoryMethods.Select(m => m.ToString())) + "}\n" +
            "public sealed class WoWAura {\n" + auraMembers + "}\n" +
            "public class WoWUnit {public uint BaseAddress=Transfer.Base;public bool IsValid=true;\n" +
            string.Join("\n", unitMethods.Select(m => m.ToString())) + "}\n" +
            "public static class SupportProbe {\n" + SupportLeaves + support + "}\n" + Cases;
        string trusted = AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string
            ?? throw new InvalidOperationException("Hosted runtime references required");
        var references = trusted.Split(Path.PathSeparator).Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(path => MetadataReference.CreateFromFile(path));
        var compilation = CSharpCompilation.Create("W110AuraCollection_" + Guid.NewGuid().ToString("N"),
            new[] { CSharpSyntaxTree.ParseText(source), CSharpSyntaxTree.ParseText(File.ReadAllText(
                Path.Combine(root, "Styx/Helpers/ObservationUnavailableException.cs"))) }, references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary).WithAllowUnsafe(true));
        using var output = new MemoryStream();
        var result = compilation.Emit(output);
        if (!result.Success) throw new InvalidOperationException(string.Join("; ",
            result.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).Select(d => d.ToString())));
        var assembly = Assembly.Load(output.ToArray());
        try { assembly.GetType("AuraCollectionCases", true)!.GetMethod("Run")!.Invoke(null, null); }
        catch (TargetInvocationException e) when (e.InnerException != null)
        { ExceptionDispatchInfo.Capture(e.InnerException).Throw(); throw; }
    }
    private static string Root()
    {
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d != null; d = d.Parent)
            if (File.Exists(Path.Combine(d.FullName, "CopilotBuddy.csproj"))) return d.FullName;
        throw new InvalidOperationException("Tracked checkout required");
    }
    private const string Prefix = """
#nullable disable
using System;using System.Collections.Generic;using System.Linq;using System.Text;using System.Threading;using System.Runtime.InteropServices;
public static class Transfer {
 public const uint Base=0x10000U,Count=Base+3536U,Pointer=Base+3160U,DynamicCount=Base+3156U,StaticData=Base+3152U,DynamicData=0x30000U;
 public static readonly Dictionary<uint,byte[]> Blocks=new Dictionary<uint,byte[]>();
 public static uint FaultAddress;public static string Fault;public static int Calls;
 public static bool Read(uint address,int count,out byte[] payload,out int actual){
  Calls++;payload=Array.Empty<byte>();actual=0;
  if(count==0)return true;
  if(!Blocks.TryGetValue(address,out var bytes))return false;
  string mode=address==FaultAddress?Fault:"healthy";
  if(mode=="throw")throw new InvalidOperationException("controlled transfer exception");
  if(mode=="failed")return false;
  bool ok=!mode.StartsWith("failed-",StringComparison.Ordinal);
  actual=mode=="overreported"?count+1:mode.Contains("-")?int.Parse(mode.Split('-')[1]):Math.Min(count,bytes.Length);
  payload=bytes.Take(Math.Min(Math.Min(actual,count),bytes.Length)).ToArray();return ok;
 }
}
public static class Imports {
 public static bool ReadProcessMemory(IntPtr handle,uint address,byte[] destination,int count,out int actual){
  if(handle!=new IntPtr(1))throw new InvalidOperationException("unexpected controlled handle");
  bool ok=Transfer.Read(address,count,out var bytes,out actual);Array.Copy(bytes,destination,bytes.Length);return ok;
 }
 public static bool ReadProcessMemory(IntPtr handle,uint address,IntPtr destination,int count,out int actual){
  if(handle!=new IntPtr(1))throw new InvalidOperationException("unexpected controlled handle");
  bool ok=Transfer.Read(address,count,out var bytes,out actual);if(bytes.Length>0)Marshal.Copy(bytes,0,destination,bytes.Length);return ok;
 }
}
public enum WoWClass {Paladin,Mage,Priest,Warlock}
public enum TalentSpec {RetributionPaladin,HolyPaladin}
public enum WoWContext {Normal,Battlegrounds}
public enum PaladinBlessings {Auto,Kings,Might,Wisdom,Sanctuary}
public sealed class WoWPlayer:WoWUnit {
 public ulong Guid=22;public WoWClass Class=WoWClass.Paladin;public bool IsMe,IsInParty,IsInRaid;public int MaxMana=100;
 public bool HasAura(string name)=>false; // No role/form override in these manual-Might cases.
}
public sealed class WoWAuraCollection:List<WoWAura> {public WoWAuraCollection(int capacity):base(capacity){}}
public sealed class WoWSpell {
 public string Name;public static WoWSpell ObserveFromId(int id,out string failure){bool known=World.Names.TryGetValue(id,out var name);failure=known?"":"controlled-metadata-unavailable";return known?new WoWSpell{Name=name}:null;}
}
public sealed class Clock {public uint PerformanceCounter()=>1000U;}
public static class ObjectManager {public static Memory Wow;}
public static class StyxWoW {public static bool IsInGame=true;public static WoWPlayer Me;public static Clock WoWClient=new Clock();}
public sealed class PaladinSettings {public PaladinBlessings Blessings=PaladinBlessings.Might;public bool UsePallyPowerAssignments;}
public sealed class SingularSettings {public static SingularSettings Instance=new SingularSettings();public PaladinSettings Paladin=new PaladinSettings();}
public static class TalentManager {public static TalentSpec CurrentSpec=TalentSpec.RetributionPaladin;}
public static class SingularRoutine {public static WoWContext CurrentWoWContext=WoWContext.Normal;}
public static class SpellManager {public static bool HasSpell(string name)=>true;public static bool CanCast(string name,WoWPlayer player)=>true;}
public static class World {public static WoWPlayer Target;public static readonly Dictionary<int,string> Names=new Dictionary<int,string>();}
""";
    private const string MemoryLeaves = """
 private readonly IntPtr _hProcess=new IntPtr(1);public IntPtr ProcessHandle=>_hProcess;
 public string ReadString(Encoding encoding,uint address)=>throw new InvalidOperationException("string read outside this fixture");
 public void Seed(uint address,byte[] bytes){_cacheEnabled.Value=true;_cache.Value[new IntPtr(unchecked((int)address))]=bytes;}
""";
    private const string SupportLeaves = """
 private enum PallyPowerReadStatus {Absent,Verified,Uncertain}
 private sealed class PallyPowerAssignment {public PallyPowerReadStatus Status;public string Blessing;}
 private static PallyPowerAssignment ReadPallyPowerAssignment(WoWPlayer player)=>new PallyPowerAssignment{Status=PallyPowerReadStatus.Absent};
 private static bool CanMaintainSupport()=>true;
 private static bool IsCurrentRecipient(WoWPlayer player,bool includeGroup)=>ReferenceEquals(player,World.Target);
 public static string Select(WoWPlayer player)=>SelectNormalBlessing(player);
""";
    private const string Cases = """
public static class AuraCollectionCases {
 private sealed class Failure(string reason):Exception(reason){}
 private static void Check(bool value,string reason){if(!value)throw new Failure(reason);}
 private static void Reset(bool dynamic=false,int count=2){
  Transfer.Blocks.Clear();Transfer.Fault=null;Transfer.FaultAddress=0;Transfer.Calls=0;World.Names.Clear();
  ObjectManager.Wow=new Memory();World.Target=new WoWPlayer();StyxWoW.Me=new WoWPlayer{Guid=7,IsMe=true};StyxWoW.IsInGame=true;
  SingularSettings.Instance=new SingularSettings();
  Transfer.Blocks[Transfer.Count]=BitConverter.GetBytes(dynamic?-1:count);
  Transfer.Blocks[Transfer.Pointer]=BitConverter.GetBytes(Transfer.DynamicData);Transfer.Blocks[Transfer.DynamicCount]=BitConverter.GetBytes(count);
  var data=new byte[24*Math.Max(0,count)];
  for(int i=0;i<count;i++){
   int start=i*24,id=7000+i;World.Names[id]=i==0?"Blessing of Kings":i==1?"Battle Shout":"Other"+i;
   Array.Copy(BitConverter.GetBytes(9UL),0,data,start,8);Array.Copy(BitConverter.GetBytes(id),0,data,start+8,4);
   data[start+12]=0x31;data[start+13]=80;data[start+14]=1;
   Array.Copy(BitConverter.GetBytes(10000U),0,data,start+16,4);Array.Copy(BitConverter.GetBytes(6000U),0,data,start+20,4);
  }
  Transfer.Blocks[dynamic?Transfer.DynamicData:Transfer.StaticData]=data;
 }
 private static bool Rejected(Action action){try{action();return false;}catch(InvalidOperationException){return true;}}
 public static void Run(){
  int passed=0,assertions=0,unexpected=0,total=0;
  void Case(string name,Action body){total++;try{body();passed++;Console.WriteLine("PASS aura collection validity: "+name);}
   catch(Failure e){assertions++;Console.Error.WriteLine("FAIL aura collection validity: "+name+": "+e.Message);}
   catch(Exception e){unexpected++;Console.Error.WriteLine("ERROR aura collection validity: "+name+": "+e);}}
  foreach(bool dynamic in new[]{false,true})foreach(int count in new[]{0,1,2,255})
   Case("complete-collection/"+dynamic+"/"+count,()=>{Reset(dynamic,count);var auras=World.Target.GetAllAuras();Check(auras.Count==count,"complete bounded collection changed");Check(auras.All(a=>a.Duration==10000U&&a.CreatorGuid==9&&a.TimeLeft>TimeSpan.Zero),"complete aura fields changed");});
  foreach(string stage in new[]{"static-count","dynamic-pointer","dynamic-count","static-bulk","dynamic-bulk"}){
   string[] faults=stage.EndsWith("bulk",StringComparison.Ordinal)?new[]{"failed","partial-0","partial-1","partial-23","partial-24","partial-47","failed-24","overreported","throw"}:new[]{"failed","partial-0","partial-1","partial-3","failed-3","overreported","throw"};
   foreach(string fault in faults){
    Case("unavailable-current-collection/"+stage+"/"+fault,()=>{
     Reset(stage.StartsWith("dynamic",StringComparison.Ordinal));Transfer.Fault=fault;
     Transfer.FaultAddress=stage=="static-count"?Transfer.Count:stage=="dynamic-pointer"?Transfer.Pointer:stage=="dynamic-count"?Transfer.DynamicCount:stage=="static-bulk"?Transfer.StaticData:Transfer.DynamicData;
     Check(Rejected(()=>World.Target.GetAllAuras()),"failed or partial observation became an authoritative empty/partial aura collection");
    });
   }
  }
  foreach(bool dynamic in new[]{false,true})foreach(string fault in new[]{"failed","partial-24","partial-47"})
   Case("actual-blessing-consumer/"+dynamic+"/"+fault,()=>{Reset(dynamic);Transfer.FaultAddress=dynamic?Transfer.DynamicData:Transfer.StaticData;Transfer.Fault=fault;Check(Rejected(()=>SupportProbe.Select(World.Target)),"missing Shout observation authorized Might instead of deferring unavailable coverage");});
  foreach(bool dynamic in new[]{false,true})
   Case("complete-coverage-and-real-absence/"+dynamic,()=>{Reset(dynamic);Check(SupportProbe.Select(World.Target)==null,"observed active Shout not preserved");Reset(dynamic,1);Check(SupportProbe.Select(World.Target)=="Blessing of Might","complete observation of absent Shout did not permit existing policy");});
  foreach(string condition in new[]{"non-world","invalid-unit"})foreach(string stage in new[]{"count","bulk"})
   Case("unavailable-object-disposition/"+condition+"/"+stage,()=>{Reset();if(condition=="non-world")StyxWoW.IsInGame=false;else World.Target.IsValid=false;Transfer.FaultAddress=stage=="count"?Transfer.Count:Transfer.StaticData;Transfer.Fault=stage=="bulk"?"partial-24":"failed";Check(World.Target.GetAllAuras().Count==0,"unavailable non-world object returned partially valid aura data");});
  foreach(string mode in new[]{"missing-memory","zero-base","unknown-spell"})
   Case("missing-object-and-metadata/"+mode,()=>{
    Reset();if(mode=="missing-memory")ObjectManager.Wow=null;if(mode=="zero-base")World.Target.BaseAddress=0;
    if(mode=="unknown-spell"){
     World.Names.Remove(7001);
     Check(Rejected(()=>World.Target.GetAllAuras()),"unresolved active spell metadata became a complete partial collection");
    }else Check(World.Target.GetAllAuras().Count==0,"existing missing-object disposition changed");
   });
  // Raw record flags establish that an effect is active independently of its
  // localized spell lookup. Missing metadata cannot erase that observation.
  foreach(bool dynamic in new[]{false,true})foreach(int slot in new[]{0,1})foreach(byte flags in new byte[]{0x31,0x81})
   Case("active-metadata-loss/"+dynamic+"/"+slot+"/"+flags,()=>{
    Reset(dynamic);var data=Transfer.Blocks[dynamic?Transfer.DynamicData:Transfer.StaticData];
    data[slot*24+12]=flags;World.Names.Remove(7000+slot);
    Check(Rejected(()=>World.Target.GetAllAuras()),"known active record vanished when its metadata lookup failed");
   });
  foreach(bool dynamic in new[]{false,true})
   Case("actual-blessing-consumer-missing-metadata/"+dynamic,()=>{
    Reset(dynamic);World.Names.Remove(7001);
    Check(Rejected(()=>SupportProbe.Select(World.Target)),"missing Shout metadata incorrectly authorized Might");
   });
  foreach(bool dynamic in new[]{false,true})foreach(string mode in new[]{"inactive","empty"})
   Case("nonactive-metadata-filter/"+dynamic+"/"+mode,()=>{
    Reset(dynamic);World.Names.Remove(7001);var data=Transfer.Blocks[dynamic?Transfer.DynamicData:Transfer.StaticData];
    if(mode=="inactive")data[24+12]=0x80;else Array.Clear(data,24,24);
    Check(World.Target.GetAllAuras().Count==1,"inactive unknown or genuinely empty slot changed existing collection filtering");
   });
  foreach(bool dynamic in new[]{false,true})
   Case("metadata-recovery-without-reset/"+dynamic,()=>{
    Reset(dynamic);World.Names.Remove(7001);
    Check(Rejected(()=>World.Target.GetAllAuras()),"missing metadata was accepted");
    World.Names[7001]="Battle Shout";
    Check(World.Target.GetAllAuras().Count==2&&SupportProbe.Select(World.Target)==null,"recovered metadata remained unavailable or lost Shout coverage");
   });
  foreach(string condition in new[]{"non-world","invalid-unit"})
   Case("unknown-metadata-object-disposition/"+condition,()=>{
    Reset();World.Names.Remove(7001);if(condition=="non-world")StyxWoW.IsInGame=false;else World.Target.IsValid=false;
    Check(World.Target.GetAllAuras().Count==0,"unavailable object returned a partial apparently usable collection");
   });
  foreach(bool dynamic in new[]{false,true})foreach(int count in new[]{-2,256,int.MaxValue})
   Case("count-bound-before-allocation/"+dynamic+"/"+count,()=>{Reset(dynamic);Transfer.Blocks[dynamic?Transfer.DynamicCount:Transfer.Count]=BitConverter.GetBytes(count);Check(Rejected(()=>World.Target.GetAllAuras()),"implausible count bypassed bounded allocation guard");});
  foreach(string mode in new[]{"wrong-count-cache","bulk-cache-cannot-mask-failure","complete-count-cache"})
   Case("actual-cache-contract/"+mode,()=>{
    Reset();var memory=ObjectManager.Wow;uint address=mode=="bulk-cache-cannot-mask-failure"?Transfer.StaticData:Transfer.Count;
    memory.Seed(address,mode=="wrong-count-cache"?new byte[3]:Transfer.Blocks[address]);Transfer.FaultAddress=address;Transfer.Fault="failed";
    if(mode=="complete-count-cache")Check(World.Target.GetAllAuras().Count==2,"existing complete cached count observation rejected");
    else Check(Rejected(()=>World.Target.GetAllAuras()),"failed count or uncached bulk transfer became usable coverage");
   });
  foreach(bool dynamic in new[]{false,true})
   Case("later-valid-recovery/"+dynamic,()=>{Reset(dynamic);Transfer.FaultAddress=dynamic?Transfer.DynamicData:Transfer.StaticData;Transfer.Fault="failed";Check(Rejected(()=>World.Target.GetAllAuras()),"failed observation admitted");Transfer.Fault="healthy";Check(World.Target.GetAllAuras().Count==2,"later complete collection did not recover");});
  Case("global-typed-read-compatibility-unchanged",()=>{Reset();Transfer.FaultAddress=Transfer.Count;Transfer.Fault="failed";Check(ObjectManager.Wow.Read<int>(Transfer.Count)==0,"unrelated generic read contract changed");});
  Case("global-raw-read-result-unchanged",()=>{Reset();Transfer.FaultAddress=Transfer.StaticData;Transfer.Fault="partial-24";IntPtr output=Marshal.AllocHGlobal(48);try{Check(ObjectManager.Wow.ReadRawMemory(new IntPtr(1),Transfer.StaticData,output,48)==24,"raw transfer count contract changed");}finally{Marshal.FreeHGlobal(output);}});
  Console.WriteLine($"Aura collection validity scenarios: {passed}/{total}; assertions={assertions}; unexpected={unexpected}; complete tracked collection/Memory typed-byte-raw-cache chain and blessing selector; external transfer/world/spell lookup controlled; no process/native/game/server execution or atomic snapshot proof.");
  if(assertions+unexpected!=0)throw new InvalidOperationException("Aura collection validity regression");
 }
}
""";
}
