using System.Globalization;

namespace Styx.Logic.Questing.Recovery;

public sealed class QuestRelation
{
    public uint Entry { get; init; }
    public double X { get; init; }
    public double Y { get; init; }
    public double Z { get; init; }
}

public sealed class QuestRelationParseResult
{
    public IReadOnlyList<QuestRelation> Relations { get; init; } = Array.Empty<QuestRelation>();
    public IReadOnlyList<string> Diagnostics { get; init; } = Array.Empty<string>();
}

public static class QuestRelationParser
{
    public static QuestRelationParseResult Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return new QuestRelationParseResult();

        var relations = new List<QuestRelation>();
        var diagnostics = new List<string>();
        var segments = text.Split(';');

        for (var index = 0; index < segments.Length; index++)
        {
            var segment = segments[index].Trim();
            var fields = segment.Split(',');
            if (fields.Length != 4)
            {
                diagnostics.Add(
                    $"Segment {index + 1} ('{segment}') rejected: expected entry,x,y,z.");
                continue;
            }

            if (!uint.TryParse(fields[0].Trim(), NumberStyles.None,
                    CultureInfo.InvariantCulture, out var entry) || entry == 0)
            {
                diagnostics.Add(
                    $"Segment {index + 1} ('{segment}') rejected: entry must be a positive integer.");
                continue;
            }

            if (!TryParseCoordinate(fields[1], out var x) ||
                !TryParseCoordinate(fields[2], out var y) ||
                !TryParseCoordinate(fields[3], out var z))
            {
                diagnostics.Add(
                    $"Segment {index + 1} ('{segment}') rejected: coordinates must be finite invariant-culture numbers.");
                continue;
            }

            relations.Add(new QuestRelation { Entry = entry, X = x, Y = y, Z = z });
        }

        return new QuestRelationParseResult
        {
            Relations = Array.AsReadOnly(relations.ToArray()),
            Diagnostics = Array.AsReadOnly(diagnostics.ToArray())
        };
    }

    private static bool TryParseCoordinate(string text, out double coordinate) =>
        double.TryParse(text.Trim(), NumberStyles.Float,
            CultureInfo.InvariantCulture, out coordinate) && double.IsFinite(coordinate);
}
