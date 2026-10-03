using System;
using System.Linq;
using Styx.Helpers;
using Styx.Logic.Pathing;

namespace Styx.Logic.Questing;

/// <summary>Bounded discovery hints for an absent quest relation, never a spawn or completion receipt.</summary>
internal sealed class QuestRelationSearch
{
    private readonly WoWPoint[] _route;
    private int _index, _visited;
    private double _started = double.NaN, _lastClock, _lastProgress;
    private WoWPoint _progress;
    internal bool Exhausted { get; private set; }
    internal int SearchPointCount => _route.Length;
    internal string Phase => Exhausted ? "search-exhausted" : _route.Length == 0 ? "waiting-for-loaded-npc" : "patrol-search";

    internal QuestRelationSearch(uint map, uint quest, uint entry, WoWPoint origin)
    {
        // TrinityCore TDB335.25101, TDB_full_world_335.25101_2025_10_21.sql:
        // creature guid 86873 / id 20159 / map530; creature_addon.path_id868730;
        // waypoint_data, SQL line6046. Quest ender: 20159 -> 10286.
        // Retained primary-reference receipt: regression-followup-20261003/
        // aledis-waypoints-reference.json. These are patrol hints, not 44 spawns.
        _route = map == 530 && quest == 10286 && entry == 20159
            && origin.DistanceSqr(new WoWPoint(-689.583f, 4167.8f, 58.5228f)) <= 25
            ? AledisPatrol() : Array.Empty<WoWPoint>();
        if (_route.Length != 0)
            _index = Enumerable.Range(0, _route.Length).OrderBy(i => _route[i].DistanceSqr(origin)).First();
    }

    internal WoWPoint? Next(double now, WoWPoint position)
    {
        if (!double.IsFinite(now) || !float.IsFinite(position.X) || !float.IsFinite(position.Y) || !float.IsFinite(position.Z)
            || !double.IsNaN(_started) && now < _lastClock)
            throw new ObservationUnavailableException("quest-relation-search", "Search clock or position unavailable.");
        if (double.IsNaN(_started)) { _started = _lastProgress = now; _progress = position; }
        _lastClock = now;
        if (Exhausted) return null;
        if (now - _started >= (_route.Length == 0 ? 30 : 300)) { Exhausted = true; return null; }
        if (_route.Length == 0 || now - _started < 2) return null;
        if (position.DistanceSqr(_progress) >= .25f) { _progress = position; _lastProgress = now; }
        if (now - _lastProgress >= 30) { Exhausted = true; return null; }
        while (_visited < _route.Length && position.DistanceSqr(_route[_index]) <= 64)
        {
            _visited++; _index = (_index + 1) % _route.Length;
        }
        if (_visited >= _route.Length) { Exhausted = true; return null; }
        return _route[_index];
    }

    internal double EstimateRemainingDistance(WoWPoint position)
    {
        if (Exhausted || _route.Length == 0 || _visited >= _route.Length) return 0;
        if (!float.IsFinite(position.X) || !float.IsFinite(position.Y) || !float.IsFinite(position.Z))
            throw new ObservationUnavailableException("quest-relation-search", "Patrol distance origin unavailable.");
        double distance = position.Distance(_route[_index]);
        for (int offset = 1; offset < _route.Length - _visited; offset++)
            distance += _route[(_index + offset - 1) % _route.Length].Distance(_route[(_index + offset) % _route.Length]);
        return distance;
    }

    private static WoWPoint[] AledisPatrol() => new[]
    {
        new WoWPoint(-693.036f,4187.63f,57.0026f), new WoWPoint(-686.684f,4207.12f,56.9095f),
        new WoWPoint(-673.93f,4220f,56.0948f), new WoWPoint(-624.874f,4218.4f,50.713f),
        new WoWPoint(-596.977f,4224.52f,48.6696f), new WoWPoint(-576.122f,4218.94f,48.58f),
        new WoWPoint(-553.78f,4199.66f,46.1163f), new WoWPoint(-519.57f,4119.03f,47.3006f),
        new WoWPoint(-485.123f,4058.18f,53.734f), new WoWPoint(-478.09f,3999.92f,57.6679f),
        new WoWPoint(-496.692f,3932.76f,57.9235f), new WoWPoint(-493.247f,3891.55f,58.318f),
        new WoWPoint(-474.63f,3861.86f,58.288f), new WoWPoint(-450.394f,3863.51f,59.0796f),
        new WoWPoint(-349.7f,3907.2f,69.8351f), new WoWPoint(-279.334f,3999.36f,95.9918f),
        new WoWPoint(-232.732f,4117.03f,97.6089f), new WoWPoint(-202.878f,4449.16f,42.317f),
        new WoWPoint(-242.425f,4671.69f,13.1567f), new WoWPoint(-238.807f,4959.8f,48.0196f),
        new WoWPoint(-253.257f,5040.5f,65.242f), new WoWPoint(-249.016f,5098.27f,79.0187f),
        new WoWPoint(-236.775f,5118.65f,80.6934f), new WoWPoint(-240.258f,5160.67f,82.8653f),
        new WoWPoint(-264.781f,5218.37f,71.2915f), new WoWPoint(-240.832f,5165.19f,82.9734f),
        new WoWPoint(-235.967f,5118.04f,80.8555f), new WoWPoint(-250.706f,5090.25f,76.6508f),
        new WoWPoint(-237.145f,4591.76f,21.3237f), new WoWPoint(-205.064f,4496.58f,32.8833f),
        new WoWPoint(-222.911f,4135.43f,96.5037f), new WoWPoint(-271.347f,4011.59f,98.3579f),
        new WoWPoint(-335.207f,3913.54f,73.2693f), new WoWPoint(-463.987f,3857.78f,57.9562f),
        new WoWPoint(-484.733f,3870.49f,59.5534f), new WoWPoint(-496.964f,3917.13f,57.8782f),
        new WoWPoint(-478.162f,4003.4f,57.2783f), new WoWPoint(-481.994f,4050.11f,54.1059f),
        new WoWPoint(-559.431f,4206.77f,47.1758f), new WoWPoint(-587.111f,4223.03f,48.9446f),
        new WoWPoint(-625.306f,4216.87f,50.828f), new WoWPoint(-674.66f,4220.79f,56.2164f),
        new WoWPoint(-691.373f,4198.99f,56.9174f), new WoWPoint(-689.993f,4167.72f,58.5173f)
    };
}
