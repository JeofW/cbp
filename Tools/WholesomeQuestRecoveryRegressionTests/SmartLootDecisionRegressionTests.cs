using System;
using System.CodeDom.Compiler;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;

// Compile all four tracked scoring/decision/settings/preset owners unchanged.
// Only world/item/Lua observations and dispatch are controlled; no live roll,
// item binding, settings-file write or game attachment occurs.
internal static class SmartLootDecisionRegressionTests
{
    [ModuleInitializer]
    internal static void Run()
    {
        const BindingFlags flags=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance|BindingFlags.Static;
        string? root=null;
        for(var d=new DirectoryInfo(AppContext.BaseDirectory);d!=null;d=d.Parent)
            if(File.Exists(Path.Combine(d.FullName,"CopilotBuddy.csproj"))){root=d.FullName;break;}
        if(root==null)throw new InvalidOperationException("Tracked checkout required");
        string temp=Path.Combine(Path.GetTempPath(),"cb-loot-owner-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        bool logging=Styx.Helpers.Logging.FileLogging;
        try
        {
            Styx.Helpers.Logging.FileLogging=false;
            foreach(string name in new[]{"SmartLootRoller.cs","SmartLootRollerSettings.cs","PawnScorer.cs","StatWeightsPresets.cs"})
                File.Copy(Path.Combine(root,"runtime-snapshot","Plugins","SmartLootRoller",name),Path.Combine(temp,name));
            File.WriteAllText(Path.Combine(temp,"Boundary.cs"),Boundary);
            Type type=typeof(Styx.StyxWoW).Assembly.GetType("Styx.Loaders.SourceCompiler",true)!;
            object compiler=Activator.CreateInstance(type,new object[]{temp})!;
            foreach(string path in ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator))
                type.GetMethod("AddReference",flags)!.Invoke(compiler,new object[]{path});
            var result=(CompilerResults)type.GetMethod("Compile",flags)!.Invoke(compiler,null)!;
            var errors=result.Errors.Cast<CompilerError>().Where(e=>!e.IsWarning).ToArray();
            if(errors.Length!=0)throw new InvalidOperationException("Actual SmartLoot source compile failed: "+string.Join(";",errors.Select(e=>e.ToString())));
            var assembly=(Assembly)type.GetProperty("CompiledAssembly",flags)!.GetValue(compiler)!;
            try{assembly.GetType("LootCases",true)!.GetMethod("Run")!.Invoke(null,null);}
            catch(TargetInvocationException e)when(e.InnerException!=null){ExceptionDispatchInfo.Capture(e.InnerException).Throw();throw;}
        }
        finally{Styx.Helpers.Logging.FileLogging=logging;Directory.Delete(temp,true);}
    }
    private const string Boundary="""
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using SmartLootRoller;
using Styx;
using Styx.Logic.Inventory;
using Styx.WoWInternals;
using Styx.WoWInternals.WoWObjects;
public static class LootCases
{
    private sealed class Failure(string text):Exception(text){}
    internal static readonly List<string> Scripts=new();
    internal static string Stats="ITEM_MOD_STRENGTH_SHORT:10,";
    internal static bool CanDisenchant, CanNeed, CanGreed;
    public static void Run()
    {
        var cases=new List<(string,Action)>();
        void Add(string name,Action run)=>cases.Add((name,run));
        foreach(var entry in new[]{(MatchRollType.Need,1),(MatchRollType.Greed,2),(MatchRollType.Pass,0)})
        {var e=entry;Add("matching "+e.Item1+" uses actual roll code",()=>{var s=Reset();s.MatchRule=e.Item1;Roll();Expect(e.Item2);});}
        foreach(var entry in new[]{(NoMatchRollType.Greed,2),(NoMatchRollType.Pass,0)})
        {var e=entry;Add("nonmatching "+e.Item1+" uses actual roll code",()=>{var s=Reset();s.Weight_Strength=0;s.NoMatchRule=e.Item1;Roll();Expect(e.Item2);});}
        Add("saved numeric matching Pass remains Pass",()=>{var s=Reset();s.MatchRule=(MatchRollType)Enum.Parse(typeof(MatchRollType),"3");Roll();Expect(0);});
        Add("saved numeric nonmatching Pass remains Pass",()=>{var s=Reset();s.Weight_Strength=0;s.NoMatchRule=(NoMatchRollType)Enum.Parse(typeof(NoMatchRollType),"3");Roll();Expect(0);});
        Add("saved named matching Pass remains Pass",()=>{var s=Reset();s.MatchRule=(MatchRollType)Enum.Parse(typeof(MatchRollType),"Pass");Roll();Expect(0);});
        Add("explicit disenchant with capability remains disenchant",()=>{var s=Reset();s.Weight_Strength=0;s.RollForLootDE=true;CanDisenchant=true;Roll();Expect(3);});
        Add("unavailable disenchant uses configured Greed",()=>{var s=Reset();s.Weight_Strength=0;s.RollForLootDE=true;Roll();Expect(2);});
        Add("unavailable disenchant with configured Pass stays Pass",()=>{var s=Reset();s.Weight_Strength=0;s.RollForLootDE=true;s.NoMatchRule=NoMatchRollType.Pass;Roll();Expect(0);});
        foreach(int value in new[]{-1,4,99})
        {int n=value;Add("invalid matching rule "+n+" cannot dispatch a foreign code",()=>{var s=Reset();s.MatchRule=(MatchRollType)n;Roll();Expect(0);});
         Add("invalid nonmatching rule "+n+" cannot dispatch a foreign code",()=>{var s=Reset();s.Weight_Strength=0;s.NoMatchRule=(NoMatchRollType)n;Roll();Expect(0);});}
        Add("disabled rolling performs no dispatch",()=>{var s=Reset();s.RollForLoot=false;Roll();Check(Scripts.Count==0,"disabled roll crossed dispatch");});
        Add("settings MP5 reaches the actual Lua scorer",()=>{var s=Reset();s.ClearWeights();s.Weight_Mp5=2;Stats="ITEM_MOD_MANA_REGENERATION_SHORT:10,";Score(s,20);});
        Add("Strength and Agility both contribute to actual score",()=>{var s=Reset();s.Weight_Strength=2.7f;s.Weight_Agility=1.8f;Stats="ITEM_MOD_STRENGTH_SHORT:10,ITEM_MOD_AGILITY_SHORT:10,";Score(s,45);});
        foreach(string key in new[]{"Stamina","Armor","DefenseRating","DodgeRating","ParryRating","BlockRating","BlockValue","Strength","Agility","HitRating","ExpertiseRating","ArmorPenetrationRating","HasteRating","CritRating","AttackPower","FeralAttackPower","WeaponDps","SpellPower","Intellect","Spirit","Mp5"})
        {string k=key;Add("settings round-trip canonical "+k,()=>{var s=Reset();s.ClearWeights();typeof(SmartLootRollerSettings).GetProperty("Weight_"+k)!.SetValue(s,2f);var weights=s.GetWeightsDictionary();Check(weights.Count==1&&weights.TryGetValue(k,out var v)&&v==2,"setting not mapped to scorer key "+k);});}
        Add("Ret preset weights Strength above positive Agility",()=>{Reset();var w=PawnScorer.ParseWeights(StatWeightsPresets.GetPresets()["Paladin - Retribution"]);Check(w["Strength"]>w["Agility"]&&w["Agility"]>0&&w["WeaponDps"]>0,"Ret preset lost expected weighted stats");});
        Add("two equal Ret items differ by their real weighted stats",()=>{var s=Reset();s.Weight_Strength=2.7f;s.Weight_Agility=1.8f;Stats="ITEM_MOD_STRENGTH_SHORT:10,";float strength=PawnScorer.CalculateScore(ItemInfo.Current,"item:41001",s.GetWeightsDictionary());Stats="ITEM_MOD_AGILITY_SHORT:10,";float agility=PawnScorer.CalculateScore(ItemInfo.Current,"item:41001",s.GetWeightsDictionary());Check(strength>agility&&agility>0,"actual scorer lost Strength/Agility ordering");});
        // Original 3.3.5 FrameXML separates canNeed, canGreed and canDisenchant.
        // A weighted upgrade never grants a roll option disabled by the client.
        foreach(bool allowDE in new[]{false,true})
        foreach(bool passFallback in new[]{false,true})
        foreach(int flags in Enumerable.Range(0,8))
        {
            bool de=allowDE, pass=passFallback;int bits=flags;
            Add($"Need availability matrix de={de} pass={pass} flags={bits}",()=>{
                var s=Reset();s.MatchRule=MatchRollType.Need;s.RollForLootDE=de;
                s.NoMatchRule=pass?NoMatchRollType.Pass:NoMatchRollType.Greed;
                CanNeed=(bits&1)!=0;CanGreed=(bits&2)!=0;CanDisenchant=(bits&4)!=0;
                Roll();Expect(CanNeed?1:de&&CanDisenchant?3:!pass&&CanGreed?2:0);
            });
        }
        foreach(bool matching in new[]{false,true})
        foreach(bool allowed in new[]{false,true})
        {
            bool match=matching, can=allowed;
            Add($"configured Greed matches={match} available={can}",()=>{
                var s=Reset();if(!match)s.Weight_Strength=0;s.MatchRule=MatchRollType.Greed;
                CanNeed=true;CanGreed=can;Roll();Expect(can?2:0);
            });
        }
        int pass=0,failed=0,unexpected=0;
        foreach(var item in cases){try{item.Item2();pass++;Console.WriteLine("PASS loot decision: "+item.Item1);}catch(Failure e){failed++;Console.Error.WriteLine("FAIL loot decision assertion: "+item.Item1+": "+e.Message);}catch(Exception e){unexpected++;Console.Error.WriteLine("ERROR loot decision fixture: "+item.Item1+": "+e);}}
        Console.WriteLine($"SmartLoot decision scenarios: {pass}/{cases.Count}; assertions={failed}; unexpected={unexpected}; actual four tracked owners; controlled world and Lua; no live roll/equip.");
        if(failed+unexpected!=0)throw new InvalidOperationException("SmartLoot decision failures");
    }
    private static SmartLootRollerSettings Reset()
    {
        Scripts.Clear();Stats="ITEM_MOD_STRENGTH_SHORT:10,";CanDisenchant=false;CanNeed=true;CanGreed=true;StyxWoW.Me=new Player();
        ItemInfo.Current=new ItemInfo{InventoryType=InventoryType.Neck,ItemClass=WoWItemClass.Armor,Name="test-neck"};
        var s=(SmartLootRollerSettings)RuntimeHelpers.GetUninitializedObject(typeof(SmartLootRollerSettings));
        typeof(SmartLootRollerSettings).GetField("_instance",BindingFlags.Static|BindingFlags.NonPublic)!.SetValue(null,s);
        s.RollForLoot=true;s.MatchRule=MatchRollType.Need;s.NoMatchRule=NoMatchRollType.Greed;s.Weight_Strength=2;s.AllowedArmor="Plate";s.AllowedWeapons="SwordTwoHand";return s;
    }
    private static void Roll()=>typeof(SmartLootRoller.SmartLootRoller).GetMethod("HandleLootRoll",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(new SmartLootRoller.SmartLootRoller(),new object[]{null!,new LuaEventArgs{Args=new object[]{42}}});
    private static void Expect(int code)=>Check(Scripts.SequenceEqual(new[]{"RollOnLoot(42, "+code+")"}),"expected code "+code+", actual "+string.Join(";",Scripts));
    private static void Score(SmartLootRollerSettings settings,float expected)=>Check(Math.Abs(PawnScorer.CalculateScore(ItemInfo.Current,"item:41001",settings.GetWeightsDictionary())-expected)<.001f,"actual score does not equal "+expected);
    private static void Check(bool yes,string why){if(!yes)throw new Failure(why);}
}
/* Controlled item/world observation, not scorer replacement. */ namespace Styx.WoWInternals.WoWObjects
{
    public class ItemInfo
    {
        public static ItemInfo Current=new();public static ItemInfo FromId(uint id)=>Current;
        public InventoryType InventoryType;public WoWItemClass ItemClass;public WoWItemArmorClass ArmorClass;public WoWItemWeaponClass WeaponClass;
        public WoWItemBondType Bond;public WoWItemQuality Quality;public int RequiredLevel,Level;public float Armor,DPS;public string Name="test";
        public Dictionary<Stat,float> GetItemStats()=>new();
    }
    public class WoWItem
    {
        public ItemInfo ItemInfo=new();public string Name="test",Link="item:41001";public bool Usable=true;
        public void UseContainerItem()=>LootCases.Scripts.Add("EQUIP");
    }
}
/* Controlled player and inventory observations. */ namespace Styx
{
    public class Player{public string Name="test";public int Level=80;public List<WoWItem> BagItems=new();public Inventory Inventory=new();}
    public class Inventory{public Equipment Equipped=new();}
    public class Equipment{public List<WoWItem> Items=new();public WoWItem? MainHand,OffHand;}
    public static class StyxWoW{public static Player Me=new();}
}
/* Controlled Lua and event boundary. */ namespace Styx.WoWInternals
{
    public class LuaEventArgs:EventArgs{public object[] Args=Array.Empty<object>();}
    public delegate void LuaEventHandlerDelegate(object sender,LuaEventArgs e);
    public class LuaEvents{public void AttachEvent(string name,LuaEventHandlerDelegate handler){}public void DetachEvent(string name,LuaEventHandlerDelegate handler){}}
    public static class Lua
    {
        public static LuaEvents Events=new();
        public static T GetReturnVal<T>(string text,int index)
        {if(typeof(T)==typeof(string))return (T)(object)(text.Contains("GetItemStats")?LootCases.Stats:"|Hitem:41001:0|h[test]|h");if(typeof(T)==typeof(bool))return (T)(object)(index==5?LootCases.CanNeed:index==6?LootCases.CanGreed:index==7?LootCases.CanDisenchant:throw new InvalidOperationException("Unexpected loot availability index"));throw new InvalidOperationException("Unexpected Lua observation");}
        public static void DoString(string text,params object[] args)=>LootCases.Scripts.Add(args.Length==0?text:string.Format(text,args));
    }
}
/* UI is never entered. */ namespace SmartLootRoller{public class FormSettings{public void ShowDialog(){}}}
""";
}
