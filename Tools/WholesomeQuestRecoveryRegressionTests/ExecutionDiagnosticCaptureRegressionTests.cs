using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Bots.Quest.Objectives;
using Bots.Quest.QuestOrder;
using Styx.Logic.Questing;

internal static class ExecutionDiagnosticCaptureRegressionTests
{
    [ModuleInitializer]
    internal static void Run()
    {
        using var live=new QuestDatasetObservationFixture();
        live.SetQuest(10161,"In Case of Emergency...",58,new int[4],new int[4],new[]{28116,0,0,0,0,0},new[]{30,0,0,0,0,0});
        live.LoadProfile("<HBProfile><Name>Execution diagnostic</Name><MinLevel>1</MinLevel><MaxLevel>80</MaxLevel><Quest Id=\"10161\" Name=\"Debris\"><Objective Type=\"CollectItem\" ItemId=\"28116\" CollectCount=\"30\"><CollectFrom><GameObject Id=\"183394\" /><GameObject Id=\"183395\" /></CollectFrom><Hotspots><Hotspot X=\"10\" Y=\"10\" Z=\"10\" /></Hotspots></Objective></Quest><QuestOrder /></HBProfile>");
        using var objective=new CollectItemObjective(live.Quest,new(),live.Quest.GetObjectives().First(o=>o.ID==28116),new());
        using var behavior=new ForcedQuestObjective(objective);
        Type? type=typeof(PlayerQuest).Assembly.GetType("Styx.Logic.Questing.QuestExecutionDiagnostics");
        var method=type?.GetMethod("Capture");
        if(method==null)throw new InvalidOperationException("State-aware diagnostic capture is missing");
        string json=(string)method.Invoke(null,new object[]{behavior,new[]{0,0,0,0,5},new[]{0,0,0,0,5},30d,0})!;
        using var doc=JsonDocument.Parse(json);var root=doc.RootElement;
        string[] fields={"quest_id","objective_index","objective_id","primitive","progress_before","progress_current","selected_guid","selected_entry","source_type","declared_sources","static_coordinates","live_coordinates","player_coordinates","map_id","z_delta","mounted","flying","movement_flags","movement_known","navigation_destination","navigation_result","interaction_range","line_of_sight","inventory_status","inventory_count","free_normal_bag_slots","blacklisted","retry_state","blocking_node","last_interaction","last_interaction_utc","phase","reason"};
        foreach(var name in fields)if(!root.TryGetProperty(name,out _))throw new InvalidOperationException("Missing objective diagnostic field "+name);
        if(root.GetProperty("quest_id").GetInt32()!=10161||root.GetProperty("objective_id").GetInt32()!=28116||root.GetProperty("objective_index").GetInt32()!=0)
            throw new InvalidOperationException("Diagnostic does not identify the actual quest/objective");
        if(root.GetProperty("declared_sources").GetArrayLength()!=2||root.GetProperty("static_coordinates").GetArrayLength()!=1)
            throw new InvalidOperationException("Diagnostic omitted declared source or ground coordinates");
        if(root.GetProperty("inventory_count").ValueKind!=JsonValueKind.Null)
            throw new InvalidOperationException("Unmapped client inventory globals were reported as an authoritative zero");
        if(root.GetProperty("player_coordinates")[2].GetDouble()!=10)
            throw new InvalidOperationException("Diagnostic changed the actual player Z");
        var existing=typeof(ForcedBehavior).GetProperty("ExistingBranch")!.GetValue(behavior);
        if(existing!=null)throw new InvalidOperationException("Diagnostic observation lazily created a behavior tree");
        var classify=type!.GetMethod("ClassifyUnselected",BindingFlags.Public|BindingFlags.Static);
        if(classify==null)throw new InvalidOperationException("Unselected live candidates cannot be distinguished from waiting for respawn");
        var cases=new (int? Count,bool Arrived,double Seconds,string Expected)[]{
            (2,true,90,"live-object-found-but-not-selected"),
            (1,false,20,"live-object-found-but-not-selected"),
            (0,true,30,"arrived-but-no-live-object"),
            (0,true,90,"waiting-for-respawn"),
            (0,false,90,"travelling"),
            (null,true,90,"live-acquisition-unobserved")};
        foreach(var item in cases)
            if((string)classify.Invoke(null,new object?[]{item.Count,item.Arrived,item.Seconds})! != item.Expected)
                throw new InvalidOperationException("Candidate observation was misclassified: "+item.Expected);
        Console.WriteLine($"Execution diagnostic fields: {fields.Length}/{fields.Length}; actual quest/profile/player capture; null inventory remains unknown; no tree created or game action.");
    }
}
