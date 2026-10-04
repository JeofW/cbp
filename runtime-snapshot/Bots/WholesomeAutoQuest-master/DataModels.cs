using System;
using System.Collections.Generic;
using System.Linq;
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
        Escort,
        ArelionsMistress
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
        // Only the separately validated catalog can publish mechanism authority.
        // Null remains the legacy in-memory API used by explicit profile callers;
        // DataLoader always installs a bound or explicitly unavailable contract.
        [System.Text.Json.Serialization.JsonIgnore]
        public QuestSourceExecutionContract SourceExecution { get; internal set; }
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

    public sealed class QuestSourceExecutionContract
    {
        public bool IsBound { get; init; }
        public string Status { get; init; } = "SourceUnresolved";
        public IReadOnlyList<QuestObjectiveExecutionContract> Objectives { get; init; }
            = Array.Empty<QuestObjectiveExecutionContract>();
    }

    public sealed class QuestObjectiveExecutionContract
    {
        public int RowIndex { get; init; }
        public int ObjectiveIndex { get; init; }
        public ObjectiveType Type { get; init; }
        public int MobId { get; init; }
        public int ItemId { get; init; }
        public int GameObjectId { get; init; }
        public int KillCount { get; init; }
        public int CollectCount { get; init; }
        public string Driver { get; init; } = "Unsupported";
        public IReadOnlyList<string> Reasons { get; init; } = Array.Empty<string>();
        internal QuestStrategyRecipe BuiltInRecipe { get; init; }
        internal bool Matches(QuestObjective objective) => objective != null && ObjectiveIndex == objective.Index
            && Type == objective.Type && MobId == objective.MobId && ItemId == objective.ItemId
            && GameObjectId == objective.GameObjectId && KillCount == objective.KillCount && CollectCount == objective.CollectCount;
    }

    /// <summary>One mechanism decision shared by scheduler and profile materializer.</summary>
    public static class QuestExecutionPolicy
    {
        private static QuestObjectiveExecutionContract Contract(QuestEntry quest, QuestObjective objective)
        {
            if (quest?.SourceExecution?.IsBound != true) return null;
            int ordinal = quest.Objectives.FindIndex(row => ReferenceEquals(row, objective));
            if (ordinal < 0 || ordinal >= quest.SourceExecution.Objectives.Count) return null;
            var contract = quest.SourceExecution.Objectives[ordinal];
            return contract.RowIndex == ordinal && contract.Matches(objective) ? contract : null;
        }

        public static QuestStrategyRecipe[] Strategies(QuestEntry quest, QuestObjective objective, QuestStrategyPack pack)
        {
            var declared = pack?.Recipes?.Where(recipe => recipe != null && recipe.QuestId == quest.Id
                && recipe.ObjectiveIndex == objective.Index).Take(2).ToArray() ?? Array.Empty<QuestStrategyRecipe>();
            if (declared.Length != 0) return declared;
            var generated = Contract(quest, objective)?.BuiltInRecipe;
            return generated == null ? Array.Empty<QuestStrategyRecipe>() : new[] { generated };
        }

        public static bool CanExecutePrimitive(QuestEntry quest, QuestObjective objective)
        {
            if (quest == null || objective == null) return false;
            if (quest.SourceExecution != null && Contract(quest, objective)?.Driver != "Primitive") return false;
            return (objective.Type == ObjectiveType.KillMob && objective.MobId > 0 && (quest.SpecialFlags & 0x20) == 0)
                || (objective.Type == ObjectiveType.CollectItem && objective.ItemId > 0 && objective.MobId > 0)
                || (objective.Type == ObjectiveType.CollectFromGameObject && objective.GameObjectId > 0)
                || objective.Type == ObjectiveType.TurnInOnly;
        }

        public static bool CanExecuteStrategy(QuestEntry quest, QuestObjective objective,
            QuestStrategyPack pack, QuestStrategyRecipe recipe)
        {
            if (quest == null || objective == null || recipe == null) return false;
            var contract = Contract(quest, objective);
            if (quest.SourceExecution != null && (contract == null || contract.Driver == "Unsupported")) return false;
            bool generated = contract?.BuiltInRecipe != null && ReferenceEquals(contract.BuiltInRecipe, recipe);
            if (!generated && (pack?.Status != QuestStrategyPackStatus.DeclaredAndBound || pack.ClientBuild != 12340)) return false;
            if (contract?.Driver == "ArelionsMistress" && recipe.Kind != QuestStrategyKind.ArelionsMistress) return false;
            bool typedCredit = (generated || pack?.SchemaVersion == 2)
                && recipe.Kind is QuestStrategyKind.UseItemOn or QuestStrategyKind.ArelionsMistress
                && recipe.SuccessEvidence == QuestStrategySuccessEvidence.ObjectiveProgress
                && objective.Type == ObjectiveType.KillMob && recipe.CreditId == objective.MobId
                && recipe.CreditCount == objective.KillCount && recipe.CreditCount > 0 && recipe.CreditCount <= ushort.MaxValue
                && recipe.WaitTime >= 0 && recipe.WaitTime <= 60000;
            if (recipe.QuestId != quest.Id || recipe.ObjectiveIndex != objective.Index
                || (recipe.SuccessEvidence != QuestStrategySuccessEvidence.QuestComplete && !typedCredit)
                || recipe.TargetType != QuestStrategyTargetType.Creature || recipe.TargetId <= 0 || recipe.TargetId != objective.MobId
                || (objective.Type != ObjectiveType.KillMob && objective.Type != ObjectiveType.CollectItem)
                || !double.IsFinite(recipe.Range) || recipe.Range <= 0 || recipe.Range > 100
                || recipe.MaxAttempts < 1 || recipe.MaxAttempts > 20) return false;
            if (recipe.Kind == QuestStrategyKind.ArelionsMistress)
                return generated && quest.Id == 9472 && objective.Type == ObjectiveType.KillMob
                    && objective.MobId == 17226 && objective.KillCount == 1 && recipe.ItemId == 23693
                    && recipe.TargetState == QuestStrategyTargetState.Alive && typedCredit;
            if (recipe.Kind == QuestStrategyKind.UseItemOn)
                return recipe.ItemId > 0 && recipe.TargetState is QuestStrategyTargetState.Alive or QuestStrategyTargetState.Dead or QuestStrategyTargetState.DontCare;
            if (recipe.Kind == QuestStrategyKind.GossipEvent)
                return recipe.GossipOptionIndex >= 0 && recipe.GossipOptionIndex <= 64;
            return false;
        }

        public static string Rejection(QuestEntry quest, QuestObjective objective, QuestStrategyPack pack)
        {
            var recipes = Strategies(quest, objective, pack);
            if (recipes.Length == 1 && CanExecuteStrategy(quest, objective, pack, recipes[0])) return null;
            if (recipes.Length == 0 && CanExecutePrimitive(quest, objective)) return null;
            var contract = Contract(quest, objective);
            return contract == null ? "source-execution-contract-unavailable"
                : string.Join(",", contract.Reasons.Prepend("source-executor-" + contract.Driver));
        }
    }
}
