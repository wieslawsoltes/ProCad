using System.Globalization;
using ACadInspector.Editing.Identifiers;
using ACadInspector.Editing.Operations;
using ACadInspector.Editing.Selection;
using ACadInspector.Editing.Sessions;
using CSMath;

namespace ACadInspector.Editing.Commands;

internal static class CadAnnotationCommandParsing
{
    public static bool TryParseArguments(
        IReadOnlyList<string> args,
        int minimumPoints,
        int maximumPoints,
        IReadOnlySet<string> keywords,
        out IReadOnlyList<XYZ> points,
        out string? error,
        string usage)
    {
        var parsed = new List<XYZ>(Math.Max(1, minimumPoints));
        error = null;

        foreach (var token in args)
        {
            if (string.IsNullOrWhiteSpace(token))
            {
                continue;
            }

            if (CadOperationPayloadCodec.TryParsePoint(token, out var point))
            {
                parsed.Add(point);
                continue;
            }

            if (keywords.Contains(token))
            {
                continue;
            }

            error = usage;
            points = Array.Empty<XYZ>();
            return false;
        }

        if (parsed.Count < minimumPoints || parsed.Count > maximumPoints)
        {
            error = usage;
            points = Array.Empty<XYZ>();
            return false;
        }

        points = parsed;
        return true;
    }
}

public abstract class CadAnnotationCadCommandBase : ICadDescribedCommandHandler
{
    private const double AnnotationTextHeight = 2.5;
    private const double Epsilon = 1e-9;
    private readonly int _minimumPoints;
    private readonly int _maximumPoints;
    private readonly string _usage;
    private readonly IReadOnlySet<string> _keywords;

    protected CadAnnotationCadCommandBase(
        string name,
        IReadOnlyList<string> aliases,
        string description,
        string usage,
        IReadOnlyList<CadCommandParameterDescriptor> parameters,
        IReadOnlyList<CadCommandKeywordDescriptor> keywords,
        int minimumPoints,
        int maximumPoints)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Command name is required.", nameof(name));
        }

        Name = name.Trim().ToUpperInvariant();
        Aliases = aliases ?? Array.Empty<string>();
        _usage = usage;
        _minimumPoints = minimumPoints;
        _maximumPoints = Math.Max(minimumPoints, maximumPoints);
        _keywords = new HashSet<string>(
            keywords.Select(static keyword => keyword.Keyword),
            StringComparer.OrdinalIgnoreCase);
        Descriptor = new CadCommandDescriptor(
            Name,
            Aliases,
            description,
            new[]
            {
                new CadCommandSyntax(
                    Usage: usage,
                    Description: description,
                    Parameters: parameters,
                    Keywords: keywords)
            });
    }

    public string Name { get; }
    public IReadOnlyList<string> Aliases { get; }
    public CadCommandDescriptor Descriptor { get; }

    public bool CanExecute(CadCommandContext context)
    {
        return context.Session is not null;
    }

    public ValueTask<CadCommandResult> ExecuteAsync(CadCommandContext context)
    {
        if (!CadCommandSessionHelper.TryGetSession(context, out var session, out var sessionError))
        {
            return ValueTask.FromResult(sessionError);
        }

        if (!CadAnnotationCommandParsing.TryParseArguments(
                context.Arguments,
                _minimumPoints,
                _maximumPoints,
                _keywords,
                out var points,
                out var parseError,
                _usage))
        {
            return ValueTask.FromResult(CadCommandResult.Fail(parseError!));
        }

        return ValueTask.FromResult(ExecuteCore(session, points));
    }

    protected abstract CadCommandResult ExecuteCore(CadDocumentSession session, IReadOnlyList<XYZ> points);

    protected CadCommandResult ApplyCreatedOperations(
        CadDocumentSession session,
        IReadOnlyList<CadOperation> forwardOperations,
        IReadOnlyList<CadOperation> inverseOperations,
        IReadOnlyList<CadEntityId> createdIds,
        string successMessage)
    {
        if (forwardOperations.Count == 0 || inverseOperations.Count == 0)
        {
            return CadCommandResult.Fail($"{Name} did not produce any operations.");
        }

        var actorId = session.SessionId.Value;
        var forwardBatch = session.NextBatch(actorId, forwardOperations);
        var inverseBatch = session.NextBatch(actorId, inverseOperations);
        session.Apply(forwardBatch);
        session.UndoRedo.Record(forwardBatch, inverseBatch);

        var createdEntities = new List<object?>(createdIds.Count);
        foreach (var id in createdIds)
        {
            if (session.EntityIndex.TryGetEntity(id, out var entity))
            {
                createdEntities.Add(entity);
            }
        }

        if (createdEntities.Count > 0)
        {
            session.SetSelection(createdEntities, CadSelectionMode.Replace);
        }

        return CadCommandResult.Ok(successMessage, forwardOperations);
    }

    protected static CadOperation CreateAnnotationText(CadEntityId entityId, XYZ point, string value, double rotationRadians, CadDocumentSession session)
    {
        return CadOperationPayloadCodec.CreateText(
                entityId,
                point,
                point,
                AnnotationTextHeight,
                rotationRadians,
                value,
                XYZ.AxisZ)
            .WithCurrentProperties(session.Document);
    }

    protected static CadOperation DeleteAnnotationText(CadEntityId entityId, XYZ point, string value, double rotationRadians)
    {
        return CadOperationPayloadCodec.DeleteText(
            entityId,
            point,
            point,
            AnnotationTextHeight,
            rotationRadians,
            value,
            XYZ.AxisZ);
    }

    protected static double Distance2D(XYZ first, XYZ second)
    {
        var dx = second.X - first.X;
        var dy = second.Y - first.Y;
        return Math.Sqrt((dx * dx) + (dy * dy));
    }

    protected static bool TryNormalize2D(XYZ vector, out XYZ normalized)
    {
        var length = Math.Sqrt((vector.X * vector.X) + (vector.Y * vector.Y));
        if (length <= Epsilon)
        {
            normalized = XYZ.Zero;
            return false;
        }

        normalized = new XYZ(vector.X / length, vector.Y / length, 0.0);
        return true;
    }

    protected static XYZ MidPoint(XYZ first, XYZ second)
    {
        return new XYZ(
            (first.X + second.X) * 0.5,
            (first.Y + second.Y) * 0.5,
            (first.Z + second.Z) * 0.5);
    }

    protected static string FormatMeasurement(double value)
    {
        return value.ToString("0.###", CultureInfo.InvariantCulture);
    }

    protected static double NormalizeAngleSigned(double angle)
    {
        var value = angle;
        while (value <= -Math.PI)
        {
            value += Math.PI * 2.0;
        }

        while (value > Math.PI)
        {
            value -= Math.PI * 2.0;
        }

        return value;
    }
}

public sealed class DimLinearCadCommand : CadAnnotationCadCommandBase
{
    public DimLinearCadCommand()
        : base(
            name: "DIMLINEAR",
            aliases: ["DLI"],
            description: "Creates a linear dimension.",
            usage: "DIMLINEAR p1 p2 p3 [Horizontal|Vertical|Rotated|Text|MText]",
            parameters:
            [
                new CadCommandParameterDescriptor("p1", CadCommandParameterKind.Coordinate, Description: "First extension origin"),
                new CadCommandParameterDescriptor("p2", CadCommandParameterKind.Coordinate, Description: "Second extension origin"),
                new CadCommandParameterDescriptor("p3", CadCommandParameterKind.Coordinate, Description: "Dimension line location")
            ],
            keywords:
            [
                new CadCommandKeywordDescriptor("Horizontal"),
                new CadCommandKeywordDescriptor("Vertical"),
                new CadCommandKeywordDescriptor("Rotated"),
                new CadCommandKeywordDescriptor("Text"),
                new CadCommandKeywordDescriptor("MText")
            ],
            minimumPoints: 3,
            maximumPoints: 3)
    {
    }

    protected override CadCommandResult ExecuteCore(CadDocumentSession session, IReadOnlyList<XYZ> points)
    {
        var p1 = points[0];
        var p2 = points[1];
        var p3 = points[2];

        var horizontal = Math.Abs(p2.X - p1.X) >= Math.Abs(p2.Y - p1.Y);
        var lineStart = horizontal
            ? new XYZ(p1.X, p3.Y, p3.Z)
            : new XYZ(p3.X, p1.Y, p3.Z);
        var lineEnd = horizontal
            ? new XYZ(p2.X, p3.Y, p3.Z)
            : new XYZ(p3.X, p2.Y, p3.Z);
        var measurement = horizontal
            ? Math.Abs(p2.X - p1.X)
            : Math.Abs(p2.Y - p1.Y);
        if (measurement <= 1e-9)
        {
            return CadCommandResult.Fail("DIMLINEAR requires non-zero measured distance.");
        }

        var textRotation = horizontal ? 0.0 : Math.PI * 0.5;
        var textBase = MidPoint(lineStart, lineEnd);
        var textPosition = horizontal
            ? new XYZ(textBase.X, textBase.Y + 0.5, textBase.Z)
            : new XYZ(textBase.X + 0.5, textBase.Y, textBase.Z);
        var textValue = FormatMeasurement(measurement);

        var lineId = CadEntityId.New();
        var textId = CadEntityId.New();
        var lineCreate = CadOperationPayloadCodec.CreateLine(lineId, lineStart, lineEnd).WithCurrentProperties(session.Document);
        var textCreate = CreateAnnotationText(textId, textPosition, textValue, textRotation, session);
        var forward = new CadOperation[] { lineCreate, textCreate };
        var inverse = new CadOperation[]
        {
            DeleteAnnotationText(textId, textPosition, textValue, textRotation),
            CadOperationPayloadCodec.DeleteLine(lineId, lineStart, lineEnd)
        };

        return ApplyCreatedOperations(
            session,
            forward,
            inverse,
            [lineId, textId],
            "Created DIMLINEAR annotation.");
    }
}

public sealed class DimAlignedCadCommand : CadAnnotationCadCommandBase
{
    public DimAlignedCadCommand()
        : base(
            name: "DIMALIGNED",
            aliases: ["DAL"],
            description: "Creates an aligned dimension.",
            usage: "DIMALIGNED p1 p2 p3 [Text|MText|Angle]",
            parameters:
            [
                new CadCommandParameterDescriptor("p1", CadCommandParameterKind.Coordinate, Description: "First extension origin"),
                new CadCommandParameterDescriptor("p2", CadCommandParameterKind.Coordinate, Description: "Second extension origin"),
                new CadCommandParameterDescriptor("p3", CadCommandParameterKind.Coordinate, Description: "Dimension line location")
            ],
            keywords:
            [
                new CadCommandKeywordDescriptor("Text"),
                new CadCommandKeywordDescriptor("MText"),
                new CadCommandKeywordDescriptor("Angle")
            ],
            minimumPoints: 3,
            maximumPoints: 3)
    {
    }

    protected override CadCommandResult ExecuteCore(CadDocumentSession session, IReadOnlyList<XYZ> points)
    {
        var p1 = points[0];
        var p2 = points[1];
        var p3 = points[2];
        if (!TryNormalize2D(new XYZ(p2.X - p1.X, p2.Y - p1.Y, 0.0), out var dir))
        {
            return CadCommandResult.Fail("DIMALIGNED requires two distinct extension points.");
        }

        var normal = new XYZ(-dir.Y, dir.X, 0.0);
        var offset = ((p3.X - p1.X) * normal.X) + ((p3.Y - p1.Y) * normal.Y);
        var lineStart = new XYZ(p1.X + (normal.X * offset), p1.Y + (normal.Y * offset), p3.Z);
        var lineEnd = new XYZ(p2.X + (normal.X * offset), p2.Y + (normal.Y * offset), p3.Z);
        var measurement = Distance2D(p1, p2);
        if (measurement <= 1e-9)
        {
            return CadCommandResult.Fail("DIMALIGNED requires non-zero measured distance.");
        }

        var textPosition = new XYZ(
            (lineStart.X + lineEnd.X) * 0.5 + (normal.X * 0.5),
            (lineStart.Y + lineEnd.Y) * 0.5 + (normal.Y * 0.5),
            lineStart.Z);
        var textRotation = Math.Atan2(lineEnd.Y - lineStart.Y, lineEnd.X - lineStart.X);
        var textValue = FormatMeasurement(measurement);

        var lineId = CadEntityId.New();
        var textId = CadEntityId.New();
        var lineCreate = CadOperationPayloadCodec.CreateLine(lineId, lineStart, lineEnd).WithCurrentProperties(session.Document);
        var textCreate = CreateAnnotationText(textId, textPosition, textValue, textRotation, session);
        var forward = new CadOperation[] { lineCreate, textCreate };
        var inverse = new CadOperation[]
        {
            DeleteAnnotationText(textId, textPosition, textValue, textRotation),
            CadOperationPayloadCodec.DeleteLine(lineId, lineStart, lineEnd)
        };

        return ApplyCreatedOperations(
            session,
            forward,
            inverse,
            [lineId, textId],
            "Created DIMALIGNED annotation.");
    }
}

public sealed class DimRadiusCadCommand : CadAnnotationCadCommandBase
{
    public DimRadiusCadCommand()
        : base(
            name: "DIMRADIUS",
            aliases: ["DRA"],
            description: "Creates a radius dimension.",
            usage: "DIMRADIUS p1 p2 [Text|MText|Angle]",
            parameters:
            [
                new CadCommandParameterDescriptor("p1", CadCommandParameterKind.Coordinate, Description: "Arc/circle reference"),
                new CadCommandParameterDescriptor("p2", CadCommandParameterKind.Coordinate, Description: "Dimension line location")
            ],
            keywords:
            [
                new CadCommandKeywordDescriptor("Text"),
                new CadCommandKeywordDescriptor("MText"),
                new CadCommandKeywordDescriptor("Angle")
            ],
            minimumPoints: 2,
            maximumPoints: 2)
    {
    }

    protected override CadCommandResult ExecuteCore(CadDocumentSession session, IReadOnlyList<XYZ> points)
    {
        var center = points[0];
        var point = points[1];
        var radius = Distance2D(center, point);
        if (radius <= 1e-9)
        {
            return CadCommandResult.Fail("DIMRADIUS requires two distinct points.");
        }

        var lineId = CadEntityId.New();
        var textId = CadEntityId.New();
        var lineCreate = CadOperationPayloadCodec.CreateLine(lineId, center, point).WithCurrentProperties(session.Document);
        var textValue = $"R={FormatMeasurement(radius)}";
        var textPosition = MidPoint(center, point);
        var textRotation = Math.Atan2(point.Y - center.Y, point.X - center.X);
        var textCreate = CreateAnnotationText(textId, textPosition, textValue, textRotation, session);
        var forward = new CadOperation[] { lineCreate, textCreate };
        var inverse = new CadOperation[]
        {
            DeleteAnnotationText(textId, textPosition, textValue, textRotation),
            CadOperationPayloadCodec.DeleteLine(lineId, center, point)
        };

        return ApplyCreatedOperations(
            session,
            forward,
            inverse,
            [lineId, textId],
            "Created DIMRADIUS annotation.");
    }
}

public sealed class DimDiameterCadCommand : CadAnnotationCadCommandBase
{
    public DimDiameterCadCommand()
        : base(
            name: "DIMDIAMETER",
            aliases: ["DIA"],
            description: "Creates a diameter dimension.",
            usage: "DIMDIAMETER p1 p2 [Text|MText|Angle]",
            parameters:
            [
                new CadCommandParameterDescriptor("p1", CadCommandParameterKind.Coordinate, Description: "Arc/circle reference"),
                new CadCommandParameterDescriptor("p2", CadCommandParameterKind.Coordinate, Description: "Dimension line location")
            ],
            keywords:
            [
                new CadCommandKeywordDescriptor("Text"),
                new CadCommandKeywordDescriptor("MText"),
                new CadCommandKeywordDescriptor("Angle")
            ],
            minimumPoints: 2,
            maximumPoints: 2)
    {
    }

    protected override CadCommandResult ExecuteCore(CadDocumentSession session, IReadOnlyList<XYZ> points)
    {
        var center = points[0];
        var point = points[1];
        if (!TryNormalize2D(new XYZ(point.X - center.X, point.Y - center.Y, 0.0), out var direction))
        {
            return CadCommandResult.Fail("DIMDIAMETER requires two distinct points.");
        }

        var radius = Distance2D(center, point);
        var opposite = new XYZ(
            center.X - (direction.X * radius),
            center.Y - (direction.Y * radius),
            point.Z);
        var diameter = radius * 2.0;

        var lineId = CadEntityId.New();
        var textId = CadEntityId.New();
        var lineCreate = CadOperationPayloadCodec.CreateLine(lineId, opposite, point).WithCurrentProperties(session.Document);
        var textValue = $"D={FormatMeasurement(diameter)}";
        var textPosition = MidPoint(opposite, point);
        var textRotation = Math.Atan2(point.Y - opposite.Y, point.X - opposite.X);
        var textCreate = CreateAnnotationText(textId, textPosition, textValue, textRotation, session);
        var forward = new CadOperation[] { lineCreate, textCreate };
        var inverse = new CadOperation[]
        {
            DeleteAnnotationText(textId, textPosition, textValue, textRotation),
            CadOperationPayloadCodec.DeleteLine(lineId, opposite, point)
        };

        return ApplyCreatedOperations(
            session,
            forward,
            inverse,
            [lineId, textId],
            "Created DIMDIAMETER annotation.");
    }
}

public sealed class DimAngularCadCommand : CadAnnotationCadCommandBase
{
    public DimAngularCadCommand()
        : base(
            name: "DIMANGULAR",
            aliases: ["DAN"],
            description: "Creates an angular dimension.",
            usage: "DIMANGULAR p1 p2 p3 p4 [Text|MText|Quadrant]",
            parameters:
            [
                new CadCommandParameterDescriptor("p1", CadCommandParameterKind.Coordinate, Description: "First line point"),
                new CadCommandParameterDescriptor("p2", CadCommandParameterKind.Coordinate, Description: "Second line point"),
                new CadCommandParameterDescriptor("p3", CadCommandParameterKind.Coordinate, Description: "Angle vertex"),
                new CadCommandParameterDescriptor("p4", CadCommandParameterKind.Coordinate, Description: "Dimension arc location")
            ],
            keywords:
            [
                new CadCommandKeywordDescriptor("Text"),
                new CadCommandKeywordDescriptor("MText"),
                new CadCommandKeywordDescriptor("Quadrant")
            ],
            minimumPoints: 4,
            maximumPoints: 4)
    {
    }

    protected override CadCommandResult ExecuteCore(CadDocumentSession session, IReadOnlyList<XYZ> points)
    {
        var first = points[0];
        var second = points[1];
        var vertex = points[2];
        var arcPoint = points[3];

        if (!TryNormalize2D(new XYZ(first.X - vertex.X, first.Y - vertex.Y, 0.0), out var firstDir) ||
            !TryNormalize2D(new XYZ(second.X - vertex.X, second.Y - vertex.Y, 0.0), out var secondDir))
        {
            return CadCommandResult.Fail("DIMANGULAR requires valid rays from the vertex.");
        }

        var radius = Distance2D(vertex, arcPoint);
        if (radius <= 1e-9)
        {
            return CadCommandResult.Fail("DIMANGULAR requires non-zero arc radius.");
        }

        var startAngle = Math.Atan2(firstDir.Y, firstDir.X);
        var endAngle = Math.Atan2(secondDir.Y, secondDir.X);
        var delta = NormalizeAngleSigned(endAngle - startAngle);
        if (Math.Abs(delta) <= 1e-9)
        {
            return CadCommandResult.Fail("DIMANGULAR requires a non-zero included angle.");
        }

        if (delta < 0.0)
        {
            (startAngle, endAngle) = (endAngle, startAngle);
            delta = -delta;
        }

        var measurementDeg = delta * (180.0 / Math.PI);
        var midAngle = startAngle + (delta * 0.5);
        var textPosition = new XYZ(
            vertex.X + (Math.Cos(midAngle) * radius),
            vertex.Y + (Math.Sin(midAngle) * radius),
            vertex.Z);
        var textValue = $"{FormatMeasurement(measurementDeg)}°";

        var arcId = CadEntityId.New();
        var textId = CadEntityId.New();
        var arcCreate = CadOperationPayloadCodec.CreateArc(arcId, vertex, radius, startAngle, endAngle)
            .WithCurrentProperties(session.Document);
        var textCreate = CreateAnnotationText(textId, textPosition, textValue, midAngle, session);
        var forward = new CadOperation[] { arcCreate, textCreate };
        var inverse = new CadOperation[]
        {
            DeleteAnnotationText(textId, textPosition, textValue, midAngle),
            CadOperationPayloadCodec.DeleteArc(arcId, vertex, radius, startAngle, endAngle)
        };

        return ApplyCreatedOperations(
            session,
            forward,
            inverse,
            [arcId, textId],
            "Created DIMANGULAR annotation.");
    }
}

public sealed class LeaderCadCommand : CadAnnotationCadCommandBase
{
    public LeaderCadCommand()
        : base(
            name: "LEADER",
            aliases: ["LE"],
            description: "Creates a leader annotation.",
            usage: "LEADER p1 p2 [p3 ...] [Annotation|Format|Undo]",
            parameters:
            [
                new CadCommandParameterDescriptor("p1", CadCommandParameterKind.Coordinate, Description: "Leader start point"),
                new CadCommandParameterDescriptor("p2", CadCommandParameterKind.Coordinate, Description: "Leader landing point"),
                new CadCommandParameterDescriptor("p3+", CadCommandParameterKind.Coordinate, IsOptional: true, IsVariadic: true, Description: "Optional extra vertices")
            ],
            keywords:
            [
                new CadCommandKeywordDescriptor("Annotation"),
                new CadCommandKeywordDescriptor("Format"),
                new CadCommandKeywordDescriptor("Undo")
            ],
            minimumPoints: 2,
            maximumPoints: int.MaxValue)
    {
    }

    protected override CadCommandResult ExecuteCore(CadDocumentSession session, IReadOnlyList<XYZ> points)
    {
        var leaderId = CadEntityId.New();
        var forward = new CadOperation[]
        {
            CadOperationPayloadCodec.CreateLwPolyline(leaderId, points, isClosed: false)
                .WithCurrentProperties(session.Document)
        };
        var inverse = new CadOperation[]
        {
            CadOperationPayloadCodec.DeleteLwPolyline(leaderId, points, isClosed: false)
        };

        return ApplyCreatedOperations(
            session,
            forward,
            inverse,
            [leaderId],
            "Created LEADER annotation.");
    }
}

public sealed class MLeaderCadCommand : CadAnnotationCadCommandBase
{
    public MLeaderCadCommand()
        : base(
            name: "MLEADER",
            aliases: ["MLE"],
            description: "Creates a multileader annotation.",
            usage: "MLEADER p1 p2 [p3 ...] [Content|Style|Landing]",
            parameters:
            [
                new CadCommandParameterDescriptor("p1", CadCommandParameterKind.Coordinate, Description: "Leader start point"),
                new CadCommandParameterDescriptor("p2", CadCommandParameterKind.Coordinate, Description: "Leader landing point"),
                new CadCommandParameterDescriptor("p3+", CadCommandParameterKind.Coordinate, IsOptional: true, IsVariadic: true, Description: "Optional extra vertices")
            ],
            keywords:
            [
                new CadCommandKeywordDescriptor("Content"),
                new CadCommandKeywordDescriptor("Style"),
                new CadCommandKeywordDescriptor("Landing")
            ],
            minimumPoints: 2,
            maximumPoints: int.MaxValue)
    {
    }

    protected override CadCommandResult ExecuteCore(CadDocumentSession session, IReadOnlyList<XYZ> points)
    {
        var leaderId = CadEntityId.New();
        var textId = CadEntityId.New();
        var textPoint = points[^1];
        var forward = new CadOperation[]
        {
            CadOperationPayloadCodec.CreateLwPolyline(leaderId, points, isClosed: false)
                .WithCurrentProperties(session.Document),
            CadOperationPayloadCodec.CreateMText(
                    textId,
                    textPoint,
                    new XYZ(1.0, 0.0, 0.0),
                    2.5,
                    30.0,
                    "MLeader",
                    XYZ.AxisZ)
                .WithCurrentProperties(session.Document)
        };
        var inverse = new CadOperation[]
        {
            CadOperationPayloadCodec.DeleteMText(
                textId,
                textPoint,
                new XYZ(1.0, 0.0, 0.0),
                2.5,
                30.0,
                "MLeader",
                XYZ.AxisZ),
            CadOperationPayloadCodec.DeleteLwPolyline(leaderId, points, isClosed: false)
        };

        return ApplyCreatedOperations(
            session,
            forward,
            inverse,
            [leaderId, textId],
            "Created MLEADER annotation.");
    }
}
