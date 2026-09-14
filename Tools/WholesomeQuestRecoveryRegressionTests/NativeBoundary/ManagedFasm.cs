using System;

namespace Fasm;

// Type identity only, for Memory's private external assembler field. There is no
// assembler API implementation: any attempt to bind/call an assembler method
// must fail rather than simulate successful assembly, injection or dispatch.
public sealed class ManagedFasm
{
    private ManagedFasm() => throw new NotSupportedException("Native assembler dispatch is forbidden in this offline test process.");
}
