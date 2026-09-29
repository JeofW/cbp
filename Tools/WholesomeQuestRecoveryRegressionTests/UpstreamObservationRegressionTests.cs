using System;
using System.Runtime.CompilerServices;

internal static class UpstreamObservationRegressionTests
{
    [ModuleInitializer]
    internal static void Run()
    {
        int failures=0;
        void Run(string name, Action action){try{action();Console.WriteLine("PASS upstream observation: "+name);}catch(Exception e){failures++;Console.Error.WriteLine("FAIL upstream observation: "+name+": "+e.Message);}}
        Run("complete threat reader",()=>UpstreamSeptemberRegressionTests.Probe("threat",Threat,
            "Styx/WoWInternals/WoWObjects/UnitThreatInfo.cs"));
        Run("actual mount wrapper",()=>UpstreamSeptemberRegressionTests.Probe("mount",MountPrefix+
            UpstreamSeptemberRegressionTests.Member("Styx/Logic/MountHelper.cs","MountWrapper",true)+MountCases));
        Run("complete terrain provider",()=>UpstreamSeptemberRegressionTests.Probe("terrain",Terrain,
            "Styx/Logic/Pathing/NavigatorTerrainHeightProvider.cs"));
        Console.WriteLine($"Upstream observation families: {3-failures}/3; failures={failures}; tracked owners with controlled leaves; no live acceptance.");
        if(failures!=0)throw new InvalidOperationException("Upstream observation regressions");
    }
    private const string Threat="""
using System;using System.Collections.Generic;using System.Runtime.InteropServices;using Styx;using Styx.WoWInternals;using Styx.WoWInternals.WoWObjects;
public static class Cases {
 public static void Run(){
  int count=0;void Check(bool x,string why){count++;if(!x)throw new Exception(why);}
  foreach(bool victim in new[]{false,true})foreach(bool found in new[]{false,true}){
   ObjectManager.Wow=new GreenMagic.Memory();var m=ObjectManager.Wow;
   var mob=new WoWUnit{BaseAddress=1000,Guid=9};var actor=new WoWUnit{BaseAddress=2000,Guid=2};
   m.U64(5056,victim?2UL:0);m.U32(5088,6000);m.U32(5096,0);
   m.U32(6000,44);m.U32(6008,7000);m.U32(7008,found?2U:3U);m.U64(7032,found?2UL:3UL);m.Bytes[7040]=4;m.Bytes[7041]=99;m.U32(7044,750);m.U32(7048,1);
   var info=UnitThreatInfo.GetThreatInfo(mob,actor);
   Check(info.ThreatStatus==(found?(victim?ThreatStatus.SecurelyTanking:ThreatStatus.NotTanking):ThreatStatus.UnitNotInThreatTable),"empty victim incorrectly discards membership");
   Check(info.TargetGuid==2,"wrong threat subject");
   Check(info.ThreatValue==(victim&&found?750U:0U),"fallback exposes stale threat amount");
  }
  ObjectManager.Wow=new GreenMagic.Memory();var cyclic=ObjectManager.Wow;
  cyclic.U32(5088,6000);cyclic.U32(5096,0);cyclic.U32(6000,44);cyclic.U32(6008,7000);
  cyclic.U32(7008,3);cyclic.U64(7032,3);cyclic.U32(7048,7000);
  var unavailable=UnitThreatInfo.GetThreatInfo(new WoWUnit{BaseAddress=1000,Guid=9},new WoWUnit{Guid=2});
  Check(unavailable.ThreatStatus==ThreatStatus.UnitNotInThreatTable&&cyclic.Reads<100,"cyclic native links were not bounded");
  Console.WriteLine("Upstream threat cases: "+count+" assertions passed; complete raw reader; controlled bytes.");
 }
}
/* Controlled memory and unit observations; never a real process. */ namespace Styx.WoWInternals {public static class ObjectManager{public static GreenMagic.Memory Wow;}}
/* Controlled memory and unit observations. */ namespace Styx.WoWInternals.WoWObjects {public class WoWUnit{public uint BaseAddress;public ulong Guid;public bool IsValid=true;}}
/* Exact-size marshaling of controlled native-layout bytes. */ namespace GreenMagic {public class Memory {
 public int Reads;public Dictionary<uint,byte> Bytes=new();public void U32(uint a,uint v){var b=BitConverter.GetBytes(v);for(uint i=0;i<4;i++)Bytes[a+i]=b[i];}public void U64(uint a,ulong v){var b=BitConverter.GetBytes(v);for(uint i=0;i<8;i++)Bytes[a+i]=b[i];}
 public T Read<T>(uint address){if(++Reads>2048)throw new Exception("controlled read budget caught an unbounded native-link traversal");int size=Marshal.SizeOf<T>();var bytes=new byte[size];for(int i=0;i<size;i++)bytes[i]=Bytes.GetValueOrDefault(address+(uint)i);var p=Marshal.AllocHGlobal(size);try{Marshal.Copy(bytes,0,p,size);return Marshal.PtrToStructure<T>(p);}finally{Marshal.FreeHGlobal(p);}}
}}
""";
    private const string MountPrefix="""
using System;using System.Collections.Generic;using Styx.Logic;using Styx.Logic.Combat;using Styx.WoWInternals;
public static class MountProbe {
""";
    private const string MountCases="""
}
public static class Cases {
 public static void Run(){int count=0;foreach(int aura in new[]{207,208,209,211,152,153,154,156,32}){
  WoWSpell.Observed=new WoWSpell{SpellEffects=new[]{new Effect{AuraType=aura,BasePoints=59}}};var m=new MountProbe.MountWrapper(1);
  bool fly=aura==207||aura==208||aura==209||aura==211;if((m.Type==MountType.Flying)!=fly)throw new Exception("wrong original-client mount classification for aura "+aura);count++;
 }
 WoWSpell.Observed=new WoWSpell{SpellEffects=new[]{new Effect{AuraType=32,BasePoints=309}}};if(new MountProbe.MountWrapper(1).Type==MountType.Flying)throw new Exception("ground-speed magnitude invented flight capability");count++;
 WoWSpell.Observed=new WoWSpell{SpellEffects=new[]{new Effect{AuraType=32,MiscValueB=229}}};if(new MountProbe.MountWrapper(1).Type==MountType.Flying)throw new Exception("later-expansion MiscValueB invented flight capability");count++;
 WoWSpell.Observed=new WoWSpell{SpellEffects=new[]{new Effect{AuraType=207,MiscValueB=230}}};if(new MountProbe.MountWrapper(1).Type!=MountType.Flying)throw new Exception("later-expansion MiscValueB hid original flight aura");count++;
 WoWSpell.Observed=null;if(new MountProbe.MountWrapper(1).Type!=MountType.Unknown)throw new Exception("missing metadata became known mount");count++;
 Console.WriteLine("Upstream mount cases: "+count+" passed; actual wrapper with controlled spellbook.");}
}
/* Controlled spell metadata. */ namespace Styx.Logic.Combat {public class Effect{public int AuraType,MiscValueB,BasePoints;}public class WoWSpell{public static WoWSpell Observed;public static WoWSpell FromId(int id)=>Observed;public Effect[] SpellEffects;public Effect SpellEffect1=>SpellEffects?[0];public bool CanCast=>true;}}
/* Controlled original-client companion tuple. */ namespace Styx.WoWInternals {public static class Lua{public static List<string> GetReturnValues(string code)=>new(){"1","mount","123","icon","0"};}}
""";
    private const string Terrain="""
using System;using System.Collections.Generic;using System.Numerics;using Styx.Logic.Pathing;
public static class Cases {
 public static void Run(){int count=0;void Reset(){Styx.StyxWoW.Me=new Player();Navigator.TripperNavigator=new Native();Navigator.IsNavigatorLoaded=true;}void Check(bool x,string why){count++;if(!x)throw new Exception(why);}
  Reset();Check(new NavigatorTerrainHeightProvider().FindHeights(10,20).Contains(42),"valid map zero rejected");
  Reset();Styx.StyxWoW.Me=null;Check(new NavigatorTerrainHeightProvider().FindHeights(10,20).Count==0&&Navigator.TripperNavigator.Calls==0,"missing actor borrowed map zero");
  Reset();Styx.StyxWoW.Me.MapId=1;Navigator.TripperNavigator.ElevatedBoundary=true;Check(new NavigatorTerrainHeightProvider().FindHeights(10,20).Contains(42),"vertical height incorrectly counted as XY boundary distance");
  Reset();Styx.StyxWoW.Me.MapId=1;Navigator.TripperNavigator.Height=float.NaN;Check(new NavigatorTerrainHeightProvider().FindHeights(10,20).Count==0,"nonfinite height admitted");
  Reset();Styx.StyxWoW.Me.MapId=1;Navigator.TripperNavigator.Callback=()=>Styx.StyxWoW.Me.MapId=2;Check(new NavigatorTerrainHeightProvider().FindHeights(10,20).Count==0,"map replacement inherited heights");
  Reset();Check(new NavigatorTerrainHeightProvider().FindHeights(float.NaN,20).Count==0,"nonfinite query reached native height");
  Console.WriteLine("Upstream terrain cases: "+count+" passed; complete provider; controlled native geometry.");
 }
}
public class Player{public uint MapId;public ulong Guid=1;public bool IsValid=true;}
public class Native {
 public int Calls;public float Height=42;public bool ElevatedBoundary;public Action Callback;
 public Tripper.Navigation.PolygonReference[] QueryPolygons(uint map,Vector3 center,Vector3 extents,int limit){Calls++;var cb=Callback;Callback=null;cb?.Invoke();return new[]{new Tripper.Navigation.PolygonReference()};}
 public bool ClosestPointOnPolyBoundary(uint map,Tripper.Navigation.PolygonReference poly,Vector3 center,out Vector3 point){point=center;if(ElevatedBoundary)point.Z=42;return true;}
 public bool GetPolyHeight(uint map,Tripper.Navigation.PolygonReference poly,Vector3 center,out float height){height=Height;return true;}
 public bool IsTileLoaded(uint map,int x,int y)=>true;public bool LoadTile(Tripper.Navigation.TileIdentifier tile)=>true;
}
/* Controlled actor and terrain provider. */ namespace Styx {public static class StyxWoW{public static Player Me;}}
/* Controlled native dispatch. */ namespace Styx.Logic.Pathing {public interface ITerrainHeightProvider{List<float> FindHeights(float x,float y);}public static class Navigator{public static bool IsNavigatorLoaded;public static Native TripperNavigator;}}
""";
}
