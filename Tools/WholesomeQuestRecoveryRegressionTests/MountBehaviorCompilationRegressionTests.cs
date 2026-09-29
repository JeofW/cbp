using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Styx.Helpers;
using Styx.Logic.Profiles.Quest;
using Styx.Logic.Questing;

// Full runtime source compatibility complements the focused behavior tests.
// Use a fresh process because other retained fixtures intentionally load shadow
// host types. Keep the production compiler's reference discovery unchanged.
internal static class MountBehaviorCompilationRegressionTests
{
    [ModuleInitializer]
    internal static void Run()
    {
        string assemblyPath = typeof(MountBehaviorCompilationRegressionTests).Assembly.Location;
        string runtime = Environment.ProcessPath ?? throw new InvalidOperationException("Test runtime required.");
        if (!string.Equals(Path.GetFileNameWithoutExtension(runtime), "dotnet", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Run through the verified dotnet test runtime.");
        string launcherPath = Path.Combine(AppContext.BaseDirectory, "mount-behavior-compile-" + Guid.NewGuid().ToString("N") + ".dll");
        const string launcher = "using System;using System.Reflection;public static class Entry{public static int Main(string[] args){try{Assembly.LoadFrom(args[0]).GetType(\"MountBehaviorCompilationRegressionTests\",true).GetMethod(\"RunIsolated\",BindingFlags.NonPublic|BindingFlags.Static).Invoke(null,null);return 0;}catch(Exception error){Console.Error.WriteLine(error);return 1;}}}";
        try
        {
            string trusted = AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string ?? throw new InvalidOperationException("Runtime references required.");
            var compilation = CSharpCompilation.Create(Path.GetFileNameWithoutExtension(launcherPath),
                new[] { CSharpSyntaxTree.ParseText(launcher) },
                trusted.Split(Path.PathSeparator).Distinct(StringComparer.OrdinalIgnoreCase).Select(path => MetadataReference.CreateFromFile(path)),
                new CSharpCompilationOptions(OutputKind.ConsoleApplication, platform: Platform.X86));
            using (var stream = new FileStream(launcherPath, FileMode.CreateNew, FileAccess.Write))
            {
                var emitted = compilation.Emit(stream);
                if (!emitted.Success) throw new InvalidOperationException(string.Join("; ", emitted.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error)));
            }
            var start = new ProcessStartInfo(runtime)
            {
                UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true,
                WorkingDirectory = AppContext.BaseDirectory
            };
            foreach (string argument in new[] { "exec", "--runtimeconfig", Path.ChangeExtension(assemblyPath, ".runtimeconfig.json"),
                "--depsfile", Path.ChangeExtension(assemblyPath, ".deps.json"), launcherPath, assemblyPath })
                start.ArgumentList.Add(argument);
            using var child = Process.Start(start) ?? throw new InvalidOperationException("Could not start compiler verification.");
            var output = child.StandardOutput.ReadToEndAsync();
            var error = child.StandardError.ReadToEndAsync();
            if (!child.WaitForExit(60000))
            {
                child.Kill(entireProcessTree: true);
                child.WaitForExit();
                throw new InvalidOperationException("Full behavior compilation exceeded its one-minute test bound.");
            }
            Console.Write(output.GetAwaiter().GetResult());
            Console.Error.Write(error.GetAwaiter().GetResult());
            if (child.ExitCode != 0) throw new InvalidOperationException("Full behavior compiler verification failed: " + child.ExitCode);
        }
        finally { if (File.Exists(launcherPath)) File.Delete(launcherPath); }
    }

    private static void RunIsolated()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root != null && !File.Exists(Path.Combine(root.FullName, "CopilotBuddy.csproj"))) root = root.Parent;
        if (root == null) throw new InvalidOperationException("Tracked checkout required.");
        bool previousLogging = Logging.FileLogging;
        Action<LogLevel, string> record = (_, message) => Console.WriteLine("MOUNT_BEHAVIOR_COMPILER: " + message);
        Logging.FileLogging = false;
        Logging.OnMessageLogged += record;
        int passed = 0;
        try
        {
            foreach (var entry in new[]
            {
                (File: "ForcedDismount.cs", Type: "Styx.Bot.Quest_Behaviors.ForcedDismount"),
                (File: "InteractWith.cs", Type: "Styx.Bot.Quest_Behaviors.InteractWith.InteractWith")
            })
            {
                string path = Path.Combine(root.FullName, "runtime-snapshot", "Quest Behaviors", entry.File);
                var assembly = new QuestBehaviorHelper(path).GetAssembly();
                var type = assembly?.GetType(entry.Type);
                if (type == null || !typeof(CustomForcedBehavior).IsAssignableFrom(type))
                    throw new InvalidOperationException("Actual runtime compiler did not produce the expected behavior: " + entry.File);
                passed++;
                Console.WriteLine("PASS full mount behavior compilation: " + entry.File);
            }
        }
        finally { Logging.OnMessageLogged -= record; Logging.FileLogging = previousLogging; }
        Console.WriteLine($"Full mount behavior compilation: {passed}/2; complete tracked files and production compiler; no behavior instantiated, game attached or native dispatch.");
    }
}
