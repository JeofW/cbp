using System;
using System.CodeDom.Compiler;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;

// Full public WoWClient and exact NetStats layout, including the pointer-chain
// property that the earlier algorithm-only ring fixture never executed.
// Original build12340 510AC0 calls 6B0970 -> dword_C79CF4, then 6320D0:
// ring +11860, begin +11924, end +11928, sent +11932, received +11936,
// start +11940. Pinned image SHA256:
// bf644876709c591acc17c0da8cdf1814edcc9f1e6bc109a8c0d5c38c79dc953c.
// Only process memory is simulated; no game is attached or commanded.
internal static class NetworkClientEntryRegressionTests
{
#if !NETWORK_ENTRY_FOCUSED
    [ModuleInitializer]
#endif
    internal static void Run()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root != null && !File.Exists(Path.Combine(root.FullName, "CopilotBuddy.csproj"))) root = root.Parent;
        if (root == null) throw new InvalidOperationException("Tracked source is required");
        string temp = Path.Combine(Path.GetTempPath(), "cb-network-entry-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        try
        {
            foreach (string name in new[] { "WoWClient", "NetStats" })
                File.Copy(Path.Combine(root.FullName, "Styx/WoWInternals/Misc/" + name + ".cs"), Path.Combine(temp, name + ".cs"));
            File.WriteAllText(Path.Combine(temp, "Boundary.cs"), Boundary);
            Type type = typeof(Styx.StyxWoW).Assembly.GetType("Styx.Loaders.SourceCompiler", true)!;
            object compiler = Activator.CreateInstance(type, new object[] { temp })!;
            var result = (CompilerResults)type.GetMethod("Compile")!.Invoke(compiler, null)!;
            var errors = result.Errors.Cast<CompilerError>().Where(e => !e.IsWarning).ToArray();
            if (errors.Length != 0) throw new InvalidOperationException("Complete network reader: " + string.Join("; ", errors.Select(e => e.ToString())));
            var assembly = (Assembly)type.GetProperty("CompiledAssembly")!.GetValue(compiler)!;
            try { assembly.GetType("NetworkEntryCases", true)!.GetMethod("Run")!.Invoke(null, null); }
            catch (TargetInvocationException error) when (error.InnerException != null)
            { ExceptionDispatchInfo.Capture(error.InnerException).Throw(); throw; }
        }
        finally { Directory.Delete(temp, true); }
        RunCompiledHostMemory();
    }

    internal static void RunCompiledHostMemory()
    {
        if (!OperatingSystem.IsWindows() || IntPtr.Size != 4)
            throw new PlatformNotSupportedException("Original reader replay requires Windows/x86");
        const BindingFlags fields = BindingFlags.Instance | BindingFlags.NonPublic;
        var previous = Styx.WoWInternals.ObjectManager.Wow;
        var memory = (GreenMagic.Memory)RuntimeHelpers.GetUninitializedObject(typeof(GreenMagic.Memory));
        using var cache = new System.Threading.ThreadLocal<System.Collections.Generic.Dictionary<IntPtr, byte[]>>(() => new());
        using var enabled = new System.Threading.ThreadLocal<bool>(() => true);
        IntPtr allocation = System.Runtime.InteropServices.Marshal.AllocHGlobal(65536);
        void Set(string name, object value) => typeof(GreenMagic.Memory).GetField(name, fields)!.SetValue(memory, value);
        try
        {
            System.Runtime.InteropServices.Marshal.Copy(new byte[65536], 0, allocation, 65536);
            uint start = unchecked((uint)allocation.ToInt32());
            void Word(uint offset, uint value) => System.Runtime.InteropServices.Marshal.WriteInt32(
                allocation + checked((int)offset), unchecked((int)value));
            for (uint i = 0; i < 16; i++) Word(11860 + i * 4, 125);
            Word(11928, 4); Word(11932, 1024); Word(11936, 2048);
            Word(32768 + 11928, 5000); // a distinct unrelated structure, not a valid ring
            Set("_cache", cache); Set("_cacheEnabled", enabled); Set("_hProcess", new IntPtr(-1));
            cache.Value![new IntPtr(0xC79CF4)] = BitConverter.GetBytes(start);
            cache.Value![new IntPtr(0xC7B1F4)] = BitConverter.GetBytes(start + 32768);
            cache.Value![new IntPtr(0xD4159C)] = BitConverter.GetBytes(0U);
            typeof(Styx.WoWInternals.ObjectManager).GetProperty("Wow")!.SetValue(null, memory);
            uint observed = 0; Exception? failure = null;
            try { observed = ((Styx.WoWInternals.Misc.WoWClient)Activator.CreateInstance(
                typeof(Styx.WoWInternals.Misc.WoWClient), nonPublic: true)!).Latency; }
            catch (Exception error) { failure = error; }
            if (failure != null || observed != 125)
                throw new InvalidOperationException("Actual compiled host/Memory/public latency did not read original layout: " + failure?.Message + "; value=" + observed);
            Console.WriteLine("Compiled host network replay: 1/1; actual Memory and public reader over allocated original-layout bytes; only fixed globals mapped; no game handle or executor.");
        }
        finally
        {
            typeof(Styx.WoWInternals.ObjectManager).GetProperty("Wow")!.SetValue(null, previous);
            Set("_hProcess", IntPtr.Zero);
            System.Runtime.InteropServices.Marshal.FreeHGlobal(allocation);
        }
    }

    internal const string Boundary = """
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using Styx.Helpers;
using Styx.WoWInternals;
using Styx.WoWInternals.Misc;
using NetworkMemoryOwner = Styx.WoWInternals.ObjectManager;
namespace Styx.WoWInternals {
 public static class ObjectManager { public static ObservedMemory Wow = new(); }
 public sealed class ObservedMemory {
  public readonly Dictionary<uint,byte[]> Blocks = new();
  public readonly List<uint> Reads = new();
  public Exception Error;
  public T Read<T>(uint[] path) {
   uint at=path[0];
   for(int i=1;i<path.Length;i++)at=checked(Read<uint>(at)+path[i]);
   return Read<T>(at);
  }
  public T Read<T>(uint address) {
   Reads.Add(address);
   if(Error!=null)throw Error;
   int count=Marshal.SizeOf<T>();
   if(!Blocks.TryGetValue(address,out byte[] bytes)||bytes.Length!=count)
    throw new ObservationUnavailableException("network-memory","source bytes unavailable");
   IntPtr value=Marshal.AllocHGlobal(count);
   try{Marshal.Copy(bytes,0,value,count);return Marshal.PtrToStructure<T>(value);}
   finally{Marshal.FreeHGlobal(value);}
  }
 }
}
public static class NetworkEntryCases {
 static void Check(bool good,string reason){if(!good)throw new InvalidOperationException(reason);}
 static byte[] Stats(uint first=0,uint end=4,uint value=125) {
  byte[] b=new byte[84];
  for(int i=0;i<16;i++)BitConverter.GetBytes(value).CopyTo(b,i*4);
  BitConverter.GetBytes(first).CopyTo(b,64);BitConverter.GetBytes(end).CopyTo(b,68);
  BitConverter.GetBytes(1024U).CopyTo(b,72);BitConverter.GetBytes(2048U).CopyTo(b,76);
  return b;
 }
 public static void Reset(bool plausibleLegacy=false) {
  var m=new ObservedMemory();NetworkMemoryOwner.Wow=m;
  m.Blocks[0xC79CF4]=BitConverter.GetBytes(0x12340000U);
  m.Blocks[0xC7B1F4]=BitConverter.GetBytes(0x23450000U);
  m.Blocks[0x12340000U+11860]=Stats();
  m.Blocks[0x23450000U+11860]=Stats(end:plausibleLegacy?4U:5000U,value:999);
  m.Blocks[0xD4159C]=BitConverter.GetBytes(0U); // actual OS timer fallback, no client call
 }
 public static void Run(){int passed=0,total=0;
  void Case(string name,Action body){total++;try{body();passed++;Console.WriteLine("PASS network public entry: "+name);}catch(Exception e){Console.Error.WriteLine("FAIL network public entry: "+name+": "+e.Message);}}
  foreach(bool plausible in new[]{false,true})Case("uses original NetClient global instead of unrelated memory/"+plausible,()=>{
   Reset(plausible);uint value=0;Exception escaped=null;
   try{value=new WoWClient().Latency;}catch(Exception e){escaped=e;}
   Check(escaped==null&&value==125,"public latency followed the wrong global: "+escaped?.Message+"; value="+value);
   Check(NetworkMemoryOwner.Wow.Reads.Contains(0xC79CF4)&&!NetworkMemoryOwner.Wow.Reads.Contains(0xC7B1F4),"legacy global still participates in current observation");
  });
  Case("public structure preserves original byte offsets",()=>{
   Reset();var data=new WoWClient().NetStats;
   Check(Marshal.SizeOf<NetStats>()==84&&data.LatencyIndex==0&&data.LatencyCount==4
    &&data.BytesSent==1024&&data.BytesReceived==2048&&data.Latencies.Length==16&&data.Latencies.All(x=>x==125),"original native structure was not read intact");
  });
  Case("missing original owner cannot borrow a plausible obsolete global",()=>{
   Reset(true);NetworkMemoryOwner.Wow.Blocks.Remove(0xC79CF4);
   bool unknown=false;try{_=new WoWClient().Latency;}catch(ObservationUnavailableException){unknown=true;}
   Check(unknown,"unavailable original NetClient acquired a latency value");
  });
  Case("absent memory owner is an unavailable observation",()=>{
   Reset();NetworkMemoryOwner.Wow=null;
   Exception observed=null;try{_=new WoWClient().Latency;}catch(Exception e){observed=e;}
   Check(observed is ObservationUnavailableException,"missing process memory escaped as an unrelated runtime exception");
  });
  Case("invalid current ring remains unavailable rather than unlimited work",()=>{
   Reset();NetworkMemoryOwner.Wow.Blocks[0x12340000U+11860]=Stats(end:5000);
   bool unknown=false;try{_=new WoWClient().Latency;}catch(ObservationUnavailableException){unknown=true;}
   Check(unknown,"malformed original ring became a usable measurement");
  });
  Console.WriteLine($"Network public entry: {passed}/{total}; full public reader and original layout; source-bound memory transport, no live measurement.");
  if(passed!=total)throw new InvalidOperationException("Network public entry regressions");
 }
}
""";
}
