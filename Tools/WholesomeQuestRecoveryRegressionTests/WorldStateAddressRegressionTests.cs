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

// Complete tracked world getters and ammo admission, with distinct original
// address observations. Build12340: B6A9E0 is a screen-name buffer; B6AA38 is
// the numeric state word used alongside byte BD0792 by native helper 8C6330.
// Controlled memory/API leaves do not execute the image or certify live state.
internal static class WorldStateAddressRegressionTests
{
    [ModuleInitializer]
    internal static void Run()
    {
        string root = Root();
        var styx = CSharpSyntaxTree.ParseText(File.ReadAllText(Path.Combine(root, "Styx/StyxWoW.cs"))).GetRoot();
        var worldNames = new HashSet<string> { "IsInGame", "GameState", "IsInWorld" };
        var worldProperties = styx.DescendantNodes().OfType<PropertyDeclarationSyntax>()
            .Where(p => worldNames.Contains(p.Identifier.ValueText)).ToArray();
        if (worldProperties.Length != 3) throw new InvalidOperationException("Complete world getters required");
        var objects = CSharpSyntaxTree.ParseText(File.ReadAllText(Path.Combine(root, "Styx/WoWInternals/ObjectManager.cs"))).GetRoot();
        string inGame = objects.DescendantNodes().OfType<PropertyDeclarationSyntax>()
            .Single(p => p.Identifier.ValueText == "IsInGame").ToString();
        string flagOffset = objects.DescendantNodes().OfType<FieldDeclarationSyntax>()
            .Single(f => f.Declaration.Variables.Any(v => v.Identifier.ValueText == "IsInGameOffset")).ToString();
        var auto = CSharpSyntaxTree.ParseText(File.ReadAllText(Path.Combine(root, "runtime-snapshot/Plugins/AutoEquip2/AutoEquip.cs"))).GetRoot();
        var names = new HashSet<string> { "CheckAndEquipAmmo", "CanEquipNow", "CursorHasAnyItem" };
        var methods = auto.DescendantNodes().OfType<MethodDeclarationSyntax>()
            .Where(m => names.Contains(m.Identifier.ValueText)).ToArray();
        if (methods.Length != 3) throw new InvalidOperationException("Complete ammo/context/cursor owners required");
        string pending = auto.DescendantNodes().OfType<PropertyDeclarationSyntax>()
            .Single(p => p.Identifier.ValueText == "HasPendingEquip").ToString();
        string EnumSource(string path, string name) => CSharpSyntaxTree.ParseText(File.ReadAllText(Path.Combine(root, path)))
            .GetRoot().DescendantNodes().OfType<EnumDeclarationSyntax>().Single(e => e.Identifier.ValueText == name).ToString();
        string directory = Path.Combine(Path.GetTempPath(), "cb-world-state-address-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            File.WriteAllText(Path.Combine(directory, "Probe.cs"), Prefix +
                EnumSource("Styx/GameState.cs", "GameState") + "\n" +
                EnumSource("Styx/Logic/Inventory/InventorySlot.cs", "InventorySlot") + "\n" +
                "public static class ObjectManager {public static ProbeMemory Wow;public static LocalPlayer Me;\n" + flagOffset + inGame + "}\n" +
                "public static class StyxWoW {public static LocalPlayer Me=>ObjectManager.Me;\n" +
                string.Join("\n", worldProperties.Select(p => p.ToString())) + "}\n" +
                "public sealed class AutoEquipProbe {private bool _isDisposed;private ulong _pendingEquipGuid;private uint _pendingEquipEntry;\n" +
                "private void Log(string format,params object[] arguments){}\n" + pending +
                string.Join("\n", methods.Select(m => m.ToString())) + "public void Invoke(){CheckAndEquipAmmo();}\n}\n" + Cases);
            Type type = typeof(Styx.StyxWoW).Assembly.GetType("Styx.Loaders.SourceCompiler", true)!;
            object compiler = Activator.CreateInstance(type, new object[] { directory })!;
            var result = (CompilerResults)type.GetMethod("Compile")!.Invoke(compiler, null)!;
            var errors = result.Errors.Cast<CompilerError>().Where(e => !e.IsWarning).ToArray();
            if (errors.Length != 0) throw new InvalidOperationException(string.Join("; ", errors.Select(e => e.ToString())));
            var assembly = (Assembly)type.GetProperty("CompiledAssembly")!.GetValue(compiler)!;
            try { assembly.GetType("WorldAddressCases", true)!.GetMethod("Run")!.Invoke(null, null); }
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
using System;using System.Collections.Generic;using System.Linq;using System.Text;
public sealed class ProbeMemory {
 public byte InGame=1;public uint NativeState,ScreenWord;
 public bool ThrowFlag,ThrowNative,ThrowScreen;public int FlagReads,NativeReads,ScreenReads,OtherReads;
 // Preserve the exact address-aware observation and counters for byte callers.
 public byte[] ReadBytes(uint address,int count){if(count!=4)throw new InvalidOperationException("unexpected state width");return BitConverter.GetBytes(Read<uint>(address));}
 public T Read<T>(uint address){
  if(typeof(T)==typeof(byte)&&address==0xBD0792U){FlagReads++;if(ThrowFlag)throw new InvalidOperationException("controlled in-game read failure");return (T)(object)InGame;}
  if(typeof(T)==typeof(uint)&&address==0xB6AA38U){NativeReads++;if(ThrowNative)throw new InvalidOperationException("controlled numeric-state read failure");return (T)(object)NativeState;}
  if(typeof(T)==typeof(uint)&&address==0xB6A9E0U){ScreenReads++;if(ThrowScreen)throw new InvalidOperationException("controlled unrelated screen-buffer read failure");return (T)(object)ScreenWord;}
  OtherReads++;throw new InvalidOperationException("unexpected address/type observation");
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
  throw new InvalidOperationException("unexpected controlled ammo request");
 }
}
public static class Logging {public static void WriteException(Exception exception){World.Exceptions++;}}
""";

    private const string Cases = """
public static class WorldAddressCases {
 private sealed class Failure(string reason):Exception(reason){}
 private static void Check(bool value,string reason){if(!value)throw new Failure(reason);}
 private static uint Screen(string name)=>BitConverter.ToUInt32(Encoding.ASCII.GetBytes(name+"\0\0\0\0"),0);
 private static ProbeMemory Reset(string screen=""){
  var memory=new ProbeMemory{NativeState=0,ScreenWord=Screen(screen)};ObjectManager.Wow=memory;ObjectManager.Me=new LocalPlayer();
  TreeRoot.IsRunning=true;TreeRoot.IsPaused=Battlegrounds.IsInsideBattleground=false;AutoEquipSettings.Instance=new AutoEquipSettings();
  World.CursorReads=World.AmmoRequests=World.Exceptions=0;World.AfterCursor=null;return memory;
 }
 private static void Reads(ProbeMemory memory,int expected){
  Check(memory.NativeReads==expected&&memory.ScreenReads==0&&memory.OtherReads==0,
   "actual getter read screen-name/unrelated data instead of exactly the numeric state word");
 }
 public static void Run(){
  int passed=0,assertions=0,unexpected=0,total=0;
  void Case(string name,Action body){total++;try{body();passed++;Console.WriteLine("PASS world state address: "+name);}
   catch(Failure e){assertions++;Console.Error.WriteLine("FAIL world state address: "+name+": "+e.Message);}
   catch(Exception e){unexpected++;Console.Error.WriteLine("ERROR world state address: "+name+": "+e);}}
  foreach(string screen in new[]{"","login","charselect","charcreate","patchdownload"}){
   Case("screen-buffer-is-not-state/"+screen,()=>{
    var memory=Reset(screen);Check(StyxWoW.GameState==GameState.Idling,"screen-name bytes replaced the actual zero numeric state");Reads(memory,1);
   });
  }
  foreach(uint state in new uint[]{0,1,3,9,10,11,12,13,14,99,uint.MaxValue}){
   Case("actual-word/"+state,()=>{
    var memory=Reset("login");memory.NativeState=state;
    Check(unchecked((uint)(int)StyxWoW.GameState)==state,"getter failed to return the observed numeric word");Reads(memory,1);
   });
  }
  foreach(uint state in new uint[]{0,10,13,14,99,uint.MaxValue}){
   Case("world-policy-native-word/"+state,()=>{
    var memory=Reset();memory.NativeState=state;bool expected=state==0||state==14;
    Check(StyxWoW.IsInWorld==expected,"screen-buffer zero bypassed the retained numeric-world guard or valid policy changed");Reads(memory,1);
   });
  }
  foreach(string mode in new[]{"missing-memory","not-in-game","flag-read-failure","state-read-failure","screen-read-failure","direct-state-read-failure"}){
   Case("read-boundary/"+mode,()=>{
    var memory=Reset();
    if(mode=="missing-memory")ObjectManager.Wow=null;
    if(mode=="not-in-game")memory.InGame=0;
    if(mode=="flag-read-failure")memory.ThrowFlag=true;
    if(mode=="state-read-failure"||mode=="direct-state-read-failure")memory.ThrowNative=true;
    if(mode=="screen-read-failure")memory.ThrowScreen=true;
    if(mode=="direct-state-read-failure")Check(StyxWoW.GameState==GameState.Unknown,"numeric read failure did not preserve Unknown");
    else Check(StyxWoW.IsInWorld==(mode=="screen-read-failure"),"failure/short-circuit boundary changed or unrelated buffer controlled admission");
    Reads(memory,mode=="missing-memory"||mode=="not-in-game"||mode=="flag-read-failure"?0:1);
   });
  }
  foreach(string mode in new[]{"healthy-screen-present","zoning-screen-empty","unknown-screen-empty","undefined-screen-empty","state-read-failure","zoning-after-cursor","unknown-after-cursor","state-read-failure-after-cursor","screen-change-after-cursor"}){
   Case("actual-ammo-owner/"+mode,()=>{
    var memory=Reset(mode=="healthy-screen-present"?"charselect":"");
    if(mode=="zoning-screen-empty")memory.NativeState=10;
    if(mode=="unknown-screen-empty")memory.NativeState=13;
    if(mode=="undefined-screen-empty")memory.NativeState=99;
    if(mode=="state-read-failure")memory.ThrowNative=true;
    if(mode=="zoning-after-cursor")World.AfterCursor=()=>memory.NativeState=10;
    if(mode=="unknown-after-cursor")World.AfterCursor=()=>memory.NativeState=13;
    if(mode=="state-read-failure-after-cursor")World.AfterCursor=()=>memory.ThrowNative=true;
    if(mode=="screen-change-after-cursor")World.AfterCursor=()=>memory.ScreenWord=Screen("charcreate");
    new AutoEquipProbe().Invoke();
    bool admitted=mode=="healthy-screen-present"||mode=="screen-change-after-cursor";
    bool late=mode.EndsWith("-after-cursor",StringComparison.Ordinal);
    Check(World.AmmoRequests==(admitted?1:0),"screen contents controlled actual ammo permission instead of numeric state");
    Check(World.CursorReads==(admitted||late?1:0),"failed early numeric admission reached cursor work or late boundary was lost");
    Reads(memory,admitted||late?2:1);
    Check(World.Exceptions==0,"ordinary unavailable-state observation escaped to ammo exception logging");
   });
  }
  Console.WriteLine($"World state address scenarios: {passed}/{total}; assertions={assertions}; unexpected={unexpected}; complete tracked world/in-game getters, enums and ammo/context/cursor methods; separate build12340 screen-buffer/numeric-word/flag observations; no native/client/server execution.");
  if(assertions+unexpected!=0)throw new InvalidOperationException("World state address regression");
 }
}
""";
}
