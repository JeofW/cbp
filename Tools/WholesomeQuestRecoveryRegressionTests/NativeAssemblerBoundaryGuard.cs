using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;

internal static class NativeAssemblerBoundaryGuard
{
    [ModuleInitializer]
    internal static void Run()
    {
        var assembly = Assembly.Load("fasmdll_managed, Version=1.0.3262.20709, Culture=neutral, PublicKeyToken=null");
        if (!assembly.GetCustomAttributes<AssemblyMetadataAttribute>().Any(value => value.Key == "OfflineBoundary" && value.Value == "deny-native-dispatch"))
            throw new InvalidOperationException("Wholesome offline fixture did not load its explicit deny-dispatch assembler boundary.");
        var type = assembly.GetType("Fasm.ManagedFasm", throwOnError: true)!;
        if (type.GetConstructors().Length != 0 || type.GetMethods(BindingFlags.Public | BindingFlags.DeclaredOnly | BindingFlags.Instance | BindingFlags.Static).Length != 0)
            throw new InvalidOperationException("Offline boundary unexpectedly exposes an assembler API.");
        Console.WriteLine("OFFLINE NATIVE BOUNDARY verified: " + assembly.FullName + "; SHA256=" + Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(assembly.Location))).ToLowerInvariant());
        Console.WriteLine("Host production methods execute; native assembler, injection, release and native process-exit compatibility are NOT validated by this executable.");
    }
}
