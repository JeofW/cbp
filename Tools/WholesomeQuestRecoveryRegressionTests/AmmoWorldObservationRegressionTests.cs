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

// Complete actual world-state getters and ammo admission, not a configured
// IsInWorld boolean. Memory/API/actor leaves are controlled; the existing host
// enum/address usage is not asserted as independently verified client mapping.
internal static class AmmoWorldObservationRegressionTests
{
    [ModuleInitializer]
    internal static void Run()
    {
        string root = Root();
        var styx = CSharpSyntaxTree.ParseText(File.ReadAllText(Path.Combine(root, "Styx/StyxWoW.cs"))).GetRoot();
        var propertyNames = new HashSet<string> { "IsInGame", "GameState", "IsInWorld" };
        string worldProperties = string.Join("\n", styx.DescendantNodes().OfType<PropertyDeclarationSyntax>()
            .Where(p => propertyNames.Contains(p.Identifier.ValueText)).Select(p => p.ToString()));
        if (styx.DescendantNodes().OfType<PropertyDeclarationSyntax>().Count(p => propertyNames.Contains(p.Identifier.ValueText)) != 3)
            throw new InvalidOperationException("Complete world observation owners required");
        var objects = CSharpSyntaxTree.ParseText(File.ReadAllText(Path.Combine(root, "Styx/WoWInternals/ObjectManager.cs"))).GetRoot();
        string inGame = objects.DescendantNodes().OfType<PropertyDeclarationSyntax>()
            .Single(p => p.Identifier.ValueText == "IsInGame").ToString();
        string offset = objects.DescendantNodes().OfType<FieldDeclarationSyntax>()
            .Single(f => f.Declaration.Variables.Any(v => v.Identifier.ValueText == "IsInGameOffset")).ToString();
        var auto = CSharpSyntaxTree.ParseText(File.ReadAllText(Path.Combine(root, "runtime-snapshot/Plugins/AutoEquip2/AutoEquip.cs"))).GetRoot();
        var methodNames = new HashSet<string> { "CheckAndEquipAmmo", "CanEquipNow", "CursorHasAnyItem" };
        string methods = string.Join("\n", auto.DescendantNodes().OfType<MethodDeclarationSyntax>()
            .Where(m => methodNames.Contains(m.Identifier.ValueText)).Select(m => m.ToString()));
        if (auto.DescendantNodes().OfType<MethodDeclarationSyntax>().Count(m => methodNames.Contains(m.Identifier.ValueText)) != 3)
            throw new InvalidOperationException("Complete ammo/context/cursor owners required");
        string pending = auto.DescendantNodes().OfType<PropertyDeclarationSyntax>()
            .Single(p => p.Identifier.ValueText == "HasPendingEquip").ToString();
        string Enums(string path, string name) => CSharpSyntaxTree.ParseText(File.ReadAllText(Path.Combine(root, path)))
            .GetRoot().DescendantNodes().OfType<EnumDeclarationSyntax>().Single(e => e.Identifier.ValueText == name).ToString();
        string directory = Path.Combine(Path.GetTempPath(), "cb-ammo-world-observation-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            File.WriteAllText(Path.Combine(directory, "Probe.cs"), Prefix +
                Enums("Styx/GameState.cs", "GameState") + "\n" +
                Enums("Styx/Logic/Inventory/InventorySlot.cs", "InventorySlot") + "\n" +
                "public static class ObjectManager { public static ProbeMemory Wow; public static LocalPlayer Me;\n" + offset + inGame + "}\n" +
                "public static class StyxWoW { public static LocalPlayer Me => ObjectManager.Me;\n" + worldProperties + "}\n" +
                "public sealed class AutoEquipProbe { private bool _isDisposed; private ulong _pendingEquipGuid; private uint _pendingEquipEntry;\n" +
                "private void Log(string format,params object[] arguments) {}\n" + pending + methods +
                "public void Invoke() { CheckAndEquipAmmo(); }\n}\n" + Cases);
            Type compilerType = typeof(Styx.StyxWoW).Assembly.GetType("Styx.Loaders.SourceCompiler", true)!;
            object compiler = Activator.CreateInstance(compilerType, new object[] { directory })!;
            var result = (CompilerResults)compilerType.GetMethod("Compile")!.Invoke(compiler, null)!;
            var errors = result.Errors.Cast<CompilerError>().Where(e => !e.IsWarning).ToArray();
            if (errors.Length != 0) throw new InvalidOperationException(string.Join("; ", errors.Select(e => e.ToString())));
            var assembly = (Assembly)compilerType.GetProperty("CompiledAssembly")!.GetValue(compiler)!;
            try { assembly.GetType("WorldObservationCases", true)!.GetMethod("Run")!.Invoke(null, null); }
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
using System;using System.Collections.Generic;using System.Linq;
public sealed class ProbeMemory {
 public byte InGame=1;public uint State;public bool ThrowState;public int FlagReads,StateReads;
 public T Read<T>(uint address) {
  // Type-specific observations exercise the actual getter's read/failure path.
  // No native image is loaded and no new offset/enum provenance is asserted.
  if(typeof(T)==typeof(byte)){FlagReads++;return (T)(object)InGame;}
  if(typeof(T)==typeof(uint)){StateReads++;if(ThrowState)throw new InvalidOperationException("controlled state-read failure");return (T)(object)State;}
  throw new InvalidOperationException("Unexpected controlled read type");
 }
}
public sealed class LocalPlayer {public ulong Guid=7;public bool IsValid=true,IsAlive=true,Combat,IsGhost;}
public static class TreeRoot {public static bool IsRunning=true,IsPaused;}
public static class Battlegrounds {public static bool IsInsideBattleground;}
public sealed class AutoEquipSettings {
 public static AutoEquipSettings Instance=new AutoEquipSettings();
 public bool AutoEquipItems=true;public InventorySlot[] ProtectedSlots=Array.Empty<InventorySlot>();
}
public static class World {public static int CursorReads,AmmoRequests,Exceptions;public static Action AfterCursor;}
public static class Lua {
 public static T GetReturnVal<T>(string script,uint index) {
  if(script.Contains("GetCursorInfo")){World.CursorReads++;var action=World.AfterCursor;World.AfterCursor=null;action?.Invoke();return (T)(object)2;}
  if(script.Contains("EquipItemByName")){World.AmmoRequests++;return (T)(object)"Ammo";}
  throw new InvalidOperationException("Unexpected controlled ammo API request");
 }
}
public static class Logging {public static void WriteException(Exception error){World.Exceptions++;}}
""";

    private const string Cases = """
public static class WorldObservationCases {
 private sealed class Failure(string message):Exception(message){}
 private static void Check(bool value,string why){if(!value)throw new Failure(why);}
 public static void Run() {
  int passed=0,assertions=0,unexpected=0,total=0;
  foreach(string mode in new[]{"healthy-known-state","zoning","not-in-game","explicit-unknown","state-read-failure","undefined-state","unknown-after-cursor","state-read-failure-after-cursor"}) {
   total++;
   try {
    ObjectManager.Wow=new ProbeMemory{State=(uint)GameState.Idling};ObjectManager.Me=new LocalPlayer();
    World.CursorReads=World.AmmoRequests=World.Exceptions=0;World.AfterCursor=null;
    if(mode=="zoning")ObjectManager.Wow.State=(uint)GameState.Zoning;
    if(mode=="not-in-game")ObjectManager.Wow.InGame=0;
    if(mode=="explicit-unknown")ObjectManager.Wow.State=(uint)GameState.Unknown;
    if(mode=="undefined-state")ObjectManager.Wow.State=uint.MaxValue;
    if(mode=="state-read-failure")ObjectManager.Wow.ThrowState=true;
    if(mode=="unknown-after-cursor")World.AfterCursor=()=>ObjectManager.Wow.State=(uint)GameState.Unknown;
    if(mode=="state-read-failure-after-cursor")World.AfterCursor=()=>ObjectManager.Wow.ThrowState=true;
    new AutoEquipProbe().Invoke();
    bool healthy=mode=="healthy-known-state";
    bool late=mode.EndsWith("-after-cursor",StringComparison.Ordinal);
    Check(World.AmmoRequests==(healthy?1:0),"unavailable/undefined world observation authorized an ammo request or healthy behavior was lost");
    Check(World.CursorReads==(healthy||late?1:0),"failed early world observation reached cursor work, or the late boundary was not exercised");
    Check(ObjectManager.Wow.StateReads==(mode=="not-in-game"?0:healthy||late?2:1),"world state was not observed once per actual admission boundary");
    Check(World.Exceptions==0,"ordinary unavailable state escaped to ammo exception handling");
    passed++;Console.WriteLine("PASS ammo world observation: "+mode);
   }
   catch(Failure e){assertions++;Console.Error.WriteLine("FAIL ammo world observation: "+mode+": "+e.Message);}
   catch(Exception e){unexpected++;Console.Error.WriteLine("ERROR ammo world observation: "+mode+": "+e);}
  }
  Console.WriteLine($"Ammo world observation scenarios: {passed}/{total}; assertions={assertions}; unexpected={unexpected}; complete tracked world/in-game getters, host enums and ammo/context/cursor methods; controlled memory/API/actor leaves; no native mapping, game or server acceptance.");
  if(assertions+unexpected!=0)throw new InvalidOperationException("Ammo world observation regression");
 }
}
""";
}
