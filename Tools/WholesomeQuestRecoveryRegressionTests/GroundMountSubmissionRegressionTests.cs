using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;

internal static class GroundMountSubmissionRegressionTests
{
    [ModuleInitializer] internal static void Run()
    {
        var builder=typeof(Styx.StyxWoW).Assembly.GetType("Styx.Logic.Pathing.GroundTravelMount",true)!
            .GetMethod("BuildScript",BindingFlags.NonPublic|BindingFlags.Static)!;
        string script=(string)builder.Invoke(null,new object[]{35022,1UL})!;
        var directory=new DirectoryInfo(AppContext.BaseDirectory);
        while(directory!=null&&!File.Exists(Path.Combine(directory.FullName,"CopilotBuddy.csproj")))directory=directory.Parent;
        using var lua=new RewardLua51Boundary.StockLua51(directory!.FullName);
        int total=0,passed=0;
        void Check(bool ok,string why){if(!ok)throw new InvalidOperationException(why);}
        RewardLua51Boundary.Observation Exec(RewardLua51Boundary.StockLua51.Session s,string code)=>s.Execute(code,(uint)Encoding.UTF8.GetByteCount(code));
        void Case(string name,Action<RewardLua51Boundary.StockLua51.Session> test)
        {
            total++;using var s=lua.BeginSession(Setup);
            try{test(s);passed++;Console.WriteLine("PASS ground mount submission: "+name);}
            catch(Exception e){Console.Error.WriteLine("FAIL ground mount submission: "+name+": "+e.Message);}
        }
        void Receipt(RewardLua51Boundary.StockLua51.Session s,string expected,int calls)
        {
            var r=Exec(s,script);Check(r.Load==0&&r.Call==0&&r.Values.SequenceEqual(new[]{expected})&&r.Clicks==calls,
                "receipt="+string.Join("|",r.Values)+" calls="+r.Clicks+" error="+r.Error);
        }
        Case("exact selected companion submits once without pretending mounted",s=>{
            Receipt(s,"cb-ground-mount-submitted",1);Receipt(s,"cb-ground-mount-pending",1);
            Check(Exec(s,"return mounted and 'yes' or 'no',selectedSlot").Values.SequenceEqual(new[]{"no","2"}),"wrong companion or fabricated mount");
        });
        foreach(string change in new[]{"mounted=true","combat=true","flying=true","falling=true","swimming=true","outdoors=false","taxi=true","vehicle=true","casting=true","channel=true","speed=1","speed=0/0","guid='foreign'","known=false","usable=false","usable='unknown'","outdoors='unknown'","enabled=0","duration=10;start=now","now=0/0","count=0/0","count=2.5","GetCompanionInfo=nil"})
            Case("denied current state/"+change,s=>{Exec(s,change);Receipt(s,"cb-ground-mount-rejected",0);});
        Case("failed action retains client-side pending lease",s=>{
            Exec(s,"fail=true");var r=Exec(s,script);Check(r.Call!=0&&r.Clicks==1,"post-entry error not reached");
            Exec(s,"fail=false");Receipt(s,"cb-ground-mount-pending",1);
        });
        Case("lease expires with bounded retry",s=>{
            Receipt(s,"cb-ground-mount-submitted",1);Exec(s,"now=109");Receipt(s,"cb-ground-mount-pending",1);
            Exec(s,"now=110");Receipt(s,"cb-ground-mount-submitted",2);
        });
        foreach(string foreign in new[]{"42","'foreign'","{}","{schema='cb-ground-mount-v1',started=0,untilAt=0/0}","{schema='cb-ground-mount-v1',actor={},spell=35022,started=90,untilAt=100}","{schema='cb-ground-mount-v1',actor=guid,spell=0/0,started=90,untilAt=100}"})
            Case("foreign or incomplete lease does not authorize overwrite/"+foreign,s=>{
                Exec(s,"foreign="+foreign+";CopilotBuddy_GroundMountLease=foreign");Receipt(s,"cb-ground-mount-rejected",0);
                Check(Exec(s,"return CopilotBuddy_GroundMountLease==foreign and 'same' or 'changed'").Values.Single()=="same","foreign state overwritten");
            });
        Console.WriteLine($"Ground mount submission: {passed}/{total}; actual script and Lua5.1, controlled client state/companion calls; no live mount.");
        if(passed!=total)throw new InvalidOperationException("Ground mount submission regression");
    }
    private const string Setup="""
now=100;clicks=0;selectedSlot=0;known=true;usable=true;enabled=1;start=0;duration=0;speed=0;count=2
guid='0x0000000000000001';mounted=false;combat=false;flying=false;falling=false;swimming=false;outdoors=true;taxi=false;vehicle=false;casting=false;channel=false;fail=false
function UnitGUID() return guid end
function IsMounted() return mounted end
function UnitAffectingCombat() return combat end
function IsFlying() return flying end
function IsFalling() return falling end
function IsSwimming() return swimming end
function IsOutdoors() return outdoors end
function UnitOnTaxi() return taxi end
function UnitInVehicle() return vehicle end
function UnitCastingInfo() if casting then return 'cast' end end
function UnitChannelInfo() if channel then return 'channel' end end
function GetUnitSpeed() return speed end
function GetSpellCooldown() return start,duration,enabled end
function IsUsableSpell() return usable end
function GetTime() return now end
function GetNumCompanions() return count end
function GetCompanionInfo(kind,slot) if slot==2 and known then return 1,'Selected mount',35022,0,false else return 2,'Other mount',123,0,false end end
function CallCompanion(kind,slot) clicks=clicks+1;selectedSlot=slot;if fail then error('post-entry error') end end
""";
}
