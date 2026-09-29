using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using Styx.Logic.Questing;

internal static class QuestTypedStrategyProgressRegressionTests
{
    private sealed class Failure(string message):Exception(message) { }
    [ModuleInitializer]
    internal static void Run()
    {
        var cases=new List<(string,Action)>();
        const int id=996010, required=3;int go=unchecked((int)0x80000000)|id;
        void Case(string name,int[] ids,int[] requirements,int[] progress,int expectedId,int expectedCount,bool valid,int count=0,bool accepted=true,bool failed=false)
            =>cases.Add((name,()=>
            {
                using var f=new QuestDatasetObservationFixture();f.SetQuest(996001,"Typed strategy",60,ids,requirements,new int[6],new int[6]);
                f.SetAccepted(accepted,failed:failed);f.SetProgress(progress);
                MethodInfo? method=typeof(QuestObjectiveCompletion).GetMethod("TryReadTypedNormalObjectiveProgress",BindingFlags.Public|BindingFlags.Static);
                Check(method!=null,"typed strategy progress reader is missing");
                object?[] args={f.Quest,expectedId,expectedCount,0};bool actual=(bool)method!.Invoke(null,args)!;
                Check(actual==valid,"typed progress validity disagreed");Check((int)args[3]! == (valid?count:0),"typed progress did not preserve its receipt or clear an unknown result");
            }));
        Case("sparse physical slot zero receipt is known",new[]{0,0,id,0},new[]{0,0,required,0},new int[4],id,required,true);
        Case("partial receipt is known",new[]{0,0,id,0},new[]{0,0,required,0},new[]{0,0,2,0},id,required,true,2);
        Case("complete receipt retains its exact count",new[]{0,0,id,0},new[]{0,0,required,0},new[]{0,0,3,0},id,required,true,3);
        Case("other physical slot is not borrowed",new[]{0,0,id,0},new[]{0,0,required,0},new[]{3,0,0,0},id,required,true,0);
        Case("same-numbered gameobject cannot become creature credit",new[]{go,0,0,0},new[]{required,0,0,0},new[]{3,0,0,0},id,required,false);
        Case("typed gameobject credit is readable",new[]{0,go,0,0},new[]{0,required,0,0},new[]{0,2,0,0},go,required,true,2);
        Case("duplicate normal identities are ambiguous",new[]{id,id,0,0},new[]{required,required,0,0},new[]{1,2,0,0},id,required,false);
        Case("different counts cannot disambiguate duplicate identities",new[]{id,id,0,0},new[]{required,required+1,0,0},new[]{1,2,0,0},id,required,false);
        Case("changed required count is rejected",new[]{id,0,0,0},new[]{required+1,0,0,0},new[]{1,0,0,0},id,required,false);
        Case("unaccepted quest is unknown",new[]{id,0,0,0},new[]{required,0,0,0},new[]{1,0,0,0},id,required,false,accepted:false);
        Case("failed quest is unknown",new[]{id,0,0,0},new[]{required,0,0,0},new[]{1,0,0,0},id,required,false,failed:true);
        Case("zero identity cannot read a counter",new int[4],new int[4],new[]{1,0,0,0},0,required,false);
        Case("zero requirement is rejected",new[]{id,0,0,0},new int[4],new[]{1,0,0,0},id,0,false);
        Case("complete unsigned sixteen-bit count is preserved",new[]{id,0,0,0},new[]{65535,0,0,0},new[]{65535,0,0,0},id,65535,true,65535);
        int passed=0,assertions=0,unexpected=0;
        foreach(var test in cases)
        {
            try {test.Item2();passed++;Console.WriteLine("PASS typed strategy progress: "+test.Item1);}
            catch(Failure e){assertions++;Console.Error.WriteLine("FAIL typed strategy progress: "+test.Item1+": "+e.Message);}
            catch(Exception e){unexpected++;Console.Error.WriteLine("ERROR typed strategy progress: "+test.Item1+": "+e);}
        }
        Console.WriteLine($"Typed strategy progress scenarios: {passed}/{cases.Count}; assertions={assertions}; unexpected={unexpected}; actual original-client raw owner, controlled memory, no game.");
        if(assertions+unexpected!=0)throw new InvalidOperationException("Typed strategy progress regression");
    }
    private static void Check(bool value,string reason){if(!value)throw new Failure(reason);}
}
