using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Styx.Helpers;
using Styx.Logic.AreaManagement.Triangulation;
using Styx.Logic.Pathing;
using Styx.Logic.Questing;

namespace Styx.Logic.AreaManagement;

/// <summary>
/// Represents a quest area with dynamic hotspot generation.
/// </summary>
public class QuestArea : GrindArea
{
    private readonly List<List<Vector3>> _areaDefinitions;
    private readonly CircularQueue<Hotspot> _circularHotspots = new();

    public QuestArea(PlayerQuest quest, IList<WoWQuestStep> questSteps)
    {
        Quest = quest;
        _areaDefinitions = new List<List<Vector3>>(questSteps.Count);

        for (int i = 0; i < questSteps.Count; i++)
        {
            var areaPoints = questSteps[i].AreaPoints.ToList();
            _areaDefinitions.Add(areaPoints.ConvertAll(v2 => new Vector3(v2.X, v2.Y, 0f)));
        }
    }

    /// <summary>
    /// Gets the associated quest.
    /// </summary>
    public PlayerQuest Quest { get; }

    /// <summary>
    /// Gets whether hotspots have been created.
    /// </summary>
    public bool HotspotsCreated { get; private set; }

    /// <summary>
    /// Gets the area type.
    /// </summary>
    public override AreaType Type => AreaType.Quest;

    /// <summary>
    /// Gets the area definitions.
    /// </summary>
    public List<List<Vector3>> AreaDefinitions => _areaDefinitions;

    /// <summary>
    /// Creates hotspots from the quest area definitions.
    /// </summary>
    public void CreateHotspots()
    {
        if (HotspotsCreated)
            return;

        var polys = _areaDefinitions.ConvertAll(lv3 => lv3.ConvertAll(v3 => new Vector2(v3.X, v3.Y)));
        foreach (var pnt in GenerateHotspots(polys))
        {
            WoWPoint woWPoint = new WoWPoint(pnt.X, pnt.Y, pnt.Z);
            _circularHotspots.Enqueue(woWPoint.ToHotspot());
            Hotspots.Add(woWPoint);
        }

        if (_circularHotspots.Count <= 0)
        {
            Logging.Write($"No hotspots created for quest: {Quest.Name}");
            return;
        }

        CircledHotspots = _circularHotspots;
        HotspotsCreated = true;
    }

    /// <summary>
    /// Generates hotspots from polygon definitions using triangulation.
    /// </summary>
    private static List<Vector3> GenerateHotspots(IList<List<Vector2>> polys)
    {
        var result = new List<Vector3>();

        for (int i = 0; i < polys.Count; i++)
        {
            var poly = polys[i];
            if (poly.Count == 0)
                continue;

            if (poly.Count >= 3)
            {
                // Use triangulation for complex polygons
                var triangles = Triangulate(poly);
                foreach (var triangle in triangles)
                {
                    var v1 = poly[triangle.P1];
                    var v2 = poly[triangle.P2];
                    var v3 = poly[triangle.P3];
                    var centroid = (v1 + v2 + v3) / 3f;

                    var xnaPos = new Tripper.XNAMath.Vector3(centroid.X, centroid.Y, 0f);
                    if (Navigator.FindHeight(ref xnaPos) && float.IsFinite(xnaPos.Z))
                    {
                        result.Add(new Vector3(xnaPos.X, xnaPos.Y, xnaPos.Z));
                    }
                }
            }
            else
            {
                // For 1-2 points, just use them directly
                foreach (var point in poly)
                {
                    var xnaPos = new Tripper.XNAMath.Vector3(point.X, point.Y, 0f);
                    if (Navigator.FindHeight(ref xnaPos) && float.IsFinite(xnaPos.Z))
                    {
                        result.Add(new Vector3(xnaPos.X, xnaPos.Y, xnaPos.Z));
                    }
                }
            }
        }

        return result;
    }

    /// <summary>
    /// FEAT-41: Ear-clipping triangulation for simple (convex and concave) polygons.
    /// Falls back to fan triangulation for < 4 vertices.
    /// </summary>
    private static List<Triangle> Triangulate(List<Vector2> polygon)
    {
        var triangles = new List<Triangle>();
        
        if (polygon.Count < 3)
            return triangles;

        // For 3 vertices, just one triangle
        if (polygon.Count == 3)
        {
            triangles.Add(new Triangle(0, 1, 2));
            return triangles;
        }

        // Build index list for ear-clipping
        var indices = new List<int>(polygon.Count);
        
        // Determine winding order (CW vs CCW)
        float area = 0f;
        for (int i = 0; i < polygon.Count; i++)
        {
            int j = (i + 1) % polygon.Count;
            area += polygon[i].X * polygon[j].Y;
            area -= polygon[j].X * polygon[i].Y;
        }

        if (area > 0) // CCW
        {
            for (int i = 0; i < polygon.Count; i++)
                indices.Add(i);
        }
        else // CW — reverse
        {
            for (int i = polygon.Count - 1; i >= 0; i--)
                indices.Add(i);
        }

        int n = indices.Count;
        int errorCount = 0;

        while (n > 2)
        {
            bool earFound = false;
            for (int i = 0; i < n; i++)
            {
                int prev = (i + n - 1) % n;
                int next = (i + 1) % n;

                int a = indices[prev];
                int b = indices[i];
                int c = indices[next];

                var va = polygon[a];
                var vb = polygon[b];
                var vc = polygon[c];

                // Check if angle at b is convex (cross product > 0 for CCW)
                float cross = (vb.X - va.X) * (vc.Y - va.Y) - (vb.Y - va.Y) * (vc.X - va.X);
                if (cross <= 0)
                    continue;

                // Check no other vertex inside this triangle
                bool isEar = true;
                for (int j = 0; j < n; j++)
                {
                    if (j == prev || j == i || j == next)
                        continue;

                    if (PointInTriangle(polygon[indices[j]], va, vb, vc))
                    {
                        isEar = false;
                        break;
                    }
                }

                if (isEar)
                {
                    triangles.Add(new Triangle(a, b, c));
                    indices.RemoveAt(i);
                    n--;
                    earFound = true;
                    break;
                }
            }

            if (!earFound)
            {
                // Degenerate polygon — fall back to fan
                errorCount++;
                if (errorCount > n)
                    break;
            }
        }

        return triangles;
    }

    /// <summary>
    /// Checks if point p is inside triangle (a, b, c) using barycentric method.
    /// </summary>
    private static bool PointInTriangle(Vector2 p, Vector2 a, Vector2 b, Vector2 c)
    {
        float d1 = Sign(p, a, b);
        float d2 = Sign(p, b, c);
        float d3 = Sign(p, c, a);
        bool hasNeg = (d1 < 0) || (d2 < 0) || (d3 < 0);
        bool hasPos = (d1 > 0) || (d2 > 0) || (d3 > 0);
        return !(hasNeg && hasPos);
    }

    private static float Sign(Vector2 p1, Vector2 p2, Vector2 p3)
    {
        return (p1.X - p3.X) * (p2.Y - p3.Y) - (p2.X - p3.X) * (p1.Y - p3.Y);
    }
}
