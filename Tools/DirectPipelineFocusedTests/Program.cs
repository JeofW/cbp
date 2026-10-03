using System.Reflection;
using System.Runtime.ExceptionServices;

if (IntPtr.Size != 4 || Styx.WoWInternals.ObjectManager.Executor != null)
    throw new InvalidOperationException("Windows/x86 without a game executor is required");
var assembly = Assembly.Load("WholesomeQuestRecoveryRegressionTests");
string family = args.Length > 0 ? args[0] : "DirectGameObjectPipelineRegressionTests";
int repeats = args.Length > 1 ? int.Parse(args[1]) : 3;
if (family is not ("DirectGameObjectPipelineRegressionTests" or "QuestInventorySnapshotRegressionTests" or "QuestFixtureIsolationRegressionTests" or "RecoveryActionFlushCount")
    || repeats < 1 || repeats > (family == "RecoveryActionFlushCount" ? 100 : 20)) throw new ArgumentOutOfRangeException(nameof(args));
MethodInfo method;
object? target = null;
if (family == "RecoveryActionFlushCount")
{
    method = assembly.GetTypes().SelectMany(type => type.GetMethods(BindingFlags.Instance | BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public))
        .Single(value => value.Name.Contains("g__TestRecoveryActionFlushCountTracksActualMutations|", StringComparison.Ordinal));
    if (method.IsStatic || method.GetParameters().Length != 0)
        throw new InvalidOperationException("The actual recovery test closure changed; review its captured state.");
    target = Activator.CreateInstance(method.DeclaringType!, nonPublic: true)!;
    var clock = method.DeclaringType!.GetField("utcNow", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!;
    if (clock.FieldType != typeof(DateTime)) throw new InvalidOperationException("The recovery test clock contract changed.");
    clock.SetValue(target, new DateTime(2026, 9, 3, 0, 0, 0, DateTimeKind.Utc));
}
else
    method = assembly.GetType(family, true)!.GetMethod("Run", BindingFlags.NonPublic | BindingFlags.Static)!;
for (int repeat = 1; repeat <= repeats; repeat++)
{
    Console.WriteLine($"BEGIN focused {family} iteration {repeat}/{repeats}");
    try { method.Invoke(target, null); }
    catch (TargetInvocationException error) when (error.InnerException != null)
    { ExceptionDispatchInfo.Capture(error.InnerException).Throw(); throw; }
}
Console.WriteLine($"Focused {family}: {repeats}/{repeats} complete iterations; actual allocated observation owners, no game attached.");
