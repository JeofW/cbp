using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

internal static class ConsumableDiscoveryRegressionTests
{
    [ModuleInitializer]
    internal static void Run()
    {
        var root=new DirectoryInfo(AppContext.BaseDirectory);
        while(root!=null&&!File.Exists(Path.Combine(root.FullName,"CopilotBuddy.csproj")))root=root.Parent;
        if(root==null)throw new InvalidOperationException("Tracked checkout required.");
        var consumable=CSharpSyntaxTree.ParseText(File.ReadAllText(Path.Combine(root.FullName,"Styx/Logic/Inventory/Consumable.cs")))
            .GetRoot().DescendantNodes().OfType<ClassDeclarationSyntax>().Single(x=>x.Identifier.ValueText=="Consumable");
        var spells=CSharpSyntaxTree.ParseText(File.ReadAllText(Path.Combine(root.FullName,"Styx/WoWInternals/WoWObjects/WoWItem.cs")))
            .GetRoot().DescendantNodes().OfType<PropertyDeclarationSyntax>().Single(x=>x.Identifier.ValueText=="ItemSpells");
        string source=Prefix+spells+"}\n"+consumable+"\n"+Cases;
        var trusted=(string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")??throw new InvalidOperationException("References missing.");
        var compilation=CSharpCompilation.Create("ConsumableDiscovery_"+Guid.NewGuid().ToString("N"),new[]{CSharpSyntaxTree.ParseText(source)},
            trusted.Split(Path.PathSeparator).Distinct(StringComparer.OrdinalIgnoreCase).Select(x=>MetadataReference.CreateFromFile(x)),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using var output=new MemoryStream();var result=compilation.Emit(output);
        if(!result.Success)throw new InvalidOperationException(string.Join("; ",result.Diagnostics.Where(x=>x.Severity==DiagnosticSeverity.Error)));
        try{Assembly.Load(output.ToArray()).GetType("ConsumableCases",true)!.GetMethod("Run")!.Invoke(null,null);}
        catch(TargetInvocationException e)when(e.InnerException!=null){ExceptionDispatchInfo.Capture(e.InnerException).Throw();throw;}
    }
    private const string Prefix="""
#nullable disable
using System;using System.Collections.Generic;using System.Linq;
public enum WoWItemClass{Consumable,Miscellaneous}public class ItemInfo{public int RequiredLevel;public WoWItemClass ItemClass;}
public class WoWSpell{public string Name;}
public class LocalPlayer{public int Level=60;public List<WoWItem> Items=new();public Action OnBags;public List<WoWItem> BagItems{get{OnBags?.Invoke();return Items;}}}
public static class ObjectManager{public static LocalPlayer Me=new();}public static class StyxWoW{public static LocalPlayer Me=>ObjectManager.Me;}
public class WoWItem{
 public bool IsValid=true;public uint StackCount=5;public ItemInfo ItemInfo=new();public WoWItemSpell[] Effects=new WoWItemSpell[5];
 public class WoWItemSpell{public bool IsValid=true;public WoWSpell ActualSpell;}
 public WoWItemSpell GetSpell(int i)=>Effects[i];
""";
    private const string Cases="""
public static class ConsumableCases{
 static void Check(bool ok,string why){if(!ok)throw new InvalidOperationException(why);}
 static WoWItem Item(string name,int level=55,uint stack=5,int slot=0){var item=new WoWItem{StackCount=stack};item.ItemInfo.RequiredLevel=level;item.Effects[slot]=new(){ActualSpell=new(){Name=name}};ObjectManager.Me.Items.Add(item);return item;}
 public static void Run(){int passed=0;var errors=new List<string>();
 void Case(string name,Action test){ObjectManager.Me=new();try{test();passed++;Console.WriteLine("PASS consumable discovery: "+name);}catch(Exception e){errors.Add(name+": "+e.Message);Console.Error.WriteLine("FAIL consumable discovery: "+errors[^1]);}}
 foreach(bool drink in new[]{false,true}){bool d=drink;string name=d?"Drink":"Food";WoWItem Best(bool specialty=false)=>d?Consumable.GetBestDrink(specialty):Consumable.GetBestFood(specialty);
 Case(name+"/empty",()=>Check(Best()==null,"empty bags fabricated an item"));
 Case(name+"/current",()=>{var item=Item(name);Check(ReferenceEquals(Best(),item),"ordinary item missed");});
 foreach(int slot in new[]{1,2,3,4}){int s=slot;Case(name+"/sparse effect slot"+s,()=>{var item=Item(name,slot:s);Check(ReferenceEquals(Best(),item),"first empty effect suppressed later valid food/drink");});}
 Case(name+"/unknown secondary metadata is skipped without exception",()=>{var item=Item(name);item.Effects[1]=new();Check(Best()==null,"unknown specialty coverage was treated as absent");item.Effects[1].ActualSpell=new(){Name=name};Check(ReferenceEquals(Best(),item),"fresh metadata did not recover");});
 Case(name+"/unknown primary metadata later hydrates",()=>{var item=Item(name);item.Effects[0].ActualSpell=null;Check(Best()==null,"unknown metadata fabricated classification");item.Effects[0].ActualSpell=new(){Name=name};Check(ReferenceEquals(Best(),item),"negative observation stuck");});
 Case(name+"/zero stack",()=>{Item(name,stack:0);Check(Best()==null,"exhausted stack was chosen");});
 Case(name+"/invalid item",()=>{Item(name).IsValid=false;Check(Best()==null,"invalid object was chosen");});
 Case(name+"/missing item info",()=>{Item(name).ItemInfo=null;Check(Best()==null,"missing info fabricated classification");});
 Case(name+"/level requirement",()=>{Item(name,61);var okay=Item(name,55);Check(ReferenceEquals(Best(),okay),"unusable level requirement selected");});
 Case(name+"/level then stack ordering",()=>{Item(name,40,20);Item(name,55,2);var best=Item(name,55,8);Check(ReferenceEquals(Best(),best),"quality/stack ordering changed");});
 Case(name+"/bag transition",()=>{Check(Best()==null,"nonempty initial bags");var item=Item(name);Check(ReferenceEquals(Best(),item),"newly acquired consumable hidden by negative cache");ObjectManager.Me.Items.Clear();Check(Best()==null,"spent stack retained");});
 Case(name+"/combined refreshment",()=>{var item=Item("Refreshment");Check(ReferenceEquals(Best(),item),"combined food/drink missed");});
 Case(name+"/potions do not substitute for rest",()=>{Item("Restore Mana");Item("Healing Potion");Check(Best()==null,"potion was treated as ordinary rest");});
 Case(name+"/specialty setting",()=>{var item=Item(name);item.Effects[1]=new(){ActualSpell=new(){Name="Well Fed"}};Check(Best()==null&&ReferenceEquals(Best(true),item),"specialty exclusion was erased");});
 Case(name+"/world gap",()=>{ObjectManager.Me=null;Check(Best()==null,"world gap fabricated inventory");});
 Case(name+"/actor replacement during bags",()=>{Item(name);ObjectManager.Me.OnBags=()=>ObjectManager.Me=new();Check(Best()==null,"old bags crossed actor boundary");});
 }
 Console.WriteLine($"Consumable discovery scenarios: {passed}/{passed+errors.Count}; complete inventory classifier and sparse-effect enumeration; native use admission tested separately.");
 if(errors.Count!=0)throw new InvalidOperationException(string.Join("; ",errors));}
}
""";
}
