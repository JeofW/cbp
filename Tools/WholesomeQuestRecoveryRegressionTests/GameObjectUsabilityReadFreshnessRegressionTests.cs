using System.CodeDom.Compiler;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using System.Security.Cryptography;
using System.Text;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

// Complete WoWSubObject methods and the real Memory cache/read path execute.
// Only assembly execution is replaced by an explicit native-return observation
// written to owned test storage. No client process is attached or written.
internal static class GameObjectUsabilityReadFreshnessRegressionTests
{
    [ModuleInitializer]
    internal static void Run()
    {
        string root = Root();
        string path = Path.Combine(root, "Styx/WoWInternals/WoWObjects/WoWSubObject.cs");
        string owner = CSharpSyntaxTree.ParseText(File.ReadAllText(path)).GetRoot().DescendantNodes()
            .OfType<ClassDeclarationSyntax>().Single(row => row.Identifier.ValueText == "WoWSubObject").ToFullString();
        string source = Fixture + owner;
        string directory = Path.Combine(Path.GetTempPath(), "cb-go-usability-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            File.WriteAllText(Path.Combine(directory, "Probe.cs"), source, Encoding.UTF8);
            Type type = typeof(Styx.StyxWoW).Assembly.GetType("Styx.Loaders.SourceCompiler", true)!;
            object compiler = Activator.CreateInstance(type, new object[] { directory })!;
            var result = (CompilerResults)type.GetMethod("Compile")!.Invoke(compiler, null)!;
            var errors = result.Errors.Cast<CompilerError>().Where(row => !row.IsWarning).ToArray();
            if (errors.Length != 0) throw new InvalidOperationException("Usability fixture compile: " + string.Join("; ", errors.Select(row => row.ToString())));
            var assembly = (Assembly)type.GetProperty("CompiledAssembly")!.GetValue(compiler)!;
            var run = assembly.GetType("UsabilityProbe", true)!.GetMethod("Run")!;
            int cases = 0, failed = 0;
            foreach (bool cached in new[] { false, true })
            foreach (bool currentReadiness in new[] { false, true })
            foreach (var values in new[] { ((byte)0, (byte)1), ((byte)1, (byte)0), ((byte)0, (byte)255), ((byte)255, (byte)0) })
            {
                cases++;
                using var observation = new QuestDatasetObservationFixture();
                try
                {
                    run.Invoke(null, new object[] { Styx.WoWInternals.ObjectManager.Wow!, cached, currentReadiness, values.Item1, values.Item2 });
                }
                catch (TargetInvocationException error) when (error.InnerException is InvalidOperationException
                    && error.InnerException.Message == "Returned the prior cached byte instead of the current native AL")
                {
                    failed++;
                    Console.WriteLine($"FAIL usability freshness cache={cached},now={currentReadiness},old={values.Item1},new={values.Item2}: {error.InnerException.Message}");
                }
            }
            Console.WriteLine($"GameObject usability freshness cases: {cases - failed}/{cases}; complete CanUse/CanUseNow and real Memory cache, native return controlled, read-only self-process; no game.");
            Console.WriteLine("Usability complete-source SHA256: " + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(source))).ToLowerInvariant());
            if (failed != 0) throw new InvalidOperationException("Usability freshness failures: " + failed);
        }
        finally { Directory.Delete(directory, true); }
    }
    private static string Root()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory != null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "CopilotBuddy.csproj"))) return directory.FullName;
        throw new InvalidOperationException("Tracked source checkout required");
    }
    private const string Fixture = """
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using GreenMagic;
using Styx.Helpers;
public enum GameError { None }
public sealed class WoWGameObject { }
public static class ObjectManager
{
    public static Memory Wow;
    public static ExecutorRand Executor;
    public static T GetObjectByGuid<T>(ulong guid) where T:class => null;
}
public static class StyxWoW { public static Memory Memory=>ObjectManager.Wow; }
public sealed class ExecutorRand
{
    public readonly object AssemblyLock=new();public uint ReturnPointer;public byte NativeReturn;public int Calls;
    public readonly List<string> Lines=new();
    public void Clear()=>Lines.Clear();public void AddLine(string line)=>Lines.Add(line);
    public void Execute(){Calls++;Marshal.WriteByte(new IntPtr(unchecked((int)ReturnPointer)),NativeReturn);}
}
public static class UsabilityProbe
{
    public static void Run(Memory memory,bool cached,bool currentReadiness,byte oldValue,byte returnedValue)
    {
        IntPtr storage=Marshal.AllocHGlobal(4);
        try
        {
            uint pointer=unchecked((uint)storage.ToInt32());
            Marshal.WriteInt32(storage,0);Marshal.WriteByte(storage,oldValue);
            ObjectManager.Wow=memory;ObjectManager.Executor=new ExecutorRand{ReturnPointer=pointer,NativeReturn=returnedValue};
            using(memory.TemporaryCacheState(cached))
            {
                if(memory.Read<byte>(pointer)!=oldValue)throw new InvalidOperationException("Owned return observation did not prime correctly");
                var subject=new WoWSubObject(1);
                bool actual=currentReadiness?subject.CanUseNow():subject.CanUse();
                if(actual!=(returnedValue!=0))throw new InvalidOperationException("Returned the prior cached byte instead of the current native AL");
                if(memory.CacheEnabled!=cached)throw new InvalidOperationException("The reader changed its caller's cache lifetime");
                if(ObjectManager.Executor.Calls!=1)throw new InvalidOperationException("The reader submitted more than one native query");
                string slot=currentReadiness?"mov eax, [eax+28]":"mov eax, [eax+24]";
                if(!ObjectManager.Executor.Lines.Contains(slot))throw new InvalidOperationException("The existing vtable selector changed");
            }
        }
        finally{ObjectManager.Wow=null;ObjectManager.Executor=null;Marshal.FreeHGlobal(storage);}
    }
}
""";
}
