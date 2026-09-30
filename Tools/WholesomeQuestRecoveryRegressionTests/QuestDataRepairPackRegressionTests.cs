using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using WholesomeAQ;

// Real DataLoader, optional repair bytes and dependency publication. Synthetic
// source references test validation only; they are not shipped quest knowledge.
internal static class QuestDataRepairPackRegressionTests
{
    private sealed class Failure(string message) : Exception(message) { }
    [ModuleInitializer]
    internal static void Run()
    {
        var tests = new List<(string Name, Action Test)>();
        void Case(string name, Action<Fixture> body) => tests.Add((name, () => { using var f = new Fixture(); body(f); }));
        Case("absent pack preserves the original dataset", f => Check(f.Load().Quests.Single().AllowableClasses == null, "absence fabricated metadata"));
        Case("bound pack supplies absent class metadata", f => { f.Write(); Check(f.Load().Quests.Single().AllowableClasses == 2, "loader ignored bound metadata"); });
        Case("repair bytes change the execution fingerprint", f => { string old = f.Loader().ExecutionFingerprint; f.Write(); var loader = f.Loader(); Check(old != loader.ExecutionFingerprint, "repair changes did not invalidate execution context"); });
        Case("same repair bytes have stable fingerprints", f => { f.Write(); Check(f.Loader().ExecutionFingerprint == f.Loader().ExecutionFingerprint, "content fingerprint unstable"); });
        Case("typed missing creature geometry is recovered", f => { f.Write(); var db=f.Load(); Check(db.CreatureSpawns.TryGetValue("991020", out var points) && points.Single().Z == 37 && !db.GameObjectSpawns.ContainsKey("991020"), "wrong or missing typed geometry"); });
        Case("reference geometry never becomes confirmed safe", f => { f.Write(); var db=f.Load(); Check(db.CreatureSpawns.TryGetValue("991020",out var points) && points.Count==1,"reference geometry was not loaded"); var p=points![0]; Check(p.IsKnownSafe != true && p.IsKnownReachable != true, "static reference promoted navigation authority"); });
        Case("explicit missing relation is added without duplicating the quest", f => { f.Write(); var db=f.Load(); Check(db.Quests.Count==1 && db.QuestEnders.Count==1 && db.QuestEnders[0].EnderId==991030, "relation did not preserve quest identity"); });
        Case("dependency metadata does not become an executable quest", f => { f.Write(); var db=f.Load(); var property=typeof(QuestDatabase).GetProperty("DependencyMetadata"); Check(property!=null && property.GetValue(db)!=null && db.Quests.All(q=>q.Id!=991099), "dependency catalog missing or executable"); });
        foreach (string fault in new[] { "wrong-base", "wrong-client", "wrong-core", "bad-sql-hash", "unknown-field", "duplicate-metadata", "foreign-quest", "negative-point-map", "overwrite-geometry", "overwrite-known-metadata", "duplicate-relation", "missing-source", "invalid-delivery-count", "missing-negative-member" })
        {
            string current=fault;
            Case("reject "+current, f => { f.Corrupt(current); f.Write(); bool refused=false; try { f.Load(); } catch (InvalidDataException) { refused=true; } Check(refused,"invalid repair accepted: "+current); });
        }
        Case("invalid pack cannot be cached as successful load", f => { f.Corrupt("wrong-base"); f.Write(); var loader=new DataLoader(f.DataPath); try { loader.Load(); } catch(InvalidDataException) { } f.Pack["QuestDataSha256"]=f.BaseHash; f.Write(); Check(loader.Load().Quests.Single().AllowableClasses==2,"load retained partial failed state"); });
        int passed=0,assertions=0,unexpected=0;
        foreach(var t in tests)
        {
            try { t.Test(); passed++; Console.WriteLine("PASS data repair: "+t.Name); }
            catch(Failure e) { assertions++; Console.Error.WriteLine("FAIL data repair: "+t.Name+": "+e.Message); }
            catch(Exception e) { unexpected++; Console.Error.WriteLine("ERROR data repair: "+t.Name+": "+e); }
        }
        Console.WriteLine($"Data repair scenarios: {passed}/{tests.Count}; assertions={assertions}; unexpected={unexpected}; actual loader; controlled metadata; no game.");
        if(assertions+unexpected!=0)throw new InvalidOperationException("Data repair pack regression");
    }
    private static void Check(bool value,string message) { if(!value)throw new Failure(message); }
    internal sealed class Fixture : IDisposable
    {
        private readonly List<(FieldInfo Field,object? Value)> Authority = typeof(Styx.Logic.Questing.Recovery.QuestPrerequisiteAuthority)
            .GetFields(BindingFlags.NonPublic|BindingFlags.Static).Where(f=>f.Name.StartsWith("_published",StringComparison.Ordinal))
            .Select(f=>(f,f.GetValue(null))).ToList();
        internal readonly string Folder=Path.Combine(Path.GetTempPath(),"cb-data-repair-"+Guid.NewGuid().ToString("N"));
        internal string DataPath=>Path.Combine(Folder,"quest_data.json");
        internal string BaseHash;
        internal JsonObject Pack;
        internal Fixture()
        {
            Directory.CreateDirectory(Folder);
            byte[] bytes=JsonSerializer.SerializeToUtf8Bytes(new QuestDatabase { Quests=new() { new QuestEntry { Id=991001,Name="Repair fixture",MinLevel=1,QuestLevel=1,StartItem=991010,
                Objectives=new() { new QuestObjective { Type=ObjectiveType.TurnInOnly,Index=0 } } } } });
            File.WriteAllBytes(DataPath,bytes);BaseHash=Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
            Pack=JsonNode.Parse("""
            {"Schema":"quest-data-repair-pack-335-v1","ClientBuild":12340,"QuestDataSha256":"BASE","SourceCore":"trinitycore-3.3.5","CoreRevision":"1111111111111111111111111111111111111111","DatabaseRevision":"controlled-fixture","SourceSqlSha256":"2222222222222222222222222222222222222222222222222222222222222222",
             "QuestMetadata":[{"QuestId":991001,"SourceRef":"controlled://quest_template/991001","Fields":{"AllowableClasses":2},"DeliveryItems":[{"ItemId":991010,"Count":1}],"AcceptanceSupplies":[{"ItemId":991010,"Count":1}]}],
             "SpawnAdditions":[{"ObjectType":"Creature","Entry":991020,"SourceRef":"controlled://creature/1","Points":[{"Map":530,"X":10,"Y":20,"Z":37}]}],
             "RelationAdditions":[{"Role":"Ender","QuestId":991001,"ObjectType":"Creature","Entry":991030,"Name":"Controlled ender","SourceRef":"controlled://creature_questender/991001"}],
             "DependencyMetadata":[{"QuestId":991099,"ExclusiveGroup":0,"GroupMembers":[],"SourceRef":"controlled://quest_template_addon/991099"}]}
            """)!.AsObject();
            Pack["QuestDataSha256"]=BaseHash;
        }
        internal void Write()=>File.WriteAllText(Path.Combine(Folder,"quest_data.repairs.json"),Pack.ToJsonString());
        internal QuestDatabase Load()=>new DataLoader(DataPath).Load();
        internal DataLoader Loader() { var l=new DataLoader(DataPath); l.Load(); return l; }
        internal void SetBase(Action<JsonNode> edit) { var data=JsonNode.Parse(File.ReadAllText(DataPath))!;edit(data);Rebase(data); }
        internal void Corrupt(string fault)
        {
            var m=Pack["QuestMetadata"]!.AsArray();var s=Pack["SpawnAdditions"]!.AsArray();var r=Pack["RelationAdditions"]!.AsArray();
            switch(fault)
            {
                case "wrong-base":Pack["QuestDataSha256"]=new string('0',64);break;
                case "wrong-client":Pack["ClientBuild"]=30403;break;
                case "wrong-core":Pack["SourceCore"]="trinitycore-master";break;
                case "bad-sql-hash":Pack["SourceSqlSha256"]="unbound";break;
                case "unknown-field":Pack["ExecuteLua"]="not executed";break;
                case "duplicate-metadata":m.Add(m[0]!.DeepClone());break;
                case "foreign-quest":m[0]!["QuestId"]=991888;break;
                case "negative-point-map":s[0]!["Points"]![0]!["Map"]=-1;break;
                case "overwrite-geometry":
                    var root=JsonNode.Parse(File.ReadAllText(DataPath))!;root["CreatureSpawns"]!["991020"]=JsonNode.Parse("[{\"Map\":530,\"X\":1,\"Y\":2,\"Z\":3,\"IsKnownSafe\":false}]");Rebase(root);break;
                case "overwrite-known-metadata":
                    var data=JsonNode.Parse(File.ReadAllText(DataPath))!;data["Quests"]![0]!["AllowableClasses"]=1;Rebase(data);break;
                case "duplicate-relation":r.Add(r[0]!.DeepClone());break;
                case "missing-source":m[0]!["SourceRef"]="";break;
                case "invalid-delivery-count":m[0]!["DeliveryItems"]![0]!["Count"]=0;break;
                case "missing-negative-member":Pack["DependencyMetadata"]![0]!["ExclusiveGroup"]=-99;Pack["DependencyMetadata"]![0]!["GroupMembers"]=new JsonArray(991099,991098);break;
            }
        }
        private void Rebase(JsonNode data) { byte[] bytes=System.Text.Encoding.UTF8.GetBytes(data.ToJsonString());File.WriteAllBytes(DataPath,bytes);BaseHash=Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();Pack["QuestDataSha256"]=BaseHash; }
        public void Dispose()
        {
            object sync=typeof(Styx.Logic.Questing.Recovery.QuestPrerequisiteAuthority).GetField("DependencySync",BindingFlags.NonPublic|BindingFlags.Static)!.GetValue(null)!;
            lock(sync)foreach(var entry in Authority)entry.Field.SetValue(null,entry.Value);
            Directory.Delete(Folder,true);
        }
    }
}
