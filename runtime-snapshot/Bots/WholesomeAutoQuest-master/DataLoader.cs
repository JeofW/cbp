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

            // Fingerprint the exact snapshot being deserialized, not a later file read.
            // This is a dataset-load operation, never a per-pulse content hash.
            byte[] snapshot = File.ReadAllBytes(_dataFile);
            string fingerprint = FingerprintManifest(new[]
            {
                (Role: LogicalRole(_dataFile), Digest: Convert.ToHexString(SHA256.HashData(snapshot)))
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
            _database = database;
            return _database;
        }

        private static void PublishDependencies(QuestDatabase database)
        {
            QuestPrerequisiteAuthority.PublishAuthoritativeDependencies(
                database.Quests.SelectMany(quest =>
                {
                    var prerequisites = quest.PreviousQuestsIds
                        .Concat(quest.PrevQuestID > 0
                            ? new[] { quest.PrevQuestID }
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
