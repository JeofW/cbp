using System;
using System.CodeDom.Compiler;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;

// Compile the complete tracked ActionSelectReward class without changing its body.
// Item metadata, player, frame, logging and Lua are controlled external boundaries.
// Lua records requests and supplies a configured result; it is not executed here.
internal static class QuestRewardSelectionLifetimeRegressionTests
{
    private sealed class Failure(string message) : Exception(message) { }

    [ModuleInitializer]
    internal static void Run()
    {
        string source = File.ReadAllText(Path.Combine(Root(), "Bots/Quest/Actions/ActionSelectReward.cs"));
        int start = source.IndexOf("public class ActionSelectReward : Action", StringComparison.Ordinal);
        if (start < 0) throw new InvalidOperationException("Tracked reward owner declaration missing");
        string directory = Path.Combine(Path.GetTempPath(), "cb-reward-lifetime-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        int passed = 0, assertions = 0, unexpected = 0, total = 0;
        try
        {
            File.WriteAllText(Path.Combine(directory, "Probe.cs"), Prefix + source.Substring(start) + Suffix);
            Type compilerType = typeof(Styx.StyxWoW).Assembly.GetType("Styx.Loaders.SourceCompiler", true)!;
            object compiler = Activator.CreateInstance(compilerType, new object[] { directory })!;
            var results = (CompilerResults)compilerType.GetMethod("Compile")!.Invoke(compiler, null)!;
            string[] errors = results.Errors.Cast<CompilerError>().Where(e => !e.IsWarning).Select(e => e.ToString()).ToArray();
            if (errors.Length != 0) throw new InvalidOperationException("Tracked reward compilation: " + string.Join(";", errors));
            Assembly assembly = (Assembly)compilerType.GetProperty("CompiledAssembly")!.GetValue(compiler)!;
            MethodInfo execute = assembly.GetType("RewardLifetimeProbe", true)!.GetMethod("Execute")!;
            foreach (var c in new[]
            {
                (Mode: "active", Result: "Success", Calls: 1),
                (Mode: "vendor", Result: "Success", Calls: 1),
                (Mode: "unknown-quest", Result: "Failure", Calls: 0),
                (Mode: "closed-frame", Result: "Failure", Calls: 0),
                (Mode: "outside-world", Result: "Failure", Calls: 0),
                (Mode: "quest-score", Result: "Failure", Calls: 0),
                (Mode: "player-score", Result: "Failure", Calls: 0),
                (Mode: "dead-score", Result: "Failure", Calls: 0),
                (Mode: "reorder-score", Result: "Failure", Calls: 0),
                (Mode: "replace-score", Result: "Failure", Calls: 0),
                (Mode: "link-score", Result: "Failure", Calls: 0),
                (Mode: "count-score", Result: "Failure", Calls: 0),
                (Mode: "partial-score", Result: "Failure", Calls: 0),
                (Mode: "quest-log", Result: "Failure", Calls: 0),
                (Mode: "reorder-log", Result: "Failure", Calls: 0),
                (Mode: "quest-reobserve", Result: "Failure", Calls: 0),
                (Mode: "request-refused", Result: "Failure", Calls: 1)
            })
            {
                total++;
                try
                {
                    string[] actual = (string[])execute.Invoke(null, new object[] { c.Mode })!;
                    Check(actual[0] == c.Result && int.Parse(actual[1]) == c.Calls,
                        "result=" + actual[0] + ", requests=" + actual[1] + "; expected=" + c.Result + "," + c.Calls);
                    passed++; Console.WriteLine("PASS reward selection lifetime: " + c.Mode);
                }
                catch (Failure error) { assertions++; Console.Error.WriteLine("FAIL reward selection lifetime: " + c.Mode + ": " + error.Message); }
                catch (Exception error) { unexpected++; Console.Error.WriteLine("ERROR reward selection lifetime: " + c.Mode + ": " + error); }
            }
            total++;
            try
            {
                string[] actual = (string[])execute.Invoke(null, new object[] { "active" })!;
                string request = actual[2];
                Check(request.Contains("QuestFrameRewardPanel", StringComparison.Ordinal)
                    && request.Contains("GetNumQuestChoices()", StringComparison.Ordinal)
                    && request.Contains("GetQuestItemLink('choice'", StringComparison.Ordinal)
                    && request.Contains("GetQuestItemInfo('choice'", StringComparison.Ordinal)
                    && request.Contains("QuestInfoFrame.itemChoice", StringComparison.Ordinal)
                    && request.Contains("item:1000:", StringComparison.Ordinal)
                    && request.Contains("item:1001:", StringComparison.Ordinal),
                    "final request lacks the captured choice-set and original reward-panel checks");
                passed++; Console.WriteLine("PASS reward selection lifetime: final request carries captured choices");
            }
            catch (Failure error) { assertions++; Console.Error.WriteLine("FAIL reward selection lifetime: final request: " + error.Message); }
            catch (Exception error) { unexpected++; Console.Error.WriteLine("ERROR reward selection lifetime: final request: " + error); }
        }
        finally { Directory.Delete(directory, true); }
        Console.WriteLine($"Quest reward selection lifetime scenarios: {passed}/{total}; assertions={assertions}; unexpected={unexpected}; complete tracked C# owner; controlled metadata/frame/Lua boundaries; no game attached.");
        if (assertions + unexpected != 0) throw new InvalidOperationException("Quest reward selection lifetime regression");
    }

    private const string Prefix = """
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Globalization;
using System.Text.RegularExpressions;
public enum RunStatus { Success, Failure, Running }
public abstract class Action { protected abstract RunStatus Run(object context); public RunStatus Tick(){return Run(null);} }
public sealed class LocalPlayer
{
    public ulong Guid=7;public bool IsValid=true,IsAlive=true;
    public bool CanEquipItem(ItemInfo item){return RewardState.Mode!="vendor";}
}
public static class ObjectManager { public static LocalPlayer Me=new LocalPlayer(); }
public static class StyxWoW { public static bool IsInGame=true; }
public sealed class ItemInfo
{
    public uint Id;public string Name=>"Test "+Id;public int SellPrice=10;
    public static ItemInfo FromId(uint id){return new ItemInfo{Id=id,SellPrice=(int)id};}
}
public sealed class ItemStats { public ItemStats(string link){} }
public sealed class WeightSetEx
{
    public static WeightSetEx CurrentWeightSet=new WeightSetEx();
    public float EvaluateItem(ItemInfo info,ItemStats stats){RewardState.Mutate("score");return info.Id;}
}
public static class ConsumableVendorPolicy
{
    public static uint ParseItemId(string link){var m=Regex.Match(link??"",@"item:(\d+)");return m.Success?uint.Parse(m.Groups[1].Value):0;}
}
public sealed class QuestFrame
{
    public static QuestFrame Instance=new QuestFrame();
    public bool IsVisible=>RewardState.Visible;
    public uint CurrentShownQuestId=>RewardState.Quest;
    public void SelectQuestReward(int index){RewardState.Requests.Add("raw-index:"+index);}
}
public static class Logging
{
    public static void Write(string format,params object[] values){if(format=="Choosing {0}")RewardState.Mutate("log");}
}
public static class Lua
{
    public static T GetReturnVal<T>(string script,uint index)
    {
        if(typeof(T)==typeof(bool))
        {
            RewardState.Requests.Add(script);
            return (T)(object)(RewardState.Mode!="request-refused");
        }
        if(script=="return GetNumQuestChoices()")
        {
            RewardState.Reads++;
            if(RewardState.Reads>1)RewardState.Mutate("reobserve");
            return (T)(object)RewardState.Links.Length;
        }
        var m=Regex.Match(script,@"'choice',\s*(\d+)");
        if(!m.Success)throw new InvalidOperationException("Unexpected observed Lua: "+script);
        int slot=int.Parse(m.Groups[1].Value)-1;
        if(script.Contains("GetQuestItemLink"))return (T)(object)RewardState.Links[slot];
        if(script.Contains("GetQuestItemInfo"))return (T)(object)RewardState.Counts[slot];
        throw new InvalidOperationException("Unexpected Lua: "+script);
    }
}
public static class RewardState
{
    public static string Mode;
    public static uint Quest;
    public static bool Visible,Changed;
    public static int Reads;
    public static string[] Links;
    public static int[] Counts;
    public static readonly List<string> Requests=new List<string>();
    public static string Link(uint id)=>"|Hitem:"+id+":0:0:0:0:0:0:0|h[Test]|h";
    public static void Reset(string mode)
    {
        Mode=mode;Quest=77;Visible=true;Changed=false;Reads=0;
        Links=new[]{Link(1000),Link(1001)};Counts=new[]{1,2};Requests.Clear();
        ObjectManager.Me=new LocalPlayer();StyxWoW.IsInGame=true;
        if(mode=="unknown-quest")Quest=0;
        if(mode=="closed-frame")Visible=false;
        if(mode=="outside-world")StyxWoW.IsInGame=false;
    }
    public static void Mutate(string stage)
    {
        if(Changed||!Mode.EndsWith("-"+stage,StringComparison.Ordinal))return;
        Changed=true;
        string change=Mode.Substring(0,Mode.Length-stage.Length-1);
        switch(change)
        {
            case "quest":Quest=88;break;
            case "player":ObjectManager.Me=new LocalPlayer();break;
            case "dead":ObjectManager.Me.IsAlive=false;break;
            case "reorder":Array.Reverse(Links);Array.Reverse(Counts);break;
            case "replace":Links[1]=Link(1002);break;
            case "link":Links[1]=Links[1].Replace(":0:0:",":1:0:");break;
            case "count":Counts[1]++;break;
            case "partial":Links[1]="";break;
        }
    }
}
""";
    private const string Suffix = """
public static class RewardLifetimeProbe
{
    public static string[] Execute(string mode)
    {
        RewardState.Reset(mode);
        RunStatus result=new ActionSelectReward().Tick();
        return new[]{result.ToString(),RewardState.Requests.Count.ToString(),string.Join("\n",RewardState.Requests)};
    }
}
""";
    private static string Root()
    {
        for(var d=new DirectoryInfo(AppContext.BaseDirectory);d!=null;d=d.Parent)
            if(File.Exists(Path.Combine(d.FullName,"CopilotBuddy.csproj")))return d.FullName;
        throw new InvalidOperationException("Tracked checkout required");
    }
    private static void Check(bool condition,string message){if(!condition)throw new Failure(message);}
}
