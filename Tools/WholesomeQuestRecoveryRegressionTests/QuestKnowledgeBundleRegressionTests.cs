using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text.Json;
using WholesomeAQ;

// Exercises the actual tracked data deployment, not synthetic recipe fixtures.
// The immutable manifest covers JSON inputs omitted by legacy C# source scans.
internal static class QuestKnowledgeBundleRegressionTests
{
    private sealed class Failure(string message):Exception(message) { }
    [ModuleInitializer]
    internal static void Run()
    {
        using var authorityScope=new QuestDataRepairPackRegressionTests.Fixture();
        string root=QuestTypedStrategyBehaviorRegressionTests.Root();string folder=Path.Combine(root,"runtime-snapshot/Bots/WholesomeAutoQuest-master/quest_data");
        using var manifest=JsonDocument.Parse(File.ReadAllBytes(Path.Combine(folder,"quest_knowledge_manifest.json")));
        var files=manifest.RootElement.GetProperty("files").EnumerateObject().ToDictionary(p=>p.Name,p=>p.Value.GetString()!);
        int passed=0;void CheckCase(string name,Action test){test();passed++;Console.WriteLine("PASS tracked knowledge bundle: "+name);}
        foreach(var file in files)CheckCase("exact bytes "+file.Key,()=>Check(Hash(Path.Combine(folder,file.Key))==file.Value,"manifest byte mismatch: "+file.Key));
        using var baseline=JsonDocument.Parse(File.ReadAllBytes(Path.Combine(folder,"quest_data.json")));
        var originalIds=baseline.RootElement.GetProperty("Quests").EnumerateArray().Select(q=>q.GetProperty("Id").GetInt32()).OrderBy(x=>x).ToArray();
        var loader=new DataLoader(Path.Combine(folder,"quest_data.json"));var effective=loader.Load();
        CheckCase("all original identities preserved",()=>Check(originalIds.Length==4335 && effective.Quests.Select(q=>q.Id).OrderBy(x=>x).SequenceEqual(originalIds),"effective model changed quest membership"));
        CheckCase("real repair source is declared and bound",()=>Check(loader.RepairPackSource.StartsWith("trinitycore-3.3.5:95657f54779467effea8a1749a61ff93abc1d707:",StringComparison.Ordinal),"wrong primary reference"));
        CheckCase("actual strategy file is present and v2 bound",()=>Check(loader.StrategyPack.Status==QuestStrategyPackStatus.DeclaredAndBound && loader.StrategyPack.SchemaVersion==2 &&
            loader.StrategyPack.QuestDataRepairsSha256==files["quest_data.repairs.json"],"strategy does not bind actual repair deployment"));
        CheckCase("only vetted source recipes are deployed",()=>Check(loader.StrategyPack.Recipes.Count==3 && loader.StrategyPack.Recipes.Select(r=>r.QuestId).Distinct().OrderBy(x=>x)
            .SequenceEqual(new[]{9066,9447}),"unexpected or missing executable recipes"));
        CheckCase("reloading unchanged knowledge retains execution identity",()=>{var second=new DataLoader(Path.Combine(folder,"quest_data.json"));second.Load();Check(second.ExecutionFingerprint==loader.ExecutionFingerprint,"unstable content identity");});
        CheckCase("external metadata stays outside schedulable collection",()=>Check(effective.DependencyMetadata.Count>0 && effective.DependencyMetadata.Values.Any(r=>effective.Quests.All(q=>q.Id!=r.QuestId)),"catalog missing or merged into executable quests"));
        CheckCase("tracked base and knowledge bytes remain unchanged after load",()=>Check(files.All(pair=>Hash(Path.Combine(folder,pair.Key))==pair.Value),"load modified source knowledge"));
        Console.WriteLine($"Tracked knowledge bundle scenarios: {passed}/10; actual 4335-row DataLoader, three source recipes, manifest SHA256; no game or production mutation.");
        if(passed!=10)throw new InvalidOperationException("Knowledge bundle scenario accounting changed");
    }
    private static string Hash(string path)=>Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();
    private static void Check(bool value,string reason){if(!value)throw new Failure(reason);}
}
