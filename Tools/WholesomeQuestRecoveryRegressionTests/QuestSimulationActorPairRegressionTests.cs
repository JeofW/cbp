using System;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using WholesomeAQ;

internal static class QuestSimulationActorPairRegressionTests
{
    [ModuleInitializer]
    internal static void Run()
    {
        MethodInfo? method=typeof(QuestDatasetSimulationRegressionTests).GetMethod("TrySelectActorPair",BindingFlags.NonPublic|BindingFlags.Static);
        if(method==null)throw new InvalidOperationException("FAIL actor-pair fixture: no primary-pair selector; independent race/class selection can create an impossible actor");
        int passed=0;
        foreach(var row in new[]{
            (Name:"unconstrained prefers legal paladin",Races:0,Classes:0,Expected:true,Race:10,Class:2),
            (Name:"warrior cannot borrow blood elf race",Races:0,Classes:1,Expected:true,Race:1,Class:1),
            (Name:"blood elf mage selects allowed original pair",Races:512,Classes:128,Expected:true,Race:10,Class:8),
            (Name:"nonexistent blood elf warrior is rejected",Races:512,Classes:1,Expected:false,Race:0,Class:0),
            (Name:"explicit human mask uses human paladin",Races:1,Classes:2,Expected:true,Race:1,Class:2),
            (Name:"unrepresented class is not invented",Races:0,Classes:1024,Expected:false,Race:0,Class:0),
            (Name:"unconstrained negative masks preserve legal pair",Races:-1,Classes:-1,Expected:true,Race:10,Class:2)})
        {
            using var json=JsonDocument.Parse("[{\"race\":1,\"class\":1},{\"race\":1,\"class\":2},{\"race\":10,\"class\":2},{\"race\":10,\"class\":8}]");
            object?[] args={new QuestEntry{AllowableRaces=row.Races,AllowableClasses=row.Classes},json.RootElement,0,0};
            bool ok=(bool)method.Invoke(null,args)!;
            if(ok!=row.Expected || (int)args[2]!=row.Race || (int)args[3]!=row.Class)
                throw new InvalidOperationException("FAIL actor-pair fixture: "+row.Name);
            passed++;Console.WriteLine("PASS actor-pair fixture: "+row.Name);
        }
        Console.WriteLine($"Actor-pair fixture scenarios: {passed}/7; source-supplied playable pairs; no fabricated race/class product.");
    }
}
