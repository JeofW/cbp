using System;
using System.CodeDom.Compiler;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using System.Security.Cryptography;
using System.Text;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

// The complete BlackspotManager executes, including native-polygon ownership
// and restoration. Only time, actor-map, profile events, native calls and a
// private temporary persistence directory are controlled. No live mesh/game.
internal static class CollisionBlackspotLifetimeRegressionTests
{
    [ModuleInitializer]
    internal static void Run()
    {
        string root = Root();
        string owner = CSharpSyntaxTree.ParseText(File.ReadAllText(Path.Combine(root,
            "Styx/Logic/Pathing/BlackspotManager.cs"))).GetRoot().DescendantNodes()
            .OfType<ClassDeclarationSyntax>().Single(row => row.Identifier.ValueText == "BlackspotManager").ToFullString();
        string source = CollisionBlackspotLifetimeFixture.Prefix + owner + CollisionBlackspotLifetimeFixture.Boundary + "\n}";
        string folder = Path.Combine(Path.GetTempPath(), "cb-collision-blackspot-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            File.WriteAllText(Path.Combine(folder, "Probe.cs"), source, Encoding.UTF8);
            Type type = typeof(Styx.StyxWoW).Assembly.GetType("Styx.Loaders.SourceCompiler", true)!;
            object compiler = Activator.CreateInstance(type, new object[] { folder })!;
            foreach (string reference in ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator))
                type.GetMethod("AddReference", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)!
                    .Invoke(compiler, new object[] { reference });
            var result = (CompilerResults)type.GetMethod("Compile")!.Invoke(compiler, null)!;
            var errors = result.Errors.Cast<CompilerError>().Where(row => !row.IsWarning).ToArray();
            if (errors.Length != 0) throw new InvalidOperationException("Blackspot boundary compile: " + string.Join("; ", errors.Select(row => row.ToString())));
            var assembly = (Assembly)type.GetProperty("CompiledAssembly")!.GetValue(compiler)!;
            try { assembly.GetType("CollisionBlackspotProbe.Cases", true)!.GetMethod("Run")!.Invoke(null, new object[] { folder }); }
            catch (TargetInvocationException error) when (error.InnerException != null)
            { ExceptionDispatchInfo.Capture(error.InnerException).Throw(); throw; }
            Console.WriteLine("Complete blackspot source SHA256: " + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(source))).ToLowerInvariant());
        }
        finally { Directory.Delete(folder, true); }
    }
    private static string Root()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory != null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "CopilotBuddy.csproj"))) return directory.FullName;
        throw new InvalidOperationException("Tracked source checkout required");
    }
}
