using System.Reflection;
using WholesomeAQ;

// Focused execution of the exact normalized regression groups. This does not
// replace the complete aggregate gate and does not modify any assertion/fixture.
if (!OperatingSystem.IsWindows() || IntPtr.Size != 4)
    throw new PlatformNotSupportedException("The actual owners require Windows x86.");
if (args.Length == 0)
    throw new ArgumentException("Supply one or more exact regression group names.");
var boundary = Assembly.Load("fasmdll_managed");
if (!boundary.GetCustomAttributes<AssemblyMetadataAttribute>().Any(x =>
    x.Key == "OfflineBoundary" && x.Value == "deny-native-dispatch"))
    throw new InvalidOperationException("The focused output must install the existing deny-native-dispatch test boundary.");

var assembly = typeof(WholesomeAutoQuest).Assembly;
int passed = 0;
foreach (string name in args.Distinct(StringComparer.Ordinal))
{
    if (!name.EndsWith("RegressionTests", StringComparison.Ordinal) || name.Contains('.'))
        throw new ArgumentException("An exact top-level regression group is required.");
    var method = assembly.GetType(name, throwOnError: true)!.GetMethod("Run",
        BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic,
        binder: null, types: Type.EmptyTypes, modifiers: null)
        ?? throw new MissingMethodException(name, "Run");
    Console.WriteLine("BEGIN focused group: " + name);
    try
    {
        method.Invoke(null, null);
        Console.WriteLine("PASS focused group: " + name);
        passed++;
    }
    catch (TargetInvocationException error) when (error.InnerException != null)
    {
        Console.Error.WriteLine("FAIL focused group: " + name + ": " + error.InnerException);
        Environment.ExitCode = 1;
    }
}
Console.WriteLine($"Focused groups: {passed}/{args.Distinct(StringComparer.Ordinal).Count()}; no game attached; full aggregate remains required.");
