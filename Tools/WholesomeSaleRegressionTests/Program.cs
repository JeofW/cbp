using Styx;using Styx.Logic.Profiles;using WholesomeAQ;
var cases=new List<(string Name,Action<WholesomeAutoQuest> Run)>();
void Test(string n,Action<WholesomeAutoQuest> r)=>cases.Add((n,r));
void Check(bool ok,string why){if(!ok)throw new InvalidOperationException(why);}
void Sent()=>Check(MerchantFrame.Instance.Calls==1,"expected exactly one sale dispatch");
void NoSale()=>Check(MerchantFrame.Instance.Calls==0,"uncertain state authorized inventory mutation");
void Protect(uint id)=>Check(MerchantFrame.Instance.Ids.Contains(id),$"sale omitted item protection {id}");
void AddQuest(WholesomeAutoQuest bot,int id,uint item,bool scheduled,bool accepted,bool done=false)
{
 bot._dataLoader.Database!.Quests!.Add(new QuestEntry{Id=id,Objectives=new(){new Objective{ItemId=(int)item}}});
 if(scheduled)bot._scheduler!.ActiveQuestIds.Add(id);
 if(accepted)StyxWoW.Me!.QuestLog.Quests!.Add(new AcceptedQuest{Id=(uint)id,IsCompleted=done});
}
Test("shared runtime ID reaches merchant",b=>{ProtectedItemsManager.Add(500U);b.RunSale();Sent();Protect(500);});
Test("shared runtime name reaches merchant",b=>{ProtectedItemsManager.Add("Keep This");b.RunSale();Sent();Check(MerchantFrame.Instance.Names.Contains("keep this"),"runtime name lost");});
Test("profile ID reaches merchant",b=>{ProfileManager.CurrentProfile.ProtectedItems.Add(501U);b.RunSale();Sent();Protect(501);});
Test("profile name reaches merchant",b=>{ProfileManager.CurrentProfile.ProtectedItems.Add("profile item");b.RunSale();Sent();Check(MerchantFrame.Instance.Names.Contains("profile item"),"profile name lost");});
Test("accepted unscheduled collection is protected",b=>{AddQuest(b,867,600,false,true);b.RunSale();Sent();Protect(600);});
Test("completed accepted quest waiting for turn-in is protected",b=>{AddQuest(b,867,601,false,true,true);b.RunSale();Sent();Protect(601);});
Test("accepted unscheduled start item is protected",b=>{AddQuest(b,867,0,false,true);b._dataLoader.Database!.Quests![0].StartItem=602;b.RunSale();Sent();Protect(602);});
Test("scheduled quest protection is retained",b=>{AddQuest(b,867,603,true,false);b.RunSale();Sent();Protect(603);});
Test("accepted quest remains protected without scheduler",b=>{AddQuest(b,867,604,false,true);b._scheduler=null;b.RunSale();Sent();Protect(604);});
Test("unrelated inactive dataset items are not blanket protected",b=>{AddQuest(b,867,605,false,false);b.RunSale();Sent();Check(!MerchantFrame.Instance.Ids.Contains(605),"unrelated data cannot block all selling");});
Test("no sale when accepted quest has no known data",b=>{StyxWoW.Me!.QuestLog.Quests!.Add(new AcceptedQuest{Id=999});b.RunSale();NoSale();});
Test("no sale when quest database is unavailable",b=>{b._dataLoader.Database=null;b.RunSale();NoSale();});
Test("no sale when accepted quest log is unavailable",b=>{StyxWoW.Me!.QuestLog.Quests=null;b.RunSale();NoSale();});
Test("no sale for absent player",b=>{StyxWoW.Me=null;b.RunSale();NoSale();});
Test("no sale for invalid player",b=>{StyxWoW.Me!.IsValid=false;b.RunSale();NoSale();});
Test("no sale for dead player",b=>{StyxWoW.Me!.IsAlive=false;b.RunSale();NoSale();});
Test("no sale for incomplete accepted objective metadata",b=>{AddQuest(b,867,0,false,true);b._dataLoader.Database!.Quests![0].Objectives=null;b.RunSale();NoSale();});
Test("no sale when no quality is enabled",b=>{b._settings.SellWhite=false;b.RunSale();NoSale();});
Test("closed merchant performs no sale",b=>{MerchantFrame.Instance.IsVisible=false;b.RunSale();NoSale();});
Test("configured quality mask is preserved",b=>{b._settings.SellGreen=b._settings.SellBlue=true;b.RunSale();Sent();Check(MerchantFrame.Instance.Mask==(ItemQuality.Common|ItemQuality.Uncommon|ItemQuality.Rare),"mask changed");});
Test("food drink and special inventory remain protected",b=>{Consumable.Food=new Item{Entry=610};Consumable.Drink=new Item{Entry=611};for(int i=1;i<=4;i++)StyxWoW.Me!.BagItems.Add(new Item{Entry=(uint)(611+i),ItemClass=(WoWItemClass)i});b.RunSale();Sent();for(uint id=610;id<=615;id++)Protect(id);});
Test("inventory needed by a newly accepted quest is reconsidered on next call",b=>{AddQuest(b,867,620,false,false);b.RunSale();StyxWoW.Me!.QuestLog.Quests!.Add(new AcceptedQuest{Id=867});MerchantFrame.Instance=new();b.RunSale();Sent();Protect(620);});
Test("thread cancellation is not swallowed or followed by a sale",b=>{StyxWoW.Me!.QuestLog.Failure=new System.Threading.ThreadInterruptedException();try{b.RunSale();throw new InvalidOperationException("cancellation swallowed");}catch(System.Threading.ThreadInterruptedException){}NoSale();});
var errors=new List<string>();
foreach(var t in cases)
{
 ProtectedItemsManager.Remove(500U);ProtectedItemsManager.Remove("Keep This");ProfileManager.CurrentProfile=new();
 StyxWoW.Me=new();MerchantFrame.Instance=new();Consumable.Food=Consumable.Drink=null;
 try{t.Run(new WholesomeAutoQuest());Console.WriteLine("PASS sale: "+t.Name);}catch(Exception e){errors.Add(t.Name+": "+e.GetType().Name+" "+e.Message);Console.Error.WriteLine("FAIL sale: "+errors[^1]);}
}
Console.WriteLine($"Wholesome sale scenarios: {cases.Count-errors.Count}/{cases.Count}; actual extracted method and protected manager; controlled merchant/world; no client attached.");
if(errors.Count!=0)Environment.ExitCode=1;
