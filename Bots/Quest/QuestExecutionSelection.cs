using System;
using System.Collections.Generic;
using Bots.Quest.QuestOrder;
using Styx.Logic.BehaviorTree;
using Styx.Logic.Profiles;

namespace Bots.Quest;

/// <summary>
/// A read-only selection of the currently executing quest leaf and its complete
/// conditional ancestry. Resolving work does not evaluate or initialize a branch.
/// Every continuation validates the same selected orders, nodes and behaviors.
/// </summary>
internal sealed class QuestExecutionSelection
{
    private sealed record Frame(QuestOrder.QuestOrder Order, object Nodes, object? Node,
        ForcedBehavior? Behavior, QuestOrder.QuestOrder? Child);

    private readonly QuestOrder.QuestOrder root;
    private readonly object run;
    private readonly object? profile;
    private readonly List<Frame> frames;

    private QuestExecutionSelection(QuestOrder.QuestOrder root, List<Frame> frames)
    {
        this.root = root;
        this.frames = frames;
        run = TreeRoot.RunIdentity;
        profile = ProfileManager.CurrentProfileSnapshot;
    }

    internal ForcedBehavior? Behavior => frames[^1].Behavior;
    internal object? Node => frames[^1].Node;

    internal bool Current
    {
        get
        {
            if (!ReferenceEquals(QuestState.Instance.Order, root)
                || !ReferenceEquals(TreeRoot.RunIdentity, run)
                || !ReferenceEquals(ProfileManager.CurrentProfileSnapshot, profile)) return false;
            foreach (Frame frame in frames)
            {
                if (!ReferenceEquals(frame.Order.Nodes, frame.Nodes)
                    || !ReferenceEquals(frame.Order.CurrentNode, frame.Node)
                    || !ReferenceEquals(frame.Order.CurrentBehavior, frame.Behavior)) return false;
                if (frame.Behavior is ForcedIf conditional
                    && (!ReferenceEquals(conditional.IfNode, frame.Node) || !ReferenceEquals(conditional.ActiveOrder, frame.Child))) return false;
                if (frame.Behavior is ForcedWhile loop
                    && (!ReferenceEquals(loop.WhileNode, frame.Node) || !ReferenceEquals(loop.ActiveOrder, frame.Child))) return false;
            }
            return true;
        }
    }

    internal static QuestExecutionSelection? Capture()
    {
        var root = QuestState.Instance.Order;
        var order = root;
        var frames = new List<Frame>();
        var seen = new HashSet<QuestOrder.QuestOrder>();
        for (int depth = 0; depth < 64 && order != null && seen.Add(order); depth++)
        {
            var nodes = order.Nodes;
            var node = order.CurrentNode;
            var behavior = order.CurrentBehavior;
            if (nodes == null || node == null) return null;
            QuestOrder.QuestOrder? child = null;
            if (behavior is ForcedIf conditional)
            {
                if (!ReferenceEquals(conditional.IfNode, node)) return null;
                child = conditional.ActiveOrder;
            }
            else if (behavior is ForcedWhile loop)
            {
                if (!ReferenceEquals(loop.WhileNode, node)) return null;
                child = loop.ActiveOrder;
            }
            frames.Add(new Frame(order, nodes, node, behavior, child));
            if (child == null)
            {
                var selection = new QuestExecutionSelection(root, frames);
                return selection.Current ? selection : null;
            }
            order = child;
        }
        return null; // A cycle or excessive nesting has no selected leaf.
    }
}
