using System.Reflection;

int failed = 0, passed = 0;
foreach (var type in Assembly.GetExecutingAssembly().GetTypes()
    .Where(type => type.Name.StartsWith("Integrated", StringComparison.Ordinal) && type.Name.EndsWith("Tests", StringComparison.Ordinal))
    .OrderBy(type => type.Name, StringComparer.Ordinal))
{
    try
    {
        type.GetMethod("Run", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, null);
        passed++;
    }
    catch (TargetInvocationException error)
    {
        failed++;
        Console.Error.WriteLine(type.Name + ": " + error.InnerException);
    }
}
Console.WriteLine($"Integrated combat and recovery groups: {passed}/{passed + failed}; failed={failed}.");
Environment.ExitCode = failed == 0 ? 0 : 1;
