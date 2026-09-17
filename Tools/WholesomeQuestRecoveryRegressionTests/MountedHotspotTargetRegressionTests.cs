using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Styx;
using Styx.Helpers;
using Styx.Logic;
using Styx.Logic.AreaManagement;
using Styx.Logic.Combat;
using Styx.Logic.Pathing;
using Styx.Logic.POI;
using Styx.Logic.Profiles;
using Styx.WoWInternals;
using Styx.WoWInternals.WoWObjects;
using Levelbot.Decorators.Combat;
using TreeSharp;

internal static class MountedHotspotTargetRegressionTests
{
    private const BindingFlags S = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
    private const BindingFlags I = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private sealed class AssertionFailure(string text) : Exception(text) { }
    [ModuleInitializer]
    internal static void Run()
    {
        var cases = new (string Name, System.Action<Fixture> Test)[] {
            ("mounted requested MobID without maximum-level override", f => f.Expect(true)),
            ("mounted requested MobID with kill-between enabled", f => { StyxSettings.Instance.KillBetweenHotspots=true; f.Expect(true); }),
            ("unmounted requested MobID retains ordinary admission", f => { f.Player.Riding=false; f.Expect(true); }),
            ("unlisted mob is not authorized by an unlimited level interval", f => { f.Area.MobIDs.Clear(); f.Expect(false); }),
            ("explicit MobID does not bypass minimum level", f => { f.Area.TargetMinLevel=30; f.Expect(false); }),
            ("explicit MobID does not bypass maximum level", f => { f.Area.TargetMaxLevel=10; f.Expect(false); }),
            ("finite level interval still permits existing target", f => { f.Area.MobIDs.Clear(); f.Area.TargetMaxLevel=25; f.Expect(true); }),
            ("area faction control remains admitted", f => { f.Area.MobIDs.Clear(); f.Area.Factions.Add(77); f.Expect(true); }),
            ("profile faction control remains admitted", f => { f.Area.MobIDs.Clear(); f.Profile.Factions.Add(77); f.Expect(true); }),
            ("requested mob outside pull range remains denied", f => { f.Target.Position=new WoWPoint(80,20,30); f.Expect(false); }),
            ("requested mob outside hotspot collection remains denied", f => { f.Area.MaxDistance=11; f.SetHotspot(new WoWPoint(300,20,30)); f.Expect(false); }),
            ("ground farming mode keeps its explicit no-targeting contract", f => { LevelbotSettings.Instance.GroundMountFarmingMode=true; f.Expect(false); }),
            ("empty target list remains harmless", f => { f.Targets.Clear(); f.Expect(false); }),
            ("nearby selected Kill POI already requests dismount", f => { BotPoi.Current=new BotPoi(f.Target.Position,PoiType.Kill); Check(Mount.ShouldDismount(f.Target.Position),"existing Kill dismount control failed"); }),
            ("nearby hotspot with target already requests dismount", f => { BotPoi.Current=new BotPoi(f.Player.Location,PoiType.Hotspot); Check(Mount.ShouldDismount(f.Player.Location),"existing hotspot dismount control failed"); }),
            ("distant Kill POI does not dismount early", f => { var p=new WoWPoint(300,20,30); BotPoi.Current=new BotPoi(p,PoiType.Kill); Check(!Mount.ShouldDismount(p),"distant transit was dismounted"); })
        };
        int passed=0, assertions=0, unexpected=0;
        foreach(var c in cases)
        {
            try { using var f=new Fixture(); c.Test(f); passed++; Console.WriteLine("PASS mounted hotspot: "+c.Name); }
            catch(AssertionFailure e) { assertions++; Console.Error.WriteLine("FAIL mounted hotspot assertion: "+c.Name+": "+e.Message); }
            catch(Exception e) { unexpected++; Console.Error.WriteLine("ERROR mounted hotspot fixture: "+c.Name+": "+e); }
        }
        Console.WriteLine($"Mounted hotspot scenarios: {passed}/{cases.Length}; assertions={assertions}; unexpected={unexpected}; actual target and dismount admission; no game attached.");
        if(assertions+unexpected!=0) throw new InvalidOperationException($"Mounted hotspot regressions: assertions={assertions}; unexpected={unexpected}");
    }
    private sealed class PlayerState(uint p) : LocalPlayer(p)
    {
        internal bool Riding=true;
        public override bool Mounted => Riding;
        public override WoWPoint Location => new(10,20,30);
        public override bool IsAlive => true;
    }
    private sealed class TargetState(uint p) : WoWUnit(p)
    {
        internal WoWPoint Position=new(15,20,30);
        public override WoWPoint Location => Position;
        public override bool IsAlive => true;
    }
    private sealed class Fixture : IDisposable
    {
        private readonly IDisposable world;
        private readonly object? oldArea, oldSettings, oldRoutine;
        private readonly Targeting oldTargeting;
        private readonly BotPoi oldPoi;
        private readonly bool oldFarming,oldBetween;
        private readonly IntPtr storage=Marshal.AllocHGlobal(4096);
        internal readonly PlayerState Player;
        internal readonly TargetState Target;
        internal readonly GrindArea Area;
        internal readonly Profile Profile;
        internal readonly List<WoWObject> Targets;
        internal Fixture()
        {
            world=(IDisposable)Activator.CreateInstance(typeof(QuestPublicationRegressionTests).GetNestedType("Fixture",BindingFlags.NonPublic)!,true)!;
            oldArea=typeof(StyxWoW).GetField("_areaManager",S)!.GetValue(null);
            oldSettings=typeof(CharacterSettings).GetField("<Instance>k__BackingField",S)!.GetValue(null);
            oldRoutine=typeof(RoutineManager).GetField("_current",S)!.GetValue(null);
            oldTargeting=Targeting.Instance; oldPoi=BotPoi.Current;
            oldFarming=LevelbotSettings.Instance.GroundMountFarmingMode; oldBetween=StyxSettings.Instance.KillBetweenHotspots;
            typeof(CharacterSettings).GetField("<Instance>k__BackingField",S)!.SetValue(null,RuntimeHelpers.GetUninitializedObject(typeof(CharacterSettings)));
            CharacterSettings.Instance.PullDistance=30;
            typeof(RoutineManager).GetField("_current",S)!.SetValue(null,null);
            typeof(StyxWoW).GetField("_areaManager",S)!.SetValue(null,new AreaManager());
            Player=new PlayerState(ObjectManager.Me.BaseAddress); ObjectManager.Me=Player;
            Marshal.Copy(new byte[4096],0,storage,4096);
            uint p=unchecked((uint)storage.ToInt32()),d=p+2048;
            Write(p+8,d); Write(p+0x14,3); Write(p+48,456); Write(d,456); Write(d+12,54321);
            Write(d+4*(uint)UnitFields.Level,20); Write(d+4*(uint)UnitFields.FactionTemplate,77);
            Target=new TargetState(p);
            Targeting.Instance=new Targeting(); Targets=new List<WoWObject>{Target};
            typeof(Targeting).GetProperty("ObjectList",I)!.SetValue(Targeting.Instance,Targets);
            Profile=new Profile();
            typeof(ProfileManager).GetField("_currentProfile",S)!.SetValue(null,Profile);
            Area=new GrindArea(); Area.MobIDs.Add(54321); Area.Hotspots.Add(Player.Location); Area.CircledHotspots.Enqueue(Player.Location);
            StyxWoW.AreaManager.SetArea(Area); SetHotspot(Player.Location);
            LevelbotSettings.Instance.GroundMountFarmingMode=false; StyxSettings.Instance.KillBetweenHotspots=false;
            Check(Target.Entry==54321 && Target.Level==20 && Target.FactionId==77 && Player.Mounted,"controlled descriptors mismatch");
            Check(!Battlegrounds.IsInsideBattleground && ObjectManager.Executor==null,"test must stay outside a game and battleground");
        }
        internal void SetHotspot(WoWPoint p)
        {
            typeof(GrindArea).GetField("_lastProfile",I)!.SetValue(Area,Profile);
            typeof(GrindArea).GetField("_currentHotspot",I)!.SetValue(Area,(Hotspot)p);
        }
        internal void Expect(bool expected)
        {
            int entered=0;
            var gate=new DecoratorNeedToFindTarget(new TreeSharp.Action(_=>{entered++;return RunStatus.Success;}));
            gate.Start(null!); try { var status=gate.Tick(null!); Check((status==RunStatus.Success)==expected && entered==(expected?1:0),"target gate rejected/admitted the wrong hunting candidate"); } finally {gate.Stop(null!);}
        }
        public void Dispose()
        {
            Targeting.Instance=oldTargeting; BotPoi.Current=oldPoi;
            LevelbotSettings.Instance.GroundMountFarmingMode=oldFarming; StyxSettings.Instance.KillBetweenHotspots=oldBetween;
            typeof(StyxWoW).GetField("_areaManager",S)!.SetValue(null,oldArea);
            typeof(CharacterSettings).GetField("<Instance>k__BackingField",S)!.SetValue(null,oldSettings);
            typeof(RoutineManager).GetField("_current",S)!.SetValue(null,oldRoutine);
            world.Dispose(); Marshal.FreeHGlobal(storage);
        }
    }
    private static void Write(uint p,uint value)=>Marshal.WriteInt32(new IntPtr(unchecked((int)p)),unchecked((int)value));
    private static void Check(bool ok,string why) { if(!ok) throw new AssertionFailure(why); }
}
