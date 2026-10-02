using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

// Actual memory-string reader before repair, then the complete observed reader.
// External bytes/process-read results are controlled. Actual stock Lua5.1 runs
// the production observed-query wrapper; no game/native game dispatch is installed.
internal static class ObservedLuaReturnRegressionTests
{
    [ModuleInitializer]
    internal static void Run()
    {
        string root = Root();
        var lua = CSharpSyntaxTree.ParseText(File.ReadAllText(Path.Combine(root, "Styx/WoWInternals/Lua.cs"))).GetRoot();
        var memory = CSharpSyntaxTree.ParseText(File.ReadAllText(Path.Combine(root, "GreenMagic/Memory.cs"))).GetRoot();
        string[] names = { "ReadObservedLuaString", "ReadObservedLuaWord", "ReadObservedLuaValues", "BuildObservedReturnScript" };
        string methods = string.Join("\n", lua.DescendantNodes().OfType<MethodDeclarationSyntax>()
            .Where(m => names.Contains(m.Identifier.ValueText)).Select(m => m.ToFullString()));
        string fields = string.Join("\n", lua.DescendantNodes().OfType<FieldDeclarationSyntax>()
            .Where(f => f.Declaration.Variables.Any(v => v.Identifier.ValueText is "ObservedReturnLimit" or "ObservedStringLimit"))
            .Select(f => f.ToFullString()));
        bool strict = methods.Contains("ReadObservedLuaString", StringComparison.Ordinal);
        string read = strict ? "ReadObservedLuaString(m,a)" : "m.ReadString(a)";
        string wrap = methods.Contains("BuildObservedReturnScript", StringComparison.Ordinal) ? "BuildObservedReturnScript(script)" : "script";
        string legacy = string.Join("\n", memory.DescendantNodes().OfType<MethodDeclarationSyntax>()
            .Where(m => m.Identifier.ValueText == "ReadString").Select(m => m.ToFullString()));
        string source = Prefix + fields + methods +
            "public static string Read(Memory m,uint a)=>" + read + ";public static string Wrap(string script)=>" + wrap + ";}\n"
            + "public sealed class Memory {public IntPtr ProcessHandle=new IntPtr(1);public byte[] ReadBytes(uint a,int n)=>State.Read(a,n);" + legacy + "}\n" + Cases;
        var refs = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator)
            .Append(typeof(Styx.StyxWoW).Assembly.Location).Distinct().Select(p => MetadataReference.CreateFromFile(p));
        var compile = CSharpCompilation.Create("ObservedLua_" + Guid.NewGuid().ToString("N"),
            new[] { CSharpSyntaxTree.ParseText(source) }, refs, new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using var bytes = new MemoryStream();
        var result = compile.Emit(bytes);
        if (!result.Success) throw new InvalidOperationException(string.Join("; ", result.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error)));
        var assembly = Assembly.Load(bytes.ToArray());
        Exception? memoryFailure = null;
        try { assembly.GetType("ObservedLuaProbe.Cases", true)!.GetMethod("Run")!.Invoke(null, null); }
        catch (TargetInvocationException error) when (error.InnerException != null) { memoryFailure = error.InnerException; }
        var wrapper = (Func<string, string>)assembly.GetType("ObservedLuaProbe.Owner", true)!.GetMethod("Wrap")!
            .CreateDelegate(typeof(Func<string, string>));
        int passed = 0, failed = 0, total = 0;
        using var runtime = new RewardLua51Boundary.StockLua51(root);
        using var session = runtime.BeginSession("clicks=0");
        void Case(string name, string script, string[]? expected)
        {
            total++;
            string query = wrapper(script);
            var reply = session.Execute(query, (uint)Encoding.UTF8.GetByteCount(query));
            bool success = expected == null ? reply.Load == 0 && reply.Call != 0
                : reply.Load == 0 && reply.Call == 0 && reply.Values.SequenceEqual(expected);
            if (success) passed++;
            else { failed++; Console.Error.WriteLine("FAIL observed Lua wrapper: " + name + ": " + reply.Error + " values=" + string.Join("|", reply.Values)); }
        }
        Case("ordinary strings and zero", "return 'tag',0,'',42", new[] { "tag", "0", "", "42" });
        Case("known booleans retain their value", "return true,false", new[] { "true", "false" });
        Case("nil remains unavailable", "return nil", null);
        Case("nil in the middle remains unavailable", "return 'tag',nil,1", null);
        Case("tables are not scalar observations", "return {}", null);
        Case("functions are not scalar observations", "return function()end", null);
        Case("known empty return", "return", Array.Empty<string>());
        Case("unicode byte count stays intact", "return '神聖なる光'", new[] { "神聖なる光" });
        Case("return count fits native buffer", "local t={} for i=1,64 do t[i]=i end return unpack(t)", Enumerable.Range(1,64).Select(i=>i.ToString()).ToArray());
        Case("excess return count is rejected", "local t={} for i=1,65 do t[i]=i end return unpack(t)", null);
        Case("oversized scalar is rejected", "return string.rep('x',385)", null);
        Case("field limit control", "return string.rep('x',384)", new[] { new string('x',384) });
        Console.WriteLine($"Observed Lua wrapper cases: {passed}/{total}; assertions={failed}; stock Lua5.1 x86; native game is not attached.");
        if (memoryFailure != null) ExceptionDispatchInfo.Capture(memoryFailure).Throw();
        if (failed != 0) throw new InvalidOperationException("Observed Lua wrapper regressions failed");
    }
    private static string Root()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "CopilotBuddy.csproj"))) return dir.FullName;
        throw new InvalidOperationException("Tracked source required");
    }
    private const string Prefix = """
using System;using System.Collections.Generic;using System.Globalization;using System.Linq;using System.Text;using System.Reflection;using Styx.Helpers;using Styx.Logic.Combat;
using Memory=ObservedLuaProbe.Memory; namespace ObservedLuaProbe { public static class Owner {
""";
    private const string Cases = """
public static class State {
 public static string Mode;public static int Reads;public static readonly Dictionary<uint,byte[]> Data=new();
 public static byte[] Read(uint address,int count){Reads++;if(Mode=="cancel")throw new OperationCanceledException("controlled read");if(Mode=="wrapped")throw new TargetInvocationException(new OperationCanceledException("controlled wrapped read"));if(Mode=="missing")return null;if(Mode=="short")return new byte[Math.Max(0,count-1)];foreach(var pair in Data){ulong offset=(ulong)address-pair.Key;if(address>=pair.Key&&offset+(ulong)count<=(ulong)pair.Value.Length)return pair.Value.Skip((int)offset).Take(count).ToArray();}return null;}
 public static void Text(string value){var bytes=Encoding.UTF8.GetBytes(value);var data=new byte[1024];Array.Copy(bytes,data,bytes.Length);Data[4096]=data;}
}
public static class Imports {public static bool ReadProcessMemory(IntPtr process,uint address,byte[] destination,int count,out int actual){var bytes=State.Read(address,count);actual=bytes?.Length??0;if(bytes!=null)Array.Copy(bytes,destination,bytes.Length);return bytes!=null&&bytes.Length==count;}}
public static class Cases {
 private sealed class Failure(string message):Exception(message){}
 private static void Check(bool value,string message){if(!value)throw new Failure(message);}
 private static void Unknown(uint address=4096){bool unavailable=false;try{_=Owner.Read(new Memory(),address);}catch(ObservationUnavailableException){unavailable=true;}Check(unavailable,"failed/incomplete read became usable text");}
 public static void Run(){int passed=0,failed=0,unexpected=0,total=0;
  void Case(string name,Action body){total++;State.Data.Clear();State.Mode=null;State.Reads=0;State.Text("Rank 1");try{body();passed++;}catch(Failure error){failed++;Console.Error.WriteLine("FAIL observed Lua memory: "+name+": "+error.Message);}catch(Exception error){unexpected++;Console.Error.WriteLine("ERROR observed Lua memory: "+name+": "+error);}}
  Case("known text",()=>Check(Owner.Read(new Memory(),4096)=="Rank 1","known text changed"));
  Case("known empty string is preserved",()=>{State.Text("");Check(Owner.Read(new Memory(),4096)=="","valid empty rank rejected");});
  Case("known unicode",()=>{State.Text("神聖なる光");Check(Owner.Read(new Memory(),4096)=="神聖なる光","UTF8 identity changed");});
  Case("missing read is not an empty rank",()=>{State.Mode="missing";Unknown();});
  Case("short read is not text",()=>{State.Mode="short";Unknown();});
  Case("zero string pointer is unavailable",()=>Unknown(0));
  Case("unterminated read is not truncated metadata",()=>{State.Data[4096]=Enumerable.Repeat((byte)65,1024).ToArray();Unknown();});
  Case("invalid UTF8 is unavailable",()=>{State.Data[4096][0]=255;State.Data[4096][1]=0;Unknown();});
  foreach(string mode in new[]{"cancel","wrapped"})Case("cancellation / "+mode,()=>{State.Mode=mode;bool cancelled=false;try{_=Owner.Read(new Memory(),4096);}catch(OperationCanceledException){cancelled=true;}catch(Exception error){throw new Failure("cancellation changed to "+error.GetType().Name);}Check(cancelled,"cancellation was swallowed or changed type");});
  Case("read does not touch the next page after a terminator",()=>{State.Data.Clear();State.Data[8190]=new byte[]{65,0};Check(Owner.Read(new Memory(),8190)=="A","valid page-ending string rejected");});
  Case("string length stays bounded",()=>{State.Text(new string('a',384));Check(Owner.Read(new Memory(),4096).Length==384&&State.Reads<=16,"valid bounded field changed or excessive reads");});
  Console.WriteLine($"Observed Lua memory cases: {passed}/{total}; assertions={failed}; unexpected={unexpected}; actual string reader, controlled process-read results.");
  if(failed+unexpected!=0)throw new InvalidOperationException("Observed Lua memory regressions failed");
 }
}}
""";
}
