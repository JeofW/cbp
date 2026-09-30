using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

#nullable disable
namespace WholesomeAQ
{
    public static class QuestDependencyCatalog
    {
        // This lookup is ONLY for prerequisite/group interpretation. Work owners
        // continue to enumerate and resolve db.Quests, never these metadata rows.
        public static IReadOnlyDictionary<uint, QuestEntry> CreateLookup(QuestDatabase db)
        {
            if (db == null) throw new ArgumentNullException(nameof(db));
            var result = db.Quests.Where(q => q != null && q.Id > 0).ToDictionary(q => (uint)q.Id);
            foreach (var pair in db.DependencyMetadata)
            {
                QuestDependencyMetadata metadata = pair.Value;
                if (metadata == null || pair.Key <= 0 || metadata.QuestId != pair.Key || string.IsNullOrWhiteSpace(metadata.SourceRef))
                    throw new InvalidDataException("External dependency metadata is not source-bound.");
                if (result.TryGetValue((uint)pair.Key, out QuestEntry existing))
                {
                    if (existing.ExclusiveGroup != metadata.ExclusiveGroup)
                        throw new InvalidDataException("External dependency metadata conflicts with a schedulable quest.");
                    continue;
                }
                result.Add((uint)pair.Key, new QuestEntry { Id = pair.Key, ExclusiveGroup = metadata.ExclusiveGroup });
            }
            return result;
        }
    }
}
