using System.Reflection;
using System.Runtime.ExceptionServices;

if (IntPtr.Size != 4 || Styx.WoWInternals.ObjectManager.Executor != null)
    throw new InvalidOperationException("Windows/x86 without a game executor is required");
var assembly = Assembly.Load("WholesomeQuestRecoveryRegressionTests");
string family = args.Length > 0 ? args[0] : "DirectGameObjectPipelineRegressionTests";
int repeats = args.Length > 1 ? int.Parse(args[1]) : 3;
if (family is not ("DirectGameObjectPipelineRegressionTests" or "QuestInventorySnapshotRegressionTests")
    || repeats < 1 || repeats > 20) throw new ArgumentOutOfRangeException(nameof(args));
var method = assembly.GetType(family, true)!.GetMethod("Run", BindingFlags.NonPublic | BindingFlags.Static)!;
for (int repeat = 1; repeat <= repeats; repeat++)
{
    Console.WriteLine($"BEGIN focused {family} iteration {repeat}/{repeats}");
    try { method.Invoke(null, null); }
    catch (TargetInvocationException error) when (error.InnerException != null)
    { ExceptionDispatchInfo.Capture(error.InnerException).Throw(); throw; }
}
Console.WriteLine($"Focused {family}: {repeats}/{repeats} complete iterations; actual allocated observation owners, no game attached.");
