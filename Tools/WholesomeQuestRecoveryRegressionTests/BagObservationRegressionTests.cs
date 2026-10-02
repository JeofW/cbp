using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

internal static class BagObservationRegressionTests
{
    [ModuleInitializer]
    internal static void Run()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root != null && !File.Exists(Path.Combine(root.FullName, "CopilotBuddy.csproj"))) root = root.Parent;
        if (root == null) throw new InvalidOperationException("Tracked checkout required.");
        var method = CSharpSyntaxTree.ParseText(File.ReadAllText(Path.Combine(root.FullName, "Styx/WoWInternals/WoWObjects/LocalPlayer.cs")))
            .GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>().Single(x => x.Identifier.ValueText == "TryGetBagItems");
        string source = Prefix + method + "}\n" + Cases;
        var trusted = (string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") ?? throw new InvalidOperationException("References missing.");
        var compilation = CSharpCompilation.Create("BagObservation_" + Guid.NewGuid().ToString("N"), new[] { CSharpSyntaxTree.ParseText(source) },
            trusted.Split(Path.PathSeparator).Distinct(StringComparer.OrdinalIgnoreCase).Select(x => MetadataReference.CreateFromFile(x)),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using var output = new MemoryStream();
        var result = compilation.Emit(output);
        if (!result.Success) throw new InvalidOperationException(string.Join("; ", result.Diagnostics.Where(x => x.Severity == DiagnosticSeverity.Error)));
        try { Assembly.Load(output.ToArray()).GetType("BagCases", true)!.GetMethod("Run")!.Invoke(null, null); }
        catch (TargetInvocationException e) when (e.InnerException != null) { ExceptionDispatchInfo.Capture(e.InnerException).Throw(); throw; }
    }
    private const string Prefix = """
#nullable enable
using System;using System.Linq;using System.Collections.Generic;
public static class Flow { public static void Throw(Exception e){if(e is OperationCanceledException or System.Threading.ThreadInterruptedException)System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(e).Throw();} } namespace Styx.Logic.Combat { public static class RecoveryActions {public static void RethrowControlFlow(Exception e)=>Flow.Throw(e);} }
public class BagStructure{}
public class InvalidProcessException:Exception{public InvalidProcessException(string message,Exception error):base(message,error){}}
public class Memory{public IntPtr ProcessHandle=new IntPtr(1);public byte[] ReadBytes(uint address,int count){World.OnRead?.Invoke();if(World.Error!=null)throw World.Error;return BitConverter.GetBytes(World.BagGuids[(address-12727616U)/8U]);}}
public class WoWBag{public WoWBag(){}public WoWBag(BagStructure b){Guids=World.ContainerGuids;}public static BagStructure ReadStructure(uint address){if(World.Error!=null)throw World.Error;return new();}public ulong[] Guids=Array.Empty<ulong>();public ulong[] ReadItemGuids(){World.OnSlots?.Invoke();if(World.SlotError!=null)throw World.SlotError;return Guids;}}
public class WoWPlayerInventory{public WoWPlayerInventory(BagStructure s){}public WoWBag Backpack=>World.Backpack;}
public class WoWItem{public ulong Guid;public uint BaseAddress=200;public bool IsValid=true;}
public class WoWContainer:WoWItem{public WoWBag Bag=new();}
public static class ObjectManager{public static Memory? Wow;public static LocalPlayer? Me;public static T? GetObjectByGuid<T>(ulong id)where T:class{World.OnObject?.Invoke();return World.Objects.TryGetValue(id,out var o)?o as T:null;}}
public static class World{
public static WoWBag Backpack=new();public static ulong[] BagGuids=new ulong[4],ContainerGuids=Array.Empty<ulong>();public static Dictionary<ulong,WoWItem> Objects=new();public static Exception? Error,SlotError;public static Action? OnRead,OnSlots,OnObject;
public static void Reset(){Backpack=new();BagGuids=new ulong[4];ContainerGuids=Array.Empty<ulong>();Objects=new();Error=SlotError=null;OnRead=OnSlots=OnObject=null;ObjectManager.Wow=new();ObjectManager.Me=new();}}
public class LocalPlayer{public uint BaseAddress=100;public ulong Guid=5;public bool IsValid=true;
""";
    private const string Cases = """
public static class BagCases{
static void Check(bool ok,string why){if(!ok)throw new InvalidOperationException(why);}
public static void Run(){int pass=0;var errors=new List<string>();void Case(string name,Action action){World.Reset();try{action();pass++;Console.WriteLine("PASS bag observation: "+name);}catch(Exception e){errors.Add(name+": "+e.Message);}}
Case("complete empty",()=>Check(ObjectManager.Me!.TryGetBagItems(out var items,out _)&&items.Count==0,"empty observation failed"));
Case("unresolved item is unknown",()=>{World.Backpack.Guids=new ulong[]{10};Check(!ObjectManager.Me!.TryGetBagItems(out var items,out _)&&items.Count==0,"missing object became absent");});
Case("known positive retained beside unresolved",()=>{World.Backpack.Guids=new ulong[]{10,11};World.Objects[10]=new(){Guid=10};Check(!ObjectManager.Me!.TryGetBagItems(out var items,out _)&&items.Count==1,"known positive or unknown lost");});
Case("equipped bag unresolved",()=>{World.BagGuids[0]=8;Check(!ObjectManager.Me!.TryGetBagItems(out _,out _),"missing bag became empty");});
Case("sparse bags and duplicate guids",()=>{World.BagGuids[3]=8;World.ContainerGuids=new ulong[]{10,0,10};World.Objects[8]=new WoWContainer{Guid=8};World.Objects[10]=new(){Guid=10};Check(ObjectManager.Me!.TryGetBagItems(out var items,out _)&&items.Count==1,"sparse bag lost or duplicate item repeated");});
Case("wrong item guid",()=>{World.Backpack.Guids=new ulong[]{10};World.Objects[10]=new(){Guid=11};Check(!ObjectManager.Me!.TryGetBagItems(out _,out _),"replaced guid accepted");});
Case("invalid item",()=>{World.Backpack.Guids=new ulong[]{10};World.Objects[10]=new(){Guid=10,IsValid=false};Check(!ObjectManager.Me!.TryGetBagItems(out _,out _),"invalid object accepted");});
Case("slot read failure",()=>{World.SlotError=new InvalidOperationException();Check(!ObjectManager.Me!.TryGetBagItems(out _,out _),"slot fault became empty");});
Case("read failure",()=>{World.Error=new InvalidOperationException();Check(!ObjectManager.Me!.TryGetBagItems(out _,out _),"read fault became empty");});
Case("closed process remains fatal",()=>{ObjectManager.Wow!.ProcessHandle=IntPtr.Zero;World.Error=new InvalidOperationException("Process handle is not open");try{ObjectManager.Me!.TryGetBagItems(out _,out _);throw new InvalidOperationException("closed process became optional inventory UNKNOWN");}catch(InvalidProcessException){}});
Case("actor replaced",()=>{var owner=ObjectManager.Me!;World.OnSlots=()=>ObjectManager.Me=new();Check(!owner.TryGetBagItems(out var items,out _)&&items.Count==0,"old actor inventory retained");});
Case("memory replaced",()=>{World.OnSlots=()=>ObjectManager.Wow=new();Check(!ObjectManager.Me!.TryGetBagItems(out var items,out _)&&items.Count==0,"old memory retained");});
foreach(bool cancelled in new[]{false,true}){bool c=cancelled;Case(c?"cancellation preserved":"interrupt preserved",()=>{World.Error=c?new OperationCanceledException():new System.Threading.ThreadInterruptedException();try{ObjectManager.Me!.TryGetBagItems(out _,out _);throw new InvalidOperationException("control flow swallowed");}catch(OperationCanceledException)when(c){}catch(System.Threading.ThreadInterruptedException)when(!c){}});}
Console.WriteLine($"Bag observation scenarios: {pass}/{pass+errors.Count}; complete production enumeration; controlled memory/object leaves.");if(errors.Count!=0)throw new InvalidOperationException(string.Join("; ",errors));}}
""";
}
