using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using System.Threading;
using GreenMagic;
using Styx;
using Styx.Logic.Combat;
using Styx.Patchables;
using Styx.WoWInternals;

// Actual FromId/DbTable/Row over test-process storage, plus bounded decoder
// vectors. This is not a client capture or live spell/rotation acceptance run.
internal static class PackedSpellRowRegressionTests
{
    private const BindingFlags Hidden = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
    private sealed class AssertionFailure(string text) : Exception(text) { }
    [ModuleInitializer]
    internal static void Run()
    {
        Console.WriteLine("Packed spell fixture: managed prefix="+Marshal.SizeOf<SpellEntry>()+"; native decoded record=704.");
        var cases = new List<(string Name, Action Test)>();
        void Add(string name, Action<Fixture> test) => cases.Add((name, () => { using var f = new Fixture(); test(f); }));
        Add("uncompressed current FromId control", f => f.ExpectSpell(77));
        foreach (uint level in new uint[] {1, 17, 77, 255})
        {
            uint wanted = level;
            Add("packed metadata remains aligned at level " + level, f => { f.Publish(wanted); f.Pack(); f.ExpectSpell(wanted); });
        }
        Add("localized raw snapshot reads scalar fields", f => { var row=f.Localized(); Check(row != null && row.GetField<uint>(38)==77, "owned baseLevel field differs"); });
        Add("localized packed snapshot reads scalar fields", f => { f.Pack(); var row=f.Localized(); Check(row != null && row.GetField<uint>(38)==77, "decoded baseLevel field differs"); });
        Add("owned snapshot survives raw payload replacement", f => { f.Pack(); var row=f.Localized(); f.Payload(new byte[704]); Check(row != null && row.GetStruct<SpellEntry>().baseLevel==77, "row retained remote rather than owned bytes"); });
        Add("owned scalar snapshot remains readable without a memory owner", f => { f.Pack(); var row=f.Localized(); f.DropMemory(); Check(row != null && row.GetField<uint>(38)==77, "owned scalar depends on live memory"); });
        Add("owned struct snapshot remains readable without a memory owner", f => { f.Pack(); var row=f.Localized(); f.DropMemory(); Check(row != null && row.GetStruct<SpellEntry>().baseLevel==77, "owned struct depends on live memory"); });
        Add("out-of-range owned field is rejected", f => { var row=f.Localized(); Check(row != null && row.GetField<uint>(uint.MaxValue)==0, "overflowed field index escaped snapshot"); });
        Add("owned row cannot write through its remote origin", f => { var row=f.Localized(); Check(row != null,"no row"); bool denied=false; try { row!.SetField<uint>(38,999); } catch(InvalidOperationException) { denied=true; } Check(denied,"owned row allowed mutation"); f.ExpectSpell(77); });
        foreach(byte mode in new byte[]{2,255})
        { byte m=mode; Add("unknown compression mode " + mode + " is not raw permission", f => { f.Flag(m); Check(WoWSpell.FromId(f.Id)==null,"unknown mode became valid spell metadata"); }); }
        Add("unloaded header cannot produce a localized row", f => { f.Header("IsLoaded",0); Check(f.Localized()==null,"unloaded header accepted"); });
        Add("header becoming loaded recovers without replacing table", f => { f.Header("IsLoaded",0); Check(f.Localized()==null,"initial unloaded table accepted"); f.Header("IsLoaded",1); Check(f.Localized()!=null,"late loaded header remained unavailable"); });
        Add("changed header range revokes old index", f => { f.Header("MinIndex",f.Id+1); Check(f.Localized()==null,"constructor-time bounds overrode current header"); });
        Add("decoded row ID must match requested ID", f => { f.RawId(f.Id+1); f.Pack(); Check(WoWSpell.FromId(f.Id)==null,"wrong spell ID accepted"); });
        Add("oversized RLE run cannot spill past output", f => { f.Flag(1); f.Payload(new byte[]{7,7,255,7,7,255,7,7,255}); Check(f.Localized()==null,"overlong stream became a row"); });
        Add("missing memory remains unknown", f => { f.DropMemory(); Check(WoWSpell.FromId(f.Id)==null,"missing memory supplied a spell"); });
        foreach (int id in new[]{0,-1,41000,41003})
        { int value=id; Add("invalid or out-of-range spell " + id, f => Check(f.Localized(value)==null,"invalid index returned row")); }
        void Vector(string name, byte[] input, int size, byte[]? expected)
        {
            cases.Add((name,()=>{
                int reads=0;
                Func<int,byte?> reader=i=>{ reads++; return i>=0 && i<input.Length ? input[i] : null; };
                var actual=Decode(reader,size);
                Check(expected==null ? actual==null : actual!=null && actual.SequenceEqual(expected),"decoder vector differs");
                Check(reads <= Math.Max(0,2*size+1),"decoder exceeded input budget");
            }));
        }
        Vector("one literal",new byte[]{0xAB},1,new byte[]{0xAB});
        Vector("distinct literals",new byte[]{1,2,3,4},4,new byte[]{1,2,3,4});
        Vector("zero-length repeat",new byte[]{5,5,0},2,new byte[]{5,5});
        Vector("repeated run",new byte[]{5,5,3},5,Enumerable.Repeat((byte)5,5).ToArray());
        Vector("literal after repeated run",new byte[]{7,7,2,8,9,9,1},8,new byte[]{7,7,7,7,8,9,9,9});
        Vector("maximum repeat count",new byte[]{9,9,255},257,Enumerable.Repeat((byte)9,257).ToArray());
        Vector("missing first literal",Array.Empty<byte>(),1,null);
        Vector("missing repeated-run count",new byte[]{5,5},2,null);
        Vector("truncated distinct literal stream",new byte[]{1,2},3,null);
        Vector("repeat crosses output boundary",new byte[]{5,5,4},5,null);
        foreach(int size in new[]{0,-1,705,int.MaxValue})
        { int n=size; cases.Add(("invalid output size "+size,()=>{int reads=0;Check(Decode(_=>{reads++;return 1;},n)==null && reads==0,"invalid size read or allocated input");})); }
        cases.Add(("complete decode does not read the following record",()=>{var result=Decode(i=>i<4?(byte?)(i+1):throw new AssertionFailure("read next record"),4);Check(result!.SequenceEqual(new byte[]{1,2,3,4}),"bad literals");}));
        foreach(Exception error in new Exception[]{new OperationCanceledException("decoder stop"),new ThreadInterruptedException("decoder stop")})
        { var e=error; cases.Add((e.GetType().Name+" is preserved",()=>{Exception? got=null;try{Decode(_=>throw e,4);}catch(Exception ex){got=ex;}Check(ReferenceEquals(got,e),"original stop exception was replaced");})); }
        int passed=0, assertions=0, unexpected=0;
        foreach(var item in cases)
        {
            try { item.Test();passed++;Console.WriteLine("PASS packed spell: "+item.Name); }
            catch(AssertionFailure e){assertions++;Console.Error.WriteLine("FAIL packed spell assertion: "+item.Name+": "+e.Message);}
            catch(Exception e){unexpected++;Console.Error.WriteLine("ERROR packed spell fixture/owner: "+item.Name+": "+e);}
        }
        Console.WriteLine($"Packed spell scenarios: {passed}/{cases.Count}; assertions={assertions}; unexpected={unexpected}; actual metadata owners and decoder vectors; no game attached.");
        if(assertions+unexpected!=0)throw new InvalidOperationException($"Packed spell regressions: assertions={assertions}; unexpected={unexpected}");
    }
    private static byte[]? Decode(Func<int,byte?> read,int size)
    {
        var method=typeof(WoWDb).GetMethod("DecodePackedRow",Hidden);
        Check(method!=null,"bounded decoder contract is absent");
        return (byte[]?)Invoke(method!,null,new object[]{read,size});
    }
    private sealed class Fixture : IDisposable
    {
        private readonly IDisposable rows;
        private readonly IntPtr storage;
        private readonly Memory memory;
        private readonly Dictionary<IntPtr,byte[]> cache;
        internal readonly int Id=41001;
        internal Fixture()
        {
            rows=(IDisposable)Activator.CreateInstance(typeof(SpellRowLookupRegressionTests).GetNestedType("Fixture",BindingFlags.NonPublic)!,true)!;
            try
            {
                storage=(IntPtr)rows.GetType().GetField("storage",Hidden)!.GetValue(rows)!;
                memory=ObjectManager.Wow!;
                cache=((ThreadLocal<Dictionary<IntPtr,byte[]>>)typeof(Memory).GetField("_cache",Hidden)!.GetValue(memory)!).Value!;
                int prefix=Marshal.SizeOf<SpellEntry>();
                if(prefix<=0 || prefix>704) throw new InvalidOperationException("Managed SpellEntry exceeds the supplied 704-byte native record contract: "+prefix);
                Flag(0);Publish(77);
            }
            catch { rows.Dispose(); throw; }
        }
        internal void Publish(uint level)=>Invoke(rows.GetType().GetMethod("Publish",Hidden)!,rows,new object[]{0,level});
        internal void Flag(byte value)=>cache[new IntPtr(0xC5DEA0)]=new[]{value};
        private IntPtr Address=>new IntPtr(Marshal.ReadInt32(IntPtr.Add(storage,256)));
        private void InvalidatePayload()
        {
            ulong start=unchecked((uint)Address.ToInt32());
            foreach(var key in cache.Keys.Where(k=>unchecked((uint)k.ToInt32())>=start && unchecked((uint)k.ToInt32())<start+2048).ToArray())
                cache.Remove(key);
        }
        internal void RawId(int id){Marshal.WriteInt32(Address,id);InvalidatePayload();Flag(0);}
        internal void Payload(byte[] value)
        {
            var address=Address;Marshal.Copy(new byte[2048],0,address,2048);Marshal.Copy(value,0,address,value.Length);
            InvalidatePayload();
        }
        internal void Pack()
        {
            byte[] raw=new byte[704];Marshal.Copy(Address,raw,0,raw.Length);
            var encoded=new List<byte>{raw[0]};int i=1;
            while(i<raw.Length)
            {
                byte value=raw[i++];encoded.Add(value);
                if(value==raw[i-2])
                {
                    byte repeat=0;while(i<raw.Length && raw[i]==value && repeat<byte.MaxValue){repeat++;i++;}
                    encoded.Add(repeat);if(i<raw.Length)encoded.Add(raw[i++]);
                }
            }
            Payload(encoded.ToArray());Flag(1);
        }
        internal void Header(string name,int value)
        {
            var type=typeof(WoWDb).GetNestedType("DbTableHeader",BindingFlags.NonPublic)!;
            Marshal.WriteInt32(storage,Marshal.OffsetOf(type,name).ToInt32(),value);cache.Remove(storage);
        }
        internal WoWDb.Row? Localized(int? index=null)
        {
            var method=typeof(WoWDb.DbTable).GetMethod("GetLocalizedRow",Hidden);
            Check(method!=null,"localized row API is absent");
            return (WoWDb.Row?)Invoke(method!,StyxWoW.Db[ClientDb.Spell],new object[]{index??Id});
        }
        internal void ExpectSpell(uint level)
        {
            var spell=WoWSpell.FromId(Id);
            Check(spell!=null && spell.InternalInfo.Id==Id && spell.BaseLevel==level && spell.Level==level+1 && spell.ManaCostPercent==level+2,"current spell metadata differs");
        }
        internal void DropMemory()=>typeof(ObjectManager).GetProperty("Wow")!.SetValue(null,null);
        public void Dispose(){typeof(ObjectManager).GetProperty("Wow")!.SetValue(null,memory);rows.Dispose();}
    }
    private static object? Invoke(MethodInfo method,object? target,object[] values)
    {try{return method.Invoke(target,values);}catch(TargetInvocationException e)when(e.InnerException!=null){ExceptionDispatchInfo.Capture(e.InnerException).Throw();throw;}}
    private static void Check(bool condition,string why){if(!condition)throw new AssertionFailure(why);}
}
