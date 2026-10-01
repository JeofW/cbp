using System;
using System.Collections.Generic;
using Styx.Logic.Questing.Recovery;

namespace WholesomeAQ
{
    public class VendorDatabase
    {
        public List<VendorEntry> Vendors { get; set; } = new List<VendorEntry>();
    }

    public class VendorEntry
    {
        public string Type { get; set; }
        public int Entry { get; set; }
        public string Name { get; set; }
        public string TrainClass { get; set; }
        public double X { get; set; }
        public double Y { get; set; }
        public double Z { get; set; }
        public int Map { get; set; }
    }

    public enum QuestDatasetSourceStatus
    {
        Unknown,
        DeclaredAndBound
    }

    public sealed class QuestDatasetSourceIdentity
    {
        public QuestDatasetSourceStatus Status { get; init; } = QuestDatasetSourceStatus.Unknown;
        public int ClientBuild { get; init; }
        public string SourceCore { get; init; } = "unknown";
        public string SourceBranch { get; init; } = "";
        public string CoreRevision { get; init; } = "";
        public string DatabaseRevision { get; init; } = "";
        public string Exporter { get; init; } = "";
        public string ExporterVersion { get; init; } = "";
        public string QuestDataSha256 { get; init; } = "";
        public bool RealmOverridesDeclared { get; init; }
    }

    public enum QuestStrategyPackStatus
    {
        Missing,
        DeclaredAndBound
    }

    public enum QuestStrategyKind
    {
        UseItemOn,
        GossipEvent,
        Escort
    }

    public enum QuestStrategyTargetType
    {
        Creature,
        GameObject
    }

    public enum QuestStrategyTargetState
    {
        Alive,
        Dead,
        BelowHp,
        DontCare
    }

    public enum QuestStrategySuccessEvidence
    {
        ObjectiveProgress,
        QuestComplete
    }

    public sealed class QuestStrategyRecipe
    {
        public int CreditId { get; init; }
        public int CreditCount { get; init; }
        public int WaitTime { get; init; }
        public int QuestId { get; init; }
        public int ObjectiveIndex { get; init; }
        public QuestStrategyKind Kind { get; init; }
        public string SourceRef { get; init; } = "";
        public int ItemId { get; init; }
        public QuestStrategyTargetType TargetType { get; init; }
        public int TargetId { get; init; }
        public QuestStrategyTargetState TargetState { get; init; } = QuestStrategyTargetState.DontCare;
        public double Range { get; init; }
        public bool RequireLos { get; init; }
        public int MaxAttempts { get; init; }
        public int GossipOptionIndex { get; init; } = -1;
        public QuestStrategySuccessEvidence SuccessEvidence { get; init; }
    }

    public sealed class QuestStrategyPack
    {
        public int SchemaVersion { get; init; } = 1;
        public string QuestDataRepairsSha256 { get; init; } = "";
        public QuestStrategyPackStatus Status { get; init; } = QuestStrategyPackStatus.Missing;
        public int ClientBuild { get; init; }
        public string QuestDataSha256 { get; init; } = "";
        public string SourceKind { get; init; } = "";
        public string SourceRevision { get; init; } = "";
        public List<QuestStrategyRecipe> Recipes { get; init; } = new List<QuestStrategyRecipe>();
    }

    public class QuestDatabase
    {
        [System.Text.Json.Serialization.JsonIgnore]
        public IReadOnlyDictionary<int, QuestDependencyMetadata> DependencyMetadata { get; internal set; }
            = new Dictionary<int, QuestDependencyMetadata>();

        // Validated repair-pack metadata only; never a placeholder spawn or a
        // source of authoritative observations supplied through base JSON.
        [System.Text.Json.Serialization.JsonIgnore]
        public IReadOnlyList<QuestCreditSource> ObjectiveCreditSources { get; internal set; }
            = Array.Empty<QuestCreditSource>();
        public List<QuestEntry> Quests { get; set; } = new List<QuestEntry>();
        public List<QuestGiverEntry> QuestGivers { get; set; } = new List<QuestGiverEntry>();
        public List<QuestEnderEntry> QuestEnders { get; set; } = new List<QuestEnderEntry>();
        public Dictionary<string, List<SpawnPoint>> CreatureSpawns { get; set; } = new Dictionary<string, List<SpawnPoint>>();
        public Dictionary<string, List<SpawnPoint>> GameObjectSpawns { get; set; } = new Dictionary<string, List<SpawnPoint>>();
    }

    public class ZoneQuestData
    {
        public int ZoneId { get; set; }
        public string ZoneName { get; set; }
        public List<int> SubzoneIds { get; set; } = new List<int>();
        public ZoneBoundary Boundary { get; set; }
        public List<QuestEntry> Quests { get; set; } = new List<QuestEntry>();
        public List<QuestGiverEntry> QuestGivers { get; set; } = new List<QuestGiverEntry>();
        public List<QuestEnderEntry> QuestEnders { get; set; } = new List<QuestEnderEntry>();
        public Dictionary<string, List<SpawnPoint>> CreatureSpawns { get; set; } = new Dictionary<string, List<SpawnPoint>>();
        public Dictionary<string, List<SpawnPoint>> GameObjectSpawns { get; set; } = new Dictionary<string, List<SpawnPoint>>();
    }

    public class ZoneBoundary
    {
        public double X1 { get; set; }
        public double X2 { get; set; }
        public double Y1 { get; set; }
        public double Y2 { get; set; }
    }

    public class QuestEntry
    {
        // Only a validated repair pack may supply this server-side contract.
        // Base JSON cannot inject availability predicates or observed quest state.
        [System.Text.Json.Serialization.JsonIgnore]
        public QuestAvailabilityContract AvailabilityConditions { get; internal set; }
        // Additional required materials with no acquisition owner. Only a
        // validated source-bound repair may declare them; never observed stock.
        [System.Text.Json.Serialization.JsonIgnore]
        public IReadOnlyList<QuestItemRequirement> RequiredStockItems { get; internal set; }
        // Null means no source-bound delivery contract. These are requirements,
        // never a claim that acceptance actually supplied an item to this actor.
        public List<QuestItemRequirement> DeliveryItems { get; set; }
        public List<QuestItemRequirement> AcceptanceSupplies { get; set; }
        public QuestSupplementalSupply SupplementalSupply { get; set; }
        public int Id { get; set; }
        public string Name { get; set; }
        public int QuestLevel { get; set; }
        public int MinLevel { get; set; }
        public int AllowableRaces { get; set; }
        // Absent source fields stay unknown. Zero is an explicit unconstrained
        // value, not a substitute for omitted server-side eligibility metadata.
        public int? AllowableClasses { get; set; }
        public int? MaxLevel { get; set; }
        public int? RequiredSkillID { get; set; }
        public int? RequiredSkillPoints { get; set; }
        public int? RequiredMinRepFaction { get; set; }
        public int? RequiredMinRepValue { get; set; }
        public int? RequiredMaxRepFaction { get; set; }
        public int? RequiredMaxRepValue { get; set; }
        public int? RequiredFactionValue1 { get; set; }
        public int? RequiredFactionValue2 { get; set; }
        public int Flags { get; set; }
        public int QuestSortID { get; set; }
        public int QuestInfoID { get; set; }
        public int RequiredFactionId1 { get; set; }
        public int RequiredFactionId2 { get; set; }
        public int PrevQuestID { get; set; }
        public int NextQuestID { get; set; }
        public int ExclusiveGroup { get; set; }
        public int SpecialFlags { get; set; }
        public int StartItem { get; set; }
        public List<int> PreviousQuestsIds { get; set; } = new List<int>();
        public List<QuestObjective> Objectives { get; set; } = new List<QuestObjective>();
    }

    public class QuestObjective
    {
        public ObjectiveType Type { get; set; }
        public int MobId { get; set; }
        public int ItemId { get; set; }
        public int GameObjectId { get; set; }
        public string GameObjectName { get; set; }
        public int KillCount { get; set; }
        public int CollectCount { get; set; }
        public int Index { get; set; }
    }

    public enum ObjectiveType
    {
        KillMob,
        CollectItem,
        CollectFromGameObject,
        TurnInOnly
    }

    public class QuestGiverEntry
    {
        public int QuestId { get; set; }
        public int GiverId { get; set; }
        public string GiverName { get; set; }
        public QuestObjectType GiverType { get; set; }
    }

    public class QuestEnderEntry
    {
        public int QuestId { get; set; }
        public int EnderId { get; set; }
        public string EnderName { get; set; }
        public QuestObjectType EnderType { get; set; }
    }

    public enum QuestObjectType
    {
        Creature,
        GameObject,
        Item
    }

    public sealed class QuestItemStarterObservation
    {
        public int QuestId { get; init; }
        public int ItemEntry { get; init; }
        public ulong ItemGuid { get; init; }
        public ulong PlayerGuid { get; init; }
        public DateTime ObservedUtc { get; init; }
        public int MapId { get; init; }
        public string Name { get; init; } = "";
        public bool IsActive { get; init; }
    }

    public sealed class QuestCreatureCreditObservation
    {
        public int Entry { get; init; }
        public int Credit1 { get; init; }
        public int Credit2 { get; init; }
        public ulong Guid { get; init; }
        public ulong PlayerGuid { get; init; }
        public DateTime ObservedUtc { get; init; }
        public int MapId { get; init; }
        public double X { get; init; }
        public double Y { get; init; }
        public double Z { get; init; }
        public bool AliveAttackableSelectable { get; init; }
    }

    public class SpawnPoint
    {
        public double X { get; set; }
        public double Y { get; set; }
        public double Z { get; set; }
        public int Map { get; set; }
        public bool? IsKnownReachable { get; set; }
        public bool? IsKnownSafe { get; set; }
        public int SafetyScore { get; set; }
    }

    public sealed class SpawnNavigationAssessment
    {
        public bool? IsKnownReachable { get; init; }
        public bool? IsKnownSafe { get; init; }
        public int SafetyScore { get; init; }
    }

    public class ZoneIndex
    {
        public int ZoneId { get; set; }
        public string ZoneName { get; set; }
        public string FileName { get; set; }
        public List<int> SubzoneIds { get; set; } = new List<int>();
    }

    public enum QuestWorkStage
    {
        TurnIn,
        Objective,
        AncestorCorrection,
        Pickup,
        HalfOpen
    }

    public sealed class QuestWorkCandidate
    {
        public uint QuestId { get; init; }
        public QuestWorkStage Stage { get; init; }
        public double Distance { get; init; }
        public int ChainValue { get; init; }
        public int SafetyScore { get; init; }
        public QuestRecoveryDecision Recovery { get; init; } = null!;
    }

    public sealed class QuestEndpointCandidate
    {
        public QuestRecoveryKey Key { get; init; } = null!;
        public SpawnPoint Point { get; init; } = null!;
        public double Distance { get; init; }
        public bool RequiresHalfOpen { get; init; }
        public bool? IsKnownReachable { get; init; }
        public bool? IsKnownSafe { get; init; }
        public int SafetyScore { get; init; }
        public QuestRecoveryDecision Recovery { get; init; } = null!;
    }

    public sealed class QuestPlanEntry
    {
        public QuestEntry Quest { get; init; } = null!;
        public QuestWorkStage Stage { get; init; }
        public int ObjectiveIndex { get; init; } = -1;
        public QuestGiverEntry Giver { get; init; } = null!;
        public QuestEnderEntry Ender { get; init; } = null!;
        public IReadOnlyList<SpawnPoint> Hotspots { get; init; } = Array.Empty<SpawnPoint>();
    }

    public sealed class QuestScheduleResult
    {
        public IReadOnlyList<QuestWorkCandidate> Selected { get; init; } = Array.Empty<QuestWorkCandidate>();
        public IReadOnlyList<QuestPlanEntry> Plan { get; init; } = Array.Empty<QuestPlanEntry>();
        public DateTime? EarliestRetryUtc { get; init; }
        public QuestFallbackMode FallbackMode { get; init; }
        public string ValidatedGrindProfilePath { get; init; } = "";
        public string Status { get; init; } = "";
    }

    public enum QuestFallbackMode
    {
        None,
        ValidatedGrind,
        TimedIdle
    }
}
