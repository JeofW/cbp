using System;
using Styx.Logic.Pathing;

namespace Styx.Logic;

/// <summary>
/// Bounded escape for one mounted actor/world. Movement flags alone cannot prove
/// progress; a brief path pause alone cannot prove escape failure. Thresholds
/// below are recovery policy, not assertions about client combat mechanics.
/// </summary>
internal sealed class MountedTravelProgress
{
    private object? actor, memory;
    private ulong guid;
    private uint map;
    private WoWPoint position, destination;
    private float bestDistance;
    private long movedAt, approachedAt, sampledAt, healthAt;
    private double initialHealth;

    public void Reset() { actor = memory = null; guid = 0; }

    public bool ShouldStop(object currentActor, object currentMemory, ulong currentGuid, uint currentMap,
        long now, WoWPoint currentPosition, WoWPoint target, double health,
        bool rooted, bool stunned, out string reason)
    {
        reason = string.Empty;
        if (rooted || stunned) { reason = "movement is prevented"; return true; }
        if (!double.IsFinite(health) || health <= 35) { reason = "health requires defensive combat"; return true; }
        if (target == WoWPoint.Empty || !Finite(target) || !Finite(currentPosition))
        { reason = "no usable escape destination"; return true; }

        float distance = currentPosition.Distance(target);
        if (!ReferenceEquals(actor, currentActor) || !ReferenceEquals(memory, currentMemory)
            || guid != currentGuid || map != currentMap || now < sampledAt)
        {
            actor = currentActor; memory = currentMemory; guid = currentGuid; map = currentMap;
            position = currentPosition; destination = target; bestDistance = distance;
            movedAt = approachedAt = healthAt = sampledAt = now; initialHealth = health;
            return false;
        }
        sampledAt = now;
        if (now - healthAt <= 3000 && initialHealth - health >= 20)
        { reason = "incoming damage is overtaking escape"; return true; }
        if (now - healthAt > 3000 || health > initialHealth)
        { healthAt = now; initialHealth = health; }

        if (position.DistanceSqr(currentPosition) >= 4)
        { position = currentPosition; movedAt = now; }
        if (destination.DistanceSqr(target) > 25)
        {
            // Destination churn cannot renew a stalled escape indefinitely.
            destination = target; bestDistance = distance;
        }
        else if (bestDistance - distance >= 2)
        { bestDistance = distance; approachedAt = now; }

        if (now - movedAt >= 3000) { reason = "no movement progress for three seconds"; return true; }
        if (now - approachedAt >= 6000) { reason = "no route progress for six seconds"; return true; }
        return false;
    }

    private static bool Finite(WoWPoint point) => float.IsFinite(point.X)
        && float.IsFinite(point.Y) && float.IsFinite(point.Z);
}
