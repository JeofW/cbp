// External address/byte observations for the retained 24-case boundary fixture.
// CaptureSnapshot and IsSnapshotCurrent are copied verbatim from the pinned
// production source by extract_owners.py. No snapshot decision is mocked here.
namespace Styx.WoWInternals.WoWObjects
{
    public class LocalPlayer
    {
        public uint BaseAddress = 4096, Descriptor = 8192;
        public ulong Guid = 123;
        public bool IsValid = true;
    }
}
namespace Styx.WoWInternals
{
    public static partial class ObjectManager
    {
        public static GreenMagic.Memory Wow = new();
        public static bool IsInGame => Me != null;
    }
}
namespace GreenMagic
{
    public sealed class Memory
    {
        public bool CacheEnabled = true;
        public byte[] ReadBytes(uint address, int count)
        {
            var me = Styx.WoWInternals.ObjectManager.Me;
            if (me == null) return null;
            if (address == me.BaseAddress + 8U && count == 4)
                return BitConverter.GetBytes(me.Descriptor);
            if ((address == me.BaseAddress + 48U || address == me.Descriptor) && count == 8)
                return BitConverter.GetBytes(me.Guid);
            if (address == me.Descriptor + 632U && count == 500)
            {
                var bytes = new byte[500];
                for (uint i = 0; i < 25; i++)
                {
                    // Preserve the original descriptor observation/cancellation hook.
                    uint id = me.ReadDescriptor<uint>(158U + i * 5U);
                    BitConverter.GetBytes(id).CopyTo(bytes, (int)i * 20);
                }
                return bytes;
            }
            throw new InvalidOperationException("Unexpected raw byte address/count in boundary fixture.");
        }
        public IDisposable TemporaryCacheState(bool enabled)
        {
            bool previous = CacheEnabled;
            CacheEnabled = enabled;
            return new Restore(() => CacheEnabled = previous);
        }
        private sealed class Restore(Action action) : IDisposable
        {
            public void Dispose() => action();
        }
    }
}
