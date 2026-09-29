using System;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using WholesomeAQ;

// Actual linked writer and real isolated filesystem errors, not fabricated
// exceptions or rewritten write behavior. Optimized callers exercise the
// diagnostic frame relied upon by the retained publication assertion.
internal static class ProfileWriterDiagnosticRegressionTests
{
    private sealed class Failure(string message) : Exception(message) { }

    [ModuleInitializer]
    internal static void Run()
    {
        if (!OperatingSystem.IsWindows() || IntPtr.Size != 4)
            throw new PlatformNotSupportedException("Writer diagnostic controls require hosted Windows x86.");
        int passed = 0, assertions = 0, unexpected = 0, total = 0;
        void Case(string name, Action<string> body)
        {
            total++;
            string directory = Path.Combine(Path.GetTempPath(), "cb-writer-diagnostic-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            try { body(directory); passed++; Console.WriteLine("PASS profile writer diagnostic: " + name); }
            catch (Failure e) { assertions++; Console.Error.WriteLine("FAIL profile writer diagnostic: " + name + ": " + e.Message); }
            catch (Exception e) { unexpected++; Console.Error.WriteLine("ERROR profile writer diagnostic: " + name + ": " + e); }
            finally { Directory.Delete(directory, true); }
        }

        // A diagnostic contract, not evidence that a native/game operation ran.
        Case("owner-frame-is-an-explicit-runtime-contract", _ => {
            var method = typeof(ProfileBuilder).GetMethod(nameof(ProfileBuilder.WriteProfile))!;
            Check((method.GetMethodImplementationFlags() & MethodImplAttributes.NoInlining) != 0,
                "diagnostic owner is still eligible for optimization that removes its frame");
        });
        foreach (string invocation in new[] { "direct", "optimized", "optimized-after-success" })
        foreach (string destination in new[] { "directory", "missing-parent", "invalid-name" })
            Case(invocation + "/" + destination, directory => {
                if (invocation == "optimized-after-success")
                {
                    string path = Path.Combine(directory, "warm.xml");
                    Check(Optimized(new ProfileBuilder(path), "<warm />") == path && File.ReadAllText(path) == "<warm />",
                        "healthy warmup did not reach the actual writer");
                }
                string output = destination == "directory" ? directory
                    : destination == "missing-parent" ? Path.Combine(directory, "absent", "profile.xml")
                    : Path.Combine(directory, "invalid\0.xml");
                var builder = new ProfileBuilder(output);
                Exception? failure = null;
                try { if (invocation == "direct") Direct(builder, "<failure />"); else Optimized(builder, "<failure />"); }
                catch (Exception e) when (e is UnauthorizedAccessException || e is DirectoryNotFoundException || e is ArgumentException) { failure = e; }
                Check(failure != null, "invalid output did not produce the real filesystem/argument failure");
                Check(destination == "directory" ? failure is UnauthorizedAccessException
                    : destination == "missing-parent" ? failure is DirectoryNotFoundException : failure is ArgumentException,
                    "exception type was translated or replaced");
                Check(failure!.StackTrace?.Contains("ProfileBuilder.WriteProfile", StringComparison.Ordinal) == true,
                    "actual writer frame was optimized away: " + failure);
            });
        Case("successful-write-preserves-content-and-return", directory => {
            string path = Path.Combine(directory, "profile.xml"), xml = "<profile name='sustain'>\u5149</profile>";
            Check(Optimized(new ProfileBuilder(path), xml) == path && File.ReadAllText(path) == xml,
                "successful writer result/content changed");
        });
        Case("successful-overwrite-is-not-append", directory => {
            string path = Path.Combine(directory, "profile.xml");
            var builder = new ProfileBuilder(path); Direct(builder, "first");
            Check(Optimized(builder, "second") == path && File.ReadAllText(path) == "second", "overwrite semantics changed");
        });
        Case("null-output-remains-a-no-write-result", directory => {
            var builder = new ProfileBuilder(null!);
            Check(Direct(builder, "unused") == null && Optimized(builder, "unused") == null
                && Directory.GetFileSystemEntries(directory).Length == 0, "null-output contract changed");
        });

        Console.WriteLine($"Profile writer diagnostic scenarios: {passed}/{total}; assertions={assertions}; unexpected={unexpected}; actual linked writer, optimized/direct callers and isolated file errors; no game/native execution or assertion weakening.");
        if (assertions + unexpected != 0) throw new InvalidOperationException("Profile writer diagnostic regression");
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static string? Direct(ProfileBuilder writer, string xml) => writer.WriteProfile(xml);

    [MethodImpl(MethodImplOptions.NoInlining | MethodImplOptions.AggressiveOptimization)]
    private static string? Optimized(ProfileBuilder writer, string xml) => writer.WriteProfile(xml);

    private static void Check(bool value, string message) { if (!value) throw new Failure(message); }
}
