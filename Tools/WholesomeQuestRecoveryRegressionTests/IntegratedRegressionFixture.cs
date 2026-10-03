using System;
using System.CodeDom.Compiler;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.ExceptionServices;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

// Shared compiler harness for connected production callers. Only observations
// and native submission are controlled by each test's explicit boundary.
internal static class IntegratedRegressionFixture
{
    internal static string Root
    {
        get
        {
            for (var path = new DirectoryInfo(AppContext.BaseDirectory); path != null; path = path.Parent)
                if (File.Exists(Path.Combine(path.FullName, "CopilotBuddy.csproj"))) return path.FullName;
            throw new InvalidOperationException("A tracked CopilotBuddy checkout is required.");
        }
    }

    internal static string Methods(string relativePath, params string[] names)
    {
        var syntax = CSharpSyntaxTree.ParseText(File.ReadAllText(Path.Combine(Root, relativePath))).GetRoot();
        return string.Join("\n", syntax.DescendantNodes().OfType<MethodDeclarationSyntax>()
            .Where(method => names.Contains(method.Identifier.ValueText))
            .Select(method => method.WithAttributeLists(default).ToString()));
    }

    internal static void Run(string name, string source, params string[] productionFiles)
    {
        string temporary = Path.Combine(Path.GetTempPath(), "cb-integrated-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporary);
        try
        {
            File.WriteAllText(Path.Combine(temporary, "Probe.cs"), source);
            for (int i = 0; i < productionFiles.Length; i++)
                File.Copy(Path.Combine(Root, productionFiles[i]), Path.Combine(temporary, "Production" + i + ".cs"));
            Type compilerType = typeof(Styx.StyxWoW).Assembly.GetType("Styx.Loaders.SourceCompiler", true)!;
            object compiler = Activator.CreateInstance(compilerType, new object[] { temporary })!;
            foreach (string reference in ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator))
                compilerType.GetMethod("AddReference")!.Invoke(compiler, new object[] { reference });
            var result = (CompilerResults)compilerType.GetMethod("Compile")!.Invoke(compiler, null)!;
            var errors = result.Errors.Cast<CompilerError>().Where(error => !error.IsWarning).ToArray();
            if (errors.Length != 0)
                throw new InvalidOperationException(name + " fixture compile: " + string.Join("; ", errors.Select(error => error.ToString())));
            var assembly = (Assembly)compilerType.GetProperty("CompiledAssembly")!.GetValue(compiler)!;
            try { assembly.GetType("Cases", true)!.GetMethod("Run")!.Invoke(null, null); }
            catch (TargetInvocationException error) when (error.InnerException != null)
            { ExceptionDispatchInfo.Capture(error.InnerException).Throw(); throw; }
        }
        finally { Directory.Delete(temporary, true); }
    }
}
