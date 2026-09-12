using System.Xml.Linq;
using Styx.Logic.Profiles;
using Styx.Logic.Profiles.Quest;
using Styx.Logic.Questing;
using Styx.Logic.Pathing;

internal static class UpstreamMergeRegression
{
    public static void Run()
    {
        var composition = new CodeComposition();
        var nodes = Enumerable.Range(0, 200).Select(i => IfNode.FromXml(
            XElement.Parse($"<If Condition='Me == null &amp;&amp; {i} == {i}' />"))).ToArray();
        foreach (var node in nodes) composition.Add(node);
        var nested = IfNode.FromXml(XElement.Parse("<If Condition='false'><ElseIf Condition='true'><While Condition='false'/></ElseIf><Else><GrindTo Condition='true'/></Else></If>"));
        composition.Add(nested);
        var setVendor = SetVendorNode.FromXml(XElement.Parse("<SetVendor><Vendor Name='Conditional' Entry='4' Type='Food' X='1' Y='2' Z='3' UsableWhen='true'/></SetVendor>"));
        var setMailbox = SetMailboxNode.FromXml(XElement.Parse("<SetMailbox><Mailbox X='1' Y='2' Z='3' UsableWhen='false'/></SetMailbox>"));
        composition.Add(setVendor);
        composition.Add(setMailbox);
        var loopNode = WhileNode.FromXml(XElement.Parse("<While Condition='!IsQuestCompleted(867)'/>"));
        var grindNode = GrindToNode.FromXml(XElement.Parse("<GrindTo Condition='!IsQuestCompleted(867)'/>"));
        composition.Add(loopNode);
        composition.Add(grindNode);
        var completionNode = IfNode.FromXml(XElement.Parse("<If Condition='!IsQuestCompleted(867)'/>"));
        composition.Add(completionNode);
        var vendors = new VendorManager(XElement.Parse("<Vendors><Vendor Name='Blocked' Entry='1' Type='Food' X='1' Y='2' Z='3' UsableWhen='false'/><Vendor Name='Allowed' Entry='2' Type='Food' X='1' Y='2' Z='3' UsableWhen='true'/><Vendor Name='Unknown' Entry='3' Type='Food' X='1' Y='2' Z='3' UsableWhen='!IsQuestCompleted(867)'/></Vendors>"));
        Check(vendors.AllVendors.Count == 3, "UsableWhen vendors must parse without being dropped.");
        foreach (var vendor in vendors.AllVendors) composition.Add(vendor);
        var mailboxes = new MailboxManager(XElement.Parse("<Mailboxes><Mailbox X='1' Y='2' Z='3' UsableWhen='!IsQuestCompleted(867)'/><Mailbox X='10' Y='2' Z='3' UsableWhen='true'/></Mailboxes>"));
        foreach (var mailbox in mailboxes.AllMailboxes) composition.Add(mailbox);
        CompileBatch batch = composition.Batch;
        Check(batch.Compile(), "Merged profile batch must compile: " + string.Join("; ", batch.Errors.Select(e => e.Error)));
        Check(setVendor.Vendors[0].UsableWhen.IsCompiled && setVendor.Vendors[0].UsableWhen.CallableExpression(),
            "SetVendor guards must be compiled before activation.");
        Check(setMailbox.Mailboxes[0].UsableWhen.IsCompiled && !setMailbox.Mailboxes[0].UsableWhen.CallableExpression(),
            "SetMailbox guards must be compiled before activation.");
        foreach (IfNode node in nodes)
            Check(node.Condition.CallableExpression(), "Every batch condition must be bound.");
        Check(nodes.Cast<IfNode>().Select(n => n.Condition.CompiledExpression.Method.Module.Assembly).Distinct().Count() == 1,
            "All 200 conditions must share one compiled assembly.");
        var branch = (IfNode)nested;
        Check(branch.ElseIfs[0].Condition.CallableExpression(), "Nested ElseIf must be bound.");
        var unknown = QuestConditionEvaluation.Evaluate(() =>
        {
            var forcedIf = new Bots.Quest.QuestOrder.ForcedIf((IfNode)completionNode);
            forcedIf.OnStart();
            Check(!forcedIf.IsDone, "Batched If must wait while completion is unknown.");
            var grind = new Bots.Quest.QuestOrder.ForcedGrindTo((GrindToNode)grindNode);
            grind.OnStart();
            Check(grind.IsExecutionDeferred && !grind.IsDone, "Batched GrindTo must defer unknown completion.");
            var loop = new Bots.Quest.QuestOrder.ForcedWhile((WhileNode)loopNode);
            var context = new object();
            loop.Branch.Start(context);
            Check(loop.Branch.Tick(context) == TreeSharp.RunStatus.Running &&
                loop.Branch.Tick(context) == TreeSharp.RunStatus.Running && !loop.IsDone,
                "Batched While must keep running while completion is unknown.");
            loop.Branch.Stop(context);
            Check(vendors.Vendors[Vendor.VendorType.Food].Select(v => v.Entry).SequenceEqual(new[] { 2 }),
                "Vendor guards must exclude false AND unknown completion conditions.");
            Check(mailboxes.GetClosestMailbox(new WoWPoint(0, 0, 0)) == mailboxes.AllMailboxes[1],
                "Mailbox guards must exclude unknown completion conditions.");
            return true;
        }, _ => QuestCompletionState.Unknown);
        Check(unknown == QuestConditionEvaluationState.Unknown, "Unknown guard state must propagate.");
        QuestConditionEvaluation.Evaluate(() =>
        {
            Check(vendors.Vendors[Vendor.VendorType.Food].Count() == 2, "Known-incomplete vendor must become usable.");
            return true;
        }, _ => QuestCompletionState.KnownIncomplete);
        vendors.Blacklist.Add(vendors.AllVendors[1]);
        QuestConditionEvaluation.Evaluate(() =>
        {
            Check(!vendors.Vendors[Vendor.VendorType.Food].Any(), "Blacklist must still combine with eligibility guards.");
            return true;
        }, _ => QuestCompletionState.KnownComplete);
        Console.WriteLine("PASS: upstream batch binding, nested nodes, vendor/mailbox guards and local unknown-completion protection");
    }
    private static void Check(bool value, string message)
    {
        if (!value) throw new InvalidOperationException(message);
    }
}
