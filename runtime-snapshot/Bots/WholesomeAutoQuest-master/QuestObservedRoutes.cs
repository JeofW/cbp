using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using Styx;
using Styx.WoWInternals;
using Styx.WoWInternals.WoWObjects;

#nullable disable
namespace WholesomeAQ
{
    public partial class QuestScheduler
    {
        private static QuestItemStarterObservation[] ValidItemStarters(QuestSchedulerSnapshot snapshot) =>
            (snapshot.ItemStarters ?? Array.Empty<QuestItemStarterObservation>())
                .Where(value => value != null && value.ItemGuid != 0)
                .GroupBy(value => value.ItemGuid)
                .Where(group => group.Select(value => (value.QuestId, value.ItemEntry, value.PlayerGuid, value.ObservedUtc, value.MapId, value.IsActive)).Distinct().Count() == 1)
                .Select(group => group.First())
                .Where(value => value.QuestId > 0 && value.ItemEntry > 0 && !value.IsActive && snapshot.PlayerGuid != 0 &&
                    value.PlayerGuid == snapshot.PlayerGuid && value.ObservedUtc == snapshot.UtcNow && value.MapId == snapshot.MapId &&
                    snapshot.MapId >= 0 && Finite(snapshot.X) && Finite(snapshot.Y) && Finite(snapshot.Z) &&
                    snapshot.CarriedItemCounts != null && snapshot.CarriedItemCounts.TryGetValue(value.ItemEntry, out long count) && count > 0)
                .ToArray();

        private static IEnumerable<QuestGiverEntry> GetPickupRelations(int questId, QuestDatabase db, QuestSchedulerSnapshot snapshot)
        {
            var items = ValidItemStarters(snapshot).Where(value => value.QuestId == questId).ToArray();
            return db.QuestGivers.Where(giver => giver.QuestId == questId &&
                    (giver.GiverType != QuestObjectType.Item || items.Any(value => value.ItemEntry == giver.GiverId)))
                .Concat(items.Select(value => new QuestGiverEntry
                {
                    QuestId = questId, GiverId = value.ItemEntry, GiverType = QuestObjectType.Item, GiverName = value.Name
                })).GroupBy(value => (value.GiverType, value.GiverId)).Select(group => group.First());
        }

        private static IEnumerable<SpawnPoint> GetObservedPickupSpawns(
            int questId, int entry, QuestObjectType type, QuestDatabase db, QuestSchedulerSnapshot snapshot)
        {
            if (type != QuestObjectType.Item) return GetObservedRelationSpawns(entry, type, db, snapshot);
            if (!ValidItemStarters(snapshot).Any(value => value.QuestId == questId && value.ItemEntry == entry))
                return Array.Empty<SpawnPoint>();
            // The item is carried: it has no world spawn. Retain the actor's current
            // point for existing recovery/safety/profile machinery, without claiming
            // navigation is proven. The Item behavior revalidates at dispatch.
            return new[] { new SpawnPoint { Map = snapshot.MapId, X = snapshot.X, Y = snapshot.Y, Z = snapshot.Z } };
        }

        private static QuestItemStarterObservation[] CaptureItemStarters(QuestDatabase db, LocalPlayer me, DateTime now)
        {
            var values = new List<QuestItemStarterObservation>();
            var memory = ObjectManager.Wow;
            if (memory == null || me == null || !me.IsValid || me.Guid == 0) return values.ToArray();
            ulong player = me.Guid; int map = (int)me.MapId;
            var ids = new HashSet<int>(db.Quests.Select(quest => quest.Id));
            foreach (var item in me.CarriedItems)
            {
                try
                {
                    if (item == null || !item.IsValid || item.Guid == 0 || item.Entry == 0) continue;
                    ulong guid = item.Guid; uint entry = item.Entry;
                    // IDA build12340 GetContainerItemQuestInfo 0x5D9400 reads the
                    // BeginQuestId field at cache+396. This only filters reads;
                    // the validated live Lua result supplies the actual relation.
                    var info = item.ItemInfo;
                    if (info == null || info.BeginQuestId <= 0 || !ids.Contains(info.BeginQuestId)) continue;
                    if (!item.TryGetContainerItemQuestInfo(out _, out int questId, out bool active) ||
                        questId <= 0 || !ids.Contains(questId) || active || questId != info.BeginQuestId) continue;
                    if (!ReferenceEquals(memory, ObjectManager.Wow) || !ReferenceEquals(me, ObjectManager.Me) ||
                        !me.IsValid || me.Guid != player || (int)me.MapId != map || !item.IsValid || item.Guid != guid || item.Entry != entry)
                        return Array.Empty<QuestItemStarterObservation>();
                    values.Add(new QuestItemStarterObservation { QuestId = questId, ItemEntry = checked((int)entry),
                        ItemGuid = guid, PlayerGuid = player, MapId = map, ObservedUtc = now, Name = DiagnosticText(item.Name), IsActive = false });
                }
                catch (Exception error) when (error is not ThreadInterruptedException && error is not OperationCanceledException) { }
            }
            return ReferenceEquals(me, ObjectManager.Me) && ReferenceEquals(memory, ObjectManager.Wow) && me.Guid == player
                ? values.ToArray() : Array.Empty<QuestItemStarterObservation>();
        }

        private static QuestCreatureCreditObservation[] CaptureCreatureCredits(QuestDatabase db, LocalPlayer me, DateTime now)
        {
            var values = new List<QuestCreatureCreditObservation>();
            var memory = ObjectManager.Wow;
            if (memory == null || me == null || !me.IsValid || me.Guid == 0) return values.ToArray();
            ulong player = me.Guid; int map = (int)me.MapId; var origin = me.Location;
            var wanted = new HashSet<int>(db.Quests.SelectMany(quest => quest.Objectives)
                .Where(value => value.Type == ObjectiveType.KillMob && value.MobId > 0).Select(value => value.MobId));
            foreach (var unit in ObjectManager.GetObjectsOfType<WoWUnit>(true, false))
            {
                try
                {
                    if (unit is WoWPlayer || !unit.IsValid || unit.Guid == 0 || unit.Entry == 0 ||
                        !unit.IsAlive || !unit.Attackable || !unit.CanSelect || unit.MyReaction > WoWUnitReaction.Neutral) continue;
                    ulong guid = unit.Guid; uint entry = unit.Entry; var point = unit.Location;
                    if (!Finite(point.X) || !Finite(point.Y) || !Finite(point.Z) || origin.Distance(point) > NearbyGiverRadius ||
                        !unit.GetCachedInfo(out var cache)) continue;
                    int first = checked((int)cache.GroupID), second = checked((int)cache.GroupID2);
                    if (!wanted.Contains(first) && !wanted.Contains(second)) continue;
                    if (!unit.IsValid || unit.Guid != guid || unit.Entry != entry) continue;
                    values.Add(new QuestCreatureCreditObservation { Entry = checked((int)entry), Credit1 = first, Credit2 = second,
                        Guid = guid, PlayerGuid = player, ObservedUtc = now, MapId = map,
                        X = point.X, Y = point.Y, Z = point.Z, AliveAttackableSelectable = true });
                }
                catch (Exception error) when (error is not ThreadInterruptedException && error is not OperationCanceledException) { }
            }
            return ReferenceEquals(memory, ObjectManager.Wow) && ReferenceEquals(me, ObjectManager.Me) && me.IsValid &&
                me.Guid == player && (int)me.MapId == map ? values.ToArray() : Array.Empty<QuestCreatureCreditObservation>();
        }

        private static IEnumerable<SpawnPoint> GetObservedObjectiveSpawns(
            QuestEntry quest, QuestObjective objective, QuestSchedulerAcceptedQuest accepted,
            QuestDatabase db, QuestSchedulerSnapshot snapshot)
        {
            var stored = GetObjectiveSpawns(objective, db).ToArray();
            if (objective.Type != ObjectiveType.KillMob || objective.MobId <= 0 || objective.KillCount <= 0 ||
                (quest.SpecialFlags & 0x20) != 0 || accepted == null || accepted.IsFailed || accepted.IsCompleted ||
                accepted.NormalObjectiveIds == null || accepted.NormalObjectiveRequiredCounts == null ||
                accepted.NormalObjectiveIds.Count != 4 || accepted.NormalObjectiveRequiredCounts.Count != 4)
                return stored;
            var slots = Enumerable.Range(0, 4).Where(index => accepted.NormalObjectiveIds[index] == objective.MobId).ToArray();
            if (slots.Length != 1 || accepted.NormalObjectiveRequiredCounts[slots[0]] != objective.KillCount) return stored;
            var observed = (snapshot.CreatureCredits ?? Array.Empty<QuestCreatureCreditObservation>())
                .Where(value => value != null && value.Entry > 0 && value.Guid != 0 && value.AliveAttackableSelectable &&
                    (value.Credit1 == objective.MobId || value.Credit2 == objective.MobId) &&
                    snapshot.PlayerGuid != 0 && value.PlayerGuid == snapshot.PlayerGuid && value.ObservedUtc == snapshot.UtcNow &&
                    value.MapId == snapshot.MapId && Finite(value.X) && Finite(value.Y) && Finite(value.Z) &&
                    Math.Pow(value.X - snapshot.X, 2) + Math.Pow(value.Y - snapshot.Y, 2) + Math.Pow(value.Z - snapshot.Z, 2)
                        <= NearbyGiverRadius * NearbyGiverRadius)
                .Select(value => new SpawnPoint { Map = value.MapId, X = value.X, Y = value.Y, Z = value.Z }).ToArray();
            // The host's GrindObjective already recognizes cached GroupID/GroupID2.
            // Add observed locations without rewriting the credit ID or inventing a
            // script action. Exact stored safety vetoes survive the added geometry.
            foreach (var point in observed)
            {
                var exact = stored.Where(value => value.Map == point.Map && value.X == point.X && value.Y == point.Y && value.Z == point.Z);
                if (exact.Any(value => value.IsKnownSafe == false)) point.IsKnownSafe = false;
                if (exact.Any(value => value.IsKnownReachable == false)) point.IsKnownReachable = false;
            }
            return stored.Concat(observed).DistinctBy(value => (value.Map, value.X, value.Y, value.Z));
        }
    }
}
