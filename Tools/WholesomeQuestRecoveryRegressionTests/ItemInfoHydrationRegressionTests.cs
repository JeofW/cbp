using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

internal static class ItemInfoHydrationRegressionTests
{
    [ModuleInitializer]
    internal static void Run()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root != null && !File.Exists(Path.Combine(root.FullName, "CopilotBuddy.csproj"))) root = root.Parent;
        if (root == null) throw new InvalidOperationException("Tracked source required.");
        var info = CSharpSyntaxTree.ParseText(File.ReadAllText(Path.Combine(root.FullName, "Styx/WoWInternals/WoWObjects/ItemInfo.cs"))).GetRoot();
        var item = CSharpSyntaxTree.ParseText(File.ReadAllText(Path.Combine(root.FullName, "Styx/WoWInternals/WoWObjects/WoWItem.cs"))).GetRoot();
        string source = Prefix + string.Join("\n", info.DescendantNodes().OfType<FieldDeclarationSyntax>())
            + info.DescendantNodes().OfType<ConstructorDeclarationSyntax>().Single()
            + info.DescendantNodes().OfType<MethodDeclarationSyntax>().Single(m => m.Identifier.ValueText == "FromId")
            + info.DescendantNodes().OfType<PropertyDeclarationSyntax>().Single(p => p.Identifier.ValueText == "Name")
            + "}\npublic class InfoOwner {public uint Entry=123;private ItemInfo? _itemInfo;"
            + item.DescendantNodes().OfType<PropertyDeclarationSyntax>().Single(p => p.Identifier.ValueText == "ItemInfo") + "}\n" + Cases;
        var refs = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator)
            .Append(typeof(GreenMagic.Memory).Assembly.Location).Distinct(StringComparer.OrdinalIgnoreCase).Select(p => MetadataReference.CreateFromFile(p));
        var compilation = CSharpCompilation.Create("ItemInfoHydration_" + Guid.NewGuid().ToString("N"), new[] { CSharpSyntaxTree.ParseText(source) }, refs,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using var bytes = new MemoryStream();
        var result = compilation.Emit(bytes);
        if (!result.Success) throw new InvalidOperationException(string.Join("; ", result.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error)));
        try { Assembly.Load(bytes.ToArray()).GetType("InfoCases", true)!.GetMethod("Run")!.Invoke(null, null); }
        catch (TargetInvocationException e) when (e.InnerException != null) { ExceptionDispatchInfo.Capture(e.InnerException).Throw(); throw; }
    }
    private const string Prefix = """
#nullable enable
using System;using System.Collections.Generic;using System.Linq;using System.Reflection;using System.Runtime.CompilerServices;using System.Runtime.InteropServices;using System.Threading;using GreenMagic;using Styx.WoWInternals;using Styx.WoWInternals.WoWCache;using WoWCache=Styx.WoWInternals.WoWCache;
public static class StyxWoW {public static CacheLeaf Cache=new();}
public class CacheLeaf {public CacheLeaf this[CacheDb id]=>this;public WoWCache.WoWCache.InfoBlock? Block;public Action? OnLookup;public WoWCache.WoWCache.InfoBlock? GetInfoBlockById(uint id){OnLookup?.Invoke();return Block;}}
public class ItemInfo {public uint Id{get;private set;}public int AllowedClasses=>_cacheEntry.AllowedClasses;public int[] SpellId=>_cacheEntry.SpellId;
""";
    private const string Cases = """
public static class InfoCases {
const BindingFlags Hidden=BindingFlags.Instance|BindingFlags.NonPublic;
[DllImport("kernel32.dll",SetLastError=true)]static extern IntPtr VirtualAlloc(IntPtr address,UIntPtr size,uint type,uint protect);
[DllImport("kernel32.dll",SetLastError=true)]static extern bool VirtualFree(IntPtr address,UIntPtr size,uint type);
[DllImport("kernel32.dll",SetLastError=true)]static extern bool VirtualProtect(IntPtr address,UIntPtr size,uint protect,out uint old);
static readonly int NameOffset=Marshal.OffsetOf<WoWCache.WoWCache.ItemCacheEntry>("NamePtr").ToInt32();
static IntPtr NamePage(IntPtr row)=>new IntPtr(Marshal.ReadInt32(row,NameOffset));
static void Check(bool okay,string why){if(!okay)throw new InvalidOperationException(why);}
static void MemoryOwner(Memory? value)=>typeof(ObjectManager).GetProperty("Wow")!.SetValue(null,value);
static WoWCache.WoWCache.InfoBlock Block(uint address)=>(WoWCache.WoWCache.InfoBlock)Activator.CreateInstance(typeof(WoWCache.WoWCache.InfoBlock),Hidden,null,new object[]{address,123u},null)!;
public static void Run(){int pass=0;var failures=new List<string>();
void Case(string name,Action<InfoOwner,IntPtr,Memory> body){var previous=ObjectManager.Wow;var memory=(Memory)RuntimeHelpers.GetUninitializedObject(typeof(Memory));var cache=new ThreadLocal<Dictionary<IntPtr,byte[]>>(()=>new());var enabled=new ThreadLocal<bool>(()=>false);var ptr=Marshal.AllocHGlobal(1024);var namePage=VirtualAlloc(IntPtr.Zero,new UIntPtr(4096),0x3000,4);try{Check(namePage!=IntPtr.Zero,"name page allocation failed");Marshal.Copy(new byte[1024],0,ptr,1024);typeof(Memory).GetField("_cache",Hidden)!.SetValue(memory,cache);typeof(Memory).GetField("_cacheEnabled",Hidden)!.SetValue(memory,enabled);typeof(Memory).GetField("_hProcess",Hidden)!.SetValue(memory,new IntPtr(-1));MemoryOwner(memory);StyxWoW.Cache=new(){Block=Block(unchecked((uint)ptr.ToInt32()))};Marshal.WriteInt32(ptr,44,-1);Marshal.WriteInt32(ptr,256,433);Marshal.WriteInt32(ptr,NameOffset,namePage.ToInt32());var nameBytes=System.Text.Encoding.UTF8.GetBytes("Chosen food\0");Marshal.Copy(nameBytes,0,namePage,nameBytes.Length);body(new InfoOwner(),ptr,memory);pass++;Console.WriteLine("PASS item metadata hydration: "+name);}catch(Exception e){failures.Add(name+": "+e.Message);Console.Error.WriteLine("FAIL item metadata hydration: "+failures[^1]);}finally{MemoryOwner(previous);typeof(Memory).GetField("_hProcess",Hidden)!.SetValue(memory,IntPtr.Zero);cache.Dispose();enabled.Dispose();Marshal.FreeHGlobal(ptr);if(namePage!=IntPtr.Zero)VirtualFree(namePage,UIntPtr.Zero,0x8000);}}
Case("complete original struct",(item,p,m)=>{var info=item.ItemInfo;Check(info!=null&&info.AllowedClasses==-1&&info.SpellId.Length==5&&info.SpellId[0]==433,"original complete item row lost");});
Case("complete original name",(item,p,m)=>Check(item.ItemInfo.Name=="Chosen food","complete cache name lost"));
Case("unreadable name remains retryable",(item,p,m)=>{var info=item.ItemInfo;var page=NamePage(p);Check(VirtualProtect(page,new UIntPtr(4096),1,out _),"name page protection failed");Check(info.Name=="","unreadable name was usable");Check(VirtualProtect(page,new UIntPtr(4096),4,out _),"name page recovery failed");Check(info.Name=="Chosen food","failed name read was cached across hydration");});
Case("unterminated name is unknown",(item,p,m)=>{Marshal.Copy(System.Linq.Enumerable.Repeat((byte)'x',512).ToArray(),0,NamePage(p),512);Check(item.ItemInfo.Name=="","partial unterminated name authorized matching");});
Case("invalid UTF8 remains retryable",(item,p,m)=>{var info=item.ItemInfo;Marshal.Copy(new byte[]{0xff,0},0,NamePage(p),2);Check(info.Name=="","invalid UTF8 supplied an authoritative item name");var bytes=System.Text.Encoding.UTF8.GetBytes("Hydrated food\0");Marshal.Copy(bytes,0,NamePage(p),bytes.Length);Check(info.Name=="Hydrated food","invalid name read was cached permanently");});
Case("terminated name at page end",(item,p,m)=>{var page=NamePage(p);var bytes=System.Text.Encoding.UTF8.GetBytes("Food\0");var end=IntPtr.Add(page,4096-bytes.Length);Marshal.Copy(bytes,0,end,bytes.Length);Marshal.WriteInt32(p,NameOffset,end.ToInt32());try{Check(item.ItemInfo.Name=="Food","read beyond the terminated page tail discarded a complete name");}finally{Marshal.WriteInt32(p,NameOffset,page.ToInt32());}});
Case("cached name rejects replaced memory",(item,p,m)=>{var info=item.ItemInfo;Check(info.Name=="Chosen food","fixture name was not captured");MemoryOwner((Memory)RuntimeHelpers.GetUninitializedObject(typeof(Memory)));Check(info.Name=="","cached name crossed its captured memory owner");});
Case("unread name rejects replaced memory",(item,p,m)=>{var info=item.ItemInfo;MemoryOwner((Memory)RuntimeHelpers.GetUninitializedObject(typeof(Memory)));Check(info.Name=="","old cache pointer was read through a new memory owner");});
Case("closed process remains fatal for cached name",(item,p,m)=>{var info=item.ItemInfo;Check(info.Name=="Chosen food","fixture name was not captured");typeof(Memory).GetField("_hProcess",Hidden)!.SetValue(m,IntPtr.Zero);try{_=info.Name;throw new InvalidOperationException("closed process borrowed cached name");}catch(Styx.InvalidProcessException){}finally{typeof(Memory).GetField("_hProcess",Hidden)!.SetValue(m,new IntPtr(-1));}});
Case("unreadable struct remains unknown",(item,p,m)=>{StyxWoW.Cache.Block=Block(1);Check(item.ItemInfo==null,"failed actual item-cache read became known metadata");});
Case("failed row later hydrates on same item",(item,p,m)=>{StyxWoW.Cache.Block=Block(1);Check(item.ItemInfo==null,"initial missing row cached default metadata");StyxWoW.Cache.Block=Block(unchecked((uint)p.ToInt32()));Check(item.ItemInfo?.AllowedClasses==-1&&item.ItemInfo.SpellId[0]==433,"same item retained a failed metadata observation");});
Case("missing memory remains unknown",(item,p,m)=>{MemoryOwner(null);Check(item.ItemInfo==null,"missing memory produced known zero metadata");});
Case("memory replaced during lookup",(item,p,m)=>{StyxWoW.Cache.OnLookup=()=>MemoryOwner(null);Check(item.ItemInfo==null,"lookup carried metadata across memory replacement");});
Case("cancellation preserved",(item,p,m)=>{StyxWoW.Cache.OnLookup=()=>throw new OperationCanceledException();try{_=item.ItemInfo;throw new InvalidOperationException("cancel swallowed");}catch(OperationCanceledException){}});
Case("interrupt preserved",(item,p,m)=>{StyxWoW.Cache.OnLookup=()=>throw new ThreadInterruptedException();try{_=item.ItemInfo;throw new InvalidOperationException("interrupt swallowed");}catch(ThreadInterruptedException){}});
Console.WriteLine($"Item metadata hydration: {pass}/{pass+failures.Count}; production FromId/constructor/item cache owner; actual original ItemCacheEntry marshal and GreenMagic byte reads; only cache lookup publication controlled.");if(failures.Count!=0)throw new InvalidOperationException(string.Join("; ",failures));}}
""";
}
