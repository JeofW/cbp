#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using CommonBehaviors.Actions;
using Styx;
using Styx.Helpers;
using Styx.Logic;
using Styx.Logic.Pathing;
using Styx.Logic.Profiles.Quest;
using Styx.Logic.Questing;
using Styx.WoWInternals;
using Styx.WoWInternals.WoWObjects;
using TreeSharp;

namespace Bots.Quest.Objectives;

public class UseGameObjectObjective : QuestObjective
{
    private QuestGameObjectInteraction? _interaction;

    public UseGameObjectObjective(PlayerQuest quest, List<WoWQuestStep> questSteps,
        Styx.Logic.Questing.Quest.QuestObjective objective, List<QuestObjective> prerequisites)
        : base(quest, questSteps, prerequisites)
    {
        Objective = objective;
    }

    public Styx.Logic.Questing.Quest.QuestObjective Objective { get; private set; }

    public override bool IsCompleted => QuestObjectiveCompletion.IsTypedNormalObjectiveComplete(Quest, Objective);

    public override bool CanComplete
    {
        get
        {
            if (!DonePrerequisites) return false;
            if (OverridedQuestInfo != null)
            {
                UseObjectObjectiveInfo value = OverridedQuestInfo.FindUseGameObject((uint)Objective.ID);
                if (value != null && value.OverridedHotspots != null && value.OverridedHotspots.Count > 0) return true;
            }
            if (!QuestArea.HotspotsCreated) QuestArea.CreateHotspots();
            return QuestArea.Hotspots.Count > 0;
        }
    }

    internal bool IsCurrentGameObject(WoWGameObject? subject)
    {
        if (!ValidSource(subject)) return false;
        return ObjectManager.GetObjectsOfType<WoWGameObject>()
            .Any(candidate => ReferenceEquals(candidate, subject) && ValidSource(candidate));
    }

    private bool ValidSource(WoWGameObject? subject) => subject != null && subject.BaseAddress != 0
        && subject.IsValid && !subject.IsDisabled && subject.Type == WoWObjectType.GameObject
        && subject.Guid != 0 && subject.DescriptorGuid == subject.Guid
        && subject.Entry == (uint)Objective.ID && Finite(subject.Location);

    internal WoWGameObject? FindCurrentGameObject(IEnumerable<ulong>? excluded = null)
    {
        var actor = ObjectManager.Me;
        if (actor == null || !actor.IsValid || actor.Guid == 0) return null;
        var denied = excluded == null ? new HashSet<ulong>() : new HashSet<ulong>(excluded);
        return ObjectManager.GetObjectsOfType<WoWGameObject>()
            .Where(candidate => ValidSource(candidate) && !Blacklist.Contains(candidate.Guid) && !denied.Contains(candidate.Guid))
            .OrderBy(candidate => actor.Location.DistanceSqr(candidate.Location)).FirstOrDefault();
    }

    public override WoWPoint GetObjectiveLocation()
    {
        var actor = ObjectManager.Me;
        if (actor == null || !actor.IsValid || actor.Guid == 0) return WoWPoint.Empty;
        var live = FindCurrentGameObject();
        if (live != null) return live.Location;
        UseObjectObjectiveInfo? definition = OverridedQuestInfo?.FindUseGameObject((uint)Objective.ID);
        if (definition?.OverridedHotspots != null && definition.OverridedHotspots.Count > 0)
            return definition.OverridedHotspots.FindClosestTo(actor.Location);
        if (!QuestArea.HotspotsCreated) QuestArea.CreateHotspots();
        if (QuestArea.Hotspots.Count > 0)
            return QuestArea.Hotspots.Select(hotspot => hotspot.Position).Where(Finite)
                .OrderBy(point => actor.Location.DistanceSqr(point)).FirstOrDefault();
        var step = GetClosestQuestStep().StepPosition;
        Vector3 fallback = new Vector3(step.X, step.Y, 0);
        if (fallback == Vector3.Zero || !float.IsFinite(fallback.X) || !float.IsFinite(fallback.Y)) return WoWPoint.Empty;
        if (!MeshHeightHelper.FindMeshHeight(ref fallback)) return WoWPoint.Empty;
        return new WoWPoint(fallback.X, fallback.Y, fallback.Z);
    }

    public QuestGameObjectInteraction.Observation? ExecutionObservation => _interaction?.LastObservation;

    public override Composite CreateBranch() => _interaction ??= new QuestGameObjectInteraction(this);

    private static bool Finite(WoWPoint point) => point != WoWPoint.Zero
        && float.IsFinite(point.X) && float.IsFinite(point.Y) && float.IsFinite(point.Z);
}
