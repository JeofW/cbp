using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

// Complete production scalar/stack owners over the real GreenMagic byte reader.
// Only object publication and test-process storage are controlled; no game attached.
internal static class ItemStackObservationRegressionTests
{
    [ModuleInitializer]
    internal static void Run()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root != null && !File.Exists(Path.Combine(root.FullName, "CopilotBuddy.csproj"))) root = root.Parent;
        if (root == null) throw new InvalidOperationException("Tracked source required.");
        var item = CSharpSyntaxTree.ParseText(File.ReadAllText(Path.Combine(root.FullName, "Styx/WoWInternals/WoWObjects/WoWItem.cs"))).GetRoot();
        var obj = CSharpSyntaxTree.ParseText(File.ReadAllText(Path.Combine(root.FullName, "Styx/WoWInternals/WoWObjects/WoWObject.cs"))).GetRoot();
        string Method(SyntaxNode source, string name) => source.DescendantNodes().OfType<MethodDeclarationSyntax>().Single(m => m.Identifier.ValueText == name).ToString();
        var strict = item.DescendantNodes().OfType<MethodDeclarationSyntax>().SingleOrDefault(m => m.Identifier.ValueText == "TryGetStackCount");
        // Baseline mode intentionally retains the old accessor so the failing
        // unknown-read behavior is executable before the repair exists.
        string source = Prefix + Method(obj, "GetDescriptorField") + Method(obj, "ReadObservedUInt32") + Method(item, "GetItemDescriptor")
            + item.DescendantNodes().OfType<PropertyDeclarationSyntax>().Single(p => p.Identifier.ValueText == "StackCount")
            + (strict?.ToString() ?? "public bool TryGetStackCount(out uint count){count=StackCount;return true;}") + "}\n" + Cases;
        var paths = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator)
            .Append(typeof(GreenMagic.Memory).Assembly.Location).Distinct(StringComparer.OrdinalIgnoreCase);
        var compile = CSharpCompilation.Create("ItemStack_" + Guid.NewGuid().ToString("N"), new[] { CSharpSyntaxTree.ParseText(source) },
            paths.Select(p => MetadataReference.CreateFromFile(p)), new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using var bytes = new MemoryStream();
        var result = compile.Emit(bytes);
        if (!result.Success) throw new InvalidOperationException(string.Join("; ", result.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error)));
        try { Assembly.Load(bytes.ToArray()).GetType("StackCases", true)!.GetMethod("Run")!.Invoke(null, null); }
        catch (TargetInvocationException e) when (e.InnerException != null) { ExceptionDispatchInfo.Capture(e.InnerException).Throw(); throw; }
    }
    private const string Prefix = """
#nullable enable
using System;using System.Collections.Generic;using System.Reflection;using System.Runtime.CompilerServices;using System.Runtime.InteropServices;using System.Threading;using GreenMagic;using Styx;using Styx.Logic.Combat;
public static class ObjectManager {public static Memory? Wow;}
public class ObservationUnavailableException:InvalidOperationException {public ObservationUnavailableException(string key,string message):base(message){}public static void RethrowCancellation(Exception e){if(e is OperationCanceledException or ThreadInterruptedException)System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(e).Throw();}}
/* controlled control-flow classifier */ namespace Styx.Logic.Combat {public static class RecoveryActions {public static void RethrowControlFlow(Exception e){ObservationUnavailableException.RethrowCancellation(e);if(e is InvalidProcessException or InvalidExecutorException)System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(e).Throw();}}}
public class Item {const uint DescriptorOffset=8;const int OBJECT_FIELD_COUNT=6,ITEM_FIELD_STACK_COUNT=8;public uint BaseAddress;public ulong Guid=42;public Action? BeforeValidity;public bool IsValid{get{BeforeValidity?.Invoke();return true;}}protected static Memory? Memory=>ObjectManager.Wow;
""";
    private const string Cases = """
public static class StackCases {
const BindingFlags Private=BindingFlags.Instance|BindingFlags.NonPublic;
static void Check(bool value,string why){if(!value)throw new InvalidOperationException(why);}
public static void Run(){int pass=0;var failures=new List<string>();
void Case(string name,Action<Item,IntPtr,Memory> body){var saved=ObjectManager.Wow;var owner=(Memory)RuntimeHelpers.GetUninitializedObject(typeof(Memory));var cache=new ThreadLocal<Dictionary<IntPtr,byte[]>>(()=>new());var enabled=new ThreadLocal<bool>(()=>false);var storage=Marshal.AllocHGlobal(256);try{Marshal.Copy(new byte[256],0,storage,256);typeof(Memory).GetField("_cache",Private)!.SetValue(owner,cache);typeof(Memory).GetField("_cacheEnabled",Private)!.SetValue(owner,enabled);typeof(Memory).GetField("_hProcess",Private)!.SetValue(owner,new IntPtr(-1));ObjectManager.Wow=owner;Marshal.WriteInt32(storage,8,IntPtr.Add(storage,128).ToInt32());var item=new Item{BaseAddress=unchecked((uint)storage.ToInt32())};body(item,storage,owner);pass++;Console.WriteLine("PASS item stack bytes: "+name);}catch(Exception e){failures.Add(name+": "+e.Message);Console.Error.WriteLine("FAIL item stack bytes: "+failures[^1]);}finally{ObjectManager.Wow=saved;typeof(Memory).GetField("_hProcess",Private)!.SetValue(owner,IntPtr.Zero);cache.Dispose();enabled.Dispose();Marshal.FreeHGlobal(storage);}}
Case("complete positive",(item,p,m)=>{Marshal.WriteInt32(p,184,7);Check(item.TryGetStackCount(out uint count)&&count==7,"complete stack lost");});
Case("complete zero",(item,p,m)=>Check(item.TryGetStackCount(out uint count)&&count==0,"known empty became unknown"));
Case("unreadable stack is unknown",(item,p,m)=>{Marshal.WriteInt32(p,8,1);Check(item.StackCount==0,"legacy failed read no longer defaults to zero");Check(!item.TryGetStackCount(out _),"failed real byte read proved empty");});
Case("missing descriptor is unknown",(item,p,m)=>{Marshal.WriteInt32(p,8,0);Check(!item.TryGetStackCount(out _),"missing descriptor proved empty");});
Case("unreadable object is unknown",(item,p,m)=>{item.BaseAddress=1;Check(!item.TryGetStackCount(out _),"unreadable descriptor pointer proved empty");});
Case("missing memory is unknown",(item,p,m)=>{ObjectManager.Wow=null;Check(!item.TryGetStackCount(out _),"missing memory proved empty");});
Case("closed process remains fatal",(item,p,m)=>{typeof(Memory).GetField("_hProcess",Private)!.SetValue(m,IntPtr.Zero);try{item.TryGetStackCount(out _);throw new InvalidOperationException("closed process became optional inventory UNKNOWN");}catch(InvalidProcessException){}});
Case("cancel remains control flow",(item,p,m)=>{item.BeforeValidity=()=>throw new OperationCanceledException();try{item.TryGetStackCount(out _);throw new InvalidOperationException("cancel swallowed");}catch(OperationCanceledException){}});
Case("interrupt remains control flow",(item,p,m)=>{item.BeforeValidity=()=>throw new ThreadInterruptedException();try{item.TryGetStackCount(out _);throw new InvalidOperationException("interrupt swallowed");}catch(ThreadInterruptedException){}});
Case("executor loss remains fatal",(item,p,m)=>{item.BeforeValidity=()=>throw new InvalidExecutorException();try{item.TryGetStackCount(out _);throw new InvalidOperationException("executor loss swallowed");}catch(InvalidExecutorException){}});
Case("memory replacement revokes stack",(item,p,m)=>{item.BeforeValidity=()=>ObjectManager.Wow=null;Check(!item.TryGetStackCount(out _),"replacement inherited old stack");});
Case("hydration is retried",(item,p,m)=>{Marshal.WriteInt32(p,8,1);Check(!item.TryGetStackCount(out _),"first failure became known");Marshal.WriteInt32(p,8,IntPtr.Add(p,128).ToInt32());Marshal.WriteInt32(p,184,9);Check(item.TryGetStackCount(out uint count)&&count==9,"positive hydration hidden");});
Console.WriteLine($"Item stack byte scenarios: {pass}/{pass+failures.Count}; complete production accessor/scalar methods; actual GreenMagic ReadProcessMemory on test-process bytes.");if(failures.Count!=0)throw new InvalidOperationException(string.Join("; ",failures));}}
""";
}
