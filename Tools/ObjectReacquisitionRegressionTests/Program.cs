using Styx.Logic;
using Styx.Logic.POI;
using Styx.Logic.Profiles;
using Styx.Logic.Questing;
using Styx.Logic.Pathing;
using Styx.WoWInternals;
using Styx.WoWInternals.WoWObjects;

int failed=0, passed=0;
foreach(string kind in new[]{"resume", "combat-without-kill", "combat-clear-without-pulse", "corpse-first", "combat", "absent", "new-guid", "destination", "consumed", "blacklist", "death", "actor", "map", "memory", "profile", "provider", "objective", "executor", "dispose", "successor", "late-callback", "late-subject", "route-missing", "route-invalid", "route-much-worse", "route-modestly-worse", "route-callback", "route-consumed", "alternative-callback"})
{
    ObjectManager.Me=new LocalPlayer{Guid=1,MapId=530}; ObjectManager.Wow=new object();
    ProfileManager.CurrentProfile=new Profile(); Navigator.NavigationProvider=new NavigationProvider();
    Blacklist.Entries.Clear(); ObjectManager.Objects.Clear(); LootTargeting.Instance=new(); BotPoi.Current=new(PoiType.None);
    var selected=new WoWGameObject{Guid=11,Entry=183396};var other=new WoWGameObject{Guid=12,Entry=183396};
    ObjectManager.Objects.Add(selected);ObjectManager.Objects.Add(other);
    bool active=true; object executor=new(); Action? callback=null, eligibilityCallback=null;
    float? originalDistance=100, alternateDistance=100;Action? routeCallback=null;
    using var owner=new QuestObjectReacquisition(()=>{callback?.Invoke();return active?executor:null;},go=>{eligibilityCallback?.Invoke();return go.CanLoot;},go=>{routeCallback?.Invoke();return go==selected?originalDistance:alternateDistance;});
    BotPoi.Current=new(selected,PoiType.Loot);
    if(kind=="combat-without-kill"){ObjectManager.Me.Combat=true;LootTargeting.Instance.Weigh(new());ObjectManager.Me.Combat=false;}
    else if(kind=="combat-clear-without-pulse"){ObjectManager.Me.Combat=true;BotPoi.Current=new(PoiType.None);ObjectManager.Me.Combat=false;}
    else BotPoi.Current=new(new WoWUnit{Guid=99},PoiType.Kill);
    List<Targeting.TargetPriority> Candidates()=>new(){new(){Object=other,Score=190},new(){Object=selected,Score=10}};
    var list=Candidates();
    switch(kind)
    {
        case "corpse-first":list.Add(new(){Object=new WoWUnit{Guid=99,CanLoot=true},Score=1});break;
        case "combat":ObjectManager.Me.Combat=true;ObjectManager.Me.IsActuallyInCombat=true;break;
        case "absent":list.RemoveAt(1);break;
        case "new-guid":list[1].Object=new WoWGameObject{Guid=13,Entry=183396};break;
        case "destination":selected.Location=new(20,40,60);break;
        case "consumed":selected.CanLoot=false;break;
        case "blacklist":Blacklist.Entries.Add(11);break;
        case "death":BotEvents.Player.Die();break;
        case "actor":ObjectManager.Me=new LocalPlayer{Guid=1,MapId=530};break;
        case "map":ObjectManager.Me.MapId=1;break;
        case "memory":ObjectManager.Wow=new object();break;
        case "profile":ProfileManager.CurrentProfile=new();break;
        case "provider":Navigator.NavigationProvider=new();break;
        case "objective":active=false;break;
        case "executor":executor=new();break;
        case "dispose":owner.Dispose();break;
        case "successor":BotPoi.Current=new(other,PoiType.Loot);break;
        case "late-callback":callback=()=>{callback=null;owner.Dispose();};break;
        case "late-subject":eligibilityCallback=()=>{eligibilityCallback=null;selected.Guid=77;};break;
        case "route-missing":originalDistance=null;break;
        case "route-invalid":originalDistance=float.NaN;break;
        case "route-much-worse":originalDistance=200;alternateDistance=20;break;
        case "route-modestly-worse":originalDistance=100;alternateDistance=75;break;
        case "route-callback":routeCallback=()=>{routeCallback=null;executor=new();};break;
        case "route-consumed":routeCallback=()=>{routeCallback=null;selected.CanLoot=false;};break;
        case "alternative-callback":
            originalDistance=200;alternateDistance=20;int queries=0;
            routeCallback=()=>{if(++queries==2)other.Guid=88;};break;
    }
    try
    {
        LootTargeting.Instance.Weigh(list);
        bool preferred=list.Count>1&&list[1].Score>list[0].Score;
        if(preferred!=(kind is "resume" or "combat-without-kill" or "combat-clear-without-pulse" or "route-modestly-worse"))throw new Exception("selected-object priority disagrees with current ownership");
        if(kind=="alternative-callback")
        {
            routeCallback=null;originalDistance=alternateDistance=100;list=Candidates();LootTargeting.Instance.Weigh(list);
            if(list[1].Score<=list[0].Score)throw new Exception("stale alternative query retired the preserved intent");
        }
        if(kind is "combat" or "corpse-first")
        {
            ObjectManager.Me.Combat=false;ObjectManager.Me.IsActuallyInCombat=false;list=Candidates();LootTargeting.Instance.Weigh(list);
            if(list[1].Score<=list[0].Score)throw new Exception("combat/corpse handling lost suspended exact-GUID intent");
        }
        if(kind is "absent" or "new-guid" or "consumed" or "blacklist" or "death" or "objective")
        {
            active=true;selected.CanLoot=true;Blacklist.Entries.Clear();list=Candidates();LootTargeting.Instance.Weigh(list);
            if(list[1].Score>list[0].Score)throw new Exception("revoked reservation resurrected");
        }
        passed++;Console.WriteLine("PASS object reacquisition: "+kind);
    }
    catch(Exception e){failed++;Console.WriteLine("FAIL object reacquisition: "+kind+": "+e.Message);}
}
Console.WriteLine($"Object reacquisition: {passed}/{passed+failed}; linked complete owners, controlled world observations; no game.");
return failed==0?0:1;
