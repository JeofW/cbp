using System;
using System.Runtime.CompilerServices;

internal static class PostmergeBaselineRuntimeGuard
{
    [ModuleInitializer]
    internal static void Verify()
    {
        Console.WriteLine($"Retained baseline runtime: {Environment.OSVersion}; .NET {Environment.Version}; pointer_bytes={IntPtr.Size}; game_attached=false.");
        if (!OperatingSystem.IsWindows() || IntPtr.Size != 4)
            throw new PlatformNotSupportedException("Retained baseline requires Windows x86; no validation is claimed on another architecture.");
    }
}
