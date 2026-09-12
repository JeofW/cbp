// Decompiled with JetBrains decompiler
// Type: Bots.Quest.QuestOrder.ForcedIf
// Assembly: Honorbuddy, Version=2.0.0.5999, Culture=neutral, PublicKeyToken=50a565ab5c01ae50
// MVID: FB7FEB85-27C0-4D17-B8DE-615FDFDA7752
// Assembly location: C:\Users\Texy6\Desktop\Honorbuddy-cleaned.exe

using Bots.Quest.Actions;
using Styx.Helpers;
using Styx.Logic.BehaviorTree;
using Styx.Logic.Profiles.Quest;
using System;
using System.Collections.Generic;
using System.Drawing;
using TreeSharp;

#nullable disable
namespace Bots.Quest.QuestOrder;

public class ForcedIf : ForcedBehavior
{
    private QuestOrder conditionalOrder;
    private Composite behaviorExecutor;
    private bool conditionResolved;

    public ForcedIf(IfNode node)
    {
        this.IfNode = node != null ? node : throw new ArgumentNullException(nameof(node));
    }

    public IfNode IfNode { get; private set; }

    protected override Composite CreateBehavior()
    {
        return this.behaviorExecutor ??= new ConditionalComposite(this);
    }

    public override void OnStart()
    {
        try
        {
            this.TryInitializeConditionalOrder();
        }
        catch (Exception ex)
        {
            HandleConditionException(ex);
        }
    }

    private bool TryInitializeConditionalOrder()
    {
        if (this.conditionResolved)
            return true;

        QuestConditionEvaluationState condition = QuestConditionEvaluation.Evaluate(this.IfNode.Condition.CallableExpression);
        if (condition == QuestConditionEvaluationState.Unknown)
            return false;

        OrderNodeCollection selectedBody = null;
        if (condition == QuestConditionEvaluationState.True)
        {
            Logging.WriteDiagnostic("[If] Condition is true, executing If body");
            selectedBody = this.IfNode.Body;
        }
        else
        {
            foreach (ElseIf elseIf in this.IfNode.ElseIfs)
            {
                QuestConditionEvaluationState elseIfCondition = QuestConditionEvaluation.Evaluate(elseIf.Condition.CallableExpression);
                if (elseIfCondition == QuestConditionEvaluationState.Unknown)
                    return false;
                if (elseIfCondition != QuestConditionEvaluationState.True)
                    continue;

                Logging.WriteDiagnostic("[ElseIf] Condition matched, executing ElseIf body");
                selectedBody = elseIf.Body;
                break;
            }

            if (selectedBody == null && this.IfNode.Else != null)
            {
                Logging.WriteDiagnostic("[Else] No conditions matched, executing Else body");
                selectedBody = this.IfNode.Else.Body;
            }
        }

        this.conditionResolved = true;
        if (selectedBody == null)
            return true;

        this.conditionalOrder = new QuestOrder(new OrderNodeCollection((IEnumerable<OrderNode>)selectedBody))
        {
            IgnoreCheckpoints = QuestState.Instance.Order.IgnoreCheckpoints
        };
        this.conditionalOrder.UpdateNodes();
        return true;
    }

    public override bool IsDone
    {
        get
        {
            return this.conditionResolved &&
                   (this.conditionalOrder == null || this.conditionalOrder.Nodes == null || this.conditionalOrder.Nodes.Count <= 0);
        }
    }

    private static void HandleConditionException(Exception ex)
    {
        Logging.Write(Color.Red, "Unable to evaluate compile condition in If tag. Please check your profile.");
        Logging.Write(Color.Red, "CopilotBuddy stopped!");
        Logging.WriteException(ex);
        TreeRoot.Stop();
    }

    private sealed class ConditionalComposite : Composite
    {
        private readonly ForcedIf owner;
        private ForcedBehaviorExecutor executor;

        public ConditionalComposite(ForcedIf owner) => this.owner = owner;

        protected override IEnumerable<RunStatus> Execute(object context)
        {
            while (!this.owner.conditionResolved)
            {
                bool initialized = false;
                bool evaluationFailed = false;
                try
                {
                    initialized = this.owner.TryInitializeConditionalOrder();
                }
                catch (Exception ex)
                {
                    HandleConditionException(ex);
                    evaluationFailed = true;
                }

                if (evaluationFailed)
                {
                    yield return RunStatus.Failure;
                    yield break;
                }
                if (!initialized)
                {
                    yield return RunStatus.Running;
                }
            }

            if (this.owner.conditionalOrder == null)
            {
                yield return RunStatus.Success;
                yield break;
            }

            this.executor ??= new ForcedBehaviorExecutor(this.owner.conditionalOrder);
            this.executor.Start(context);
            while (this.executor.Tick(context) == RunStatus.Running)
                yield return RunStatus.Running;
            this.executor.Stop(context);
            yield return this.executor.LastStatus ?? RunStatus.Failure;
        }
    }
}
