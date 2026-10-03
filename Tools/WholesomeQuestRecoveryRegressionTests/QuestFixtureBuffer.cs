using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

// Allocations in these fixtures share a reader with cached original-client
// globals. Keep allocation policy separate so address layouts can be replayed
// without writing to any synthetic client address in the test process.
internal static class QuestFixtureBuffer
{
    internal const uint SyntheticGlobalLimit = 0x01000000;
    internal static IntPtr Allocate(int bytes) => AllocateCore(bytes, Marshal.AllocHGlobal, Marshal.FreeHGlobal);

    // Every fixed client global seeded by these fixtures is below 16 MiB.
    // Allocated observations must not share those numeric addresses: the real
    // reader's cache and invalidator have one address namespace. Holding rejected
    // blocks until a disjoint block is found prevents allocator reuse loops.
    internal static IntPtr AllocateCore(int bytes, Func<int, IntPtr> allocate, Action<IntPtr> free)
    {
        if (IntPtr.Size != 4) throw new PlatformNotSupportedException("Original-client fixture allocations require x86.");
        if (bytes <= 0 || bytes > 16 * 1024 * 1024) throw new ArgumentOutOfRangeException(nameof(bytes));
        ArgumentNullException.ThrowIfNull(allocate);
        ArgumentNullException.ThrowIfNull(free);
        var rejected = new List<IntPtr>();
        try
        {
            for (int attempt = 0; attempt < 32; attempt++)
            {
                // Larger rejected reservations advance beyond the synthetic
                // client region even when a small-block heap is located there.
                int reservedBytes = attempt == 0 ? bytes : Math.Max(bytes, 1024 * 1024);
                IntPtr address = allocate(reservedBytes);
                if (address == IntPtr.Zero) throw new OutOfMemoryException("Fixture allocation returned no buffer.");
                ulong start = unchecked((uint)address.ToInt32());
                if (start >= SyntheticGlobalLimit && start + (uint)reservedBytes <= 0x100000000UL)
                    return address;
                rejected.Add(address);
            }
            throw new OutOfMemoryException("No fixture allocation was disjoint from the synthetic client globals.");
        }
        finally
        {
            foreach (IntPtr address in rejected) free(address);
        }
    }
}
