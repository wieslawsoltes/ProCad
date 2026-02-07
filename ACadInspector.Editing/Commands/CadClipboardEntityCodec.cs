using ACadInspector.Editing.Clipboard;
using ACadInspector.Editing.Identifiers;
using ACadInspector.Editing.Operations;
using ACadSharp.Entities;
using CSMath;

namespace ACadInspector.Editing.Commands;

internal static class CadClipboardEntityCodec
{
    public static bool TryEncode(Entity source, out CadClipboardEntity entity, out string? error)
    {
        error = null;
        switch (source)
        {
            case Line line:
            {
                var operation = CadOperationPayloadCodec.CreateLine(CadEntityId.New(), line.StartPoint, line.EndPoint)
                    .WithSourceProperties(source);
                entity = CreateClipboardEntity(operation, line.StartPoint);
                return true;
            }
            case Arc arc:
            {
                var operation = CadOperationPayloadCodec.CreateArc(CadEntityId.New(), arc.Center, arc.Radius, arc.StartAngle, arc.EndAngle)
                    .WithSourceProperties(source);
                entity = CreateClipboardEntity(operation, arc.Center);
                return true;
            }
            case Circle circle:
            {
                var operation = CadOperationPayloadCodec.CreateCircle(CadEntityId.New(), circle.Center, circle.Radius)
                    .WithSourceProperties(source);
                entity = CreateClipboardEntity(operation, circle.Center);
                return true;
            }
            case LwPolyline polyline:
            {
                var vertices = CadGeometryTransform.ToVertices(polyline);
                var operation = CadOperationPayloadCodec.CreateLwPolyline(CadEntityId.New(), vertices, polyline.IsClosed)
                    .WithSourceProperties(source);
                var reference = vertices.Count == 0 ? XYZ.Zero : vertices[0];
                entity = CreateClipboardEntity(operation, reference);
                return true;
            }
            case Point point:
            {
                var operation = CadOperationPayloadCodec.CreatePoint(CadEntityId.New(), point.Location)
                    .WithSourceProperties(source);
                entity = CreateClipboardEntity(operation, point.Location);
                return true;
            }
            case XLine xline:
            {
                var direction = CadGeometryTransform.NormalizeDirection(xline.Direction);
                var operation = CadOperationPayloadCodec.CreateXLine(CadEntityId.New(), xline.FirstPoint, direction)
                    .WithSourceProperties(source);
                entity = CreateClipboardEntity(operation, xline.FirstPoint);
                return true;
            }
            case Ray ray:
            {
                var direction = CadGeometryTransform.NormalizeDirection(ray.Direction);
                var operation = CadOperationPayloadCodec.CreateRay(CadEntityId.New(), ray.StartPoint, direction)
                    .WithSourceProperties(source);
                entity = CreateClipboardEntity(operation, ray.StartPoint);
                return true;
            }
            case Ellipse ellipse:
            {
                var operation = CadOperationPayloadCodec.CreateEllipse(
                        CadEntityId.New(),
                        ellipse.Center,
                        ellipse.MajorAxisEndPoint,
                        ellipse.RadiusRatio,
                        ellipse.StartParameter,
                        ellipse.EndParameter,
                        ellipse.Normal)
                    .WithSourceProperties(source);
                entity = CreateClipboardEntity(operation, ellipse.Center);
                return true;
            }
            case Spline spline:
            {
                var fitPoints = spline.FitPoints.ToArray();
                var controlPoints = spline.ControlPoints.ToArray();
                var operation = CadOperationPayloadCodec.CreateSpline(
                        CadEntityId.New(),
                        spline.Degree,
                        spline.IsClosed,
                        spline.IsPeriodic,
                        fitPoints,
                        controlPoints,
                        spline.Knots.ToArray(),
                        spline.Weights.ToArray(),
                        spline.StartTangent,
                        spline.EndTangent,
                        spline.Normal)
                    .WithSourceProperties(source);

                var reference = fitPoints.Length > 0
                    ? fitPoints[0]
                    : controlPoints.Length > 0
                        ? controlPoints[0]
                        : XYZ.Zero;
                entity = CreateClipboardEntity(operation, reference);
                return true;
            }
            case TextEntity text:
            {
                var operation = CadOperationPayloadCodec.CreateText(
                        CadEntityId.New(),
                        text.InsertPoint,
                        text.AlignmentPoint,
                        text.Height,
                        text.Rotation,
                        text.Value,
                        text.Normal)
                    .WithSourceProperties(source);
                entity = CreateClipboardEntity(operation, text.InsertPoint);
                return true;
            }
            case MText mtext:
            {
                var operation = CadOperationPayloadCodec.CreateMText(
                        CadEntityId.New(),
                        mtext.InsertPoint,
                        CadGeometryTransform.NormalizeDirection(mtext.AlignmentPoint),
                        mtext.Height,
                        mtext.RectangleWidth,
                        mtext.Value,
                        mtext.Normal)
                    .WithSourceProperties(source);
                entity = CreateClipboardEntity(operation, mtext.InsertPoint);
                return true;
            }
            case Hatch hatch:
            {
                if (!CadHatchGeometry.TryGetLoops(hatch, out var loops, out var loopError))
                {
                    entity = null!;
                    error = $"Clipboard could not encode HATCH: {loopError}";
                    return false;
                }

                var patternName = CadHatchGeometry.ResolvePatternName(hatch);
                var operation = CadOperationPayloadCodec.CreateHatch(
                        CadEntityId.New(),
                        loops,
                        hatch.IsSolid,
                        patternName,
                        hatch.Normal)
                    .WithSourceProperties(source);
                var reference = loops.Count == 0 || loops[0].Count == 0 ? XYZ.Zero : loops[0][0];
                entity = CreateClipboardEntity(operation, reference);
                return true;
            }
            case Insert insert:
            {
                if (insert.Block is null)
                {
                    entity = null!;
                    error = "Clipboard could not encode INSERT without a block reference.";
                    return false;
                }

                var operation = CadOperationPayloadCodec.CreateInsert(
                        CadEntityId.New(),
                        insert.Block.Name,
                        insert.InsertPoint,
                        insert.XScale,
                        insert.YScale,
                        insert.ZScale,
                        insert.Rotation,
                        insert.Normal)
                    .WithSourceProperties(source);
                entity = CreateClipboardEntity(operation, insert.InsertPoint);
                return true;
            }
            default:
                entity = null!;
                error = $"Clipboard does not support entity type '{source.GetType().Name}' yet.";
                return false;
        }
    }

    public static bool TryDecodeCreateOperation(
        CadClipboardEntity source,
        CadEntityId id,
        XYZ translation,
        out CadOperation operation,
        out string? error)
    {
        error = null;
        var payload = new Dictionary<string, string>(source.Payload, StringComparer.Ordinal);
        var template = new CadOperation(CadOperationKind.CreateEntity, default, payload);

        if (!CadOperationPayloadCodec.TryGetEntityType(template, out var entityType))
        {
            operation = null!;
            error = "Clipboard payload is missing entity type.";
            return false;
        }

        switch (entityType.ToUpperInvariant())
        {
            case CadOperationPayloadCodec.EntityTypeLine:
            {
                if (!CadOperationPayloadCodec.TryGetCreateLine(template, out var start, out var end))
                {
                    break;
                }

                operation = CadOperationPayloadCodec.CreateLine(
                    id,
                    CadGeometryTransform.Translate(start, translation),
                    CadGeometryTransform.Translate(end, translation))
                    .WithCopiedCreateProperties(template);
                return true;
            }
            case CadOperationPayloadCodec.EntityTypeArc:
            {
                if (!CadOperationPayloadCodec.TryGetCreateArc(template, out var center, out var radius, out var startAngle, out var endAngle))
                {
                    break;
                }

                operation = CadOperationPayloadCodec.CreateArc(
                    id,
                    CadGeometryTransform.Translate(center, translation),
                    radius,
                    startAngle,
                    endAngle)
                    .WithCopiedCreateProperties(template);
                return true;
            }
            case CadOperationPayloadCodec.EntityTypeCircle:
            {
                if (!CadOperationPayloadCodec.TryGetCreateCircle(template, out var center, out var radius))
                {
                    break;
                }

                operation = CadOperationPayloadCodec.CreateCircle(
                    id,
                    CadGeometryTransform.Translate(center, translation),
                    radius)
                    .WithCopiedCreateProperties(template);
                return true;
            }
            case CadOperationPayloadCodec.EntityTypeLwPolyline:
            {
                if (!CadOperationPayloadCodec.TryGetCreateLwPolyline(template, out var vertices, out var isClosed))
                {
                    break;
                }

                operation = CadOperationPayloadCodec.CreateLwPolyline(
                    id,
                    CadGeometryTransform.TranslateVertices(vertices, translation),
                    isClosed)
                    .WithCopiedCreateProperties(template);
                return true;
            }
            case CadOperationPayloadCodec.EntityTypePoint:
            {
                if (!CadOperationPayloadCodec.TryGetCreatePoint(template, out var location))
                {
                    break;
                }

                operation = CadOperationPayloadCodec.CreatePoint(
                    id,
                    CadGeometryTransform.Translate(location, translation))
                    .WithCopiedCreateProperties(template);
                return true;
            }
            case CadOperationPayloadCodec.EntityTypeXLine:
            {
                if (!CadOperationPayloadCodec.TryGetCreateXLine(template, out var firstPoint, out var direction))
                {
                    break;
                }

                operation = CadOperationPayloadCodec.CreateXLine(
                    id,
                    CadGeometryTransform.Translate(firstPoint, translation),
                    CadGeometryTransform.NormalizeDirection(direction))
                    .WithCopiedCreateProperties(template);
                return true;
            }
            case CadOperationPayloadCodec.EntityTypeRay:
            {
                if (!CadOperationPayloadCodec.TryGetCreateRay(template, out var startPoint, out var direction))
                {
                    break;
                }

                operation = CadOperationPayloadCodec.CreateRay(
                    id,
                    CadGeometryTransform.Translate(startPoint, translation),
                    CadGeometryTransform.NormalizeDirection(direction))
                    .WithCopiedCreateProperties(template);
                return true;
            }
            case CadOperationPayloadCodec.EntityTypeEllipse:
            {
                if (!CadOperationPayloadCodec.TryGetCreateEllipse(
                        template,
                        out var center,
                        out var majorAxisEndPoint,
                        out var radiusRatio,
                        out var startParameter,
                        out var endParameter,
                        out var normal))
                {
                    break;
                }

                operation = CadOperationPayloadCodec.CreateEllipse(
                    id,
                    CadGeometryTransform.Translate(center, translation),
                    majorAxisEndPoint,
                    radiusRatio,
                    startParameter,
                    endParameter,
                    normal)
                    .WithCopiedCreateProperties(template);
                return true;
            }
            case CadOperationPayloadCodec.EntityTypeSpline:
            {
                if (!CadOperationPayloadCodec.TryGetCreateSpline(
                        template,
                        out var degree,
                        out var isClosed,
                        out var isPeriodic,
                        out var fitPoints,
                        out var controlPoints,
                        out var knots,
                        out var weights,
                        out var startTangent,
                        out var endTangent,
                        out var normal))
                {
                    break;
                }

                operation = CadOperationPayloadCodec.CreateSpline(
                    id,
                    degree,
                    isClosed,
                    isPeriodic,
                    CadGeometryTransform.TranslateVertices(fitPoints, translation),
                    CadGeometryTransform.TranslateVertices(controlPoints, translation),
                    knots,
                    weights,
                    startTangent,
                    endTangent,
                    normal)
                    .WithCopiedCreateProperties(template);
                return true;
            }
            case CadOperationPayloadCodec.EntityTypeText:
            {
                if (!CadOperationPayloadCodec.TryGetCreateText(
                        template,
                        out var insertPoint,
                        out var alignmentPoint,
                        out var height,
                        out var rotation,
                        out var value,
                        out var normal))
                {
                    break;
                }

                operation = CadOperationPayloadCodec.CreateText(
                    id,
                    CadGeometryTransform.Translate(insertPoint, translation),
                    CadGeometryTransform.Translate(alignmentPoint, translation),
                    height,
                    rotation,
                    value,
                    normal)
                    .WithCopiedCreateProperties(template);
                return true;
            }
            case CadOperationPayloadCodec.EntityTypeMText:
            {
                if (!CadOperationPayloadCodec.TryGetCreateMText(
                        template,
                        out var insertPoint,
                        out var textDirection,
                        out var height,
                        out var rectangleWidth,
                        out var value,
                        out var normal))
                {
                    break;
                }

                operation = CadOperationPayloadCodec.CreateMText(
                    id,
                    CadGeometryTransform.Translate(insertPoint, translation),
                    CadGeometryTransform.NormalizeDirection(textDirection),
                    height,
                    rectangleWidth,
                    value,
                    normal)
                    .WithCopiedCreateProperties(template);
                return true;
            }
            case CadOperationPayloadCodec.EntityTypeHatch:
            {
                if (!CadOperationPayloadCodec.TryGetCreateHatch(
                        template,
                        out var loops,
                        out var isSolid,
                        out var patternName,
                        out var normal))
                {
                    break;
                }

                operation = CadOperationPayloadCodec.CreateHatch(
                    id,
                    CadHatchGeometry.TranslateLoops(loops, translation),
                    isSolid,
                    patternName,
                    normal)
                    .WithCopiedCreateProperties(template);
                return true;
            }
            case CadOperationPayloadCodec.EntityTypeInsert:
            {
                if (!CadOperationPayloadCodec.TryGetCreateInsert(
                        template,
                        out var blockName,
                        out var insertPoint,
                        out var xScale,
                        out var yScale,
                        out var zScale,
                        out var rotation,
                        out var normal))
                {
                    break;
                }

                operation = CadOperationPayloadCodec.CreateInsert(
                        id,
                        blockName,
                        CadGeometryTransform.Translate(insertPoint, translation),
                        xScale,
                        yScale,
                        zScale,
                        rotation,
                        normal)
                    .WithCopiedCreateProperties(template);
                return true;
            }
        }

        operation = null!;
        error = $"Clipboard payload decode failed for entity type '{entityType}'.";
        return false;
    }

    private static CadClipboardEntity CreateClipboardEntity(CadOperation operation, XYZ referencePoint)
    {
        return new CadClipboardEntity(
            operation.Payload![CadOperationPayloadCodec.EntityTypeKey],
            new Dictionary<string, string>(operation.Payload!, StringComparer.Ordinal),
            referencePoint);
    }
}
