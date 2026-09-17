using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;
using GreenMagic;
using Styx;
using Styx.Logic.Combat;
using Styx.Patchables;
using Styx.WoWInternals;

// Actual FromId -> DbTable.GetRow -> Memory -> Row.GetStruct. Only the
// table publication and test-process storage are controlled; no fake lookup,
// spell cast, game attachment or database-registration walk is executed.
internal static class SpellRowLookupRegressionTests
{
    private const BindingFlags Static = BindingFlags.Static | BindingFlags.NonPublic;
    private const BindingFlags Instance = BindingFlags.Instance | BindingFlags.NonPublic;
    private sealed class AssertionFailure : Exception { internal AssertionFailure(string why) : base(why) { } }

    [ModuleInitializer]
    internal static void Run()
    {
        var cases = new List<(string Name, Action<Fixture> Test)>();
        cases.Add(("readable row retains real ID and metadata", f => { f.Publish(0, 20); CheckSpell(WoWSpell.FromId(f.Id), f.Id, 20); }));
        cases.Add(("stable repeated lookup retains its metadata", f => { f.Publish(0, 20); for (int i=0;i<4;i++) CheckSpell(WoWSpell.FromId(f.Id), f.Id, 20); }));
        cases.Add(("missing row remains unknown", f => Check(WoWSpell.FromId(f.Id)==null, "missing row became a spell")));
        cases.Add(("missing row is retried after hydration", f => { Check(WoWSpell.FromId(f.Id)==null,"initial missing row"); f.Publish(0, 30); CheckSpell(WoWSpell.FromId(f.Id), f.Id, 30); }));
        cases.Add(("repeated misses do not poison later hydration", f => { for(int i=0;i<5;i++) Check(WoWSpell.FromId(f.Id)==null,"initial missing row"); f.Publish(0, 31); CheckSpell(WoWSpell.FromId(f.Id), f.Id, 31); }));
        cases.Add(("unrelated valid row remains usable after a miss", f => { Check(WoWSpell.FromId(f.Id)==null,"initial missing row"); f.Publish(1, 40); CheckSpell(WoWSpell.FromId(f.Id+1),f.Id+1,40); }));
        cases.Add(("unrelated lookup does not prevent first row recovery", f => { Check(WoWSpell.FromId(f.Id)==null,"initial missing row"); f.Publish(1,40); CheckSpell(WoWSpell.FromId(f.Id+1),f.Id+1,40); f.Publish(0,41); CheckSpell(WoWSpell.FromId(f.Id),f.Id,41); }));
        cases.Add(("missing table remains unknown", f => { f.RemoveTable(); Check(WoWSpell.FromId(f.Id)==null,"absent table supplied a row"); }));
        cases.Add(("table publication after initial absence remains retryable", f => { f.RemoveTable(); Check(WoWSpell.FromId(f.Id)==null,"absent table supplied a row"); f.Publish(0,42); f.InstallTable(); CheckSpell(WoWSpell.FromId(f.Id),f.Id,42); }));
        cases.Add(("replacement table can hydrate a previously missing row", f => { Check(WoWSpell.FromId(f.Id)==null,"initial missing row"); f.Publish(0,43); f.InstallTable(true); CheckSpell(WoWSpell.FromId(f.Id),f.Id,43); }));
        cases.Add(("updated row-array pointer replaces a previously valid row", f => { f.Publish(0,20); CheckSpell(WoWSpell.FromId(f.Id),f.Id,20); f.Publish(0,51); CheckSpell(WoWSpell.FromId(f.Id),f.Id,51); }));
        cases.Add(("removed row pointer revokes a previously valid result", f => { f.Publish(0,20); CheckSpell(WoWSpell.FromId(f.Id),f.Id,20); f.ClearRow(0); Check(WoWSpell.FromId(f.Id)==null,"removed row was returned from an ID-only cache"); }));
        cases.Add(("row can return after a valid-missing-valid sequence", f => { f.Publish(0,20); CheckSpell(WoWSpell.FromId(f.Id),f.Id,20); f.ClearRow(0); _=WoWSpell.FromId(f.Id); f.Publish(0,52); CheckSpell(WoWSpell.FromId(f.Id),f.Id,52); }));
        cases.Add(("table removal revokes a previously valid cached row", f => { f.Publish(0,20); CheckSpell(WoWSpell.FromId(f.Id),f.Id,20); f.RemoveTable(); Check(WoWSpell.FromId(f.Id)==null,"removed table borrowed an old row"); }));
        cases.Add(("replacement table cannot inherit an old row address", f => { f.Publish(0,20); CheckSpell(WoWSpell.FromId(f.Id),f.Id,20); f.Publish(0,53); f.InstallTable(true); CheckSpell(WoWSpell.FromId(f.Id),f.Id,53); }));
        cases.Add(("replaced table with an empty row remains unknown", f => { f.Publish(0,20); CheckSpell(WoWSpell.FromId(f.Id),f.Id,20); f.ClearRow(0); f.InstallTable(true); Check(WoWSpell.FromId(f.Id)==null,"empty replacement inherited cached row"); }));
        cases.Add(("missing memory revokes a previously valid row", f => { f.Publish(0,20); CheckSpell(WoWSpell.FromId(f.Id),f.Id,20); f.WithoutMemory(); Check(WoWSpell.FromId(f.Id)==null,"missing memory returned a valid-looking zero-filled spell"); }));
        cases.Add(("missing memory during an initial miss does not poison recovery", f => { f.WithoutMemory(); Check(WoWSpell.FromId(f.Id)==null,"missing memory supplied a row"); f.RestoreMemory(); f.Publish(0,54); CheckSpell(WoWSpell.FromId(f.Id),f.Id,54); }));
        cases.Add(("replacement memory reads its current row-array pointer", f => { f.Publish(0,20); CheckSpell(WoWSpell.FromId(f.Id),f.Id,20); f.Publish(0,55); f.ReplaceMemory(); CheckSpell(WoWSpell.FromId(f.Id),f.Id,55); }));
        cases.Add(("unrelated cached ID survives another row replacement", f => { f.Publish(0,20); f.Publish(1,21); CheckSpell(WoWSpell.FromId(f.Id),f.Id,20); CheckSpell(WoWSpell.FromId(f.Id+1),f.Id+1,21); f.Publish(0,56); CheckSpell(WoWSpell.FromId(f.Id+1),f.Id+1,21); }));
        cases.Add(("previous materialized spell retains its value snapshot", f => { f.Publish(0,20); var first=WoWSpell.FromId(f.Id); CheckSpell(first,f.Id,20); f.Publish(0,57); _=WoWSpell.FromId(f.Id); CheckSpell(first,f.Id,20); }));
        cases.Add(("ID below table range stays unknown", f => Check(WoWSpell.FromId(f.Id-1)==null,"below-range ID became a spell")));
        cases.Add(("ID above table range stays unknown", f => Check(WoWSpell.FromId(f.Id+2)==null,"above-range ID became a spell")));
        cases.Add(("negative ID cannot borrow an ordinary positive row", f => { f.Publish(0,20); Check(WoWSpell.FromId(-f.Id)==null,"negative ID borrowed another row"); }));
        int passed=0, assertions=0, unexpected=0;
        foreach(var item in cases)
        {
            try { using var f=new Fixture(); item.Test(f); passed++; Console.WriteLine("PASS spell row lookup: "+item.Name); }
            catch(AssertionFailure e) { assertions++; Console.Error.WriteLine("FAIL spell row lookup assertion: "+item.Name+": "+e.Message); }
            catch(Exception e) { unexpected++; Console.Error.WriteLine("ERROR spell row lookup fixture/owner: "+item.Name+": "+e); }
        }
        Console.WriteLine($"Spell row lookup scenarios: {passed}/{cases.Count}; assertions={assertions}; unexpected={unexpected}; actual FromId/table/row/memory; controlled test-process bytes; no game attached.");
        if(assertions+unexpected!=0) throw new InvalidOperationException($"Spell row lookup regressions: assertions={assertions}; unexpected={unexpected}");
    }

    private sealed class Fixture : IDisposable
    {
        private readonly IDisposable world;
        private readonly Memory memory;
        private readonly List<(Memory Memory, ThreadLocal<Dictionary<IntPtr,byte[]>> Cache, ThreadLocal<bool> Enabled)> replacements=new();
        private readonly Dictionary<ClientDb,WoWDb.DbTable> tables;
        private readonly KeyValuePair<ClientDb,WoWDb.DbTable>[] oldTables;
        // Snapshot the legacy cache only for isolation. Public assertions do not
        // require this optional implementation detail to survive a future repair.
        private readonly System.Collections.IDictionary? legacyCache;
        private readonly List<System.Collections.DictionaryEntry> oldRows=new();
        private readonly FieldInfo dbField=typeof(StyxWoW).GetField("_db",Static)!;
        private readonly object? oldDb;
        private readonly IntPtr storage;
        private int nextRow=512;
        private int nextHeader;
        internal readonly int Id=41001;
        internal Fixture()
        {
            if(IntPtr.Size!=4) throw new InvalidOperationException("This actual 32-bit table contract requires the retained Windows x86 runner.");
            world=(IDisposable)Activator.CreateInstance(typeof(QuestPublicationRegressionTests).GetNestedType("Fixture",BindingFlags.NonPublic)!,true)!;
            memory=ObjectManager.Wow!;
            tables=(Dictionary<ClientDb,WoWDb.DbTable>)typeof(WoWDb).GetField("_tables",Static)!.GetValue(null)!;
            oldTables=tables.ToArray(); oldDb=dbField.GetValue(null);
            legacyCache=typeof(WoWSpell).GetField("_rowCache",Static)?.GetValue(null) as System.Collections.IDictionary;
            if(legacyCache!=null) { foreach(System.Collections.DictionaryEntry row in legacyCache) oldRows.Add(row); legacyCache.Clear(); }
            storage=Marshal.AllocHGlobal(16384); Marshal.Copy(new byte[16384],0,storage,16384);
            tables.Clear(); dbField.SetValue(null,RuntimeHelpers.GetUninitializedObject(typeof(WoWDb)));
            InstallTable();
            SeedUncompressedFlag(memory);
            Check(ObjectManager.Executor==null,"fixture must never install a native executor");
        }
        internal void InstallTable(bool replacement=false)
        {
            if(replacement) nextHeader+=64;
            Type headerType=typeof(WoWDb).GetNestedType("DbTableHeader",BindingFlags.NonPublic)!;
            object header=Activator.CreateInstance(headerType)!;
            foreach(var field in new (string Name,object Value)[] { ("IsLoaded",1),("NumRows",2),("MinIndex",Id),("MaxIndex",Id+1),("RecordSize",704),("FieldCount",176),("RowArrayPtr",IntPtr.Add(storage,256)) })
                headerType.GetField(field.Name)!.SetValue(header,field.Value);
            IntPtr address=IntPtr.Add(storage,nextHeader); Marshal.StructureToPtr(header,address,false); Invalidate(address);
            tables[ClientDb.Spell]=(WoWDb.DbTable)Activator.CreateInstance(typeof(WoWDb.DbTable),Instance,null,new object[]{IntPtr.Add(address,24)},null)!;
        }
        internal void RemoveTable() => tables.Remove(ClientDb.Spell);
        internal void Publish(int slot,uint level)
        {
            // The managed SpellEntry is a prefix of the supplied native record.
            int size=704;
            if(nextRow+size>16384) throw new InvalidOperationException("fixture storage exhausted");
            IntPtr row=IntPtr.Add(storage,nextRow); nextRow+=size+16;
            Marshal.Copy(new byte[size],0,row,size);
            foreach(var value in new (string Field,uint Value)[]{("Id",(uint)(Id+slot)),("baseLevel",level),("spellLevel",level+1),("ManaCostPercentage",level+2)})
                Marshal.WriteInt32(row,Marshal.OffsetOf<SpellEntry>(value.Field).ToInt32(),unchecked((int)value.Value));
            Invalidate(row);
            IntPtr pointer=IntPtr.Add(storage,256+slot*4); Marshal.WriteInt32(pointer,row.ToInt32()); Invalidate(pointer);
        }
        internal void ClearRow(int slot) { var ptr=IntPtr.Add(storage,256+slot*4); Marshal.WriteInt32(ptr,0); Invalidate(ptr); }
        private void Invalidate(IntPtr address)
        {
            var owner=ObjectManager.Wow;
            if(owner==null) return;
            var cache=(ThreadLocal<Dictionary<IntPtr,byte[]>>)typeof(Memory).GetField("_cache",Instance)!.GetValue(owner)!;
            cache.Value!.Remove(address);
        }
        internal void WithoutMemory() => typeof(ObjectManager).GetProperty("Wow")!.SetValue(null,null);
        internal void RestoreMemory() => typeof(ObjectManager).GetProperty("Wow")!.SetValue(null,memory);
        internal void ReplaceMemory()
        {
            var owner=(Memory)RuntimeHelpers.GetUninitializedObject(typeof(Memory));
            var cache=new ThreadLocal<Dictionary<IntPtr,byte[]>>(()=>new()); var enabled=new ThreadLocal<bool>(()=>true);
            typeof(Memory).GetField("_cache",Instance)!.SetValue(owner,cache);
            typeof(Memory).GetField("_cacheEnabled",Instance)!.SetValue(owner,enabled);
            typeof(Memory).GetField("_hProcess",Instance)!.SetValue(owner,new IntPtr(-1));
            SeedUncompressedFlag(owner);
            replacements.Add((owner,cache,enabled));
            typeof(ObjectManager).GetProperty("Wow")!.SetValue(null,owner);
        }
        private static void SeedUncompressedFlag(Memory owner)
        {
            var bytes=(ThreadLocal<Dictionary<IntPtr,byte[]>>)typeof(Memory).GetField("_cache",Instance)!.GetValue(owner)!;
            bytes.Value![new IntPtr(0xC5DEA0)]=new byte[]{0};
        }
        public void Dispose()
        {
            RestoreMemory();
            if(legacyCache!=null) { legacyCache.Clear(); foreach(var row in oldRows) legacyCache.Add(row.Key,row.Value); }
            tables.Clear(); foreach(var entry in oldTables) tables.Add(entry.Key,entry.Value);
            dbField.SetValue(null,oldDb);
            foreach(var x in replacements) { typeof(Memory).GetField("_hProcess",Instance)!.SetValue(x.Memory,IntPtr.Zero); x.Cache.Dispose(); x.Enabled.Dispose(); }
            Marshal.FreeHGlobal(storage); world.Dispose();
        }
    }
    private static void CheckSpell(WoWSpell? spell,int id,uint level) => Check(spell!=null && spell.IsValid && spell.Id==id && spell.BaseLevel==level && spell.Level==level+1 && spell.ManaCostPercent==level+2,
        $"expected current spell {id} level {level}, observed {(spell==null?"null":$"{spell.Id}/{spell.BaseLevel}/{spell.Level}/{spell.ManaCostPercent}")}");
    private static void Check(bool valid,string why) { if(!valid) throw new AssertionFailure(why); }
}
