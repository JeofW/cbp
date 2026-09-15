using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using System.Threading;
using GreenMagic;
using Styx.Logic.Questing;
using Styx.WoWInternals;
using Styx.WoWInternals.WoWObjects;

// Real Memory.ReadBytes/Read/QuestLog/cache owners against this process's owned
// storage. No attached game, injection, native quest dispatch, or fake snapshot.
internal static class QuestRawLogMemoryRegressionTests
{
    private const BindingFlags I = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
    private sealed class AssertionFailure(string message) : Exception(message) { }
    private sealed class MissingContract(string message) : Exception(message) { }
    private static void Check(bool ok, string message) { if (!ok) throw new AssertionFailure(message); }
    [ModuleInitializer]
    internal static void Run()
    {
        if (!OperatingSystem.IsWindows() || IntPtr.Size != 4)
            throw new PlatformNotSupportedException("Raw quest-log owner tests require Windows x86.");
        var tests = new List<(string Name, System.Action<Case> Test)>
        {
            ("real legacy owner omits occupied missing-metadata identity", c =>
            { c.Slot(0,999); Check(c.Player.QuestLog.GetQuestId(0)==999 && c.Player.QuestLog.GetAllQuests().Count==0,"legacy information-loss control did not reach real owners"); }),
            ("real failed byte read becomes typed zero in legacy Memory", c =>
            { Check(c.Memory.ReadBytes(1U,4)==null && c.Memory.Read<uint>(1U)==0,"unreadable-address control did not demonstrate typed default"); }),
            ("readable accepted metadata produces a complete immutable observation", c =>
            { var s=c.Capture(); Check(Flag(s,"IsIdentityComplete") && Flag(s,"IsComplete") && Ids(s).SequenceEqual(new uint[]{867}) && Ids(s,"ReadyQuestIds").SequenceEqual(new uint[]{867}),"normal raw/hydrated/ready observation changed"); Check(c.Current(s),"immediate raw revalidation failed"); }),
            ("raw occupied identity survives absent real cache entry", c =>
            { c.Slot(0,999); var s=c.Capture(); Check(Flag(s,"IsIdentityComplete") && !Flag(s,"IsComplete") && Ids(s).SequenceEqual(new uint[]{999}),"missing metadata erased occupancy or fabricated completeness"); }),
            ("readable zero-filled log is genuinely empty", c =>
            { c.Slot(0,0); var s=c.Capture(); Check(Flag(s,"IsComplete") && Ids(s).Length==0 && Ids(s,"ReadyQuestIds").Length==0,"readable zero log became unavailable"); }),
            ("inaccessible descriptor is not a genuine empty log", c =>
            { c.Descriptor(1); Check(c.Player.QuestLog.GetAllQuests().Count==0,"legacy default-empty control changed"); var s=c.Capture(); Check(!Flag(s,"IsIdentityComplete") && !Flag(s,"IsComplete"),"failed pointer/descriptor read authorized empty log"); }),
            ("partially readable slot block is not a genuine empty log", c =>
            { c.WithPartialBlock(() => { Check(c.Memory.ReadBytes(c.DescriptorAddress+632,500)==null,"full block unexpectedly readable"); Check(c.Player.QuestLog.GetAllQuests().Count==0,"legacy partial-block control changed"); var s=c.Capture(); Check(!Flag(s,"IsIdentityComplete") && !Flag(s,"IsComplete"),"partial block authorized empty log"); }); }),
            ("snapshot bypasses stale raw byte cache and restores cache setting", c =>
            { byte[] stale=new byte[500]; BitConverter.GetBytes(867U).CopyTo(stale,0); c.Slot(0,999); c.Cache[c.DescriptorAddress+632]=stale; var s=c.Capture(); Check(Ids(s).SequenceEqual(new uint[]{999}) && c.CacheEnabled,"raw cache hid current identity or cache setting leaked"); }),
            ("duplicate occupied identities remain visible but incomplete", c =>
            { c.Slot(1,867); var s=c.Capture(); Check(!Flag(s,"IsIdentityComplete") && !Flag(s,"IsComplete") && Ids(s).Count(x=>x==867)==2,"duplicate IDs were silently normalized into authority"); }),
            ("invalid occupied identity remains visible but incomplete", c =>
            { c.Slot(0,uint.MaxValue); var s=c.Capture(); Check(!Flag(s,"IsComplete") && Ids(s).Contains(uint.MaxValue),"invalid raw ID was dropped or authorized"); }),
            ("same-count raw replacement invalidates saved observation", c =>
            { var s=c.Capture(); c.Slot(0,999); Check(!c.Current(s) && Ids(s).Single()==867,"same-count swap passed or mutated the old observation"); }),
            ("progress-only raw change invalidates saved observation", c =>
            { var s=c.Capture(); c.Write(c.DescriptorAddress+640,1); Check(!c.Current(s),"progress changed without invalidating saved raw bytes"); }),
            ("raw GUID replacement invalidates saved observation", c =>
            { var s=c.Capture(); c.Guid(124); Check(!c.Current(s),"cached wrapper identity hid raw GUID replacement"); }),
            ("descriptor replacement with identical contents invalidates observation", c =>
            { var s=c.Capture(); uint replacement=c.Base+50000; byte[] copy=new byte[1200]; Marshal.Copy(Ptr(c.DescriptorAddress),copy,0,copy.Length); Marshal.Copy(copy,0,Ptr(replacement),copy.Length); c.Descriptor(replacement); Check(!c.Current(s),"same-content descriptor replacement passed"); }),
            ("memory-owner replacement invalidates observation without native reads", c =>
            { var s=c.Capture(); var memory=(Memory)RuntimeHelpers.GetUninitializedObject(typeof(Memory)); typeof(ObjectManager).GetProperty("Wow")!.SetValue(null,memory); Check(!c.Current(s),"replaced memory owner passed"); }),
            ("zero raw owner GUID is unavailable", c =>
            { c.Guid(0); var s=c.Capture(); Check(!Flag(s,"IsIdentityComplete"),"zero raw GUID authorized an observation"); }),
            ("raw object/descriptor GUID disagreement is unavailable", c =>
            { c.Write(c.Base+48,124); var s=c.Capture(); Check(!Flag(s,"IsIdentityComplete"),"conflicting raw owner GUIDs authorized an observation"); }),
            ("accepted identity collections reject external mutation", c =>
            { var s=c.Capture(); var list=(IList<uint>)Property(s,"AcceptedQuestIds"); try { list[0]=999; throw new AssertionFailure("identity collection is mutable"); } catch(NotSupportedException) { } Check(Ids(s).Single()==867,"immutable observation changed"); })
        };
        int assertions=0, missing=0, unexpected=0;
        foreach(var t in tests)
        {
            try { using var c=new Case(); t.Test(c); Console.WriteLine("PASS raw-memory: "+t.Name); }
            catch(MissingContract e) { missing++; Console.Error.WriteLine("MISSING raw-memory contract: "+t.Name+": "+e.Message); }
            catch(AssertionFailure e) { assertions++; Console.Error.WriteLine("FAIL raw-memory assertion: "+t.Name+": "+e.Message); }
            catch(Exception e) { unexpected++; Console.Error.WriteLine("ERROR raw-memory fixture/owner: "+t.Name+": "+e); }
        }
        Console.WriteLine($"Raw-memory scenarios: {tests.Count-assertions-missing-unexpected}/{tests.Count}; assertions={assertions}; missing_contracts={missing}; unexpected={unexpected}; actual Memory.ReadBytes and QuestLog/cache; test-process storage only.");
        if(assertions+missing+unexpected!=0)throw new InvalidOperationException("Raw-memory specifications remain: assertions="+assertions+"; missing="+missing+"; unexpected="+unexpected);
    }
    private sealed class Case : IDisposable
    {
        private readonly object fixture;
        internal readonly LocalPlayer Player;
        internal readonly Memory Memory;
        internal readonly uint Base;
        internal uint DescriptorAddress;
        private readonly Dictionary<IntPtr,byte[]> cache;
        internal CacheView Cache { get; }
        internal bool CacheEnabled => ((ThreadLocal<bool>)Get(Memory,"_cacheEnabled")!).Value;
        internal Case()
        {
            Type type=typeof(QuestPublicationRegressionTests).GetNestedType("Fixture",BindingFlags.NonPublic)!;
            fixture=Activator.CreateInstance(type,true)!;
            Player=(LocalPlayer)Get(fixture,"Player")!; Memory=(Memory)Get(fixture,"memory")!;
            Base=Player.BaseAddress; DescriptorAddress=(uint)Get(fixture,"descriptor")!;
            cache=((ThreadLocal<Dictionary<IntPtr,byte[]>>)Get(fixture,"cache")!).Value!;
            Cache=new CacheView(cache); Guid(123);
        }
        internal void Guid(ulong id)
        { Marshal.WriteInt64(Ptr(Base+48),unchecked((long)id)); Marshal.WriteInt64(Ptr(DescriptorAddress),unchecked((long)id)); cache.Remove(Ptr(Base+48)); cache.Remove(Ptr(DescriptorAddress)); }
        internal void Write(uint address,uint value)
        { Marshal.WriteInt32(Ptr(address),unchecked((int)value)); cache.Remove(Ptr(address)); }
        internal void Slot(int slot,uint id)
        { Write(DescriptorAddress+632+(uint)slot*20,id); Write(DescriptorAddress+636+(uint)slot*20,id==0?0:(uint)WoWDescriptorQuestFlags.Completed); }
        internal void Descriptor(uint address) { Write(Base+8,address); DescriptorAddress=address; }
        internal object Capture() => Invoke(Player.QuestLog,"CaptureSnapshot");
        internal bool Current(object snapshot) => (bool)Invoke(Player.QuestLog,"IsSnapshotCurrent",snapshot);
        internal void WithPartialBlock(System.Action action)
        {
            uint old=DescriptorAddress;
            IntPtr block=VirtualAlloc(IntPtr.Zero,(UIntPtr)8192,0x3000,0x04);
            if(block==IntPtr.Zero)throw new InvalidOperationException("Test-process VirtualAlloc failed");
            try
            {
                uint start=unchecked((uint)block.ToInt32());
                if(!VirtualProtect(Ptr(start+4096),(UIntPtr)4096,0x01,out _))throw new InvalidOperationException("Test-process VirtualProtect failed");
                // GUID remains readable; the 500-byte slot block crosses PAGE_NOACCESS.
                Descriptor(start+4096-632-100); Guid(123); action();
            }
            finally { Descriptor(old); VirtualFree(block,UIntPtr.Zero,0x8000); }
        }
        public void Dispose() => ((IDisposable)fixture).Dispose();
    }
    private sealed class CacheView(Dictionary<IntPtr,byte[]> values)
    { internal byte[] this[uint address] { set => values[Ptr(address)]=value; } }
    private static IntPtr Ptr(uint address) => new IntPtr(unchecked((int)address));
    private static object? Get(object target,string name) => target.GetType().GetField(name,I)?.GetValue(target);
    private static object Property(object target,string name) => target.GetType().GetProperty(name)?.GetValue(target) ?? throw new MissingContract(name);
    private static bool Flag(object target,string name) => (bool)Property(target,name);
    private static uint[] Ids(object target,string name="AcceptedQuestIds") => ((IEnumerable<uint>)Property(target,name)).ToArray();
    private static object Invoke(object target,string method,params object[] args)
    {
        var m=target.GetType().GetMethods().SingleOrDefault(x=>x.Name==method && x.GetParameters().Length==args.Length);
        if(m==null)throw new MissingContract(method);
        try { return m.Invoke(target,args)!; }
        catch(TargetInvocationException e)when(e.InnerException!=null) { ExceptionDispatchInfo.Capture(e.InnerException).Throw(); throw; }
    }
    [DllImport("kernel32.dll",SetLastError=true)] private static extern IntPtr VirtualAlloc(IntPtr address,UIntPtr size,uint allocationType,uint protection);
    [DllImport("kernel32.dll",SetLastError=true)] [return:MarshalAs(UnmanagedType.Bool)] private static extern bool VirtualProtect(IntPtr address,UIntPtr size,uint protection,out uint oldProtection);
    [DllImport("kernel32.dll",SetLastError=true)] [return:MarshalAs(UnmanagedType.Bool)] private static extern bool VirtualFree(IntPtr address,UIntPtr size,uint freeType);
}
