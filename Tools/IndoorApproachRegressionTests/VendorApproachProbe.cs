using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Security.Cryptography;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using TreeSharp;

// Extract the actual vendor travel/admission predicates and native interaction
// action. The rest of the vendor frame/menu lifecycle is outside this probe.
// ActionMoveToPoi and its real transition are linked by the main fixture.
internal sealed class VendorApproachProbe
{
    private readonly Type _probe;
    internal VendorApproachProbe(string root)
    {
        string path = Path.Combine(root, "Bots", "Grind", "LevelBot.cs");
        byte[] bytes = File.ReadAllBytes(path);
        Console.WriteLine("VENDOR_ADMISSION_SOURCE " + path + " sha256=" + Convert.ToHexString(SHA256.HashData(bytes)));
        var method = CSharpSyntaxTree.ParseText(System.Text.Encoding.UTF8.GetString(bytes)).GetRoot()
            .DescendantNodes().OfType<MethodDeclarationSyntax>().Single(node => node.Identifier.ValueText == "CreateVendorBehavior");
        var travel = method.DescendantNodes().OfType<ObjectCreationExpressionSyntax>().Single(node =>
            node.Type.ToString() == "Decorator" && node.ArgumentList?.Arguments.Count == 2
            && node.ArgumentList.Arguments[1].Expression is ObjectCreationExpressionSyntax child && child.Type.ToString() == "ActionMoveToPoi");
        var branches = (ArgumentListSyntax)(travel.Parent?.Parent ?? throw new InvalidOperationException("Vendor selector boundary unavailable."));
        var arrival = (ObjectCreationExpressionSyntax)branches.Arguments.First(node => node.SpanStart > travel.Span.End).Expression;
        if (arrival.Type.ToString() != "Decorator") throw new InvalidOperationException("Vendor arrival decorator changed; review the boundary.");
        var interaction = method.DescendantNodes().OfType<ObjectCreationExpressionSyntax>().Single(node =>
            node.Type.ToString() == "TreeSharp.Action" && node.ArgumentList?.Arguments.Count == 1
            && node.ArgumentList.Arguments[0].Expression.DescendantNodesAndSelf().OfType<InvocationExpressionSyntax>()
                .Any(call => call.Expression is MemberAccessExpressionSyntax member && member.Name.Identifier.ValueText is "Interact" or "TryInteractWith"));
        string source = $$$"""
        using System;using Styx;using Styx.Logic.POI;using Styx.Logic.Pathing;using TreeSharp;
        public static class ActualVendorAdmission {
          public static bool Travel()=>((Func<object,bool>)({{{travel.ArgumentList!.Arguments[0].Expression}}}))(null);
          public static bool Arrived()=>((Func<object,bool>)({{{arrival.ArgumentList!.Arguments[0].Expression}}}))(null);
          public static RunStatus Interact(){var action={{{interaction}}};action.Start(null);try{return action.Tick(null);}finally{action.Stop(null);}}
        }
        """;
        var trusted = ((string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")
            ?? throw new InvalidOperationException("Compiler reference set unavailable.")).Split(Path.PathSeparator);
        var references = trusted.Append(typeof(CommonBehaviors.Actions.ActionMoveToPoi).Assembly.Location)
            .Distinct(StringComparer.OrdinalIgnoreCase).Select(file => MetadataReference.CreateFromFile(file));
        var compilation = CSharpCompilation.Create("VendorAdmission_" + Guid.NewGuid().ToString("N"),
            new[] { CSharpSyntaxTree.ParseText(source) }, references, new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using var stream = new MemoryStream();
        var result = compilation.Emit(stream);
        if (!result.Success) throw new InvalidOperationException("VENDOR FIXTURE COMPILE: " + string.Join("; ", result.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error)));
        _probe = Assembly.Load(stream.ToArray()).GetType("ActualVendorAdmission", true)!;
    }
    private T Invoke<T>(string name)
    {
        try { return (T)_probe.GetMethod(name)!.Invoke(null, null)!; }
        catch (TargetInvocationException error) when (error.InnerException != null)
        { ExceptionDispatchInfo.Capture(error.InnerException).Throw(); throw; }
    }
    internal bool Travel => Invoke<bool>("Travel");
    internal bool Arrived => Invoke<bool>("Arrived");
    internal RunStatus Interact() => Invoke<RunStatus>("Interact");
}
