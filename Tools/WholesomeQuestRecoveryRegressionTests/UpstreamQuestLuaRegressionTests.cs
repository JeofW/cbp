using System;
using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;

// Execute the tracked profile condition and managed transport under stock Lua5.1.
// Native quest tuples are controlled; this does not claim quest/server credit.
internal static class UpstreamQuestLuaRegressionTests
{
    [ModuleInitializer]
    internal static void Run()
    {
        string root=UpstreamSeptemberRegressionTests.Root();
        string temp=Path.Combine(Path.GetTempPath(),"cb-upstream-quest-lua-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        try
        {
            RewardLua51Boundary.WriteManagedBridge(temp,File.ReadAllText(Path.Combine(root,"Styx/WoWInternals/Lua.cs")));
            string member=UpstreamSeptemberRegressionTests.Member("Styx/Logic/Profiles/Quest/ProfileHelperFunctionsBase.cs","IsObjectiveComplete");
            File.WriteAllText(Path.Combine(temp,"Probe.cs"),Prefix+member+"}\n");
            var type=typeof(Styx.StyxWoW).Assembly.GetType("Styx.Loaders.SourceCompiler",true)!;
            var compiler=Activator.CreateInstance(type,new object[]{temp})!;
            var result=(CompilerResults)type.GetMethod("Compile")!.Invoke(compiler,null)!;
            var errors=result.Errors.Cast<CompilerError>().Where(e=>!e.IsWarning).ToArray();
            if(errors.Length!=0)throw new InvalidOperationException("Actual quest condition compilation: "+string.Join("; ",errors.Select(e=>e.ToString())));
            var assembly=(Assembly)type.GetProperty("CompiledAssembly")!.GetValue(compiler)!;
            var bridge=assembly.GetType("RewardRecordedBridge",true)!;
            var observe=bridge.GetField("Observe")!;
            var load=(Func<string,uint>)bridge.GetMethod("LoadSize")!.CreateDelegate(typeof(Func<string,uint>));
            var world=assembly.GetType("World",true)!;
            var probe=assembly.GetType("QuestProbe",true)!;
            using var lua=new RewardLua51Boundary.StockLua51(root);
            int passed=0,failed=0;
            foreach(string scenario in new[]{"complete","one","incomplete","missing","nil","zero","text","wrong-header","wrong-objective","replaced-actor","quest-removed"})
            {
                world.GetMethod("Reset")!.Invoke(null,null);
                string mode=scenario;int requests=0;
                observe.SetValue(null,new Func<string,List<string>>(code=>{
                    requests++;
                    var read=lua.Execute(code,load(code),mode,new[]{"",""},Setup);
                    if(mode=="replaced-actor")world.GetMethod("Replace")!.Invoke(null,null);
                    if(mode=="quest-removed")world.GetMethod("Remove")!.Invoke(null,null);
                    // A failed client request returns no values; do not emulate a
                    // newer convenience API absent from the original client.
                    return read.Load==0&&read.Call==0?read.Values:new List<string>();
                }));
                bool expected=scenario is "complete" or "one";
                try
                {
                    bool actual=(bool)probe.GetMethod("Test")!.Invoke(Activator.CreateInstance(probe),new object[]{2,123U})!;
                    if(actual!=expected||requests==0)throw new Exception("expected "+expected+", observed "+actual+", requests="+requests);
                    passed++;Console.WriteLine("PASS upstream quest Lua: "+scenario);
                }
                catch(Exception error){failed++;Console.Error.WriteLine("FAIL upstream quest Lua: "+scenario+": "+error.GetBaseException().Message);}
            }
            observe.SetValue(null,null);
            Console.WriteLine($"Upstream quest Lua scenarios: {passed}/{passed+failed}; assertions={failed}; actual condition/transport and stock Lua5.1; no native/server acceptance.");
            if(failed!=0)throw new InvalidOperationException("Upstream quest condition regression");
        }
        finally {Directory.Delete(temp,true);}
    }
    private const string Prefix="""
global using Styx.Helpers;
using System;using System.Collections.Generic;
public static class Lua{public static T GetReturnVal<T>(string code,uint index)=>RewardRecordedBridge.GetReturnVal<T>(code,index);}
public static class World{public static Player Actor=new();public static void Reset()=>Actor=new();public static void Replace()=>Actor=new();public static void Remove()=>Actor.QuestLog.Available=false;}
public static class ObjectManager{public static Player Me=>World.Actor;}
public class Quest{public uint Id=123;}
public class Log{public bool Available=true;public Quest Quest=new();public Quest GetQuestById(uint id)=>id==123&&Available?Quest:null;}
public class Player{public ulong Guid=1;public bool IsValid=true;public uint MapId;public Log QuestLog=new();}
public class QuestProbe{private Player Me=>ObjectManager.Me;public bool Test(int objective,uint quest)=>IsObjectiveComplete(objective,quest);
""";
    private const string Setup="""
clicks=0
function UnitGUID(unit) return '0x0000000000000001' end
function GetNumQuestLogEntries() return 3 end
function GetQuestLogTitle(index)
 if index==1 then return 'Header',0,0,0,true,false,nil,nil,123 end
 if index==2 then return 'Other',10,0,0,false,false,nil,nil,999 end
 if scenario=='missing' then return 'Absent',10,0,0,false,false,nil,nil,888 end
 return 'Selected',10,0,0,scenario=='wrong-header',false,nil,nil,123
end
function GetNumQuestLeaderBoards(index) return 2 end
function GetQuestLogLeaderBoard(objective,index)
 if index~=3 or objective~=2 or scenario=='wrong-objective' then return nil,nil,nil end
 if scenario=='nil' then return 'Progress','monster',nil end
 if scenario=='text' then return 'Progress','monster','yes' end
 if scenario=='zero' then return 'Progress','monster',0 end
 if scenario=='one' then return 'Progress','monster',1 end
 return 'Progress','monster',scenario~='incomplete'
end
""";
}
