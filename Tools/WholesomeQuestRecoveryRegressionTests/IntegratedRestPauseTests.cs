using System.Runtime.CompilerServices;

internal static class IntegratedRestPauseTests
{
    internal static void Run()
    {
        string retry = IntegratedRegressionFixture.Methods("runtime-snapshot/Bots/WholesomeAutoQuest-master/WholesomeAutoQuest.cs", "RetryRestConsumables");
        IntegratedRegressionFixture.Run("Wholesome paused recovery", Boundary + "public sealed class Bot {" + Owner + retry + "}\n" + Scenarios);
    }
    private const string Boundary = """
#nullable disable
using System;using Styx.WoWInternals.WoWObjects;
namespace Styx.WoWInternals.WoWObjects {public sealed class LocalPlayer {
 public uint BaseAddress=100;public bool IsValid=true,IsAlive=true,IsGhost,Combat,IsOnTransport,IsFlying,Mounted,IsMoving;public double HealthPercent=100,ManaPercent=100,MaxMana=100;
}}
public static class StyxWoW {public static LocalPlayer Me=new();}
public static class ObjectManager {public static object Wow=new();}
public static class Navigator {public static Mover PlayerMover=new();public sealed class Mover {public void MoveStop(){Cases.Stops++;StyxWoW.Me.IsMoving=false;}}}
namespace Styx.Logic {public static class Mount {public static void Dismount(string why){Cases.Dismounts++;}}}
public static class Rest {public static bool TryFeedImmediate(){Cases.FoodRequests++;Cases.AfterFood?.Invoke();return Cases.Submitted;}public static bool TryDrinkImmediate(){Cases.DrinkRequests++;return Cases.Submitted;}}
public sealed class Settings {public int RestHealthPercent=45,RestManaPercent=30,RestResumeHealthPercent=75,RestResumeManaPercent=60;}
""";
    private const string Owner = """
 public bool _stopped,_restingPaused=true;public Settings _settings=new();
 private static bool CanRestAtObservedLocation(LocalPlayer actor)=>Cases.Dry;
 private static bool TryObserveRestAuras(LocalPlayer actor,out bool food,out bool drink){food=Cases.FoodAura;drink=Cases.DrinkAura;return Cases.RawKnown;}
 private static void Log(string message){Cases.Logs++;}
 public void Retry()=>RetryRestConsumables(StyxWoW.Me,ObjectManager.Wow);
""";
    private const string Scenarios = """
public static class Cases {
 private sealed class Failure(string message):Exception(message){}
 public static int FoodRequests,DrinkRequests,Stops,Dismounts,Logs;public static bool FoodAura,DrinkAura,RawKnown,Dry,Submitted;public static Action AfterFood;
 private static void Check(bool ok,string why){if(!ok)throw new Failure(why);}
 public static void Run(){int total=0,passed=0,failed=0,errors=0;
  void Case(string name,Action<Bot> test){total++;FoodRequests=DrinkRequests=Stops=Dismounts=Logs=0;FoodAura=DrinkAura=false;RawKnown=Dry=Submitted=true;AfterFood=null;StyxWoW.Me=new();ObjectManager.Wow=new();var bot=new Bot();try{test(bot);passed++;}catch(Failure e){failed++;Console.Error.WriteLine("FAIL Wholesome paused recovery: "+name+": "+e.Message);}catch(Exception e){errors++;Console.Error.WriteLine("ERROR Wholesome paused recovery: "+name+": "+e);}}
  foreach(bool mana in new[]{false,true}){
   foreach(int value in mana?new[]{20,31,59,60,75}:new[]{30,46,74,75,90}){
    Case((mana?"mana/":"health/")+value,bot=>{if(mana)StyxWoW.Me.ManaPercent=value;else StyxWoW.Me.HealthPercent=value;bot.Retry();int expected=value<(mana?60:75)?1:0;Check((mana?DrinkRequests:FoodRequests)==expected,"active pause stopped retrying before its resume goal or overshot the goal");Check((mana?FoodRequests:DrinkRequests)==0,"unrelated recovered resource was consumed");});
   }
   Case((mana?"mana/":"health/")+"existing aura",bot=>{if(mana){StyxWoW.Me.ManaPercent=35;DrinkAura=true;}else{StyxWoW.Me.HealthPercent=50;FoodAura=true;}bot.Retry();Check(FoodRequests+DrinkRequests==0,"active recovery duplicated an item");});
  }
  foreach(string state in new[]{"stopped","not-paused","raw-unknown","wet","combat","flying","transport","dead"}){
   Case(state,bot=>{StyxWoW.Me.HealthPercent=10;if(state=="stopped")bot._stopped=true;if(state=="not-paused")bot._restingPaused=false;if(state=="raw-unknown")RawKnown=false;if(state=="wet")Dry=false;if(state=="combat")StyxWoW.Me.Combat=true;if(state=="flying")StyxWoW.Me.IsFlying=true;if(state=="transport")StyxWoW.Me.IsOnTransport=true;if(state=="dead")StyxWoW.Me.IsAlive=false;bot.Retry();Check(FoodRequests+DrinkRequests==0,"unsafe pause submitted a consumable");});
  }
  Case("food callback cannot hand drink to a replacement",bot=>{StyxWoW.Me.HealthPercent=20;StyxWoW.Me.ManaPercent=20;AfterFood=()=>StyxWoW.Me=new(){HealthPercent=20,ManaPercent=20};bot.Retry();Check(FoodRequests==1&&DrinkRequests==0&&Logs==0,"replacement inherited original rest admission");});
  Case("declined food is not reported submitted",bot=>{StyxWoW.Me.HealthPercent=20;Submitted=false;bot.Retry();Check(FoodRequests==1&&Logs==0,"denied request claimed submission");});
  Console.WriteLine($"Integrated Wholesome paused recovery: {passed}/{total}; assertions={failed}; unexpected={errors}; actual retry owner; controlled inventory and world leaves.");
  if(failed+errors!=0)throw new InvalidOperationException("Paused rest regressions");
 }
}
""";
}
