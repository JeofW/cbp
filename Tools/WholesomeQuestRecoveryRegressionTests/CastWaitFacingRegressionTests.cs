using System;
using System.CodeDom.Compiler;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

// Execute all actual WaitForCast overloads with real TreeSharp. The existing
// facing behavior is a controlled leaf so these cases distinguish constructing
// a Composite from executing it, while retaining cast/channel/latency policy.
internal static class CastWaitFacingRegressionTests
{
    [ModuleInitializer]
    internal static void Run()
    {
        string root = Root();
        string source = File.ReadAllText(Path.Combine(root, "runtime-snapshot/Routines/Singular wotlk/Helpers/Spell.cs"));
        var methods = CSharpSyntaxTree.ParseText(source).GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>()
            .Where(m => m.Identifier.ValueText == "WaitForCast").ToArray();
        if (methods.Length != 3) throw new InvalidOperationException("Expected the three tracked cast-wait overloads");
        string directory = Path.Combine(Path.GetTempPath(), "cb-cast-wait-face-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            // Method spans retain every statement while excluding surrounding
            // class-region directives that cannot stand alone in the fixture.
            File.WriteAllText(Path.Combine(directory, "Probe.cs"), Prefix + string.Join("\n", methods.Select(m => m.ToString())) + "}\n" + Boundary);
            Type type = typeof(Styx.StyxWoW).Assembly.GetType("Styx.Loaders.SourceCompiler", true)!;
            object compiler = Activator.CreateInstance(type, new object[] { directory })!;
            var result = (CompilerResults)type.GetMethod("Compile")!.Invoke(compiler, null)!;
            var errors = result.Errors.Cast<CompilerError>().Where(e => !e.IsWarning).ToArray();
            if (errors.Length != 0) throw new InvalidOperationException(string.Join("; ", errors.Select(e => e.ToString())));
            var assembly = (Assembly)type.GetProperty("CompiledAssembly")!.GetValue(compiler)!;
            try { assembly.GetType("CastWaitCases", true)!.GetMethod("Run")!.Invoke(null, null); }
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
using System; using System.Collections.Generic; using TreeSharp; using CommonBehaviors.Actions; using Action=TreeSharp.Action;
public sealed class Player {
 public bool IsCasting=true,Wanding;public ulong ChannelObjectGuid;public uint ChanneledCastingSpellId;
 public TimeSpan CurrentCastTimeLeft=TimeSpan.FromSeconds(2);public bool IsWanding()=>Wanding;
}
public static class StyxWoW { public static Player Me=new Player();public static class WoWClient{public static uint Latency=50;} }
public static class Movement {
 public static Composite CreateFaceTargetBehavior(){CastWaitCases.Created++;return new FacingLeaf();}
}
public sealed class FacingLeaf:Composite {
 protected override IEnumerable<RunStatus> Execute(object context){
  CastWaitCases.Faces++;CastWaitCases.SeenContext=context;
  try{if(CastWaitCases.Yields)yield return RunStatus.Running;yield return CastWaitCases.FaceResult;}
  finally{CastWaitCases.Cleanups++;}
 }
}
public static class Spell {
""";
    private const string Boundary = """
public static class CastWaitCases {
 private sealed class Failure(string text):Exception(text){}
 public static int Created,Faces,Cleanups;public static object SeenContext;public static bool Yields;
 public static RunStatus FaceResult=RunStatus.Failure;
 private static void Reset(){StyxWoW.Me=new Player();StyxWoW.WoWClient.Latency=50;Created=Faces=Cleanups=0;SeenContext=null;Yields=false;FaceResult=RunStatus.Failure;}
 private static RunStatus Tick(Composite tree,object context){tree.Start(context);try{return tree.Tick(context);}finally{tree.Stop(context);}}
 public static void Run(){
  int passed=0,assertions=0,unexpected=0,total=0;
  void Case(string name,System.Action test){total++;Reset();try{test();passed++;Console.WriteLine("PASS cast wait facing: "+name);}catch(Failure e){assertions++;Console.Error.WriteLine("FAIL cast wait facing: "+name+": "+e.Message);}catch(Exception e){unexpected++;Console.Error.WriteLine("ERROR cast wait facing: "+name+": "+e);}}
  void Expect(Composite tree,RunStatus expected,int faces){var context=new object();Check(Tick(tree,context)==expected,"wait status changed");Check(Faces==faces,"facing executions="+Faces+", expected="+faces);if(faces>0)Check(ReferenceEquals(SeenContext,context)&&Cleanups==faces,"facing did not receive context/lifecycle cleanup");}
  Case("requested facing executes while casting",()=>Expect(Spell.WaitForCast(true,false),RunStatus.Success,1));
  Case("disabled facing remains a pure wait",()=>Expect(Spell.WaitForCast(false,false),RunStatus.Success,0));
  Case("default overload retains no-facing behavior",()=>Expect(Spell.WaitForCast(),RunStatus.Success,0));
  Case("single-argument overload executes requested facing",()=>Expect(Spell.WaitForCast(true),RunStatus.Success,1));
  Case("no active cast allows the next action",()=>{StyxWoW.Me.IsCasting=false;Expect(Spell.WaitForCast(true,false),RunStatus.Failure,0);});
  Case("wand auto-repeat remains outside cast wait",()=>{StyxWoW.Me.Wanding=true;Expect(Spell.WaitForCast(true,false),RunStatus.Failure,0);});
  Case("object channel remains outside ordinary cast wait",()=>{StyxWoW.Me.ChannelObjectGuid=7;Expect(Spell.WaitForCast(true,false),RunStatus.Failure,0);});
  Case("channeled spell never turns through this helper",()=>{StyxWoW.Me.ChanneledCastingSpellId=101;Expect(Spell.WaitForCast(true,false),RunStatus.Success,0);});
  Case("latency queue window releases wait without facing",()=>{StyxWoW.Me.CurrentCastTimeLeft=TimeSpan.FromMilliseconds(99);Expect(Spell.WaitForCast(true,true),RunStatus.Failure,0);});
  Case("disabled latency tolerance retains the wait",()=>{StyxWoW.Me.CurrentCastTimeLeft=TimeSpan.FromMilliseconds(99);Expect(Spell.WaitForCast(true,false),RunStatus.Success,1);});
  Case("exact latency threshold still waits",()=>{StyxWoW.Me.CurrentCastTimeLeft=TimeSpan.FromMilliseconds(100);Expect(Spell.WaitForCast(true,true),RunStatus.Success,1);});
  Case("unknown zero cast time preserves previous wait policy",()=>{StyxWoW.Me.CurrentCastTimeLeft=TimeSpan.Zero;Expect(Spell.WaitForCast(true,true),RunStatus.Success,1);});
  Case("facing success does not release an active cast",()=>{FaceResult=RunStatus.Success;Expect(Spell.WaitForCast(true,false),RunStatus.Success,1);});
  Case("no needed turn still blocks lower-priority spell",()=>{int fallback=0;var tree=new PrioritySelector(Spell.WaitForCast(true,false),new TreeSharp.Action(_=>{fallback++;return RunStatus.Success;}));Expect(tree,RunStatus.Success,1);Check(fallback==0,"face refusal bypassed cast wait");});
  Case("same factory is reusable without discarded facing objects",()=>{var tree=Spell.WaitForCast(true,false);Tick(tree,new object());Tick(tree,new object());Check(Faces==2&&Cleanups==2&&Created==1,"facing was reconstructed instead of executing its retained child");});
  Case("stopping a running facing child cleans its owned continuation",()=>{Yields=true;var tree=Spell.WaitForCast(true,false);Expect(tree,RunStatus.Running,1);});
  Console.WriteLine($"Cast wait facing scenarios: {passed}/{total}; assertions={assertions}; unexpected={unexpected}; actual WaitForCast overloads and real TreeSharp; controlled cast/facing boundaries; no client/native execution.");
  if(assertions+unexpected!=0)throw new InvalidOperationException("Cast wait facing regression");
 }
 private static void Check(bool ok,string why){if(!ok)throw new Failure(why);}
}
""";
}
