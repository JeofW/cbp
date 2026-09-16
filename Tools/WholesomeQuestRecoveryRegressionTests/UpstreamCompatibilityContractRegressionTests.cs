using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Bots.Quest.QuestOrder;
using Styx.Logic.Combat;
using Styx.Logic.Inventory.Frames.Merchant;
using Styx.Logic.Pathing;
using Styx.Logic.Profiles.Quest;
using Styx.WoWInternals;
using Styx.WoWInternals.WoWObjects;

// Contracts from pinned Likon69 upstream b91a6016, a89ffcb1, 034f5c39.
// Execute the real merchant reader and turn-in constructors against test-owned
// memory; inspect compiled terrain-click ABI without dispatching native code.
internal static class UpstreamCompatibilityContractRegressionTests
{
    private const BindingFlags S = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;
    private const BindingFlags I = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
    private sealed class AssertionFailure(string text) : Exception(text) { }
    [ModuleInitializer]
    internal static void Run()
    {
        var cases = new List<(string Name, Action Test)>();
        Type merchant = typeof(MerchantItem).GetNestedType("MerchantItemData", BindingFlags.NonPublic)!;
        cases.Add(("merchant record is the 32-byte WotLK stride", () => Check(Marshal.SizeOf(merchant)==32,"record size differs from the existing 32-byte merchant table stride")));
        foreach(var pair in new (string Name,int Offset)[]{("ItemId",4),("TextureId",8),("NumAvailable",12),("BuyPrice",16),("Quantity",24),("ExtendedCostId",28)})
        {
            var p=pair;
            cases.Add(("merchant field "+p.Name+" has its WotLK offset",()=>Check(Marshal.OffsetOf(merchant,p.Name).ToInt32()==p.Offset,"incorrect offset for "+p.Name)));
        }
        for(int i=0;i<3;i++)
        {
            int row=i;
            cases.Add(("real merchant constructor reads distinct row "+row,()=>WithMemory((address)=>{
                for(int n=0;n<3;n++) WriteRecord(address+(uint)n*32,1001+n,201+n,n==2?-1:20+n,300+n,5+n,400+n);
                var item=new MerchantItem(address+(uint)row*32,row);
                Check(item.ItemId==1001+row && item.TextureId==201+row && item.NumAvailable==(row==2?-1:20+row)
                    && item.BuyPrice==(ulong)(300+row) && item.Quantity==5+row && item.ExtendedCostId==400+row
                    && item.Index==row+1,"decoded fields or Lua index do not match the actual row");
            })));
        }
        foreach(int quantity in new[]{0,-1,1,5})
        {
            int q=quantity;
            cases.Add(("merchant quantity compatibility "+q,()=>WithMemory(address=>{
                WriteRecord(address,1001,201,-1,300,q,0);
                Check(new MerchantItem(address,0).Quantity==Math.Max(1,q),"quantity fallback changed");
            })));
        }
        cases.Add(("null merchant row retains harmless default",()=>{ var item=new MerchantItem(0,0); Check(item.ItemId==0 && item.Index==0,"null row fabricated inventory"); }));
        var point=new WoWPoint(1,2,3);
        cases.Add(("existing five-argument turn-in preserves fields",()=>VerifyTurnIn(new ForcedQuestTurnIn(1,"Quest",2,"NPC",point),"NPC",null)));
        cases.Add(("typed object turn-in remains distinct",()=>VerifyTurnIn(new ForcedQuestTurnIn(1,"Quest",2,"NPC",point,QuestObjectType.GameObject),"NPC",QuestObjectType.GameObject)));
        cases.Add(("four-argument upstream turn-in constructor is supported",()=>{
            var ctor=typeof(ForcedQuestTurnIn).GetConstructor(new[]{typeof(uint),typeof(string),typeof(uint),typeof(WoWPoint)});
            Check(ctor!=null,"stock SafeQuestTurnin caller has no matching constructor");
            VerifyTurnIn((ForcedQuestTurnIn)ctor!.Invoke(new object[]{1U,"Quest",2U,point}),"",null);
        }));
        Type terrain=typeof(SpellManager).GetNestedType("TerrainClickInfo",BindingFlags.NonPublic)!;
        cases.Add(("terrain function address matches pinned original-client contract",()=>Check(Convert.ToUInt32(typeof(SpellManager).GetField("Spell_C__HandleTerrainClick",S)!.GetRawConstantValue())==8438592U,"terrain function hexadecimal address disagrees with 8438592/0x80C340")));
        cases.Add(("terrain native record is 24 bytes",()=>Check(Marshal.SizeOf(terrain)==24,"terrain record size mismatch")));
        foreach(var pair in new (string Name,int Offset)[]{("TargetGuid",0),("Location",8),("Button",20)})
        {
            var p=pair;
            cases.Add(("terrain field "+p.Name+" has its native offset",()=>Check(Marshal.OffsetOf(terrain,p.Name).ToInt32()==p.Offset,"terrain field offset mismatch")));
        }
        cases.Add(("terrain button uses the uint flags contract",()=>Check(terrain.GetField("Button",I)!.FieldType==typeof(SpellManager.MouseButton)
            && Enum.GetUnderlyingType(typeof(SpellManager.MouseButton))==typeof(uint)
            && (uint)SpellManager.MouseButton.Left==1U,"terrain button is not a four-byte Left=1 flags value")));
        int passed=0,assertions=0,unexpected=0;
        foreach(var c in cases)
        {
            try { c.Test(); passed++; Console.WriteLine("PASS upstream contract: "+c.Name); }
            catch(AssertionFailure e) { assertions++; Console.Error.WriteLine("FAIL upstream contract assertion: "+c.Name+": "+e.Message); }
            catch(Exception e) { unexpected++; Console.Error.WriteLine("ERROR upstream contract fixture: "+c.Name+": "+e); }
        }
        Console.WriteLine($"Upstream compatibility scenarios: {passed}/{cases.Count}; assertions={assertions}; unexpected={unexpected}; actual managed readers and constructors, metadata-only terrain ABI; no game attached.");
        if(assertions+unexpected!=0) throw new InvalidOperationException($"Upstream compatibility regressions: assertions={assertions}; unexpected={unexpected}");
    }
    private static void VerifyTurnIn(ForcedQuestTurnIn node,string npc,QuestObjectType? kind) =>
        Check(node.QuestId==1 && node.QuestName=="Quest" && node.NpcId==2 && node.NpcName==npc && node.Location==new WoWPoint(1,2,3) && node.TurnInType==kind,"constructor changed quest or typed target identity");
    private static void WithMemory(Action<uint> run)
    {
        using var world=(IDisposable)Activator.CreateInstance(typeof(QuestPublicationRegressionTests).GetNestedType("Fixture",BindingFlags.NonPublic)!,true)!;
        IntPtr allocation=Marshal.AllocHGlobal(128);
        try
        {
            Check(ObjectManager.Executor==null,"native executor must remain absent");
            Marshal.Copy(new byte[128],0,allocation,128);
            run(unchecked((uint)allocation.ToInt32()));
        }
        finally { Marshal.FreeHGlobal(allocation); }
    }
    private static void WriteRecord(uint p,int id,int texture,int available,int price,int quantity,int extended)
    {
        var values=new[]{91,id,texture,available,price,92,quantity,extended};
        for(int i=0;i<values.Length;i++) Marshal.WriteInt32(new IntPtr(unchecked((int)(p+(uint)i*4))),values[i]);
    }
    private static void Check(bool condition,string why) { if(!condition) throw new AssertionFailure(why); }
}
