using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using System.Threading;
using GreenMagic;
using Styx.WoWInternals;
using Styx.WoWInternals.WoWObjects;

// Real allocated original-player descriptors. No daily-history decision is replaced.
internal static class QuestDailySnapshotRegressionTests
{
    private const BindingFlags Hidden=BindingFlags.Instance|BindingFlags.Static|BindingFlags.Public|BindingFlags.NonPublic;
    private sealed class Failure(string message):Exception(message){}
    [ModuleInitializer]
    internal static void Run()
    {
        if(!OperatingSystem.IsWindows() || IntPtr.Size!=4)throw new PlatformNotSupportedException("Daily observations require Windows x86");
        var tests=new List<(string Name,Action<Fixture> Body)>
        {
            ("complete empty daily slots mean known empty",f=>Check(Ids(f.Capture()) is {Count:0},"complete empty daily list missing")),
            ("first daily slot preserves its quest identity",f=>{f.Slot(0,12692);Check(Ids(f.Capture())!.SequenceEqual(new uint[]{12692}),"first daily ID missing");}),
            ("last daily slot is included",f=>{f.Slot(24,12695);Check(Ids(f.Capture())!.SequenceEqual(new uint[]{12695}),"last daily ID missing");}),
            ("all twenty five slots are bounded and complete",f=>{for(int i=0;i<25;i++)f.Slot(i,(uint)(1000+i));Check(Ids(f.Capture())!.Count==25,"complete layout not represented");}),
            ("zeros between daily entries do not stop the scan",f=>{f.Slot(0,1000);f.Slot(12,2000);f.Slot(24,3000);Check(Ids(f.Capture())!.SequenceEqual(new uint[]{1000,2000,3000}),"sparse slots lost identities");}),
            ("duplicate nonzero daily ID is unknown",f=>{f.Slot(0,1000);f.Slot(1,1000);Unknown(f.Capture());}),
            ("invalid unsigned daily ID is unknown",f=>{f.Slot(0,uint.MaxValue);Unknown(f.Capture());}),
            ("unreadable daily block is unknown",f=>{f.Missing=true;Unknown(f.Capture());}),
            ("partial daily block is unknown",f=>{f.ShortRead=true;Unknown(f.Capture());}),
            ("ordinary read exception is unknown",f=>{f.Error=new IOException("controlled failure");Unknown(f.Capture());}),
            ("cancellation propagates",f=>{var e=new OperationCanceledException("stop");f.Error=e;Same(()=>f.Capture(),e);}),
            ("thread interruption propagates",f=>{var e=new ThreadInterruptedException("stop");f.Error=e;Same(()=>f.Capture(),e);}),
            ("zero player GUID cannot own daily history",f=>{f.Write64(f.Player.BaseAddress+48,0);Unknown(f.Capture());}),
            ("foreign descriptor GUID cannot donate daily history",f=>{f.Write64(f.Descriptor,456);Unknown(f.Capture());}),
            ("wrong raw object type cannot donate daily history",f=>{f.Write32(f.Player.BaseAddress+20,3);Unknown(f.Capture());}),
            ("unreadable descriptor is unknown",f=>{f.Write32(f.Player.BaseAddress+8,1);Unknown(f.Capture());}),
            ("daily reset during readback cannot publish old IDs",f=>{f.Slot(0,1000);f.AfterRead=a=>{if(a==f.Slots){f.AfterRead=null;f.Slot(0,0);}};Unknown(f.Capture());}),
            ("replacement wrapper cannot borrow a daily list",f=>{f.AfterRead=_=>{f.AfterRead=null;ObjectManager.Me=new LocalPlayer(f.Player.BaseAddress);};Unknown(f.Capture());}),
            ("foreign memory wrapper cannot relabel its reader",f=>{f.ReplaceMemory();Unknown(f.Capture());Check(f.Reads==0,"foreign reader ran");}),
            ("unchanged daily list can be revalidated",f=>{f.Slot(0,1000);var s=f.Capture();Check(Ids(s)!.Count==1 && Current(s),"current daily list rejected");}),
            ("later reset revokes the previous sample",f=>{f.Slot(0,1000);var s=f.Capture();Check(Ids(s)!.Count==1,"baseline missing");f.Slot(0,0);Check(!Current(s),"daily reset did not revoke old history");}),
            ("later newly completed daily revokes known empty",f=>{var s=f.Capture();Check(Ids(s)!.Count==0,"baseline missing");f.Slot(0,1000);Check(!Current(s),"changed daily list stayed current");}),
            ("successful capture restores cache policy",f=>{bool old=f.CacheEnabled.Value;_ = Ids(f.Capture());Check(f.CacheEnabled.Value==old,"cache policy leaked");}),
            ("unknown capture restores cache policy",f=>{bool old=f.CacheEnabled.Value;f.Missing=true;Unknown(f.Capture());Check(f.CacheEnabled.Value==old,"failed read leaked cache policy");}),
            ("daily history is independent of permanent history",f=>{f.Native.SetHistory(new uint[]{1000});Check(Ids(f.Capture())!.Count==0,"permanent reward history was copied into daily slots");}),
            ("fresh read recovers after missing data",f=>{f.Missing=true;Unknown(f.Capture());f.Missing=false;f.Slot(0,1000);Check(Ids(f.Capture())!.Contains(1000U),"fresh daily list did not recover");}),
        };
        int passed=0,failed=0,errors=0;
        foreach(var test in tests)
        {
            try{using var f=new Fixture();test.Body(f);passed++;Console.WriteLine("PASS daily snapshot: "+test.Name);}
            catch(Failure e){failed++;Console.Error.WriteLine("FAIL daily snapshot: "+test.Name+": "+e.Message);}
            catch(Exception e){errors++;Console.Error.WriteLine("ERROR daily snapshot: "+test.Name+": "+e);}
        }
        Console.WriteLine($"Daily snapshot scenarios: {passed}/{tests.Count}; assertions={failed}; unexpected={errors}; actual allocated player descriptors, no game.");
        if(failed+errors!=0)throw new InvalidOperationException("Daily snapshot regression");
    }
    internal static IReadOnlyCollection<uint>? Ids(object value)=>(IReadOnlyCollection<uint>?)value.GetType().GetProperty("QuestIds")!.GetValue(value);
    internal static bool Current(object value)=>(bool)Invoke(value.GetType().GetMethod("IsCurrent")!,value)!;
    private static void Unknown(object value)=>Check(Ids(value)==null && !(bool)value.GetType().GetProperty("IsComplete")!.GetValue(value)!,"unknown daily read became authority");
    private static void Same(Action body,Exception expected){try{body();throw new Failure("stop swallowed");}catch(Exception actual)when(ReferenceEquals(actual,expected)){}}
    private static object? Invoke(MethodInfo method,object? target,params object?[] args)
    {try{return method.Invoke(target,args);}catch(TargetInvocationException e)when(e.InnerException!=null){ExceptionDispatchInfo.Capture(e.InnerException).Throw();throw;}}
    private static void Check(bool ok,string message){if(!ok)throw new Failure(message);}

    internal sealed class Fixture:IDisposable
    {
        internal readonly QuestDatasetObservationFixture Native=new();
        private readonly Memory memory=ObjectManager.Wow!;
        private readonly ThreadLocal<Dictionary<IntPtr,byte[]>> cache;
        internal LocalPlayer Player=>Native.Player;
        internal readonly uint Descriptor;
        internal uint Slots=>Descriptor+0x1400;
        internal readonly ThreadLocal<bool> CacheEnabled;
        internal bool Missing,ShortRead;
        internal Exception? Error;
        internal Action<uint>? AfterRead;
        internal int Reads;
        internal Fixture()
        {
            cache=(ThreadLocal<Dictionary<IntPtr,byte[]>>)typeof(Memory).GetField("_cache",Hidden)!.GetValue(memory)!;
            CacheEnabled=(ThreadLocal<bool>)typeof(Memory).GetField("_cacheEnabled",Hidden)!.GetValue(memory)!;
            Descriptor=memory.Read<uint>(Player.BaseAddress+8);
            for(int i=0;i<25;i++)Slot(i,0);
        }
        internal object Capture()
        {
            var type=typeof(LocalPlayer).Assembly.GetType("Styx.Logic.Questing.QuestDailySnapshot");
            var method=type?.GetMethod("CaptureCore",Hidden);Check(method!=null,"checked daily observation API missing");
            Func<uint,int,byte[]> read=(address,count)=>
            {
                Reads++;if(Error!=null)throw Error;
                if(address==Slots && Missing)return null!;
                byte[] bytes=memory.ReadBytes(address,count);AfterRead?.Invoke(address);
                return ShortRead && bytes?.Length>0 ? bytes.Take(bytes.Length-1).ToArray():bytes!;
            };
            return Invoke(method!,null,Player,memory,read)!;
        }
        internal void Slot(int index,uint id){if(index<0 || index>=25)throw new ArgumentOutOfRangeException(nameof(index));Write32(Slots+(uint)index*4,id);}
        internal void Write32(uint address,uint value){Marshal.WriteInt32(new IntPtr(unchecked((int)address)),unchecked((int)value));Invalidate(address);}
        internal void Write64(uint address,ulong value){Marshal.WriteInt64(new IntPtr(unchecked((int)address)),unchecked((long)value));Invalidate(address);}
        private void Invalidate(uint address){foreach(var pair in cache.Value!.Where(p=>address>=unchecked((uint)p.Key.ToInt32()) && address<unchecked((uint)p.Key.ToInt32())+p.Value.Length).ToArray())cache.Value.Remove(pair.Key);}
        internal void ReplaceMemory(){var clone=typeof(object).GetMethod("MemberwiseClone",BindingFlags.NonPublic|BindingFlags.Instance)!.Invoke(memory,null);typeof(ObjectManager).GetProperty("Wow")!.SetValue(null,clone);}
        public void Dispose()=>Native.Dispose();
    }
}
