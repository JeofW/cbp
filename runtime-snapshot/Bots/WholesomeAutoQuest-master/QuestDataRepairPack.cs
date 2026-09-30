using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

#nullable disable
namespace WholesomeAQ
{
    public sealed class QuestItemRequirement
    {
        public int ItemId { get; set; }
        public int Count { get; set; }
    }

    public sealed class QuestSupplementalSupply
    {
        public int ItemId { get; set; }
        public int RequiredCount { get; set; }
        public int ProvidedCount { get; set; }
    }

    // Metadata-only records cannot create quest work. They supply the otherwise
    // unknown group contract for a completed predecessor outside the plan data.
    public sealed class QuestDependencyMetadata
    {
        public int QuestId { get; init; }
        public int ExclusiveGroup { get; init; }
        public IReadOnlyList<int> GroupMembers { get; init; } = Array.Empty<int>();
        public string SourceRef { get; init; } = "";
    }

    public sealed class QuestCreditSourcePoint
    {
        public int Map { get; init; }
        public double X { get; init; }
        public double Y { get; init; }
        public double Z { get; init; }
    }

    public sealed class QuestCreditSource
    {
        public int QuestId { get; init; }
        public int RowIndex { get; init; }
        public int ObjectiveIndex { get; init; }
        public int CreditId { get; init; }
        public int RequiredCount { get; init; }
        public int CreatureId { get; init; }
        public string CreditField { get; init; } = "";
        public string SourceRef { get; init; } = "";
        public IReadOnlyList<QuestCreditSourcePoint> Points { get; init; } = Array.Empty<QuestCreditSourcePoint>();
    }

    public static class QuestCreditSourceCatalog
    {
        public static IEnumerable<QuestCreditSource> ForObjective(QuestEntry quest, QuestObjective objective, QuestDatabase database)
        {
            if (quest == null || objective == null || database == null || objective.Type != ObjectiveType.KillMob ||
                objective.MobId <= 0 || objective.KillCount <= 0 || objective.ItemId != 0 || objective.GameObjectId != 0 ||
                (quest.SpecialFlags & 0x22) != 0 || quest.Objectives == null || database.ObjectiveCreditSources == null)
                return Enumerable.Empty<QuestCreditSource>();
            return database.ObjectiveCreditSources.Where(source => source != null && source.QuestId == quest.Id &&
                source.RowIndex >= 0 && source.RowIndex < quest.Objectives.Count &&
                ReferenceEquals(quest.Objectives[source.RowIndex], objective) && source.ObjectiveIndex == objective.Index &&
                source.CreditId == objective.MobId && source.RequiredCount == objective.KillCount);
        }

        public static IEnumerable<SpawnPoint> Locations(QuestEntry quest, QuestObjective objective, QuestDatabase database)
        {
            foreach (QuestCreditSource source in ForObjective(quest, objective, database))
                foreach (QuestCreditSourcePoint point in source.Points)
                {
                    var prior = new[] { source.CreditId, source.CreatureId }.SelectMany(entry =>
                        database.CreatureSpawns.TryGetValue(entry.ToString(), out var points) && points != null
                            ? points : Enumerable.Empty<SpawnPoint>()).Where(p => p != null && p.Map == point.Map &&
                                p.X == point.X && p.Y == point.Y && p.Z == point.Z).ToArray();
                    yield return new SpawnPoint { Map = point.Map, X = point.X, Y = point.Y, Z = point.Z,
                        IsKnownReachable = prior.Any(p => p.IsKnownReachable == false) ? false : null,
                        IsKnownSafe = prior.Any(p => p.IsKnownSafe == false) ? false : null };
                }
        }
    }

    /// <summary>
    /// Applies a fully validated data-only patch to an isolated model. Source
    /// declarations are auditable reference knowledge, not realm equivalence,
    /// executable recipes, live item receipts or positive navigation evidence.
    /// </summary>
    public static class QuestDataRepairPackLoader
    {
        private static readonly JsonSerializerOptions Json = new JsonSerializerOptions
        {
            Converters = { new JsonStringEnumConverter() }
        };
        private static readonly HashSet<string> MetadataFields = new HashSet<string>(StringComparer.Ordinal)
        {
            "AllowableClasses", "MaxLevel", "RequiredSkillID", "RequiredSkillPoints",
            "RequiredMinRepFaction", "RequiredMinRepValue", "RequiredMaxRepFaction", "RequiredMaxRepValue",
            "RequiredFactionValue1", "RequiredFactionValue2"
        };

        public static QuestDatabase Apply(byte[] bytes, string expectedBaseSha256, QuestDatabase original, out string source)
        {
            source = "absent";
            if (bytes == null || bytes.Length == 0 || bytes.Length > 64 * 1024 * 1024 || original == null)
                throw new InvalidDataException("Quest repair pack input is missing or oversized.");
            try
            {
                using var document = JsonDocument.Parse(bytes);
                JsonElement root = document.RootElement;
                string[] fields = { "Schema", "ClientBuild", "QuestDataSha256", "SourceCore", "CoreRevision", "DatabaseRevision",
                    "SourceSqlSha256", "QuestMetadata", "SpawnAdditions", "RelationAdditions", "DependencyMetadata" };
                bool hasCountRepairs = root.ValueKind == JsonValueKind.Object && root.TryGetProperty("ObjectiveCountRepairs", out _);
                bool hasObjectRepairs = root.ValueKind == JsonValueKind.Object && root.TryGetProperty("GameObjectObjectiveRepairs", out _);
                bool hasCreditSources = root.ValueKind == JsonValueKind.Object && root.TryGetProperty("ObjectiveCreditSources", out _);
                if (hasCountRepairs) fields = fields.Concat(new[] { "ObjectiveCountRepairs" }).ToArray();
                if (hasObjectRepairs) fields = fields.Concat(new[] { "GameObjectObjectiveRepairs" }).ToArray();
                if (hasCreditSources) fields = fields.Concat(new[] { "ObjectiveCreditSources" }).ToArray();
                Exact(root, fields);
                if (Text(root, "Schema") != "quest-data-repair-pack-335-v1" || Integer(root, "ClientBuild") != 12340 ||
                    Text(root, "SourceCore") != "trinitycore-3.3.5")
                    throw new InvalidDataException("Quest repair pack requires its supported original TC335 schema.");
                string baseSha = Digest(root, "QuestDataSha256", 64);
                if (!string.Equals(baseSha, expectedBaseSha256, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("Quest repair pack does not match the exact base dataset.");
                string revision = Digest(root, "CoreRevision", 40);
                string sql = Digest(root, "SourceSqlSha256", 64);
                string database = Text(root, "DatabaseRevision");

                // No caller-visible model changes occur on a later validation
                // failure. DataLoader publishes this clone only after dependency
                // validation and includes the exact repair bytes in its identity.
                var result = JsonSerializer.Deserialize<QuestDatabase>(JsonSerializer.SerializeToUtf8Bytes(original, Json), Json);
                if (result?.Quests == null || result.Quests.Any(q => q == null || q.Id <= 0) ||
                    result.Quests.Select(q => q.Id).Distinct().Count() != result.Quests.Count)
                    throw new InvalidDataException("Repair base requires unique positive quest identities.");
                var quests = result.Quests.ToDictionary(q => q.Id);
                var changed = new HashSet<int>();
                foreach (JsonElement row in Rows(root, "QuestMetadata", 10000))
                {
                    string[] metadataKeys = { "QuestId", "SourceRef", "Fields", "DeliveryItems", "AcceptanceSupplies" };
                    bool hasSupplemental = row.ValueKind == JsonValueKind.Object && row.TryGetProperty("SupplementalSupply", out _);
                    Exact(row, hasSupplemental ? metadataKeys.Concat(new[] { "SupplementalSupply" }).ToArray() : metadataKeys);
                    int id = Positive(row, "QuestId");
                    if (!changed.Add(id) || !quests.TryGetValue(id, out QuestEntry quest))
                        throw new InvalidDataException("Repair metadata has a duplicate or foreign quest identity.");
                    _ = Text(row, "SourceRef");
                    JsonElement metadataFields = row.GetProperty("Fields");
                    if (metadataFields.ValueKind != JsonValueKind.Object)
                        throw new InvalidDataException("Repair metadata fields must be an object.");
                    var names = new HashSet<string>(StringComparer.Ordinal);
                    foreach (JsonProperty field in metadataFields.EnumerateObject())
                    {
                        if (!MetadataFields.Contains(field.Name) || !names.Add(field.Name) || !field.Value.TryGetInt32(out int value))
                            throw new InvalidDataException("Repair metadata has an unsupported or duplicate field.");
                        bool signedValue = field.Name.Contains("RepValue", StringComparison.Ordinal) || field.Name.StartsWith("RequiredFactionValue", StringComparison.Ordinal);
                        if ((!signedValue && field.Name != "AllowableClasses" && value < 0) || (field.Name == "AllowableClasses" && value < -1))
                            throw new InvalidDataException("Repair eligibility metadata is outside the supported range.");
                        PropertyInfo property = typeof(QuestEntry).GetProperty(field.Name);
                        if (property == null || property.PropertyType != typeof(int?) || property.GetValue(quest) != null)
                            throw new InvalidDataException("A repair cannot overwrite existing eligibility evidence.");
                        property.SetValue(quest, value);
                    }
                    QuestItemRequirement[] delivery = Requirements(row.GetProperty("DeliveryItems"));
                    QuestItemRequirement[] supplies = Requirements(row.GetProperty("AcceptanceSupplies"));
                    if (delivery != null || supplies != null)
                    {
                        if (delivery == null || supplies == null || delivery.Length == 0 || quest.DeliveryItems != null || quest.AcceptanceSupplies != null ||
                            quest.Objectives == null || quest.Objectives.Count == 0 || quest.Objectives.Any(o => o.Type != ObjectiveType.TurnInOnly) ||
                            (quest.SpecialFlags & 0x22) != 0 || supplies.Any(item => item.ItemId != quest.StartItem || !delivery.Any(d => d.ItemId == item.ItemId)))
                            throw new InvalidDataException("Repair delivery contract is incomplete or replaces scripted/acquisition work.");
                        quest.DeliveryItems = delivery.ToList();
                        quest.AcceptanceSupplies = supplies.ToList();
                    }
                    if (hasSupplemental && row.GetProperty("SupplementalSupply").ValueKind != JsonValueKind.Null)
                    {
                        JsonElement contract = row.GetProperty("SupplementalSupply");
                        Exact(contract, "ItemId", "RequiredCount", "ProvidedCount");
                        int item = Positive(contract, "ItemId"), needed = Positive(contract, "RequiredCount"), provided = Positive(contract, "ProvidedCount");
                        if (item != quest.StartItem || provided < needed || quest.SupplementalSupply != null ||
                            quest.DeliveryItems != null || quest.AcceptanceSupplies != null || quest.Objectives == null ||
                            !quest.Objectives.Any(o => o != null && o.Type != ObjectiveType.TurnInOnly) ||
                            quest.Objectives.Any(o => o != null && o.ItemId == item))
                            throw new InvalidDataException("Supplemental source supply cannot replace or duplicate an objective/delivery owner.");
                        quest.SupplementalSupply = new QuestSupplementalSupply { ItemId = item, RequiredCount = needed, ProvidedCount = provided };
                    }
                }

                var countOwners = new HashSet<(int, int)>();
                if (hasCountRepairs)
                    foreach (JsonElement row in Rows(root, "ObjectiveCountRepairs", 30000))
                    {
                        Exact(row, "QuestId", "RowIndex", "ObjectiveIndex", "ObjectiveType", "TargetId", "ItemId", "ExpectedCount", "RequiredCount", "SourceRef");
                        int id = Positive(row, "QuestId"), rowIndex = Integer(row, "RowIndex"), objectiveIndex = Integer(row, "ObjectiveIndex");
                        int targetId = Positive(row, "TargetId"), itemId = Positive(row, "ItemId");
                        int before = Positive(row, "ExpectedCount"), after = Positive(row, "RequiredCount");
                        string kind = Text(row, "ObjectiveType"); _ = Text(row, "SourceRef");
                        if (!quests.TryGetValue(id, out QuestEntry quest) || !countOwners.Add((id, rowIndex)) ||
                            quest.Objectives == null || rowIndex < 0 || rowIndex >= quest.Objectives.Count ||
                            (quest.SpecialFlags & 0x22) != 0 || objectiveIndex < 0 || after == before)
                            throw new InvalidDataException("Collection count repair is not bound to a unique ordinary source row.");
                        QuestObjective objective = quest.Objectives[rowIndex];
                        if (objective == null || objective.Index != objectiveIndex || objective.ItemId != itemId || objective.CollectCount != before ||
                            !(kind == "CollectItem" && objective.Type == ObjectiveType.CollectItem && objective.MobId == targetId ||
                              kind == "CollectFromGameObject" && objective.Type == ObjectiveType.CollectFromGameObject && objective.GameObjectId == targetId))
                            throw new InvalidDataException("Collection count repair expected identity or old value changed.");
                        objective.CollectCount = after;
                    }

                var objectOwners = new HashSet<(int, int)>();
                if (hasObjectRepairs)
                    foreach (JsonElement row in Rows(root, "GameObjectObjectiveRepairs", 30000))
                    {
                        Exact(row, "QuestId", "RowIndex", "ObjectiveIndex", "ObjectiveType", "ExpectedGameObjectId",
                            "GameObjectId", "ItemId", "RequiredCount", "SourceRef");
                        int id = Positive(row, "QuestId"), rowIndex = Integer(row, "RowIndex"), objectiveIndex = Integer(row, "ObjectiveIndex");
                        int before = Positive(row, "ExpectedGameObjectId"), after = Positive(row, "GameObjectId");
                        int item = Positive(row, "ItemId"), count = Positive(row, "RequiredCount");
                        string kind = Text(row, "ObjectiveType"); _ = Text(row, "SourceRef");
                        if (!quests.TryGetValue(id, out QuestEntry quest) || !objectOwners.Add((id, rowIndex)) ||
                            countOwners.Contains((id, rowIndex)) || kind != "CollectFromGameObject" || before == after ||
                            objectiveIndex < 0 || quest.Objectives == null || rowIndex < 0 || rowIndex >= quest.Objectives.Count ||
                            (quest.SpecialFlags & 0x22) != 0)
                            throw new InvalidDataException("GameObject identity repair is not bound to a unique ordinary source row.");
                        QuestObjective objective = quest.Objectives[rowIndex];
                        if (objective == null || objective.Type != ObjectiveType.CollectFromGameObject || objective.Index != objectiveIndex ||
                            objective.GameObjectId != before || objective.MobId != 0 || objective.KillCount != 0 ||
                            objective.ItemId != item || objective.CollectCount != count ||
                            quest.Objectives.Any(other => other != objective && other != null &&
                                other.Type == ObjectiveType.CollectFromGameObject && other.GameObjectId == after && other.ItemId == item))
                            throw new InvalidDataException("GameObject identity repair expected objective changed or would duplicate another owner.");
                        objective.GameObjectId = after;
                    }

                var spawnOwners = new HashSet<(QuestObjectType, int)>();
                int totalPoints = 0;
                foreach (JsonElement row in Rows(root, "SpawnAdditions", 30000))
                {
                    Exact(row, "ObjectType", "Entry", "SourceRef", "Points");
                    QuestObjectType type = ObjectType(row);
                    int entry = Positive(row, "Entry");
                    _ = Text(row, "SourceRef");
                    if (!spawnOwners.Add((type, entry))) throw new InvalidDataException("Duplicate spawn repair owner.");
                    var target = type == QuestObjectType.Creature ? result.CreatureSpawns : result.GameObjectSpawns;
                    string key = entry.ToString(System.Globalization.CultureInfo.InvariantCulture);
                    if (target == null || (target.TryGetValue(key, out var prior) && (prior == null || prior.Count != 0)))
                        throw new InvalidDataException("A spawn repair cannot overwrite existing geometry or vetoes.");
                    var points = new List<SpawnPoint>();
                    var distinct = new HashSet<(int, double, double, double)>();
                    foreach (JsonElement point in Rows(row, "Points", 20000))
                    {
                        Exact(point, "Map", "X", "Y", "Z");
                        int map = Integer(point, "Map");
                        double x = Coordinate(point, "X"), y = Coordinate(point, "Y"), z = Coordinate(point, "Z");
                        if (map < 0 || !distinct.Add((map, x, y, z)) || ++totalPoints > 400000)
                            throw new InvalidDataException("Repair geometry is invalid, duplicated or oversized.");
                        points.Add(new SpawnPoint { Map = map, X = x, Y = y, Z = z });
                    }
                    if (points.Count == 0) throw new InvalidDataException("A spawn addition requires actual source geometry.");
                    target[key] = points;
                }

                var relationOwners = new HashSet<(string, int, QuestObjectType, int)>();
                foreach (JsonElement row in Rows(root, "RelationAdditions", 30000))
                {
                    Exact(row, "Role", "QuestId", "ObjectType", "Entry", "Name", "SourceRef");
                    string role = Text(row, "Role"); int id = Positive(row, "QuestId"), entry = Positive(row, "Entry");
                    QuestObjectType type = ObjectType(row); string name = Text(row, "Name"); _ = Text(row, "SourceRef");
                    if ((role != "Giver" && role != "Ender") || !quests.ContainsKey(id) || !relationOwners.Add((role, id, type, entry)))
                        throw new InvalidDataException("Repair relation is duplicated or unbound.");
                    if (role == "Giver")
                    {
                        if (result.QuestGivers.Any(r => r.QuestId == id && r.GiverType == type && r.GiverId == entry))
                            throw new InvalidDataException("A repair cannot duplicate an existing giver relation.");
                        result.QuestGivers.Add(new QuestGiverEntry { QuestId = id, GiverId = entry, GiverName = name, GiverType = type });
                    }
                    else
                    {
                        if (result.QuestEnders.Any(r => r.QuestId == id && r.EnderType == type && r.EnderId == entry))
                            throw new InvalidDataException("A repair cannot duplicate an existing ender relation.");
                        result.QuestEnders.Add(new QuestEnderEntry { QuestId = id, EnderId = entry, EnderName = name, EnderType = type });
                    }
                }

                var dependencies = new Dictionary<int, QuestDependencyMetadata>();
                foreach (JsonElement row in Rows(root, "DependencyMetadata", 20000))
                {
                    Exact(row, "QuestId", "ExclusiveGroup", "GroupMembers", "SourceRef");
                    int id = Positive(row, "QuestId"), group = Integer(row, "ExclusiveGroup");
                    int[] members = Rows(row, "GroupMembers", 10000).Select(value => value.TryGetInt32(out int member) && member > 0
                        ? member : throw new InvalidDataException("Invalid dependency group member.")).ToArray();
                    if (group == int.MinValue || dependencies.ContainsKey(id) || members.Distinct().Count() != members.Length ||
                        (group >= 0 && members.Length != 0) || (group < 0 && !members.Contains(id)) ||
                        (quests.TryGetValue(id, out QuestEntry existing) && existing.ExclusiveGroup != group))
                        throw new InvalidDataException("Dependency metadata conflicts with the base or group contract.");
                    dependencies.Add(id, new QuestDependencyMetadata { QuestId = id, ExclusiveGroup = group,
                        GroupMembers = Array.AsReadOnly(members), SourceRef = Text(row, "SourceRef") });
                }
                foreach (QuestDependencyMetadata item in dependencies.Values.Where(d => d.ExclusiveGroup < 0))
                {
                    foreach (int member in item.GroupMembers)
                        if (!dependencies.TryGetValue(member, out var other) || other.ExclusiveGroup != item.ExclusiveGroup ||
                            !other.GroupMembers.OrderBy(x => x).SequenceEqual(item.GroupMembers.OrderBy(x => x)))
                            throw new InvalidDataException("A negative dependency group requires its complete bound member catalog.");
                    if (quests.Values.Any(q => q.ExclusiveGroup == item.ExclusiveGroup && !item.GroupMembers.Contains(q.Id)))
                        throw new InvalidDataException("Dependency catalog omitted a known negative-group member.");
                }
                var creditSources = new List<QuestCreditSource>();
                var creditOwners = new HashSet<(int Quest, int Row, int Creature)>();
                int creditPointCount = 0;
                if (hasCreditSources)
                    foreach (JsonElement row in Rows(root, "ObjectiveCreditSources", 30000))
                    {
                        Exact(row, "QuestId", "RowIndex", "ObjectiveIndex", "CreditId", "RequiredCount", "CreatureId", "CreditField", "SourceRef", "Points");
                        int id = Positive(row, "QuestId"), ordinal = Integer(row, "RowIndex"), index = Integer(row, "ObjectiveIndex");
                        int credit = Positive(row, "CreditId"), count = Positive(row, "RequiredCount"), creature = Positive(row, "CreatureId");
                        string field = Text(row, "CreditField"), reference = Text(row, "SourceRef");
                        if (!quests.TryGetValue(id, out QuestEntry quest) || !creditOwners.Add((id, ordinal, creature)) ||
                            ordinal < 0 || quest.Objectives == null || ordinal >= quest.Objectives.Count || index < 0 ||
                            credit == creature || (field != "KillCredit1" && field != "KillCredit2") || (quest.SpecialFlags & 0x22) != 0)
                            throw new InvalidDataException("Credit source is not bound to a unique ordinary quest objective.");
                        QuestObjective objective = quest.Objectives[ordinal];
                        if (objective == null || objective.Type != ObjectiveType.KillMob || objective.Index != index ||
                            objective.MobId != credit || objective.KillCount != count || objective.ItemId != 0 || objective.GameObjectId != 0)
                            throw new InvalidDataException("Credit source expected objective identity or count changed.");
                        var points = new List<QuestCreditSourcePoint>(); var unique = new HashSet<(int, double, double, double)>();
                        foreach (JsonElement position in Rows(row, "Points", 10000))
                        {
                            Exact(position, "Map", "X", "Y", "Z");
                            int map = Integer(position, "Map");
                            double x = Coordinate(position, "X"), y = Coordinate(position, "Y"), z = Coordinate(position, "Z");
                            if (map < 0 || !unique.Add((map, x, y, z)) || ++creditPointCount > 200000)
                                throw new InvalidDataException("Credit source positions are invalid, duplicated or oversized.");
                            points.Add(new QuestCreditSourcePoint { Map = map, X = x, Y = y, Z = z });
                        }
                        if (points.Count == 0) throw new InvalidDataException("Credit source has no reference search locations.");
                        creditSources.Add(new QuestCreditSource { QuestId = id, RowIndex = ordinal, ObjectiveIndex = index,
                            CreditId = credit, RequiredCount = count, CreatureId = creature, CreditField = field,
                            SourceRef = reference, Points = points.AsReadOnly() });
                    }
                result.ObjectiveCreditSources = creditSources.AsReadOnly();
                result.DependencyMetadata = new ReadOnlyDictionary<int, QuestDependencyMetadata>(dependencies);
                source = "trinitycore-3.3.5:" + revision + ":" + database + ":" + sql;
                return result;
            }
            catch (JsonException error) { throw new InvalidDataException("Quest repair JSON is malformed.", error); }
        }

        private static QuestItemRequirement[] Requirements(JsonElement value)
        {
            if (value.ValueKind == JsonValueKind.Null) return null;
            if (value.ValueKind != JsonValueKind.Array || value.GetArrayLength() > 6)
                throw new InvalidDataException("A delivery contract requires at most six original-client item slots.");
            var result = new List<QuestItemRequirement>(); var ids = new HashSet<int>();
            foreach (JsonElement row in value.EnumerateArray())
            {
                Exact(row, "ItemId", "Count"); int id = Positive(row, "ItemId"), count = Positive(row, "Count");
                if (!ids.Add(id)) throw new InvalidDataException("Duplicate delivery item identity.");
                result.Add(new QuestItemRequirement { ItemId = id, Count = count });
            }
            return result.ToArray();
        }
        private static QuestObjectType ObjectType(JsonElement row) => Text(row, "ObjectType") switch
        {
            "Creature" => QuestObjectType.Creature, "GameObject" => QuestObjectType.GameObject,
            _ => throw new InvalidDataException("Repair relation/geometry requires an explicit supported object namespace.")
        };
        private static IEnumerable<JsonElement> Rows(JsonElement row, string name, int maximum)
        {
            JsonElement value = row.GetProperty(name);
            if (value.ValueKind != JsonValueKind.Array || value.GetArrayLength() > maximum)
                throw new InvalidDataException("Quest repair array is invalid or oversized: " + name);
            return value.EnumerateArray();
        }
        private static void Exact(JsonElement row, params string[] fields)
        {
            if (row.ValueKind != JsonValueKind.Object || !row.EnumerateObject().Select(p => p.Name).OrderBy(n => n, StringComparer.Ordinal)
                .SequenceEqual(fields.OrderBy(n => n, StringComparer.Ordinal), StringComparer.Ordinal))
                throw new InvalidDataException("Quest repair object has missing, duplicate or unsupported fields.");
        }
        private static string Text(JsonElement row, string name)
        {
            JsonElement value = row.GetProperty(name);
            if (value.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(value.GetString()) || value.GetString().Length > 2048)
                throw new InvalidDataException("Quest repair text is missing or oversized: " + name);
            return value.GetString();
        }
        private static string Digest(JsonElement row, string name, int length)
        {
            string value = Text(row, name);
            if (value.Length != length || value.Any(c => !Uri.IsHexDigit(c))) throw new InvalidDataException("Invalid repair digest: " + name);
            return value.ToLowerInvariant();
        }
        private static int Integer(JsonElement row, string name) => row.GetProperty(name).TryGetInt32(out int value)
            ? value : throw new InvalidDataException("Invalid repair integer: " + name);
        private static int Positive(JsonElement row, string name)
        {
            int value = Integer(row, name); return value > 0 ? value : throw new InvalidDataException("Repair identity/count must be positive: " + name);
        }
        private static double Coordinate(JsonElement row, string name) => row.GetProperty(name).TryGetDouble(out double value) &&
            !double.IsNaN(value) && !double.IsInfinity(value) ? value : throw new InvalidDataException("Repair coordinate must be finite: " + name);
    }
}
