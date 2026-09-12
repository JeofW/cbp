using System.Runtime.CompilerServices;
using System.Reflection;
using Bots.Grind;
using CommonBehaviors.Actions;
using Styx.Logic.Pathing;
using Styx.Logic;
using TreeSharp;

internal static class LootLatencyRegressionTests
{
    [ModuleInitializer]
    internal static void Run()
    {
        TestFlightorHonorsGroundMountDistance();
        TestCancelledMountRequestStopsBeforeCasting();
        TestConfirmedLootReturnsWithoutPostClearAction();
    }

    private static void TestConfirmedLootReturnsWithoutPostClearAction()
    {
        FieldInfo attachedField = typeof(LevelBot).GetField(
            "_lootEventsAttached",
            BindingFlags.Static | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("LevelBot loot event guard was not found");
        bool wasAttached = (bool)attachedField.GetValue(null)!;
        attachedField.SetValue(null, true);

        Composite lootBehavior;
        try
        {
            lootBehavior = LevelBot.CreateLootBehavior();
        }
        finally
        {
            attachedField.SetValue(null, wasAttached);
        }

        FieldInfo reasonField = typeof(ActionClearPoi).GetField(
            "_reason",
            BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("ActionClearPoi reason field was not found");
        ActionClearPoi confirmedLootClear = Descendants(lootBehavior)
            .OfType<ActionClearPoi>()
            .Single(action => (string)reasonField.GetValue(action)! == "Waiting for loot flag");
        GroupComposite completionSequence = confirmedLootClear.Parent as GroupComposite
            ?? throw new InvalidOperationException("confirmed loot clear must belong to a sequence");

        Assert(completionSequence.Children.IndexOf(confirmedLootClear)
               == completionSequence.Children.Count - 1,
            "confirmed loot must return on the next pulse immediately after clearing its POI");
    }

    private static IEnumerable<Composite> Descendants(Composite root)
    {
        yield return root;
        if (root is not GroupComposite group)
            yield break;
        foreach (Composite child in group.Children)
        foreach (Composite descendant in Descendants(child))
            yield return descendant;
    }

    private static void TestFlightorHonorsGroundMountDistance()
    {
        Assert(!Flightor.ShouldAttemptGroundMount(
                   canFly: false,
                   mounted: false,
                   mountPolicyAllows: false),
            "short ground destinations must remain on foot");
        Assert(Flightor.ShouldAttemptGroundMount(
                   canFly: false,
                   mounted: false,
                   mountPolicyAllows: true),
            "eligible long ground destinations should still mount");
        Assert(!Flightor.ShouldAttemptGroundMount(
                   canFly: true,
                   mounted: false,
                   mountPolicyAllows: true),
            "the ground fallback policy must not intercept flying behavior");
    }

    private static void TestCancelledMountRequestStopsBeforeCasting()
    {
        EventHandler<MountUpEventArgs> cancel = (_, args) => args.Cancel = true;
        Mount.OnMountUp += cancel;
        try
        {
            Assert(!Mount.AllowMountAttempt(
                    isFlying: false,
                    mountName: "ground mount",
                    destination: new Styx.Logic.Pathing.WoWPoint(100f, 0f, 0f)),
                "stuck recovery must reject a remount before the cast begins");
        }
        finally
        {
            Mount.OnMountUp -= cancel;
        }
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }
}
