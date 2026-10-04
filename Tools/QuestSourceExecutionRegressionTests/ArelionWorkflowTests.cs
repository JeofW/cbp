using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Styx.Logic.Pathing;
using Styx.Logic.Questing;

internal static class ArelionWorkflowTests
{
    internal static void Run(string root)
    {
        int passed=0,total=0;
        using var lua=new RewardLua51Boundary.StockLua51(root);
        void Case(string name,Action<World,ArelionLureController> test)
        {
            total++;
            using var world=new World(lua);var controller=new ArelionLureController(world);
            try{test(world,controller);passed++;Console.WriteLine("PASS actual lure workflow: "+name);}
            catch(Exception error){Console.Error.WriteLine("FAIL actual lure workflow: "+name+": "+error);}
        }
        Case("wine purchase, autocomplete lure, relocation, scroll and delayed credit",(w,c)=>
        {
            w.Vendor=GroundTransitionState.Pending;Tick(w,c);Check(w.Interactions==0,"travel fabricated vendor interaction");
            w.Vendor=GroundTransitionState.Ready;Tick(w,c);Check(w.Interactions==1,"vendor interaction missing");
            w.SetUi(ArelionUi.VendorGossip);Tick(w,c);Check(w.Int("vendorSelections")==1,"vendor menu was not opened");
            w.SetUi(ArelionUi.Merchant);Tick(w,c);Check(w.Int("purchases")==1&&w.State.Wine==0,"purchase fabricated inventory");
            for(int i=0;i<5;i++)Tick(w,c);
            Check(w.Int("purchases")==1&&c.Status==QuestWorkflowStatus.Running,"pending purchase repeated or completed quest");
            w.State=w.State with{Wine=1};w.Code("wine=1");w.SetUi(ArelionUi.None);w.Viera=GroundTransitionState.Pending;Tick(w,c);
            Check(w.Int("scrollUses")==0&&w.Int("lureRewards")==0,"wine stock fabricated lure or scroll use");
            w.Viera=GroundTransitionState.Ready;w.SetUi(ArelionUi.VieraGossip);Tick(w,c);
            Check(w.Int("questSelections")==1&&w.Int("selectedQuestIndex")==2,"exact available lure quest selection missing");
            w.SetUi(ArelionUi.LureProgress);Tick(w,c);Check(w.Int("lureProgress")==1&&w.Int("lureRewards")==0,"progress button fabricated reward");
            w.SetUi(ArelionUi.LureReward);Tick(w,c);Check(w.Int("lureRewards")==1&&c.Status==QuestWorkflowStatus.Running,"lure reward completed parent quest");
            w.State=w.State with{Wine=0,VieraMoving=true,LureInProgress=true};w.Code("wine=0");w.SetUi(ArelionUi.None);w.Viera=GroundTransitionState.Pending;
            for(int i=0;i<8;i++)Tick(w,c);
            Check(w.Int("purchases")==1&&w.FollowCalls>=8&&w.Int("scrollUses")==0,"follow repurchased wine or used scroll before relocation");
            w.State=w.State with{AtLureEndpoint=true,VieraMoving=false};w.Viera=GroundTransitionState.Ready;Tick(w,c);
            Check(w.Int("scrollUses")==1&&c.Status==QuestWorkflowStatus.Running,"item request was treated as quest credit");
            for(int i=0;i<6;i++)Tick(w,c);
            Check(w.Int("scrollUses")==1&&c.Status==QuestWorkflowStatus.Running,"unacknowledged scroll use repeated");
            w.State=w.State with{Credit=1};Tick(w,c);
            Check(c.Status==QuestWorkflowStatus.Completed&&w.Retirements==1,"typed credit did not settle exact workflow");
            Tick(w,c);Check(w.Int("scrollUses")==1&&w.Retirements==1,"completed workflow issued another effect");
            Check(w.Int("wineUses")==0,"the lure wine was drunk instead of handed in");
        });
        Case("already-lured endpoint resumes without another wine",(w,c)=>
        {
            w.State=w.State with{AtLureEndpoint=true,LureInProgress=true};w.SetUi(ArelionUi.None);
            Tick(w,c);Check(w.Int("scrollUses")==1&&w.Int("purchases")==0,"resumed endpoint paid another wine");
        });
        Case("already-walking lure resumes without buying wine",(w,c)=>
        {
            w.State=w.State with{LureInProgress=true,VieraMoving=true};w.Viera=GroundTransitionState.Pending;
            for(int i=0;i<8;i++)Tick(w,c);
            Check(w.FollowCalls==8&&w.VendorCalls==0&&w.Int("purchases")==0,"resumed lure lost its observed phase");
        });
        foreach(string missing in new[]{"acceptance","credit","wine","scroll"})
            Case("unknown "+missing+" cannot authorize an action",(w,c)=>
            {
                w.State=missing switch{"acceptance"=>w.State with{Accepted=null},"credit"=>w.State with{Credit=null},
                    "wine"=>w.State with{Wine=null},_=>w.State with{Scroll=null}};
                Tick(w,c);Check(w.TotalRequests==0,"missing observation authorized work");
                w.Clock+=31;Tick(w,c);Check(c.Status==QuestWorkflowStatus.Deferred&&w.TotalRequests==0,"unknown observation waited indefinitely or completed");
            });
        Case("unacknowledged purchase defers with exactly one request",(w,c)=>
        {
            w.SetUi(ArelionUi.Merchant);Tick(w,c);w.Clock+=13;Tick(w,c);
            Check(c.Status==QuestWorkflowStatus.Deferred&&w.Int("purchases")==1,"purchase timeout repeated effect");
        });
        Case("unacknowledged scroll defers without claiming credit",(w,c)=>
        {
            w.State=w.State with{AtLureEndpoint=true};Tick(w,c);w.Clock+=16;Tick(w,c);
            Check(c.Status==QuestWorkflowStatus.Deferred&&w.Int("scrollUses")==1,"item timeout repeated or fabricated credit");
        });
        Case("consumed scroll is not required again while its credit is pending",(w,c)=>
        {
            w.State=w.State with{AtLureEndpoint=true};Tick(w,c);
            w.State=w.State with{Scroll=0};w.Clock+=16;Tick(w,c);
            Check(c.Status==QuestWorkflowStatus.Deferred&&w.Int("scrollUses")==1&&c.Reason.Contains("credit-unobserved"),
                "a consumed submitted scroll restarted stock acquisition instead of its credit deadline");
        });
        Case("lure acknowledgement timeout is bounded",(w,c)=>
        {
            w.State=w.State with{Wine=1};w.Code("wine=1");w.SetUi(ArelionUi.LureReward);Tick(w,c);
            w.Clock+=151;Tick(w,c);Check(c.Status==QuestWorkflowStatus.Deferred&&w.Int("lureRewards")==1,"lure was rewarded repeatedly");
        });
        Case("replacement of exact lured recipient revokes follow",(w,c)=>
        {
            w.State=w.State with{Wine=1};w.Code("wine=1");w.SetUi(ArelionUi.LureReward);Tick(w,c);
            w.State=w.State with{VieraToken=new object()};Tick(w,c);
            Check(c.Status==QuestWorkflowStatus.Revoked&&w.Int("scrollUses")==0,"replacement inherited lure authority");
        });
        foreach(string boundary in new[]{"observe","move","submit"})
            Case("owner revocation at "+boundary+" is terminal",(w,c)=>
            {
                w.State=w.State with{AtLureEndpoint=true};w.Revoke=boundary;Tick(w,c);
                Check(c.Status==QuestWorkflowStatus.Revoked&&w.Int("scrollUses")==0,"revoked owner issued an action");
            });
        Case("mounted incidental travel continues while actions stay denied",(w,c)=>
        {
            w.State=w.State with{CanAct=false};w.Vendor=GroundTransitionState.Pending;
            for(int i=0;i<8;i++)Tick(w,c);
            Check(w.VendorCalls==8&&w.TotalRequests==0&&w.Retirements==0,"travel owner parked or interacted while mounted");
        });
        Case("parent absence revokes and known credit alone completes",(w,c)=>
        {
            w.State=w.State with{Accepted=false,Credit=1};Tick(w,c);
            Check(c.Status==QuestWorkflowStatus.Revoked&&w.TotalRequests==0,"absent parent adopted stale credit");
        });

        foreach(string state in new[]{"combat=true","mounted=true","flying=true","falling=true","swimming=true","taxi=true",
            "vehicle=true","casting=true","channel=true","speed=7","speed=0/0","player='wrong'","target='wrong'",
            "item=29112","cooldown=10","enabled=0","targeting=true","merchant=true","GetContainerItemCooldown=nil"})
            Case("actual scroll Lua denies "+state,(w,c)=>
            {
                w.Code(state);Check(w.Run(ArelionLureScripts.Scroll(1,2,0,1))=="rejected"&&w.Int("scrollUses")==0,"invalid original-client state used an item");
            });
        foreach(string state in new[]{"money=0","price=0/0","bundle=2","available=0","wine=1","npc='wrong'","merchant=false","merchantItem=123"})
            Case("actual purchase Lua denies "+state,(w,c)=>
            {
                w.SetUi(ArelionUi.Merchant);w.Code(state);
                Check(w.Run(ArelionLureScripts.BuyWine(1,3))=="rejected"&&w.Int("purchases")==0,"invalid vendor observation purchased stock");
            });
        foreach(bool reward in new[]{false,true})
            Case("disabled numeric-zero lure button is not clicked, reward="+reward,(w,c)=>
            {
                w.Code("wine=1;buttonEnabled=0");w.SetUi(reward?ArelionUi.LureReward:ArelionUi.LureProgress);
                Check(w.Run(ArelionLureScripts.AdvanceLure(1,2,reward))=="rejected"&&w.Int("lureRewards")==0&&w.Int("lureProgress")==0,"disabled button authorized a request");
            });
        Case("actual client lease survives an ambiguous post-entry error",(w,c)=>
        {
            w.Code("failAfterUse=true");var error=w.Execute(ArelionLureScripts.Scroll(1,2,0,1));
            Check(error.Call!=0&&w.Int("scrollUses")==1,"fixture did not reach ambiguous native entry");
            w.Code("failAfterUse=false");Check(w.Run(ArelionLureScripts.Scroll(1,2,0,1))=="pending"&&w.Int("scrollUses")==1,"ambiguous request repeated");
        });
        Case("pending lease cannot be borrowed by another recipient",(w,c)=>
        {
            Check(w.Run(ArelionLureScripts.Scroll(1,2,0,1))=="submitted","initial control missing");
            w.Code("target='0x0000000000000004'");
            Check(w.Run(ArelionLureScripts.Scroll(1,4,0,1))=="rejected"&&w.Int("scrollUses")==1,"different recipient borrowed pending acknowledgement");
        });
        Case("backward or malformed lease clock rejects a repeat",(w,c)=>
        {
            w.Run(ArelionLureScripts.Scroll(1,2,0,1));w.Code("now=90");
            Check(w.Run(ArelionLureScripts.Scroll(1,2,0,1))=="rejected"&&w.Int("scrollUses")==1,"backward clock admitted duplicate input");
        });
        Console.WriteLine($"Arelion source workflow: {passed}/{total}; actual controller and production scripts in stock Lua5.1; controlled movement/UI/inventory/server acknowledgements, no live game.");
        if(passed!=total)Environment.ExitCode=1;
    }
    private static void Check(bool value,string reason){if(!value)throw new InvalidOperationException(reason);}
    private static void Tick(World world,ArelionLureController controller){world.Clock+=1;world.Code("now="+world.Clock.ToString(CultureInfo.InvariantCulture));controller.Tick();}

    private sealed class World:IArelionLureRuntime,IDisposable
    {
        private readonly RewardLua51Boundary.StockLua51.Session _session;
        internal double Clock=100;internal string Revoke="";
        internal int Interactions,Retirements,VendorCalls,FollowCalls;
        internal ArelionObservation State=new(true,0,0,1,true,true,new object(),false,false,ArelionUi.None);
        internal GroundTransitionState Vendor=GroundTransitionState.Ready,Viera=GroundTransitionState.Ready;
        public bool Current{get;private set;}=true;
        public double Now=>Clock;
        internal World(RewardLua51Boundary.StockLua51 lua)=>_session=lua.BeginSession(Client);
        internal RewardLua51Boundary.Observation Execute(string code)=>_session.Execute(code,(uint)Encoding.UTF8.GetByteCount(code));
        internal string[] Values(string code){var result=Execute(code);if(result.Load!=0||result.Call!=0)throw new InvalidOperationException(result.Error);return result.Values.ToArray();}
        internal void Code(string code)=>Values(code);
        internal string Run(string code)=>Values(code).Single();
        internal int Int(string name)=>int.Parse(Run("return "+name),CultureInfo.InvariantCulture);
        internal int TotalRequests=>Interactions+Int("purchases")+Int("questSelections")+Int("lureRewards")+Int("lureProgress")+Int("scrollUses")+Int("vendorSelections");
        internal void SetUi(ArelionUi ui)
        {
            State=State with{Ui=ui};
            Code("merchant="+(ui==ArelionUi.Merchant?"true":"false")+";gossip="+(ui is ArelionUi.VieraGossip or ArelionUi.VendorGossip?"true":"false")
                +";quest="+(ui is ArelionUi.LureProgress or ArelionUi.LureReward?"true":"false")+";reward="+(ui==ArelionUi.LureReward?"true":"false")
                +";npc='0x000000000000000"+(ui is ArelionUi.Merchant or ArelionUi.VendorGossip?"3":"2")+"'");
        }
        public ArelionObservation Observe(){if(Revoke=="observe")Current=false;return State;}
        public GroundTransitionState MoveVendor(){VendorCalls++;if(Revoke=="move")Current=false;return Vendor;}
        public GroundTransitionState MoveViera(bool followingLure){if(followingLure)FollowCalls++;if(Revoke=="move")Current=false;return Viera;}
        public QuestWorkflowReceipt InteractVendor(){Interactions++;return QuestWorkflowReceipt.Submitted;}
        public QuestWorkflowReceipt InteractViera(){Interactions++;return QuestWorkflowReceipt.Submitted;}
        private QuestWorkflowReceipt Submit(string code)
        {
            if(Revoke=="submit"){Current=false;return QuestWorkflowReceipt.Rejected;}
            return Run(code) switch{"submitted"=>QuestWorkflowReceipt.Submitted,"pending"=>QuestWorkflowReceipt.Pending,
                "progress-submitted"=>QuestWorkflowReceipt.ProgressSubmitted,"reward-submitted"=>QuestWorkflowReceipt.RewardSubmitted,_=>QuestWorkflowReceipt.Rejected};
        }
        public QuestWorkflowReceipt OpenVendor()=>Submit(ArelionLureScripts.OpenVendor(1,3));
        public QuestWorkflowReceipt BuyWine()=>Submit(ArelionLureScripts.BuyWine(1,3));
        public QuestWorkflowReceipt SelectLureQuest()=>Submit(ArelionLureScripts.SelectLure(1,2,1,2));
        public QuestWorkflowReceipt AdvanceLureQuest()=>Submit(ArelionLureScripts.AdvanceLure(1,2,State.Ui==ArelionUi.LureReward));
        public QuestWorkflowReceipt UseScroll()=>Submit(ArelionLureScripts.Scroll(1,2,0,1));
        public void RetireMovement()=>Retirements++;
        public void Dispose()=>_session.Dispose();
    }
    internal const string Client="""
now=100;clicks=0;player='0x0000000000000001';target='0x0000000000000002';npc=target
wine=0;scroll=1;money=10000;price=100;bundle=1;available=-1;merchantItem=29112;item=23693
speed=0;cooldown=0;enabled=1;buttonEnabled=1;merchant=false;gossip=false;quest=false;reward=false
combat=false;mounted=false;flying=false;falling=false;swimming=false;taxi=false;vehicle=false;casting=false;channel=false;targeting=false
purchases=0;questSelections=0;selectedQuestIndex=0;lureProgress=0;lureRewards=0;scrollUses=0;wineUses=0;vendorSelections=0
function UnitGUID(u) if u=='player' then return player elseif u=='target' then return target elseif u=='npc' then return npc end end
function UnitAffectingCombat() return combat end
function GetTime() return now end
function GetUnitSpeed() return speed end
function IsMounted() return mounted end
function IsFlying() return flying end
function IsFalling() return falling end
function IsSwimming() return swimming end
function UnitOnTaxi() return taxi end
function UnitInVehicle() return vehicle end
function UnitCastingInfo() if casting then return 'cast' end end
function UnitChannelInfo() if channel then return 'channel' end end
function GetItemCount(id,bank) assert(bank==false);if id==29112 then return wine elseif id==23693 then return scroll end error('unrelated stock') end
function GetMoney() return money end
function GetMerchantNumItems() return 1 end
function GetMerchantItemLink(i) assert(i==1);return '|Hitem:'..merchantItem..':0|h[Item]|h' end
function GetMerchantItemInfo(i) assert(i==1);return 'Cenarion Spirits','icon',price,bundle,available,true end
function BuyMerchantItem(i,n) assert(i==1 and n==1);purchases=purchases+1 end
function GetGossipOptions() return 'Shop','vendor' end
function SelectGossipOption(i) assert(i==1);vendorSelections=vendorSelections+1 end
function GetNumGossipAvailableQuests() return 2 end
function SelectGossipAvailableQuest(i) assert(i==2);selectedQuestIndex=i;questSelections=questSelections+1 end
function GetNumQuestChoices() return 0 end
MerchantFrame={IsVisible=function()return merchant end};GossipFrame={IsVisible=function()return gossip end};QuestFrame={IsVisible=function()return quest end}
QuestFrameCompleteButton={IsVisible=function()return quest and not reward end,IsEnabled=function()return buttonEnabled end,Click=function()lureProgress=lureProgress+1 end}
QuestFrameCompleteQuestButton={IsVisible=function()return quest and reward end,IsEnabled=function()return buttonEnabled end,Click=function()lureRewards=lureRewards+1 end}
function GetContainerItemLink(b,s) assert(b==0 and s==1);return '|Hitem:'..item..':0|h[Scroll]|h' end
function GetContainerItemCooldown(b,s) assert(b==0 and s==1);return now,cooldown,enabled end
function SpellIsTargeting() return targeting end
function UseContainerItem(b,s) assert(b==0 and s==1);if item==29112 then wineUses=wineUses+1 else scrollUses=scrollUses+1 end;if failAfterUse then error('ambiguous post-entry failure') end end
""";
}
