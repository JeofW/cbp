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

// Real allocated owner bytes. Only fixed client-global area/map addresses are
// mapped into owned fixture storage by the read transport, never a decision owner.
internal static class QuestAreaSnapshotRegressionTests
{
    private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
    private sealed class Failure(string message) : Exception(message) { }

    [ModuleInitializer]
    internal static void Run()
    {
        if (!OperatingSystem.IsWindows() || IntPtr.Size != 4) throw new PlatformNotSupportedException("Area observations require Windows x86");
        var tests = new List<(string Name, Action<Fixture> Body)>
        {
            ("complete current area and map are published", f => { var s=f.Capture(); Check(Area(s)==99 && Map(s)==1, "current area/map missing"); }),
            ("map zero is a valid map", f => { f.Map(0); Check(Area(f.Capture())==99, "Eastern Kingdoms was treated as unknown"); }),
            ("unknown area zero never means observed absence", f => { f.Area(0); Unknown(f.Capture()); }),
            ("invalid unsigned area is withheld", f => { f.Area(uint.MaxValue); Unknown(f.Capture()); }),
            ("invalid map is withheld", f => { f.Map(uint.MaxValue); Unknown(f.Capture()); }),
            ("parent zone cannot replace a missing subarea", f => { f.Zone(99); f.Area(0); Unknown(f.Capture()); }),
            ("subarea identity remains distinct from parent zone", f => { f.Zone(99); f.Area(100); Check(Area(f.Capture())==100,"parent zone replaced area"); }),
            ("unreadable area is unknown", f => { f.MissingArea=true; Unknown(f.Capture()); }),
            ("short map/area reads are unknown", f => { f.ShortRead=true; Unknown(f.Capture()); }),
            ("ordinary read failure is unknown", f => { f.ReadError=new IOException("controlled read failure"); Unknown(f.Capture()); }),
            ("cancellation propagates", f => { var e=new OperationCanceledException("stop"); f.ReadError=e; Same(()=>f.Capture(),e); }),
            ("thread interruption propagates", f => { var e=new ThreadInterruptedException("stop"); f.ReadError=e; Same(()=>f.Capture(),e); }),
            ("zero player GUID has no area authority", f => { f.Write64(f.Player.BaseAddress+48,0); Unknown(f.Capture()); }),
            ("descriptor and player GUID must agree", f => { f.Write64(f.Descriptor,777); Unknown(f.Capture()); }),
            ("unreadable descriptor remains unknown", f => { f.Write32(f.Player.BaseAddress+8,1); Unknown(f.Capture()); }),
            ("foreign raw object type remains unknown", f => { f.Write32(f.Player.BaseAddress+20,3); Unknown(f.Capture()); }),
            ("area change during capture is not published", f => { f.AfterRead=a=>{ if(a==0xBD0810){f.AfterRead=null;f.Area(100);} }; Unknown(f.Capture()); }),
            ("map change during capture is not published", f => { f.AfterRead=a=>{ if(a==0xBD088C){f.AfterRead=null;f.Map(530);} }; Unknown(f.Capture()); }),
            ("replacement wrapper cannot borrow an area observation", f => { f.AfterRead=_=>{f.AfterRead=null;ObjectManager.Me=new LocalPlayer(f.Player.BaseAddress);}; Unknown(f.Capture()); }),
            ("reader cannot be relabelled with another memory owner", f => {f.ReplaceMemory();Unknown(f.Capture());Check(f.ReadCalls==0,"foreign reader was invoked");}),
            ("current snapshot is revalidated", f => {var s=f.Capture();Check(Area(s)==99 && Current(s),"matching sample rejected");}),
            ("later area change revokes currentness", f => {var s=f.Capture();Check(Area(s)==99,"baseline missing");f.Area(100);Check(!Current(s),"old area remained current");}),
            ("later map change revokes currentness", f => {var s=f.Capture();Check(Area(s)==99,"baseline missing");f.Map(530);Check(!Current(s),"old map remained current");}),
            ("successful capture restores cache setting", f => {bool before=f.CacheEnabled.Value;Check(Area(f.Capture())==99,"baseline missing");Check(before==f.CacheEnabled.Value,"cache state leaked");}),
            ("failed capture restores cache setting", f => {bool before=f.CacheEnabled.Value;f.ShortRead=true;Unknown(f.Capture());Check(before==f.CacheEnabled.Value,"failure leaked cache state");}),
            ("fresh valid observation recovers after unknown", f => {f.MissingArea=true;Unknown(f.Capture());f.MissingArea=false;Check(Area(f.Capture())==99,"unknown sample was cached as permission");})
        };
        int passed=0,failed=0,errors=0;
        foreach(var test in tests)
        {
            try{using var f=new Fixture();test.Body(f);passed++;Console.WriteLine("PASS area snapshot: "+test.Name);}
            catch(Failure e){failed++;Console.Error.WriteLine("FAIL area snapshot: "+test.Name+": "+e.Message);}
            catch(Exception e){errors++;Console.Error.WriteLine("ERROR area snapshot: "+test.Name+": "+e);}
        }
        Console.WriteLine($"Area snapshot scenarios: {passed}/{tests.Count}; assertions={failed}; unexpected={errors}; actual allocated owner/global transport, no game.");
        if(failed+errors!=0)throw new InvalidOperationException("Area snapshot regression");
    }

    internal static int? Area(object snapshot)=>(int?)snapshot.GetType().GetProperty("AreaId")!.GetValue(snapshot);
    internal static int? Map(object snapshot)=>(int?)snapshot.GetType().GetProperty("MapId")!.GetValue(snapshot);
    internal static bool Current(object snapshot)=>(bool)Invoke(snapshot.GetType().GetMethod("IsCurrent")!,snapshot)!;
    private static void Unknown(object snapshot)=>Check(Area(snapshot)==null && !(bool)snapshot.GetType().GetProperty("IsComplete")!.GetValue(snapshot)!,"unknown area published authority");
    private static void Same(Action body,Exception expected){try{body();throw new Failure("cancellation swallowed");}catch(Exception actual)when(ReferenceEquals(actual,expected)){}}
    private static object? Invoke(MethodInfo method,object? target,params object?[] args)
    {try{return method.Invoke(target,args);}catch(TargetInvocationException e)when(e.InnerException!=null){ExceptionDispatchInfo.Capture(e.InnerException).Throw();throw;}}
    private static void Check(bool ok,string message){if(!ok)throw new Failure(message);}

    internal sealed class Fixture:IDisposable
    {
        internal readonly QuestDatasetObservationFixture Native=new();
        internal LocalPlayer Player=>Native.Player;
        private readonly Memory memory=ObjectManager.Wow!;
        private readonly IntPtr storage=Marshal.AllocHGlobal(16);
        private readonly ThreadLocal<Dictionary<IntPtr,byte[]>> cache;
        internal readonly ThreadLocal<bool> CacheEnabled;
        internal readonly uint Descriptor;
        internal bool MissingArea,ShortRead;
        internal int ReadCalls;
        internal Exception? ReadError;
        internal Action<uint>? AfterRead;
        internal Fixture()
        {
            cache=(ThreadLocal<Dictionary<IntPtr,byte[]>>)typeof(Memory).GetField("_cache",Hidden)!.GetValue(memory)!;
            CacheEnabled=(ThreadLocal<bool>)typeof(Memory).GetField("_cacheEnabled",Hidden)!.GetValue(memory)!;
            Descriptor=memory.Read<uint>(Player.BaseAddress+8);
            Marshal.Copy(new byte[16],0,storage,16);Area(99);Map(1);Zone(12);
        }
        internal object Capture()
        {
            Type? type=typeof(LocalPlayer).Assembly.GetType("Styx.Logic.Questing.QuestAreaSnapshot");
            var method=type?.GetMethod("CaptureCore",Hidden);
            Check(method!=null,"checked area observation API missing");
            Func<uint,int,byte[]> read=(address,count)=>
            {
                ReadCalls++;
                if(ReadError!=null)throw ReadError;
                if(address==0xBD0810 && MissingArea)return null!;
                uint mapped=address==0xBD0810 ? unchecked((uint)storage.ToInt32()) : address==0xBD088C ? unchecked((uint)storage.ToInt32())+4 : address;
                byte[] value=memory.ReadBytes(mapped,count);
                AfterRead?.Invoke(address);
                return ShortRead && value?.Length>0 ? value.Take(value.Length-1).ToArray() : value!;
            };
            return Invoke(method!,null,Player,memory,read)!;
        }
        internal void Area(uint value)=>Marshal.WriteInt32(storage,unchecked((int)value));
        internal void Map(uint value)=>Marshal.WriteInt32(storage,4,unchecked((int)value));
        internal void Zone(uint value)=>cache.Value![new IntPtr(0xBD080C)]=BitConverter.GetBytes(value);
        internal void Write32(uint address,uint value){Marshal.WriteInt32(new IntPtr(unchecked((int)address)),unchecked((int)value));Invalidate(address);}
        internal void Write64(uint address,ulong value){Marshal.WriteInt64(new IntPtr(unchecked((int)address)),unchecked((long)value));Invalidate(address);}
        private void Invalidate(uint address)
        {foreach(var p in cache.Value!.Where(p=>address>=unchecked((uint)p.Key.ToInt32()) && address<unchecked((uint)p.Key.ToInt32())+p.Value.Length).ToArray())cache.Value.Remove(p.Key);}
        internal void ReplaceMemory(){object clone=typeof(object).GetMethod("MemberwiseClone",BindingFlags.NonPublic|BindingFlags.Instance)!.Invoke(memory,null)!;typeof(ObjectManager).GetProperty("Wow")!.SetValue(null,clone);}
        public void Dispose(){Native.Dispose();Marshal.FreeHGlobal(storage);}
    }
}
