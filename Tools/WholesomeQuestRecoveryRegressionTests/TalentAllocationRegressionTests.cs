using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

// Full Talented component and its actual template/planning types. Native Lua is
// a controlled boundary here; a submission is not a learned-rank acknowledgement.
internal static class TalentAllocationRegressionTests
{
    [ModuleInitializer]
    internal static void Run()
    {
        var root=new DirectoryInfo(AppContext.BaseDirectory);
        while(root!=null&&!File.Exists(Path.Combine(root.FullName,"CopilotBuddy.csproj")))root=root.Parent;
        if(root==null)throw new InvalidOperationException("Tracked source required.");
        string folder=Path.Combine(root.FullName,"runtime-snapshot/Plugins/Talented");
        string[] files={"Talented.cs","TalentPlacement.cs","TalentTree.cs","TalentAllocationPolicy.cs"};
        string owners=string.Join("\n",files.Where(f=>File.Exists(Path.Combine(folder,f))).Select(f=>
            string.Join("\n",CSharpSyntaxTree.ParseText(File.ReadAllText(Path.Combine(folder,f))).GetRoot()
                .DescendantNodes().OfType<ClassDeclarationSyntax>().Where(x=>x.Parent is not ClassDeclarationSyntax).Select(x=>x.ToString()))));
        var trusted=(string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")??throw new InvalidOperationException("References missing.");
        // Disambiguate fixture constructors from the params-array overload; the
        // extracted component source is passed through without rewriting.
        var compilation=CSharpCompilation.Create("TalentAllocation_"+Guid.NewGuid().ToString("N"),new[]{CSharpSyntaxTree.ParseText(Prefix+owners+Cases.Replace("new(3,", "new TalentPlacement(3,"))},
            trusted.Split(Path.PathSeparator).Distinct(StringComparer.OrdinalIgnoreCase).Select(x=>MetadataReference.CreateFromFile(x)),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using var output=new MemoryStream();var result=compilation.Emit(output);
        if(!result.Success)throw new InvalidOperationException(string.Join("; ",result.Diagnostics.Where(x=>x.Severity==DiagnosticSeverity.Error)));
        var assembly=Assembly.Load(output.ToArray());
        try{assembly.GetType("TalentCases",true)!.GetMethod("Run")!.Invoke(null,null);}
        catch(TargetInvocationException e)when(e.InnerException!=null){ExceptionDispatchInfo.Capture(e.InnerException).Throw();throw;}
        VerifyNativeRequest(assembly, root.FullName);
    }
    private static void VerifyNativeRequest(Assembly assembly, string root)
    {
        var observation=assembly.GetType("TalentObservation",true)!;
        object point=Activator.CreateInstance(observation)!;
        foreach(var pair in new (string Field,object Value)[]{("Tab",3),("Index",7),("Tier",2),("Rank",1),("Maximum",5),("Name","Tést's Talent\\Name")})
            observation.GetField(pair.Field)!.SetValue(point,pair.Value);
        string request=(string)assembly.GetType("TalentAllocationPolicy",true)!.GetMethod("BuildRequest",BindingFlags.Static|BindingFlags.NonPublic)!
            .Invoke(null,new[]{point,(object)1,"0x0000000000000001"})!;
        using var lua=new RewardLua51Boundary.StockLua51(root);
        int passed=0;
        foreach(string state in new[]{"valid","group","actor","preview","name","tier","rank","maximum","prerequisite","no-points"})
        {
            string setup="clicks=0; function GetActiveTalentGroup() return "+(state=="group"?2:1)+" end; "+
                "function UnitGUID() return '0x000000000000000"+(state=="actor"?"2":"1")+"' end; "+
                "function GetGroupPreviewTalentPointsSpent() return "+(state=="preview"?1:0)+" end; "+
                "function GetUnspentTalentPoints() return "+(state=="no-points"?0:1)+" end; "+
                "function GetTalentInfo(tab,index,inspect,pet,group) assert(tab==3 and index==7 and inspect==false and pet==false and group==1); return "+
                (state=="name"?"'Other'":"'Tést\\'s Talent\\\\Name'")+",'icon',"+(state=="tier"?3:2)+",1,"+(state=="rank"?2:1)+","+(state=="maximum"?3:5)+",false,"+(state=="prerequisite"?"false":"true")+" end; "+
                "function LearnTalent(tab,index,pet,group) assert(tab==3 and index==7 and pet==false and group==1); clicks=clicks+1 end";
            var result=lua.Execute(request,(uint)System.Text.Encoding.UTF8.GetByteCount(request),state,new[]{"a","b"},setup);
            if(result.Load!=0||result.Call!=0||result.Values.Count!=1||result.Clicks!=(state=="valid"?1:0)
                ||(result.Values[0]=="submitted")!=(state=="valid"))
                throw new InvalidOperationException("Actual Lua5.1 talent dispatch failed for "+state+": "+result.Error);
            passed++;
        }
        Console.WriteLine($"Talent Lua5.1 cases: {passed}/10; exact generated request; UTF8 and quote escaping; native learning leaf controlled.");
    }
    private const string Prefix="""
#nullable disable
using System;using System.Collections.Generic;using System.Linq;using System.Reflection;using System.Drawing;using System.Globalization;using System.Text.RegularExpressions;using System.Xml.Linq;
public enum WoWClass{None,Paladin,Mage}
public class LocalPlayer{public ulong Guid=1;public uint MapId=530;public int Level=60;public bool IsValid=true,IsAlive=true,Combat;public WoWClass Class=WoWClass.Paladin;}
public static class StyxWoW{public static LocalPlayer Me=new();public static bool IsInGame=true;}
public static class ObjectManager{public static object Wow=new();}public static class TreeRoot{public static bool IsRunning=true;}
public class FrameLock:IDisposable{public void Dispose(){}}
public class HBPlugin{public virtual void Pulse(){}public virtual void Initialize(){}public virtual void Dispose(){}public virtual void OnEnable(){}public virtual void OnDisable(){}public virtual string Name=>"";public virtual string Author=>"";public virtual Version Version=>new(1,0);public virtual bool WantButton=>false;public virtual string ButtonText=>"";public virtual void OnButtonPress(){}}
public class FormConfig{public void ShowDialog(){}}
public class TalentedSettings{public static TalentedSettings Instance=new();public bool FirstUseAfterChange;public TalentTree ChoosenTalentBuild;public string ChoosenTalentBuildName=>ChoosenTalentBuild?.BuildName;}
public static class Logging{public static void Write(Color color,string text,params object[] args){}public static void Write(string text,params object[] args){}public static void WriteDebug(string text,params object[] args){}public static void WriteException(Exception e){}}
public class LuaEventArgs:EventArgs{}
public static class Lua{
 public static class Events{public static void AttachEvent(string name,EventHandler<LuaEventArgs> callback){}public static void DetachEvent(string name,EventHandler<LuaEventArgs> callback){}}
 public static T GetReturnVal<T>(string text,uint index){
  if(typeof(T)==typeof(string)){
   if(text.Contains("LearnTalent(")){TalentCases.LastScript=text;TalentCases.BeforeSubmit?.Invoke();if(TalentCases.NativeResult=="submitted"){var m=Regex.Match(text,@"LearnTalent\((\d+),\s*(\d+)");TalentCases.Requests.Add((int.Parse(m.Groups[1].Value),int.Parse(m.Groups[2].Value)));}return (T)(object)TalentCases.NativeResult;}
   if(text.Contains("UnitGUID"))return (T)(object)"0x0000000000000001";
   return (T)(object)"";
  }
  int value=0;
  if(text.Contains("GetNumTalents")){int tab=int.Parse(Regex.Match(text,@"\((\d+)").Groups[1].Value);value=TalentCases.Rows.Keys.Count(k=>k.Item1==tab);}
  else if(text.Contains("GetGroupPreviewTalentPointsSpent"))value=TalentCases.Preview;
  else if(text.Contains("GetActiveTalentGroup"))value=TalentCases.Group;
  else if(text.Contains("UnitCharacterPoints")||text.Contains("GetUnspentTalentPoints"))value=TalentCases.Available;
  else if(text.Contains("GetTalentTabInfo"))value=1;
  return (T)(object)value;
 }
 public static List<string> GetReturnValues(string text){var m=Regex.Match(text,@"GetTalentInfo\((\d+),\s*(\d+)");var key=(int.Parse(m.Groups[1].Value),int.Parse(m.Groups[2].Value));TalentCases.AfterInfo?.Invoke();if(TalentCases.Partial)return new(){"incomplete"};return TalentCases.Rows[key].ToList();}
 public static void DoString(string text){if(text.Contains("AddPreviewTalentPoints")){var m=Regex.Match(text,@"\((\d+),\s*(\d+)");TalentCases.Requests.Add((int.Parse(m.Groups[1].Value),int.Parse(m.Groups[2].Value)));}if(text.Contains("LearnPreviewTalents"))TalentCases.Commits++;}
}
""";
    private const string Cases="""
public static class TalentCases{
 public static Dictionary<(int,int),string[]> Rows=new();public static List<(int,int)> Requests=new();public static int Available,Preview,Group,Commits;public static bool Partial;public static Action AfterInfo,BeforeSubmit;public static string NativeResult,LastScript;
 static void Check(bool ok,string why){if(!ok)throw new InvalidOperationException(why);}
 static string[] Row(string name,int rank=0,int max=5,int tier=1,bool prerequisite=true)=>new[]{name,"icon",tier.ToString(),"1",rank.ToString(),max.ToString(),"false",prerequisite?"true":"false","0","1","1"};
 static Talented New(params TalentPlacement[] wanted){var bot=new Talented();var build=new TalentTree(3,wanted.ToList(),WoWClass.Paladin,"fixture");TalentedSettings.Instance.ChoosenTalentBuild=build;typeof(Talented).GetField("_talentBuild",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(bot,build);return bot;}
 static void Invoke(Talented bot){try{typeof(Talented).GetMethod("HandleTalentPointsChanged",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(bot,new object[]{null,null});}catch(TargetInvocationException e){throw e.InnerException;}}
 static void NoMutation(Talented bot){Invoke(bot);Check(Requests.Count==0&&Commits==0,"an unowned or invalid allocation reached a learning mutation");}
 static void Reset(){Rows=new(){{(1,1),Row("Holy")},{(2,1),Row("Protection")},{(3,1),Row("Actual")}};Requests.Clear();Available=1;Preview=Commits=0;Group=1;Partial=false;AfterInfo=BeforeSubmit=null;NativeResult="submitted";LastScript="";StyxWoW.Me=new();StyxWoW.IsInGame=true;ObjectManager.Wow=new();TreeRoot.IsRunning=true;TalentedSettings.Instance=new();}
 public static void Run(){int passed=0;var failures=new List<string>();
 void Case(string name,Action test){Reset();try{test();passed++;Console.WriteLine("PASS talent allocation: "+name);}catch(Exception e){failures.Add(name+": "+e.Message);Console.Error.WriteLine("FAIL talent allocation: "+failures[^1]);}}
 Case("one available valid point",()=>{Invoke(New(new(3,1,1,"Actual")));Check(Requests.SequenceEqual(new[]{(3,1)}),"valid point did not target the observed talent");});
 Case("name mismatch cannot borrow an index",()=>NoMutation(New(new(3,1,1,"Zealotry"))));
 Case("validate the entire template before any point",()=>NoMutation(New(new(3,1,1,"Actual"),new(3,2,1,"Rule of Law"))));
 Case("resolve a stale exported index by exact observed name",()=>{Rows[(3,2)]=Row("Desired");Invoke(New(new(3,1,1,"Desired")));Check(Requests.SequenceEqual(new[]{(3,2)}),"legacy index changed the intended talent");});
 Case("same name in another tree is not identity",()=>{Rows[(2,1)]=Row("Desired");NoMutation(New(new(3,1,1,"Desired")));});
 Case("rank beyond actual maximum is invalid",()=>{Rows[(3,1)]=Row("Actual",max:2);NoMutation(New(new(3,1,3,"Actual")));});
 Case("missing names cannot authorize a raw index",()=>NoMutation(New(new(3,1,1))));
 Case("duplicate cumulative entries are ambiguous",()=>NoMutation(New(new(3,1,1,"Actual"),new(3,1,2,"Actual"))));
 Case("incomplete talent API is unknown",()=>{Partial=true;NoMutation(New(new(3,1,1,"Actual")));});
 Case("actor replacement during metadata",()=>{AfterInfo=()=>StyxWoW.Me=new();NoMutation(New(new(3,1,1,"Actual")));});
 Case("memory replacement during metadata",()=>{AfterInfo=()=>ObjectManager.Wow=new();NoMutation(New(new(3,1,1,"Actual")));});
 Case("active group replacement during metadata",()=>{AfterInfo=()=>Group=2;NoMutation(New(new(3,1,1,"Actual")));});
 Case("foreign preview is never committed or reset",()=>{Preview=2;NoMutation(New(new(3,1,1,"Actual")));});
 Case("zero points never commits an unrelated preview",()=>{Available=0;NoMutation(New(new(3,1,1,"Actual")));});
 Case("already learned target has no mutation",()=>{Rows[(3,1)]=Row("Actual",rank:1);NoMutation(New(new(3,1,1,"Actual")));});
 Case("inactive plugin cannot learn from an old event",()=>{var bot=New(new(3,1,1,"Actual"));bot.OnDisable();NoMutation(bot);});
 Case("stopped bot cannot learn from an old event",()=>{TreeRoot.IsRunning=false;NoMutation(New(new(3,1,1,"Actual")));});
 Case("wrong class cannot learn from an old event",()=>{StyxWoW.Me.Class=WoWClass.Mage;NoMutation(New(new(3,1,1,"Actual")));});
 Case("combat defers learning",()=>{StyxWoW.Me.Combat=true;NoMutation(New(new(3,1,1,"Actual")));});
 Case("one request awaits rank acknowledgement before another",()=>{Available=5;var bot=New(new(3,1,5,"Actual"));Invoke(bot);Invoke(bot);Check(Requests.Count==1,"requests consumed inferred points without observing learned ranks");});
 Case("a declined native request is not a preview commit",()=>{NativeResult="unavailable";NoMutation(New(new(3,1,1,"Actual")));});
 Case("current tier and prerequisite remain admission gates",()=>{Rows[(3,1)]=Row("Actual",tier:7,prerequisite:false);NoMutation(New(new(3,1,1,"Actual")));});
 Case("stock original API request carries group name rank and preview fences",()=>{Invoke(New(new(3,1,1,"Actual")));Check(LastScript.Contains("GetActiveTalentGroup")&&LastScript.Contains("UnitGUID")&&LastScript.Contains("GetTalentInfo")&&LastScript.Contains("GetGroupPreviewTalentPointsSpent")&&LastScript.Contains("GetUnspentTalentPoints")&&!LastScript.Contains("LearnPreviewTalents")&&!LastScript.Contains("ResetGroupPreview"),"atomic learning request omitted its identity or preview fences");});
 Console.WriteLine($"Talent allocation scenarios: {passed}/{passed+failures.Count}; full tracked component; native request boundary controlled; no character talents changed.");
 if(failures.Count!=0)throw new InvalidOperationException(string.Join("; ",failures));}
}
""";
}
