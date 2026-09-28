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

// Actual Memory.Read/ReadInternal/ReadBytes and FastSize, actual world/flag
// getters and ammo owner. Only the external byte transfer and world/API leaves
// are controlled. No process handle is opened and no native function executes.
internal static class WorldStateReadValidityRegressionTests
{
    [ModuleInitializer]
    internal static void Run()
    {
        string root = Root();
        SyntaxNode Parse(string path) => CSharpSyntaxTree.ParseText(File.ReadAllText(Path.Combine(root, path))).GetRoot();
        var memory = Parse("GreenMagic/Memory.cs").DescendantNodes().OfType<ClassDeclarationSyntax>()
            .Single(c => c.Identifier.ValueText == "Memory");
        var readMethods = memory.Members.OfType<MethodDeclarationSyntax>().Where(m =>
            m.Identifier.ValueText is "Read" or "ReadInternal" ||
            m.Identifier.ValueText == "ReadBytes" && m.ParameterList.Parameters.Count == 2 &&
            m.ParameterList.Parameters[0].Type!.ToString() == "uint" &&
            m.ParameterList.Parameters[1].Type!.ToString() == "int").ToArray();
        if (readMethods.Length != 3) throw new InvalidOperationException("Complete typed and byte-read chain required");
        var fields = memory.Members.OfType<FieldDeclarationSyntax>().Where(f =>
            f.Declaration.Variables.Any(v => v.Identifier.ValueText is "_cache" or "_cacheEnabled")).ToArray();
        if (fields.Length != 2) throw new InvalidOperationException("Actual read-cache fields required");
        var fastSize = Parse("GreenMagic/FastSize.cs").DescendantNodes().OfType<ClassDeclarationSyntax>()
            .Single(c => c.Identifier.ValueText == "FastSize");
        var world = Parse("Styx/StyxWoW.cs");
        var names = new HashSet<string> { "IsInGame", "GameState", "IsInWorld" };
        var properties = world.DescendantNodes().OfType<PropertyDeclarationSyntax>()
            .Where(p => names.Contains(p.Identifier.ValueText)).ToArray();
        if (properties.Length != 3) throw new InvalidOperationException("Actual world observation chain required");
        var objects = Parse("Styx/WoWInternals/ObjectManager.cs");
        var inGame = objects.DescendantNodes().OfType<PropertyDeclarationSyntax>().Single(p => p.Identifier.ValueText == "IsInGame");
        var flag = objects.DescendantNodes().OfType<FieldDeclarationSyntax>()
            .Single(f => f.Declaration.Variables.Any(v => v.Identifier.ValueText == "IsInGameOffset"));
        var auto = Parse("runtime-snapshot/Plugins/AutoEquip2/AutoEquip.cs");
        var autoNames = new HashSet<string> { "CheckAndEquipAmmo", "CanEquipNow", "CursorHasAnyItem" };
        var owners = auto.DescendantNodes().OfType<MethodDeclarationSyntax>().Where(m => autoNames.Contains(m.Identifier.ValueText)).ToArray();
        if (owners.Length != 3) throw new InvalidOperationException("Actual ammo/context/cursor owners required");
        var pending = auto.DescendantNodes().OfType<PropertyDeclarationSyntax>().Single(p => p.Identifier.ValueText == "HasPendingEquip");
        string Enum(string path, string name) => Parse(path).DescendantNodes().OfType<EnumDeclarationSyntax>()
            .Single(e => e.Identifier.ValueText == name).ToString();
        string source = Prefix + fastSize + "\n" + Enum("Styx/GameState.cs", "GameState") + "\n" +
            Enum("Styx/Logic/Inventory/InventorySlot.cs", "InventorySlot") + "\n" +
            "public sealed class ProbeMemory {\n" + string.Join("\n", fields.Select(f => f.ToString())) +
            MemoryLeaves + string.Join("\n", readMethods.Select(m => m.ToString())) + "}\n" +
            "public static class ObjectManager {public static ProbeMemory Wow;public static LocalPlayer Me;\n" + flag + inGame.ToString() + "}\n" +
            "public static class StyxWoW {public static LocalPlayer Me=>ObjectManager.Me;\n" + string.Join("\n", properties.Select(p => p.ToString())) + "}\n" +
            "public sealed class AutoEquipProbe {private bool _isDisposed;private ulong _pendingEquipGuid;private uint _pendingEquipEntry;\n" +
            "private void Log(string format,params object[] args){}\n" + pending.ToString() +
            string.Join("\n", owners.Select(m => m.ToString())) + "public void Invoke()=>CheckAndEquipAmmo();}\n" + Cases;

        // These are this new test's explicit compiler options, not a query or
        // modification of SourceCompiler or any previously refused operation.
        string trusted = AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string
            ?? throw new InvalidOperationException("Hosted runtime reference list required");
        var references = trusted.Split(Path.PathSeparator).Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(path => MetadataReference.CreateFromFile(path));
        var compilation = CSharpCompilation.Create("W110WorldRead_" + Guid.NewGuid().ToString("N"),
            new[] { CSharpSyntaxTree.ParseText(source) }, references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary).WithAllowUnsafe(true));
        using var output = new MemoryStream();
        var result = compilation.Emit(output);
        if (!result.Success) throw new InvalidOperationException(string.Join("; ",
            result.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).Select(d => d.ToString())));
        var assembly = Assembly.Load(output.ToArray());
        try { assembly.GetType("ReadValidityCases", true)!.GetMethod("Run")!.Invoke(null, null); }
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
using System;using System.Collections.Generic;using System.Linq;using System.Threading;using System.Text;using System.Runtime.InteropServices;
public static class Transfer {
 public const uint StateAddress=0xB6AA38U,FlagAddress=0xBD0792U;
 public static string Mode;public static uint State;public static byte Flag;
 public static bool FlagAvailable;public static int StateReads,FlagReads;
}
public static class Imports {
 public static bool ReadProcessMemory(IntPtr handle,uint address,byte[] buffer,int count,out int bytesRead){
  bytesRead=0;if(handle!=new IntPtr(1))throw new InvalidOperationException("unexpected controlled handle");
  if(address==Transfer.FlagAddress&&count==1){Transfer.FlagReads++;if(!Transfer.FlagAvailable)return false;buffer[0]=Transfer.Flag;bytesRead=1;return true;}
  if(address!=Transfer.StateAddress||count!=4)throw new InvalidOperationException("wrong world read address/width");
  Transfer.StateReads++;
  if(Transfer.Mode=="throw")throw new InvalidOperationException("controlled byte-transfer exception");
  if(Transfer.Mode=="failed")return false;
  bool ok=!Transfer.Mode.StartsWith("failed-",StringComparison.Ordinal);
  bytesRead=Transfer.Mode=="overreported"?5:Transfer.Mode.Contains("-")?int.Parse(Transfer.Mode.Split('-')[1]):4;
  Array.Copy(BitConverter.GetBytes(Transfer.State),buffer,Math.Min(bytesRead,4));return ok;
 }
}
public sealed class LocalPlayer {public ulong Guid=7;public bool IsValid=true,IsAlive=true,Combat,IsGhost;}
public static class TreeRoot {public static bool IsRunning=true,IsPaused;}
public static class Battlegrounds {public static bool IsInsideBattleground;}
public sealed class AutoEquipSettings {public static AutoEquipSettings Instance=new AutoEquipSettings();public bool AutoEquipItems=true;public InventorySlot[] ProtectedSlots=Array.Empty<InventorySlot>();}
public static class World {public static int CursorReads,AmmoRequests,Exceptions;public static Action AfterCursor;}
public static class Lua {
 public static T GetReturnVal<T>(string script,uint index){
  if(script.Contains("GetCursorInfo")){World.CursorReads++;var action=World.AfterCursor;World.AfterCursor=null;action?.Invoke();return (T)(object)2;}
  if(script.Contains("EquipItemByName")){World.AmmoRequests++;return (T)(object)"Ammo";}
  throw new InvalidOperationException("unexpected controlled ammo API");
 }
}
public static class Logging {public static void WriteException(Exception error){World.Exceptions++;}}
""";
    private const string MemoryLeaves = """
 public IntPtr ProcessHandle=new IntPtr(1);
 private string ReadString(Encoding encoding,uint address)=>throw new NotSupportedException("string path is outside this numeric fixture");
 public void Cache(uint address,byte[] bytes){_cacheEnabled.Value=true;_cache.Value[new IntPtr(unchecked((int)address))]=bytes;}
 public bool Contains(uint address)=>_cache.Value.ContainsKey(new IntPtr(unchecked((int)address)));
 public void Clear(){_cache.Value.Clear();}
""";
    private const string Cases = """
public static class ReadValidityCases {
 private sealed class Failure(string reason):Exception(reason){}
 private static void Check(bool value,string reason){if(!value)throw new Failure(reason);}
 private static ProbeMemory Reset(){
  Transfer.Mode="complete";Transfer.State=0;Transfer.Flag=1;Transfer.FlagAvailable=true;Transfer.StateReads=Transfer.FlagReads=0;
  World.CursorReads=World.AmmoRequests=World.Exceptions=0;World.AfterCursor=null;
  var memory=new ProbeMemory();ObjectManager.Wow=memory;ObjectManager.Me=new LocalPlayer();
  TreeRoot.IsRunning=true;TreeRoot.IsPaused=Battlegrounds.IsInsideBattleground=false;AutoEquipSettings.Instance=new AutoEquipSettings();return memory;
 }
 public static void Run(){
  int passed=0,assertions=0,unexpected=0,total=0;
  void Case(string name,Action body){total++;try{body();passed++;Console.WriteLine("PASS world read validity: "+name);}
   catch(Failure e){assertions++;Console.Error.WriteLine("FAIL world read validity: "+name+": "+e.Message);}
   catch(Exception e){unexpected++;Console.Error.WriteLine("ERROR world read validity: "+name+": "+e);}}
  Case("actual-generic-default-is-not-a-receipt",()=>{
   var memory=Reset();Transfer.Mode="failed";
   Check(memory.Read<uint>(Transfer.StateAddress)==0U,"existing generic default-return contract changed");
   Check(memory.ReadBytes(Transfer.StateAddress,4)==null,"failed byte transfer acquired a complete buffer");
   Check(Transfer.StateReads==2,"actual generic and byte chain did not reach the controlled transfer");
  });
  string[] failures={"failed","partial-0","partial-1","partial-2","partial-3","failed-1","failed-2","failed-3","overreported","throw"};
  foreach(string mode in failures)
  foreach(string owner in new[]{"direct-state","world","ammo-early","ammo-late"}){
   Case(owner+"/"+mode,()=>{
    Reset();bool late=owner=="ammo-late";
    if(late)World.AfterCursor=()=>Transfer.Mode=mode;else Transfer.Mode=mode;
    if(owner=="direct-state")Check(StyxWoW.GameState==GameState.Unknown,"incomplete state bytes became a valid zero lifecycle state");
    else if(owner=="world")Check(!StyxWoW.IsInWorld,"missing state bytes authorized world work");
    else{
     new AutoEquipProbe().Invoke();
     Check(World.AmmoRequests==0,"incomplete state observation reached ammo mutation request");
     Check(World.CursorReads==(late?1:0),"incorrect early/late observation boundary");
     Check(World.Exceptions==0,"unavailable observation escaped through the outer ammo catch");
    }
    Check(Transfer.StateReads==(late?2:1),"state was not observed exactly once per admission boundary");
   });
  }
  foreach(uint state in new uint[]{0,1,3,9,10,11,12,13,14,99,uint.MaxValue}){
   Case("complete-numeric-word/"+state,()=>{
    Reset();Transfer.State=state;
    Check(unchecked((uint)(int)StyxWoW.GameState)==state,"complete numeric state changed");
    Check(StyxWoW.IsInWorld==(state<=14&&state!=10&&state!=13),"known/non-zoning policy changed");
    Check(Transfer.StateReads==2,"direct and world getter did not use separate single observations");
   });
  }
  foreach(string mode in new[]{"complete-zero","complete-zoning","complete-undefined","undersized-failed","oversized-failed","undersized-complete","oversized-complete","flag-cached-state-failed"}){
   Case("actual-cache/"+mode,()=>{
    var memory=Reset();Transfer.Mode="failed";
    if(mode.StartsWith("complete-",StringComparison.Ordinal))memory.Cache(Transfer.StateAddress,BitConverter.GetBytes(mode=="complete-zero"?0U:mode=="complete-zoning"?10U:99U));
    if(mode.StartsWith("undersized",StringComparison.Ordinal))memory.Cache(Transfer.StateAddress,new byte[1]);
    if(mode.StartsWith("oversized",StringComparison.Ordinal))memory.Cache(Transfer.StateAddress,new byte[8]);
    if(mode.EndsWith("-complete",StringComparison.Ordinal))Transfer.Mode="complete";
    if(mode=="flag-cached-state-failed")memory.Cache(Transfer.FlagAddress,new byte[]{1});
    bool allowed=mode=="complete-zero"||mode.EndsWith("-complete",StringComparison.Ordinal);
    Check(StyxWoW.IsInWorld==allowed,"null/default or wrong-size cache entry became world authority");
    Check(Transfer.StateReads==(mode.StartsWith("complete-",StringComparison.Ordinal)?0:1),"cache-hit/miss read policy changed");
    if(mode.EndsWith("-failed",StringComparison.Ordinal))Check(!memory.Contains(Transfer.StateAddress),"failed state read was cached as success");
   });
  }
  foreach(string mode in new[]{"failed","partial-3","throw"}){
   Case("subsequent-valid-recovery/"+mode,()=>{
    Reset();Transfer.Mode=mode;Check(!StyxWoW.IsInWorld,"failed observation was admitted");
    Transfer.Mode="complete";Check(StyxWoW.IsInWorld,"next valid zero state failed to recover");
    Check(Transfer.StateReads==2,"recovery did not make a new state observation");
   });
  }
  foreach(string mode in new[]{"missing-memory","closed-handle","flag-failed","flag-zero"}){
   Case("unchanged-short-circuit/"+mode,()=>{
    var memory=Reset();if(mode=="missing-memory")ObjectManager.Wow=null;if(mode=="closed-handle")memory.ProcessHandle=IntPtr.Zero;
    if(mode=="flag-failed")Transfer.FlagAvailable=false;if(mode=="flag-zero")Transfer.Flag=0;
    Check(!StyxWoW.IsInWorld,"absent/inactive in-game observation authorized world work");
    Check(Transfer.StateReads==0,"failed in-game admission touched the numeric state");
   });
  }
  Case("healthy-ammo-remains-one-request",()=>{
   Reset();new AutoEquipProbe().Invoke();Check(World.AmmoRequests==1&&World.CursorReads==1&&World.Exceptions==0,"healthy ammo admission changed");
   Check(Transfer.StateReads==2,"healthy owner did not preserve both observation boundaries");
  });
  Console.WriteLine($"World read validity scenarios: {passed}/{total}; assertions={assertions}; unexpected={unexpected}; complete tracked Memory typed/byte/cache methods, FastSize, world/flag getters and ammo owners; external transfer/API controlled; no process/native/game/server execution or frame-freshness proof.");
  if(assertions+unexpected!=0)throw new InvalidOperationException("World read validity regression");
 }
}
""";
}
