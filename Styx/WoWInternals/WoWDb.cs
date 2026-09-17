using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using GreenMagic;
using Styx.Patchables;

namespace Styx.WoWInternals
{
    public class WoWDb
    {
        #region Fields

        private static readonly object InitializationSync = new object();
        private static Dictionary<ClientDb, DbTable> _tables = new Dictionary<ClientDb, DbTable>();
        // Publish readiness only with a complete registry; failed observations can retry.
        private const int MaximumRegistrationRecords = 512;
        private static bool _initialized = false;

        #endregion

        #region Constructor

        internal WoWDb()
        {
            Initialize();
        }

        private void Initialize()
        {
            Dictionary<ClientDb, DbTable>? published = null;
            lock (InitializationSync)
            {
                if (_initialized) return;
                var memory = ObjectManager.Wow;
                if (memory == null) return;

                try
                {
                    var candidate = new Dictionary<ClientDb, DbTable>();
                    var addresses = new Dictionary<ClientDb, uint>();
                    uint start = (uint)GlobalOffsets.ClientDb_RegisterBase;
                    // The retained original-client contract uses 17-byte entries.
                    // A missing terminator or partial field cannot publish a prefix.
                    for (int index = 0; index <= MaximumRegistrationRecords; index++)
                    {
                        ulong next = (ulong)start + (uint)index * 17;
                        if (next > uint.MaxValue - 14) return;
                        uint address = (uint)next;
                        byte[]? opcode = ReadExact(memory, address, 1);
                        if (opcode == null) return;
                        if (opcode[0] == 0xC3)
                        {
                            if (candidate.Count == 0 || !ReferenceEquals(memory, ObjectManager.Wow))
                                return;
                            _tables = candidate;
                            _initialized = true;
                            published = candidate;
                            break;
                        }
                        if (index == MaximumRegistrationRecords) return;

                        byte[]? idBytes = ReadExact(memory, address + 1, 4);
                        byte[]? pointerBytes = ReadExact(memory, address + 11, 4);
                        if (idBytes == null || pointerBytes == null) return;
                        var id = (ClientDb)BitConverter.ToUInt32(idBytes, 0);
                        if (!Enum.IsDefined(typeof(ClientDb), id)) return;
                        uint pointer = BitConverter.ToUInt32(pointerBytes, 0);
                        if (pointer == 0 || pointer > uint.MaxValue - 24) return;
                        if (addresses.TryGetValue(id, out uint previous))
                        {
                            if (previous != pointer) return;
                            continue;
                        }
                        byte[]? bytes = ReadExact(memory, pointer, Marshal.SizeOf<DbTableHeader>());
                        if (bytes == null) return;
                        var header = (DbTableHeader)ReadManagedValue(bytes, typeof(DbTableHeader), 0)!;
                        // Discovery need not wait for every table's rows to load.
                        // Preserve that state, but never treat a short read as zeros.
                        candidate.Add(id, new DbTable(new IntPtr(unchecked((int)(pointer + 24))), header));
                        addresses.Add(id, pointer);
                    }
                }
                catch (Exception error) when (error is not OperationCanceledException && error is not ThreadInterruptedException)
                {
                    return;
                }
            }

            if (published == null) return;
            // Diagnostics are external callbacks. Run them after atomic publication
            // and outside the registry lock; stop signals must retain their identity.
            try
            {
                Helpers.Logging.WriteDebug($"[WoWDb] Loaded {published.Count} DBC tables");
                int logged = 0;
                foreach (var kvp in published)
                {
                    if (logged++ < 5)
                        Helpers.Logging.WriteDebug($"[WoWDb]   Key={(int)kvp.Key} (0x{(int)kvp.Key:X8}) Rows={kvp.Value.NumRows}");
                }
                published.TryGetValue(ClientDb.Lock, out var lockDb);
                Helpers.Logging.WriteDebug($"[WoWDb] Lock DBC lookup: {(lockDb != null ? $"FOUND (rows={lockDb.NumRows})" : "NOT FOUND")}");
                Helpers.Logging.WriteDebug($"[WoWDb] ClientDb.Lock enum value = {(int)ClientDb.Lock} (0x{(int)ClientDb.Lock:X8})");
            }
            catch (Exception error) when (error is not OperationCanceledException && error is not ThreadInterruptedException)
            {
                // A diagnostic failure does not undo the complete registry.
            }
        }

        #endregion

        #region Indexer
        public DbTable? this[ClientDb db]
        {
            get
            {
                Initialize();
                lock (InitializationSync)
                    return _initialized && _tables.TryGetValue(db, out var table) ? table : null;
            }
        }

        #endregion

        // Original 3.3.5a localized Spell record contract from Likon fd79fa02.
        // The current managed SpellEntry consumes only its 680-byte prefix.
        private const int LocalizedSpellRecordSize = 704;
        private const uint ClientDbIsCompressed = 0xC5DEA0;

        // A repeated pair is followed by an additional-repeat count, then a
        // literal (unless the output is complete). Never accept partial output,
        // allocate from an untrusted size, or let a run cross the record boundary.
        internal static byte[]? DecodePackedRow(Func<int, byte?> read, int size)
        {
            if (read == null || size <= 0 || size > LocalizedSpellRecordSize)
                return null;
            int cursor = 0;
            int budget = size * 2 + 1;
            byte? Next() => cursor < budget ? read(cursor++) : null;
            byte? first = Next();
            if (!first.HasValue) return null;
            var output = new byte[size];
            output[0] = first.Value;
            int written = 1;
            byte previous = first.Value;
            while (written < size)
            {
                byte? literal = Next();
                if (!literal.HasValue) return null;
                output[written++] = literal.Value;
                if (literal.Value == previous)
                {
                    byte? repeat = Next();
                    if (!repeat.HasValue || repeat.Value > size - written)
                        return null;
                    for (int i = 0; i < repeat.Value; i++)
                        output[written++] = literal.Value;
                    if (written == size) break;
                    literal = Next();
                    if (!literal.HasValue) return null;
                    output[written++] = literal.Value;
                }
                previous = literal.Value;
            }
            return output;
        }

        private static byte[]? ReadExact(Memory memory, uint address, int count)
        {
            if (address == 0 || count <= 0 || (ulong)address + (uint)count - 1 > uint.MaxValue
                || !ReferenceEquals(memory, ObjectManager.Wow))
                return null;
            byte[]? bytes = memory.ReadBytes(address, count);
            return bytes != null && bytes.Length == count && ReferenceEquals(memory, ObjectManager.Wow)
                ? bytes : null;
        }

        private static object? ReadManagedValue(byte[] bytes, Type type, int offset)
        {
            int size = Marshal.SizeOf(type);
            if (offset < 0 || size <= 0 || (long)offset + size > bytes.Length)
                return null;
            var pin = GCHandle.Alloc(bytes, GCHandleType.Pinned);
            try { return Marshal.PtrToStructure(IntPtr.Add(pin.AddrOfPinnedObject(), offset), type); }
            finally { pin.Free(); }
        }

        #region Nested Classes
        public class DbTable
        {
            private readonly IntPtr _tablePtr;
            private readonly DbTableHeader _header;

            internal DbTable(IntPtr tablePtr)
            {
                _tablePtr = tablePtr;
                var wow = ObjectManager.Wow;
                _header = wow != null ? wow.ReadStruct<DbTableHeader>((uint)tablePtr.ToInt32() - 24) : default;
            }
            internal DbTable(IntPtr tablePtr, DbTableHeader header)
            {
                _tablePtr = tablePtr;
                _header = header;
            }
            public bool IsLoaded => _header.IsLoaded != 0;
            public int NumRows => _header.NumRows;
            public int MaxIndex => _header.MaxIndex;
            public int MinIndex => _header.MinIndex;
            public Row? GetRow(uint index)
            {
                var wow = ObjectManager.Wow;
                if (wow == null) return null;
                
                if (index < MinIndex || index > MaxIndex)
                    return null;

                uint offset = (uint)_header.RowArrayPtr.ToInt32();
                offset += (index - (uint)MinIndex) * 4;
                
                uint rowPtr = wow.Read<uint>(offset);
                if (rowPtr == 0)
                    return null;

                return new Row(new IntPtr(rowPtr));
            }

            /// <summary>
            /// Reads one complete original-client Spell record into an immutable
            /// managed snapshot. Unknown compression/read state is not raw permission.
            /// Other DBC tables retain their existing GetRow contract.
            /// </summary>
            public Row? GetLocalizedRow(int index)
            {
                var memory = ObjectManager.Wow;
                if (memory == null || index <= 0 || IntPtr.Size != 4)
                    return null;
                try
                {
                    uint tableAddress = unchecked((uint)_tablePtr.ToInt32());
                    if (tableAddress < 24) return null;
                    uint headerAddress = tableAddress - 24;
                    byte[]? headerBytes = ReadExact(memory, headerAddress, Marshal.SizeOf<DbTableHeader>());
                    if (headerBytes == null) return null;
                    headerBytes = (byte[])headerBytes.Clone();
                    var header = (DbTableHeader)ReadManagedValue(headerBytes, typeof(DbTableHeader), 0)!;
                    if (header.IsLoaded == 0 || header.NumRows <= 0 || header.MinIndex < 0
                        || header.MaxIndex < header.MinIndex || index < header.MinIndex || index > header.MaxIndex)
                        return null;
                    uint array = unchecked((uint)header.RowArrayPtr.ToInt32());
                    ulong offset = (ulong)array + (ulong)((long)index - header.MinIndex) * 4;
                    if (array == 0 || offset > uint.MaxValue - 3) return null;
                    byte[]? pointer = ReadExact(memory, (uint)offset, 4);
                    byte[]? mode = ReadExact(memory, ClientDbIsCompressed, 1);
                    if (pointer == null || mode == null || mode[0] > 1) return null;
                    pointer = (byte[])pointer.Clone();
                    mode = (byte[])mode.Clone();
                    uint address = BitConverter.ToUInt32(pointer, 0);
                    if (address == 0) return null;
                    byte[]? data;
                    if (mode[0] == 0)
                        data = ReadExact(memory, address, LocalizedSpellRecordSize);
                    else
                        data = DecodePackedRow(position =>
                        {
                            ulong next = (ulong)address + (uint)position;
                            if (next > uint.MaxValue) return null;
                            byte[]? value = ReadExact(memory, (uint)next, 1);
                            return value == null ? null : value[0];
                        }, LocalizedSpellRecordSize);
                    if (data == null) return null;
                    data = (byte[])data.Clone();
                    if (BitConverter.ToUInt32(data, 0) != (uint)index)
                        return null;

                    // Recheck observed metadata at publication. This deliberately
                    // does not claim to replace Memory's frame/cache lifetime.
                    byte[]? currentHeader = ReadExact(memory, headerAddress, headerBytes.Length);
                    byte[]? currentPointer = ReadExact(memory, (uint)offset, 4);
                    byte[]? currentMode = ReadExact(memory, ClientDbIsCompressed, 1);
                    if (currentHeader == null || currentPointer == null || currentMode == null
                        || !headerBytes.AsSpan().SequenceEqual(currentHeader)
                        || !pointer.AsSpan().SequenceEqual(currentPointer) || currentMode[0] != mode[0])
                        return null;
                    return new Row(data, memory);
                }
                catch (Exception error) when (error is not OperationCanceledException && error is not ThreadInterruptedException)
                {
                    return null;
                }
            }
        }
        public class Row
        {
            private readonly IntPtr _address;
            private readonly bool _ownsMemory;
            private readonly byte[]? _data;
            private readonly Memory? _sourceMemory;

            internal Row(IntPtr address)
            {
                _address = address;
                _ownsMemory = false;
            }

            internal Row(IntPtr address, bool ownsMemory) : this(address)
            {
                _ownsMemory = ownsMemory;
            }
            internal Row(byte[] data, Memory sourceMemory)
            {
                _data = (byte[])data.Clone();
                _sourceMemory = sourceMemory;
                _ownsMemory = true;
            }

            public bool IsValid => _data != null || _address != IntPtr.Zero;
            public T? GetField<T>(uint index)
            {
                if (_data != null)
                {
                    try
                    {
                        ulong offset = (ulong)index * 4;
                        if (offset > int.MaxValue) return default;
                        if (typeof(T) == typeof(string))
                        {
                            // String fields contain remote pointers, not inline text.
                            // Numeric/struct snapshots are independent of this owner.
                            if (_sourceMemory == null || !ReferenceEquals(_sourceMemory, ObjectManager.Wow))
                                return default;
                            object? field = ReadManagedValue(_data, typeof(uint), (int)offset);
                            if (field is not uint pointer || pointer == 0) return default;
                            string value = _sourceMemory.Read<string>(pointer);
                            return ReferenceEquals(_sourceMemory, ObjectManager.Wow) ? (T)(object)value : default;
                        }
                        if (!typeof(T).IsValueType) return default;
                        object? result = ReadManagedValue(_data, typeof(T), (int)offset);
                        return result == null ? default : (T)result;
                    }
                    catch (Exception error) when (error is not OperationCanceledException && error is not ThreadInterruptedException)
                    {
                        return default;
                    }
                }
                try
                {
                    var wow = ObjectManager.Wow;
                    if (wow == null || _ownsMemory)
                        return default;

                    if (typeof(T) == typeof(string))
                    {
                        uint strPtr = wow.Read<uint>((uint)_address.ToInt32() + index * 4);
                        object result = wow.Read<string>(strPtr);
                        return (T)result;
                    }
                    
                    return wow.Read<T>((uint)_address.ToInt32() + index * 4);
                }
                catch
                {
                    return default;
                }
            }
            public void SetField<T>(uint index, T value)
            {
                if (_data != null)
                    throw new InvalidOperationException("A localized row snapshot is immutable.");
                var wow = ObjectManager.Wow;
                if (wow != null)
                    wow.Write((uint)_address.ToInt32() + index * 4, value);
            }
            public T GetStruct<T>() where T : struct
            {
                if (_data != null)
                {
                    object? result = ReadManagedValue(_data, typeof(T), 0);
                    return result == null ? default : (T)result;
                }
                try
                {
                    var wow = ObjectManager.Wow;
                    if (wow == null)
                        return default;
                    return wow.ReadStruct<T>((uint)_address.ToInt32());
                }
                catch
                {
                    return default;
                }
            }
        }
        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential, Pack = 1)]
        internal struct DbTableHeader
        {
            private readonly IntPtr _reserved0;
            public int IsLoaded;
            public int NumRows;
            public int MaxIndex;
            public int MinIndex;
            public int RecordSize;
            private readonly IntPtr _reserved1;
            public int FieldCount;
            public IntPtr RowArrayPtr;
        }

        #endregion
    }
}
