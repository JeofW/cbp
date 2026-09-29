using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Styx.Logic.Pathing;
using Styx.Logic.Profiles;
using Styx.Combat.CombatRoutine;

internal static class UpstreamServicesRegressionTests
{
    [ModuleInitializer]
    internal static void Run()
    {
        int passed=0, failed=0;
        void Case(string name, Action test) { try { test(); passed++; Console.WriteLine("PASS upstream service: "+name); }
            catch(Exception error) { failed++; Console.Error.WriteLine("FAIL upstream service: "+name+": "+error.Message); } }
        Case("complete NPC queries",()=>UpstreamSeptemberRegressionTests.Probe("NPC",Npc,
            "Styx/Database/NpcQueries.cs", "Styx/Database/NpcResult.cs"));
        Case("missing requested vendor type permits discovery",()=>{
            var manager=new VendorManager();
            manager.AllVendors.Add(new Vendor(123,"Food",Vendor.VendorType.Food,new WoWPoint(1,2,3)));
            var policy=typeof(VendorManager).GetMethod("CanUseAutomaticFallback",BindingFlags.NonPublic|BindingFlags.Instance)!;
            if(!(bool)policy.Invoke(manager,new object[]{Vendor.VendorType.Train,WoWClass.Paladin})!)
                throw new Exception("unrelated profile vendor prevents class trainer fallback");
            manager.ForcedVendors.Add(new Vendor(124,"Forced food",Vendor.VendorType.Food,new WoWPoint(1,2,3)));
            if((bool)policy.Invoke(manager,new object[]{Vendor.VendorType.Train,WoWClass.Paladin})!)
                throw new Exception("automatic discovery bypassed forced profile intent");
        });
        Case("gather storage mailing admission",()=>{
            var tree=CSharpSyntaxTree.ParseText(File.ReadAllText(Path.Combine(UpstreamSeptemberRegressionTests.Root(),"Bots/Gatherbuddy/GatherbuddyBot.cs"))).GetRoot();
            string members=string.Join("\n",tree.DescendantNodes().OfType<MethodDeclarationSyntax>()
                .Where(m=>new[]{"NeedsMailing","NeedsBagsEmptied","GatherStorageNeedsEmptying"}.Contains(m.Identifier.ValueText)).Select(m=>m.ToString()));
            UpstreamSeptemberRegressionTests.Probe("gather mail",GatherPrefix+members+GatherCases);
        });
        Case("boss state outside draw distance",()=>{
            var tree=CSharpSyntaxTree.ParseText(File.ReadAllText(Path.Combine(UpstreamSeptemberRegressionTests.Root(),"Bots/DungeonBuddy/Helpers/ScriptHelpers.cs"))).GetRoot();
            string member=tree.DescendantNodes().OfType<MethodDeclarationSyntax>().Single(m=>m.Identifier.ValueText=="IsBossAlive"&&m.ParameterList.Parameters.Single().Type!.ToString()=="string").ToString();
            UpstreamSeptemberRegressionTests.Probe("boss state",BossPrefix+member+BossCases);
        });
        Console.WriteLine($"Upstream service families: {passed}/{passed+failed}; failures={failed}; complete owners with controlled database/world; no game acceptance.");
        if(failed!=0)throw new InvalidOperationException("Upstream service regressions");
    }
    private const string Npc="""
using System;using System.Collections.Generic;using System.Linq;using System.Reflection;using Styx;using Styx.Database;using Styx.WoWInternals;using Styx.Logic.Pathing;using Styx.Combat.CombatRoutine;
public static class Cases {
 public static void Run(){int count=0;void Check(bool ok,string why){count++;if(!ok)throw new Exception(why);}
  foreach(bool trainer in new[]{false,true}){
   StyxWoW.Me=new Player();Connection.Rows=new(){new Row(10,22),new Row(11,44),new Row(12,33)};Navigator.Checks=0;
   var result=trainer?NpcQueries.GetNearestTrainer(StyxWoW.Me.FactionTemplate.Faction,0,new WoWPoint(1,2,3),WoWClass.Paladin)
      :NpcQueries.GetNearestNpc(StyxWoW.Me.FactionTemplate.Faction,0,new WoWPoint(1,2,3),UnitNPCFlags.Repair);
   Check(result?.Entry==12,"query did not prefer known friendly template over hostile/neutral records");
   Check(Navigator.Checks==1,"unsafe faction reached navigation or preference was ignored");
   Connection.Rows=new(){new Row(20,55)};
   result=trainer?NpcQueries.GetNearestTrainer(StyxWoW.Me.FactionTemplate.Faction,0,new WoWPoint(1,2,3),WoWClass.Paladin)
      :NpcQueries.GetNearestNpc(StyxWoW.Me.FactionTemplate.Faction,0,new WoWPoint(1,2,3),UnitNPCFlags.Repair);
   Check(result==null,"missing template became neutral eligibility");
  }
  StyxWoW.Me=new Player();Connection.Rows=new(){new Row(30,33)};
  Check(NpcQueries.GetNearestNpc(new WoWFaction(99),0,new WoWPoint(1,2,3),UnitNPCFlags.Repair)==null,"unrelated legacy faction borrowed current player's identity");
  Check(NpcQueries.GetNearestNpc(StyxWoW.Me.FactionTemplate.Faction,0,new WoWPoint(1,2,3),UnitNPCFlags.Repair,_=>false)==null,"existing caller veto ignored");
  Console.WriteLine("Upstream NPC assertions: "+count+" passed; actual query/record owners and controlled rows.");
 }
}
public record Row(int Entry,uint Faction);
public class Player{public int Level=80;public WoWClass Class=WoWClass.Paladin;public ulong Guid=1;public uint MapId;public bool IsValid=true;public WoWPoint Location=new(1,2,3);public WoWFactionTemplate FactionTemplate=new(1);}
/* Controlled player observation. */ namespace Styx{public static class StyxWoW{public static Player Me=new();}}
/* Controlled DB transport; query and NpcResult parsing remain actual. */ namespace System.Data.SQLite {
 public class SQLiteCommand{public string Sql;public SQLiteCommand(string sql){Sql=sql;}}
 public class SQLiteDataReader:IDisposable{
  static readonly string[] Keys={"entry","name","title","x","y","z","flag","faction","map","trainer_type","trainer_class"};int index=-1;readonly List<Row> rows;
  public SQLiteDataReader(List<Row> values){rows=values;}public bool Read()=>++index<rows.Count;public int GetOrdinal(string key)=>Array.IndexOf(Keys,key);
  public int GetInt32(int i)=>Keys[i] switch{"entry"=>rows[index].Entry,"flag"=>32|(int)UnitNPCFlags.Repair,"faction"=>(int)rows[index].Faction,"trainer_class"=>(int)WoWClass.Paladin,_=>0};
  public string GetString(int i)=>Keys[i];public float GetFloat(int i)=>rows[index].Entry;public bool IsDBNull(int i)=>false;public void Dispose(){}
 }}
/* Controlled connection and cache-policy inputs. */ namespace Styx.Database {public static class Connection{public static object Instance=new();public static bool IsAvailable=>true;public static List<Row> Rows=new();public static System.Data.SQLite.SQLiteCommand CreateCommand(string sql)=>new(sql);public static System.Data.SQLite.SQLiteDataReader ExecuteReader(System.Data.SQLite.SQLiteCommand cmd,params object[] args)=>new(new List<Row>(Rows));}}
/* Controlled legacy faction reproduces absent-template Neutral behavior; template relation is explicit. */ namespace Styx.WoWInternals {
 public class WoWFaction{public uint Id;public WoWFaction(uint id){Id=id;}public WoWUnitReaction RelationTo(WoWFaction other)=>WoWUnitReaction.Neutral;}
 public class WoWFactionTemplate{public uint Id;public WoWFactionTemplate(uint id){Id=id;}public WoWFaction Faction=>new(Id);public static WoWFactionTemplate FromId(uint id)=>id==55?null:new(id);public WoWUnitReaction GetReactionTowards(WoWFactionTemplate other)=>other.Id==22?WoWUnitReaction.Hostile:other.Id==33?WoWUnitReaction.Friendly:WoWUnitReaction.Neutral;}
}
/* Controlled native path receipt. */ namespace Styx.Logic.Pathing{public static class Navigator{public static int Checks;public static bool CanNavigateFully(WoWPoint from,WoWPoint to){Checks++;return true;}}}
/* Controlled veto; actual query still applies extraConditions. */ namespace Styx.Logic.Profiles{public static class VendorSafetyPolicy{public static bool IsRejected(int entry)=>false;}}
""";
    private const string GatherPrefix="""
using System;using Styx;using Styx.Helpers;using Styx.Logic.Profiles;
public class GatherProbe {
 private DateTime _lastMailedAt=DateTime.MinValue,_lastSaleVisitAt=DateTime.MinValue;private static readonly TimeSpan MailCooldown=TimeSpan.FromMinutes(2);
 private static bool ShouldDeferSaleVisit(DateTime now,DateTime last)=>false;
 private object[] GetItemsToMail()=>new object[]{new()};public bool Test()=>NeedsMailing(null);
""";
    private const string GatherCases="""
}
public static class Cases{public static void Run(){
 GatherbuddySettings.Instance=new(){MailToAlt=true,MailRecipient="Alt",MinFreeBagSlots=2,GatherHerbs=true,VendorWhenFull=false};StyxWoW.Me=new();BagHelper.EmptyHerbSlots=0;BagHelper.EmptyMineSlots=50;
 if(!new GatherProbe().Test())throw new Exception("full active gather bag did not trigger mail with selling disabled");
 BagHelper.EmptyHerbSlots=50;if(new GatherProbe().Test())throw new Exception("mail triggered with adequate storage");
 GatherbuddySettings.Instance.GatherHerbs=false;if(new GatherProbe().Test())throw new Exception("no active gather types treated as full");
 StyxWoW.Me.FreeBagSlots=0;if(!new GatherProbe().Test())throw new Exception("ordinary full bags no longer trigger mail");
 StyxWoW.Me.Combat=true;if(new GatherProbe().Test())throw new Exception("combat acquired mail");
 Console.WriteLine("Upstream gather mail cases: 5 passed; actual admission and controlled mailbox/items.");}}
public class GatherbuddySettings{public static GatherbuddySettings Instance=new();public bool MailToAlt,VendorWhenFull,GatherHerbs,GatherMinerals;public string MailRecipient;public int MinFreeBagSlots;}
public static class BagHelper{public static uint EmptyHerbSlots,EmptyMineSlots;}
public class Player{public bool Combat,IsDead,IsGhost;public uint FreeBagSlots=50;}
/* Controlled observations. */ namespace Styx{public static class StyxWoW{public static Player Me=new();}}
/* Controlled profile/mailbox. */ namespace Styx.Logic.Profiles{public static class ProfileManager{public static Profile CurrentProfile=new();}public class Profile{public Mailboxes MailboxManager=new();}public class Mailboxes{public object GetClosestMailbox()=>new();}}
/* Controlled logging. */ namespace Styx.Helpers{public static class Logging{public static void WriteDebug(string message){}}}
""";
    private const string BossPrefix="""
using System;using System.Collections.Generic;using System.Linq;
public static class ScriptProbe {
""";
    private const string BossCases="""
}
public class WoWUnit{public string Name;public bool IsAlive,IsFriendly;}
public static class ObjectManager{public static IEnumerable<T> GetObjectsOfType<T>()=>Array.Empty<T>();}
public class Boss{public string Name;public bool IsAlive=true;}
public static class BossManager{public static List<Boss> BossEncounters=new();}
public static class Cases{public static void Run(){
 BossManager.BossEncounters=new(){new Boss{Name="EscortBoss"}};
 if(!ScriptProbe.IsBossAlive("EscortBoss"))throw new Exception("known live boss outside object draw distance was treated as dead");
 BossManager.BossEncounters[0].IsAlive=false;if(ScriptProbe.IsBossAlive("EscortBoss"))throw new Exception("confirmed dead boss remained alive");
 if(ScriptProbe.IsBossAlive("Unknown"))throw new Exception("unknown boss invented");
 Console.WriteLine("Upstream boss state cases: 3 passed; actual helper with controlled manager state.");}}
""";
}
