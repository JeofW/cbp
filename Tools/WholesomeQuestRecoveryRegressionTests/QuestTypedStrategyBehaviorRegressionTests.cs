using System;
using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using Styx.Logic.Questing;

internal static class QuestTypedStrategyBehaviorRegressionTests
{
    private const BindingFlags Hidden=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance|BindingFlags.Static;
    private sealed class Failure(string message):Exception(message) { }
    [ModuleInitializer]
    internal static void Run()=>QuestClosureIsolation.Run(typeof(QuestTypedStrategyBehaviorRegressionTests),nameof(RunIsolated));
    internal static void RunIsolated()
    {
        string root=Root(),temp=Path.Combine(Path.GetTempPath(),"cb-typed-behavior-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(temp);
        bool logging=Styx.Helpers.Logging.FileLogging;Styx.Helpers.Logging.FileLogging=false;
        int passed=0,assertions=0,unexpected=0,total=0;
        try
        {
            File.Copy(Path.Combine(root,"runtime-snapshot/Quest Behaviors/UseItemOn.cs"),Path.Combine(temp,"UseItemOn.cs"));
            Type compilerType=typeof(Styx.StyxWoW).Assembly.GetType("Styx.Loaders.SourceCompiler",true)!;
            object compiler=Activator.CreateInstance(compilerType,new object[]{temp})!;
            var result=(CompilerResults)compilerType.GetMethod("Compile",Hidden)!.Invoke(compiler,null)!;
            var errors=result.Errors.Cast<CompilerError>().Where(e=>!e.IsWarning).ToArray();
            if(errors.Length!=0)throw new InvalidOperationException("Real runtime behavior compilation: "+string.Join(";",errors.Select(e=>e.ToString())));
            Type kind=((Assembly)compilerType.GetProperty("CompiledAssembly",Hidden)!.GetValue(compiler)!).GetType("Styx.Bot.Quest_Behaviors.UseItemOn.UseItemOn",true)!;
            void Case(string name,Action<QuestDatasetObservationFixture,object> test,int[]? ids=null,int[]? counts=null)
            {
                total++;object? owner=null;
                try
                {
                    using var f=new QuestDatasetObservationFixture();f.SetQuest(991001,"Typed behavior",60,ids??new[]{0,0,991020,0},counts??new[]{0,0,3,0},new int[6],new int[6]);
                    f.LoadProfile("<HBProfile><Name>Typed behavior</Name><QuestOrder/></HBProfile>");
                    owner=Activator.CreateInstance(kind,new object[]{new Dictionary<string,string>{["QuestId"]="991001",["ItemId"]="991010",["MobId"]="991020",["MobType"]="Npc",["MobState"]="Alive",
                        ["SuccessEvidence"]="ObjectiveProgress",["ObjectiveIndex"]="17",["CreditId"]="991020",["RequiredCreditCount"]="3",["MaxAttempts"]="3",["Range"]="5",["WaitTime"]="100",["X"]="10",["Y"]="10",["Z"]="10"}})!;
                    GC.SuppressFinalize(owner);
                    Check(kind.GetProperty("CreditId")!=null,"typed behavior contract was ignored");
                    test(f,owner);passed++;Console.WriteLine("PASS typed strategy behavior: "+name);
                }
                catch(Failure e){assertions++;Console.Error.WriteLine("FAIL typed strategy behavior: "+name+": "+e.Message);}
                catch(Exception e){unexpected++;Console.Error.WriteLine("ERROR typed strategy behavior: "+name+": "+e);}
                finally {if(owner!=null)kind.GetMethod("Dispose",Type.EmptyTypes)!.Invoke(owner,null);}
            }
            int? Count(object owner)=>(int?)kind.GetMethod("ReadObjectiveCount",Hidden)!.Invoke(owner,null);
            bool Ack(object owner)=>(bool)kind.GetMethod("HasAuthoritativeSuccess",Hidden)!.Invoke(owner,null)!;
            void Start(object owner)=>kind.GetMethod("OnStart",Hidden)!.Invoke(owner,null);
            Case("sparse typed credit captures known zero rather than ordinal17",(f,o)=>{Start(o);Check((int?)kind.GetProperty("InitialObjectiveCount")!.GetValue(o)==0,"initial typed count missing");Check(!Ack(o),"initial zero was completed");});
            Case("partial increase is a progress acknowledgement, not quest completion",(f,o)=>{Start(o);f.SetProgress(new[]{0,0,1,0});Check(Ack(o),"actual counter increase not acknowledged");Check(!f.Quest.IsCompleted,"progress fabricated ready flag");});
            Case("another counter increase cannot acknowledge this strategy",(f,o)=>{Start(o);f.SetProgress(new[]{1,0,0,0});Check(!Ack(o),"borrowed neighboring counter");});
            Case("complete typed objective is acknowledged on restart",(f,o)=>{f.SetProgress(new[]{0,0,3,0});Start(o);Check(Ack(o),"completed credit not recognized on restart");});
            Case("unknown required-count mapping never dispatches as ordinary zero",(f,o)=>Check(Count(o)==null,"mismatched metadata read as current count"),counts:new[]{0,0,4,0});
            Case("duplicate identities stay ambiguous",(f,o)=>Check(Count(o)==null,"duplicate typed credit read as known"),ids:new[]{991020,0,991020,0},counts:new[]{3,0,3,0});
            Case("abandonment revokes typed acknowledgement",(f,o)=>{Start(o);f.SetAccepted(false);Check(Count(o)==null && !Ack(o),"abandoned quest acknowledged");});
            Case("failure revokes typed acknowledgement",(f,o)=>{Start(o);f.SetAccepted(true,failed:true);f.SetProgress(new[]{0,0,2,0});Check(Count(o)==null && !Ack(o),"failed quest acknowledged");});
            Case("regressed counter is not progress",(f,o)=>{f.SetProgress(new[]{0,0,2,0});Start(o);f.SetProgress(new[]{0,0,1,0});Check(!Ack(o),"regression became progress");});
            Case("disposed behavior cannot read or acknowledge a fresh count",(f,o)=>{Start(o);kind.GetMethod("Dispose",Type.EmptyTypes)!.Invoke(o,null);f.SetProgress(new[]{0,0,3,0});Check(Count(o)==null && !Ack(o),"disposed owner borrowed later progress");});
        }
        finally {Styx.Helpers.Logging.FileLogging=logging;Directory.Delete(temp,true);}
        Console.WriteLine($"Typed strategy behavior scenarios: {passed}/{total}; assertions={assertions}; unexpected={unexpected}; full real compiler/constructor/start/acknowledgement, real raw memory fixture; no game.");
        if(assertions+unexpected!=0)throw new InvalidOperationException("Typed strategy behavior regression");
    }
    internal static string Root(){for(var d=new DirectoryInfo(AppContext.BaseDirectory);d!=null;d=d.Parent)if(File.Exists(Path.Combine(d.FullName,"CopilotBuddy.csproj")))return d.FullName;throw new InvalidOperationException("Checkout required");}
    private static void Check(bool value,string reason){if(!value)throw new Failure(reason);}
}
