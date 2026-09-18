using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Styx.Logic.Questing.Recovery;

#nullable disable

namespace WholesomeAQ
{
    public class DataLoader
    {
        private readonly string _dataFile;
        private QuestDatabase _database;
        private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            Converters = { new JsonStringEnumConverter() }
        };

        public QuestDatabase Database => _database;
        public string DatasetFingerprint { get; private set; } = "unknown";
        public QuestDatasetSourceIdentity DatasetSourceIdentity { get; private set; } = new QuestDatasetSourceIdentity();

        public DataLoader()
        {
            _dataFile = FindDataFile();
        }

        public DataLoader(string dataFile)
        {
            _dataFile = dataFile ?? throw new ArgumentNullException(nameof(dataFile));
        }

        private static string FindDataFile()
        {
            string asmDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            if (!string.IsNullOrEmpty(asmDir))
            {
                string path = Path.Combine(asmDir, "quest_data", "quest_data.json");
                if (File.Exists(path))
                    return path;
            }

            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            string[] candidates =
            {
                Path.Combine(baseDir, "Bots", "WholesomeAutoQuest-master", "quest_data", "quest_data.json"),
                Path.Combine(baseDir, "Bots", "WholesomeAutoQuest", "quest_data", "quest_data.json"),
                Path.Combine(baseDir, "Plugins", "WholesomeAutoQuester", "quest_data", "quest_data.json"),
                Path.Combine(Environment.CurrentDirectory, "quest_data", "quest_data.json")
            };

            foreach (string candidate in candidates)
            {
                if (File.Exists(candidate))
                    return candidate;
            }

            return Path.Combine(Environment.CurrentDirectory, "quest_data", "quest_data.json");
        }

        public QuestDatabase Load()
        {
            if (_database != null)
            {
                PublishDependencies(_database);
                return _database;
            }

            QuestPrerequisiteAuthority.ClearPublishedDependencyAuthority();
            if (!File.Exists(_dataFile))
                return null;

            // Fingerprint the exact snapshots being deserialized/validated, not later file reads.
            // Provenance is optional: no sidecar means the dataset remains explicitly unknown.
            byte[] snapshot = File.ReadAllBytes(_dataFile);
            string provenancePath = Path.Combine(
                Path.GetDirectoryName(Path.GetFullPath(_dataFile)) ?? Environment.CurrentDirectory,
                "quest_data.provenance.json");
            byte[] provenanceSnapshot = File.Exists(provenancePath)
                ? File.ReadAllBytes(provenancePath)
                : null;
            QuestDatasetSourceIdentity sourceIdentity = provenanceSnapshot == null
                ? new QuestDatasetSourceIdentity()
                : ParseProvenance(provenanceSnapshot, snapshot);
            string fingerprint = FingerprintManifest(
                provenanceSnapshot == null
                    ? new[] { (Role: LogicalRole(_dataFile), Digest: FingerprintDigest(snapshot)) }
                    : new[]
                    {
                        (Role: LogicalRole(_dataFile), Digest: FingerprintDigest(snapshot)),
                        (Role: LogicalRole(provenancePath), Digest: FingerprintDigest(provenanceSnapshot))
                    });
            string json;
            using (var stream = new MemoryStream(snapshot, writable: false))
            using (var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true))
                json = reader.ReadToEnd();

            QuestDatabase database = JsonSerializer.Deserialize<QuestDatabase>(json, JsonOptions);
            if (database == null)
                return null;
            if (database.Quests == null || database.Quests.Any(quest => quest == null || quest.PreviousQuestsIds == null))
                throw new InvalidDataException("Quest dependency records cannot be null.");

            // If prerequisite validation/publication throws, a later Load must retry
            // rather than returning a partially initialized cached database.
            PublishDependencies(database);
            DatasetFingerprint = fingerprint;
            DatasetSourceIdentity = sourceIdentity;
            _database = database;
            return _database;
        }

        private static void PublishDependencies(QuestDatabase database)
        {
            QuestPrerequisiteAuthority.PublishAuthoritativeDependencies(
                database.Quests.SelectMany(quest =>
                {
                    var prerequisites = quest.PreviousQuestsIds
                        .Concat(quest.PrevQuestID != 0 && quest.PrevQuestID != int.MinValue
                            ? new[] { Math.Abs(quest.PrevQuestID) }
                            : Array.Empty<int>())
                        .Where(id => id > 0)
                        .Select(id => new QuestDependencyEvidence(
                            unchecked((uint)quest.Id), unchecked((uint)id), false, true));
                    var forward = quest.NextQuestID > 0
                        ? new[]
                        {
                            new QuestDependencyEvidence(
                                unchecked((uint)quest.NextQuestID),
                                unchecked((uint)quest.Id),
                                false,
                                true)
                        }
                        : Array.Empty<QuestDependencyEvidence>();
                    return prerequisites.Concat(forward);
                }),
                database.Quests.Where(quest => quest.Id > 0).Select(quest => unchecked((uint)quest.Id)));
        }

        private static QuestDatasetSourceIdentity ParseProvenance(byte[] snapshot, byte[] questDataSnapshot)
        {
            string text;
            using (var stream = new MemoryStream(snapshot, writable: false))
            using (var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true))
                text = reader.ReadToEnd();

            using JsonDocument document = JsonDocument.Parse(text);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
                throw new InvalidDataException("Quest dataset provenance must be a JSON object.");

            string[] required =
            {
                "Schema", "ClientBuild", "SourceCore", "SourceBranch", "CoreRevision",
                "DatabaseRevision", "Exporter", "ExporterVersion", "QuestDataSha256",
                "RealmOverridesDeclared"
            };
            var names = document.RootElement.EnumerateObject().Select(property => property.Name).ToArray();
            if (names.Length != required.Length ||
                !required.OrderBy(value => value, StringComparer.Ordinal)
                    .SequenceEqual(names.OrderBy(value => value, StringComparer.Ordinal), StringComparer.Ordinal))
                throw new InvalidDataException("Quest dataset provenance has missing or unsupported fields.");

            JsonElement root = document.RootElement;
            string schema = RequiredString(root, "Schema");
            if (schema != "quest-dataset-provenance-v1")
                throw new InvalidDataException("Unsupported quest dataset provenance schema.");

            if (!root.GetProperty("ClientBuild").TryGetInt32(out int clientBuild) || clientBuild != 12340)
                throw new InvalidDataException("Quest dataset provenance requires original client build 12340.");

            string sourceCore = RequiredString(root, "SourceCore");
            if (sourceCore != "trinitycore-3.3.5" && sourceCore != "azerothcore-wotlk")
                throw new InvalidDataException("Quest dataset provenance uses an unsupported source core.");

            string sourceBranch = RequiredString(root, "SourceBranch");
            string coreRevision = RequiredString(root, "CoreRevision");
            string databaseRevision = RequiredString(root, "DatabaseRevision");
            string exporter = RequiredString(root, "Exporter");
            string exporterVersion = RequiredString(root, "ExporterVersion");
            string questDataSha256 = RequiredString(root, "QuestDataSha256").ToLowerInvariant();
            if (questDataSha256.Length != 64 || questDataSha256.Any(value => !Uri.IsHexDigit(value)))
                throw new InvalidDataException("Quest dataset provenance has an invalid quest-data SHA256.");
            if (!string.Equals(questDataSha256, Digest(questDataSnapshot), StringComparison.Ordinal))
                throw new InvalidDataException("Quest dataset provenance does not match the quest-data snapshot.");

            JsonElement overrides = root.GetProperty("RealmOverridesDeclared");
            if (overrides.ValueKind != JsonValueKind.True && overrides.ValueKind != JsonValueKind.False)
                throw new InvalidDataException("RealmOverridesDeclared must be an explicit boolean.");

            return new QuestDatasetSourceIdentity
            {
                Status = QuestDatasetSourceStatus.DeclaredAndBound,
                ClientBuild = clientBuild,
                SourceCore = sourceCore,
                SourceBranch = sourceBranch,
                CoreRevision = coreRevision,
                DatabaseRevision = databaseRevision,
                Exporter = exporter,
                ExporterVersion = exporterVersion,
                QuestDataSha256 = questDataSha256,
                RealmOverridesDeclared = overrides.GetBoolean()
            };
        }

        private static string RequiredString(JsonElement root, string name)
        {
            JsonElement value = root.GetProperty(name);
            if (value.ValueKind != JsonValueKind.String)
                throw new InvalidDataException("Quest dataset provenance field " + name + " must be text.");
            string result = value.GetString();
            if (string.IsNullOrWhiteSpace(result) || result.Length > 512)
                throw new InvalidDataException("Quest dataset provenance field " + name + " is blank or oversized.");
            return result;
        }

        private static string Digest(byte[] bytes) =>
            Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

        private static string FingerprintDigest(byte[] bytes) =>
            Convert.ToHexString(SHA256.HashData(bytes));

        internal static string CreateDatasetFingerprint(IEnumerable<string> dataFiles)
        {
            if (dataFiles == null)
                throw new ArgumentNullException(nameof(dataFiles));
            return FingerprintManifest(dataFiles.Select(path =>
            {
                string role = LogicalRole(path);
                using (var stream = File.OpenRead(path))
                    return (Role: role, Digest: Convert.ToHexString(SHA256.HashData(stream)));
            }));
        }

        private static string LogicalRole(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                throw new ArgumentException("A dataset file path is required.", nameof(path));
            string role = Path.GetFileName(path).ToLowerInvariant();
            if (string.IsNullOrEmpty(role))
                throw new InvalidDataException("A dataset file must have a logical filename.");
            return role;
        }

        private static string FingerprintManifest(IEnumerable<(string Role, string Digest)> entries)
        {
            var ordered = entries.OrderBy(entry => entry.Role, StringComparer.Ordinal).ToArray();
            if (ordered.Length == 0)
                throw new InvalidDataException("A dataset manifest must contain at least one file.");
            if (ordered.Select(entry => entry.Role).Distinct(StringComparer.Ordinal).Count() != ordered.Length)
                throw new InvalidDataException("Dataset logical filenames must be unique, regardless of directory.");

            // Versioning intentionally invalidates old metadata-derived fingerprints.
            // Base64 roles and fixed-length digests make separators unambiguous.
            string manifest = "quest-dataset-content-v2\n" + string.Join("\n", ordered.Select(entry =>
                Convert.ToBase64String(Encoding.UTF8.GetBytes(entry.Role)) + ":" + entry.Digest));
            return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(manifest))).ToLowerInvariant();
        }
    }
}
