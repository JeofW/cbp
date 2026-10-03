using System.Runtime.CompilerServices;

internal static class IntegratedControlAuraTests
{
    internal static void Run()
    {
        string readers = IntegratedRegressionFixture.Methods("runtime-snapshot/Routines/Singular wotlk/Helpers/Unit.cs",
            "HasAuraWithMechanic", "HasHarmfulAuraWithMechanic");
        IntegratedRegressionFixture.Run("Harmful control aura", Boundary
            + "namespace Singular.Helpers { public static class Unit {" + readers + "}}\n" + Scenarios,
            "runtime-snapshot/Routines/Singular wotlk/Helpers/PVP.cs");
    }
    private const string Boundary = """
#nullable disable
using System;using System.Linq;using System.Collections.Generic;using Styx.Logic.Combat;using Styx.Helpers;using Styx.WoWInternals.WoWObjects;using Singular.Helpers;
namespace Styx.WoWInternals.WoWObjects {
 public sealed class Spell {public WoWSpellMechanic Mechanic;}
 public sealed class Aura {public bool IsActive=true,IsHarmful,Missing,Cancel;public Spell Value=new();public Spell Spell {get{Cases.MetadataReads++;if(Cancel)throw new OperationCanceledException("stop");return Missing?null:Value;}}}
 public sealed class WoWUnit {public bool RawUnknown;public List<Aura> Auras=new();
  public IEnumerable<Aura> GetRawAuras()=>RawUnknown?throw new ObservationUnavailableException("auras","raw coverage unavailable"):Auras;
  public IEnumerable<Aura> GetAllAuras(){var rows=GetRawAuras().ToArray();foreach(var row in rows)if(row.Spell==null)throw new ObservationUnavailableException("auras","irrelevant metadata unavailable");return rows;}
 }
}
""";
    private const string Scenarios = """
public static class Cases {
 private sealed class Failure(string message):Exception(message){}
 public static int MetadataReads;
 private static void Check(bool yes,string why){if(!yes)throw new Failure(why);}
 public static void Run(){int total=0,passed=0,failed=0,errors=0;
  void Case(string name,Action test){total++;MetadataReads=0;try{test();passed++;}catch(Failure e){failed++;Console.Error.WriteLine("FAIL harmful control: "+name+": "+e.Message);}catch(Exception e){errors++;Console.Error.WriteLine("ERROR harmful control: "+name+": "+e);}}
  foreach(bool root in new[]{false,true}){
   bool Read(WoWUnit unit)=>root?PVP.IsRooted(unit):PVP.IsStunned(unit);
   string label=root?"root":"stun";
   Case(label+" harmless unknown metadata is irrelevant",()=>{var unit=new WoWUnit();unit.Auras.Add(new(){Missing=true});try{Check(!Read(unit)&&MetadataReads==0,"helpful marker consumed a control query");}catch(ObservationUnavailableException){throw new Failure("helpful marker denied complete harmful control observation");}});
   Case(label+" known harmful control survives unrelated marker",()=>{var unit=new WoWUnit();unit.Auras.Add(new(){Missing=true});unit.Auras.Add(new(){IsHarmful=true,Value=new(){Mechanic=root?WoWSpellMechanic.Rooted:WoWSpellMechanic.Stunned}});try{Check(Read(unit)&&MetadataReads==1,"known control omitted or metadata reread");}catch(ObservationUnavailableException){throw new Failure("helpful marker blocked known harmful control");}});
   Case(label+" relevant unknown stays unknown",()=>{var unit=new WoWUnit();unit.Auras.Add(new(){IsHarmful=true,Missing=true});bool unknown=false;try{Read(unit);}catch(ObservationUnavailableException){unknown=true;}Check(unknown,"missing relevant mechanic became no control");});
   Case(label+" raw unknown stays unknown",()=>{bool unknown=false;try{Read(new(){RawUnknown=true});}catch(ObservationUnavailableException){unknown=true;}Check(unknown,"missing raw coverage became no control");});
   Case(label+" native cancellation propagates",()=>{var unit=new WoWUnit();unit.Auras.Add(new(){IsHarmful=true,Cancel=true});bool cancelled=false;try{Read(unit);}catch(OperationCanceledException){cancelled=true;}Check(cancelled,"control-flow stop was hidden");});
  }
  Console.WriteLine($"Integrated harmful control: {passed}/{total}; assertions={failed}; unexpected={errors}; actual Unit/PVP readers; controlled complete raw aura observation.");
  if(failed+errors!=0)throw new InvalidOperationException("Harmful control regressions");
 }
}
""";
}
