using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Xml.Linq;
using WholesomeAQ;

int passed=0,total=0;
void Check(bool value,string message){if(!value)throw new InvalidOperationException(message);}
void Case(string name,Action test)
{
    total++;
    try{test();passed++;Console.WriteLine("PASS source execution: "+name);}
    catch(Exception error){Console.Error.WriteLine("FAIL source execution: "+name+": "+error);}
}
var root=new DirectoryInfo(AppContext.BaseDirectory);
while(root!=null&&!File.Exists(Path.Combine(root.FullName,"CopilotBuddy.csproj")))root=root.Parent;
if(root==null)throw new InvalidOperationException("Tracked checkout required");
string source=Path.Combine(root.FullName,"runtime-snapshot/Bots/WholesomeAutoQuest-master/quest_data/quest_data.json");
if(args.Length==2&&args[0]=="--export-effective")
{
    byte[] raw=File.ReadAllBytes(source),repairs=File.ReadAllBytes(Path.Combine(Path.GetDirectoryName(source)!,"quest_data.repairs.json"));
    var options=new JsonSerializerOptions{Converters={new JsonStringEnumConverter()}};
    var original=JsonSerializer.Deserialize<QuestDatabase>(raw,options)!;
    var effective=QuestDataRepairPackLoader.Apply(repairs,Convert.ToHexString(SHA256.HashData(raw)).ToLowerInvariant(),original,out string provenance);
    byte[] payload=JsonSerializer.SerializeToUtf8Bytes(effective,options);
    using(var stream=new FileStream(args[1],FileMode.CreateNew))stream.Write(payload);
    File.WriteAllText(args[1]+".identity.json",JsonSerializer.Serialize(new{dataset_sha256=Convert.ToHexString(SHA256.HashData(raw)).ToLowerInvariant(),
        repairs_sha256=Convert.ToHexString(SHA256.HashData(repairs)).ToLowerInvariant(),effective_sha256=Convert.ToHexString(SHA256.HashData(payload)).ToLowerInvariant(),
        quest_count=effective.Quests.Count,source=provenance,actual_repair_loader=true}));
    Console.WriteLine("Exported actual repaired quest model: "+effective.Quests.Count);return;
}
ArelionWorkflowTests.Run(root.FullName);
ArelionRuntimeBoundaryTests.Run(root.FullName);
var loader=new DataLoader(source);var db=loader.Load();
CatalogCoverageTests.Run(source,db,loader.StrategyPack);
Case("Arelion's Mistress materializes a source-specific lure/item workflow",()=>
{
    var quest=db.Quests.Single(q=>q.Id==9472);var objective=quest.Objectives.Single();
    var entry=new QuestPlanEntry{Quest=quest,Stage=QuestWorkStage.Objective,ObjectiveIndex=objective.Index,
        Hotspots=db.CreatureSpawns["17226"].Take(1).ToArray()};
    var xml=XDocument.Parse(new ProfileBuilder().BuildProfileXml(new[]{entry},db,"Hellfire","Fixture",63,null,loader.StrategyPack));
    Check(!xml.Descendants("Objective").Any(x=>(string?)x.Attribute("QuestId")=="9472"),
        "spell-hit credit was emitted as an ordinary creature kill");
    var behavior=xml.Descendants("CustomBehavior").SingleOrDefault(x=>(string?)x.Attribute("File")=="ArelionsMistress");
    Check(behavior!=null&&(string?)behavior.Attribute("QuestId")=="9472","no implemented lure/scroll owner was selected");
    Check(xml.Descendants("ProtectedItems").Descendants("Item").Any(x=>(string?)x.Attribute("Entry")=="29112"),
        "the required lure wine is not protected during its owned journey");
});
Console.WriteLine($"Source execution: {passed}/{total}; actual loaded repaired dataset and materializer; no client or server execution.");
if(passed!=total)Environment.ExitCode=1;
