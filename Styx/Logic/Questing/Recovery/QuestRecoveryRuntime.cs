using System.Reflection;
using Styx.Helpers;

namespace Styx.Logic.Questing.Recovery;

public static class QuestRecoveryRuntime
{
    private const double CriticalDurabilityPercent = 20.0;

    public static void EnsureConfigured(
        string datasetVersion = "unknown",
        string navigationFingerprint = "unknown")
    {
        var player = global::Styx.StyxWoW.Me;
        var characterName = player?.Name ?? "unknown";
        var realmName = player?.RealmName ?? "unknown";
        var coreVersion = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "unknown";
        QuestRecoveryManager.Instance.Configure(new QuestRecoveryEnvironment(
            Settings.SettingsDirectory,
            characterName,
            realmName,
            datasetVersion,
            coreVersion,
            navigationFingerprint));
    }

    public static QuestRecoveryContext Capture(IReadOnlyList<int>? objectiveCounts = null)
    {
        var player = global::Styx.StyxWoW.Me;
        var equipmentFingerprint = "";
        if (player is not null)
        {
            try
            {
                equipmentFingerprint = CreateEquipmentFingerprint(
                    player.Inventory.Equipped.PhysicalItems.Select(item =>
                        (item.Entry, (double)item.MaxDurability, (double)item.DurabilityPercent)));
            }
            catch (Exception ex)
            {
                Logging.WriteWarning($"Quest recovery equipment fingerprint capture failed: {ex}");
            }
        }

        return new QuestRecoveryContext
        {
            PlayerLevel = player?.Level ?? 0,
            EquipmentFingerprint = equipmentFingerprint,
            ObjectiveCounts = objectiveCounts ?? Array.Empty<int>()
        };
    }

    internal static string CreateEquipmentFingerprint(
        IEnumerable<(uint Entry, double MaxDurability, double DurabilityPercent)> equippedItems)
    {
        return string.Join(
            "|",
            equippedItems.Select(item =>
                $"{item.Entry}:{(item.MaxDurability > 0 && item.DurabilityPercent < CriticalDurabilityPercent ? "critical" : "healthy")}"));
    }
}
