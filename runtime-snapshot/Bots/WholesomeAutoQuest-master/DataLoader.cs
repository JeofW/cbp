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

            string json = File.ReadAllText(_dataFile);
            _database = JsonSerializer.Deserialize<QuestDatabase>(json, JsonOptions);
            if (_database != null)
            {
                DatasetFingerprint = CreateDatasetFingerprint(new[] { _dataFile });
                PublishDependencies(_database);
            }
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
            string metadata = string.Join("\n", dataFiles
                .Select(Path.GetFullPath)
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .Select(path =>
                {
                    var file = new FileInfo(path);
                    return $"{path}|{file.Length}|{file.LastWriteTimeUtc.Ticks}";
                }));
            return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(metadata)))
                .ToLowerInvariant();
        }
    }
}
