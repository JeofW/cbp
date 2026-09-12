// Decompiled with JetBrains decompiler
// Type: Bots.Quest.QuestOrder.ForcedCodeBehavior
// Assembly: Honorbuddy, Version=2.0.0.5999, Culture=neutral, PublicKeyToken=50a565ab5c01ae50
// MVID: FB7FEB85-27C0-4D17-B8DE-615FDFDA7752
// Assembly location: C:\Users\Texy6\Desktop\Honorbuddy-cleaned.exe

using Styx.Helpers;
using Styx.Logic.Profiles.Quest;
using Styx.Logic.Questing;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using TreeSharp;

#nullable disable
namespace Bots.Quest.QuestOrder;

public class ForcedCodeBehavior : ForcedBehavior
{
    private readonly CustomForcedBehavior customBehavior;
    private QuestConditionEvaluationState _doneState = QuestConditionEvaluationState.False;
    private bool _started;

    public ForcedCodeBehavior(CodeNode codeNode)
    {
        if (codeNode == null)
            throw new ArgumentNullException(nameof(codeNode));
        // Set PendingElement before construction so the behavior ctor can read CDATA from Element.
        CustomForcedBehavior.PendingElement = codeNode.Element;
        try
        {
            this.customBehavior = ForcedCodeBehavior.CreateCustomBehaviorInstance(codeNode.AssemblyGetter(), codeNode.Arguments);
        }
        finally
        {
            CustomForcedBehavior.PendingElement = null;
        }
        if (this.customBehavior == null)
            throw new Exception("Unable to create instance of UserDefinedObjective");
        // Ensure Element is set even if the ctor somehow missed it.
        if (this.customBehavior.Element == null)
            this.customBehavior.Element = codeNode.Element;
        ProfileBatchManager.Register(this.customBehavior);
    }

    private static CustomForcedBehavior CreateCustomBehaviorInstance(
        Assembly assembly,
        Dictionary<string, string> arguments)
    {
        if (arguments == null)
        {
            arguments = new Dictionary<string, string>();
        }
        return ((IEnumerable<Type>)assembly.GetTypes())
            .Where<Type>((Func<Type, bool>)(behaviorType => behaviorType.IsSubclassOf(typeof(CustomForcedBehavior))))
            .Select<Type, CustomForcedBehavior>((Func<Type, CustomForcedBehavior>)(behaviorType =>
            {
                try
                {
                    return (CustomForcedBehavior)Activator.CreateInstance(behaviorType, new object[] { arguments });
                }
                catch
                {
                    return null;
                }
            }))
            .FirstOrDefault<CustomForcedBehavior>();
    }

    protected override Composite CreateBehavior() => this.customBehavior.Branch;

    public override bool IsDone
    {
        get
        {
            _doneState = QuestConditionEvaluation.Evaluate(() => this.customBehavior.IsDone);
            return _doneState == QuestConditionEvaluationState.True;
        }
    }

    public override bool IsExecutionDeferred => _doneState == QuestConditionEvaluationState.Unknown;

    public override bool SuppressServiceBehavior =>
        string.Equals(customBehavior.GetType().Name, "UseTransport", StringComparison.Ordinal);

    public override void OnStart()
    {
        ProfileBatchManager.EnsureCompiled();
        _doneState = QuestConditionEvaluation.Evaluate(() => this.customBehavior.IsDone);
        if (_doneState == QuestConditionEvaluationState.Unknown)
            return;

        this.StartCustomBehavior();
    }

    public override void OnTick()
    {
        if (!_started)
            this.StartCustomBehavior();
        this.customBehavior.OnTick();
    }

    private void StartCustomBehavior()
    {
        if (_started)
            return;

        Logging.Write("[Code] Executing custom behavior: {0}", (object)this.customBehavior.GetType().Name);
        this.customBehavior.OnStart();
        _started = true;
    }

    public override void Dispose() => this.customBehavior.Dispose();
}
