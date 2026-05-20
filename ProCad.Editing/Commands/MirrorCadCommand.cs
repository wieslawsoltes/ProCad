using ProCad.Editing.Operations;
using ACadSharp.Entities;
using CSMath;

namespace ProCad.Editing.Commands;

public sealed class MirrorCadCommand : ICadCommandHandler
{
    public string Name => "MIRROR";
    public IReadOnlyList<string> Aliases => ["MI"];

    public bool CanExecute(CadCommandContext context)
    {
        return context.Session is not null;
    }

    public ValueTask<CadCommandResult> ExecuteAsync(CadCommandContext context)
    {
        if (!CadCommandSessionHelper.TryGetSession(context, out var session, out var error))
        {
            return ValueTask.FromResult(error);
        }

        if (!CadCommandParsing.TryParseMirrorArguments(context.Arguments, out var axisStart, out var axisEnd, out var consumed, out var parseError))
        {
            return ValueTask.FromResult(CadCommandResult.Fail(parseError!));
        }

        var targetTokens = context.Arguments.Skip(consumed).ToArray();
        if (!CadCommandTargetResolver.TryResolve(session, targetTokens, out var targets, out var targetError))
        {
            return ValueTask.FromResult(CadCommandResult.Fail(targetError!));
        }

        var forward = new List<CadOperation>(targets.Count);
        var inverse = new List<CadOperation>(targets.Count);

        foreach (var target in targets)
        {
            if (!session.EntityIndex.TryGetId(target, out var id))
            {
                id = session.EntityIndex.Register(target);
            }

            switch (target)
            {
                case Line line:
                {
                    var fromStart = line.StartPoint;
                    var fromEnd = line.EndPoint;
                    var toStart = CadGeometryTransform.MirrorAcrossLine(fromStart, axisStart, axisEnd);
                    var toEnd = CadGeometryTransform.MirrorAcrossLine(fromEnd, axisStart, axisEnd);
                    forward.Add(CadOperationPayloadCodec.TransformLine(id, fromStart, fromEnd, toStart, toEnd));
                    inverse.Add(CadOperationPayloadCodec.TransformLine(id, toStart, toEnd, fromStart, fromEnd));
                    break;
                }
                case Arc arc:
                {
                    var fromCenter = arc.Center;
                    var fromRadius = arc.Radius;
                    var fromStartAngle = arc.StartAngle;
                    var fromEndAngle = arc.EndAngle;
                    var mirrored = CadGeometryTransform.MirrorArc(fromCenter, fromRadius, fromStartAngle, fromEndAngle, axisStart, axisEnd);
                    forward.Add(CadOperationPayloadCodec.TransformArc(
                        id,
                        fromCenter,
                        fromRadius,
                        fromStartAngle,
                        fromEndAngle,
                        mirrored.Center,
                        fromRadius,
                        mirrored.StartAngle,
                        mirrored.EndAngle));
                    inverse.Add(CadOperationPayloadCodec.TransformArc(
                        id,
                        mirrored.Center,
                        fromRadius,
                        mirrored.StartAngle,
                        mirrored.EndAngle,
                        fromCenter,
                        fromRadius,
                        fromStartAngle,
                        fromEndAngle));
                    break;
                }
                case Circle circle:
                {
                    var fromCenter = circle.Center;
                    var fromRadius = circle.Radius;
                    var toCenter = CadGeometryTransform.MirrorAcrossLine(fromCenter, axisStart, axisEnd);
                    forward.Add(CadOperationPayloadCodec.TransformCircle(id, fromCenter, fromRadius, toCenter, fromRadius));
                    inverse.Add(CadOperationPayloadCodec.TransformCircle(id, toCenter, fromRadius, fromCenter, fromRadius));
                    break;
                }
                case LwPolyline polyline:
                {
                    var fromVertices = CadGeometryTransform.ToVertices(polyline);
                    var fromClosed = polyline.IsClosed;
                    var toVertices = CadGeometryTransform.MirrorVertices(fromVertices, axisStart, axisEnd);
                    var toClosed = fromClosed;
                    forward.Add(CadOperationPayloadCodec.TransformLwPolyline(id, fromVertices, fromClosed, toVertices, toClosed));
                    inverse.Add(CadOperationPayloadCodec.TransformLwPolyline(id, toVertices, toClosed, fromVertices, fromClosed));
                    break;
                }
                case Point point:
                {
                    var fromLocation = point.Location;
                    var toLocation = CadGeometryTransform.MirrorAcrossLine(fromLocation, axisStart, axisEnd);
                    forward.Add(CadOperationPayloadCodec.TransformPoint(id, fromLocation, toLocation));
                    inverse.Add(CadOperationPayloadCodec.TransformPoint(id, toLocation, fromLocation));
                    break;
                }
                case XLine xline:
                {
                    var fromFirstPoint = xline.FirstPoint;
                    var fromDirection = CadGeometryTransform.NormalizeDirection(xline.Direction);
                    var toFirstPoint = CadGeometryTransform.MirrorAcrossLine(fromFirstPoint, axisStart, axisEnd);
                    var toDirection = CadGeometryTransform.MirrorDirection(fromDirection, axisStart, axisEnd);
                    forward.Add(CadOperationPayloadCodec.TransformXLine(id, fromFirstPoint, fromDirection, toFirstPoint, toDirection));
                    inverse.Add(CadOperationPayloadCodec.TransformXLine(id, toFirstPoint, toDirection, fromFirstPoint, fromDirection));
                    break;
                }
                case Ray ray:
                {
                    var fromStartPoint = ray.StartPoint;
                    var fromDirection = CadGeometryTransform.NormalizeDirection(ray.Direction);
                    var toStartPoint = CadGeometryTransform.MirrorAcrossLine(fromStartPoint, axisStart, axisEnd);
                    var toDirection = CadGeometryTransform.MirrorDirection(fromDirection, axisStart, axisEnd);
                    forward.Add(CadOperationPayloadCodec.TransformRay(id, fromStartPoint, fromDirection, toStartPoint, toDirection));
                    inverse.Add(CadOperationPayloadCodec.TransformRay(id, toStartPoint, toDirection, fromStartPoint, fromDirection));
                    break;
                }
                case TextEntity text:
                {
                    var fromInsertPoint = text.InsertPoint;
                    var fromAlignmentPoint = text.AlignmentPoint;
                    var fromHeight = text.Height;
                    var fromRotation = text.Rotation;
                    var fromDirection = new XYZ(Math.Cos(fromRotation), Math.Sin(fromRotation), 0.0);
                    var toInsertPoint = CadGeometryTransform.MirrorAcrossLine(fromInsertPoint, axisStart, axisEnd);
                    var toAlignmentPoint = CadGeometryTransform.MirrorAcrossLine(fromAlignmentPoint, axisStart, axisEnd);
                    var toDirection = CadGeometryTransform.MirrorDirection(fromDirection, axisStart, axisEnd);
                    var toHeight = fromHeight;
                    var toRotation = CadGeometryTransform.NormalizeAngle(Math.Atan2(toDirection.Y, toDirection.X));
                    forward.Add(CadOperationPayloadCodec.TransformText(
                        id,
                        fromInsertPoint,
                        fromAlignmentPoint,
                        fromHeight,
                        fromRotation,
                        toInsertPoint,
                        toAlignmentPoint,
                        toHeight,
                        toRotation));
                    inverse.Add(CadOperationPayloadCodec.TransformText(
                        id,
                        toInsertPoint,
                        toAlignmentPoint,
                        toHeight,
                        toRotation,
                        fromInsertPoint,
                        fromAlignmentPoint,
                        fromHeight,
                        fromRotation));
                    break;
                }
                case MText mtext:
                {
                    var fromInsertPoint = mtext.InsertPoint;
                    var fromTextDirection = CadGeometryTransform.NormalizeDirection(mtext.AlignmentPoint);
                    var fromHeight = mtext.Height;
                    var fromRectangleWidth = mtext.RectangleWidth;
                    var toInsertPoint = CadGeometryTransform.MirrorAcrossLine(fromInsertPoint, axisStart, axisEnd);
                    var toTextDirection = CadGeometryTransform.MirrorDirection(fromTextDirection, axisStart, axisEnd);
                    var toHeight = fromHeight;
                    var toRectangleWidth = fromRectangleWidth;
                    forward.Add(CadOperationPayloadCodec.TransformMText(
                        id,
                        fromInsertPoint,
                        fromTextDirection,
                        fromHeight,
                        fromRectangleWidth,
                        toInsertPoint,
                        toTextDirection,
                        toHeight,
                        toRectangleWidth));
                    inverse.Add(CadOperationPayloadCodec.TransformMText(
                        id,
                        toInsertPoint,
                        toTextDirection,
                        toHeight,
                        toRectangleWidth,
                        fromInsertPoint,
                        fromTextDirection,
                        fromHeight,
                        fromRectangleWidth));
                    break;
                }
                case Ellipse ellipse:
                {
                    var fromCenter = ellipse.Center;
                    var fromMajorAxisEndPoint = ellipse.MajorAxisEndPoint;
                    var fromRadiusRatio = ellipse.RadiusRatio;
                    var fromStartParameter = ellipse.StartParameter;
                    var fromEndParameter = ellipse.EndParameter;
                    var fromNormal = ellipse.Normal;
                    var toCenter = CadGeometryTransform.MirrorAcrossLine(fromCenter, axisStart, axisEnd);
                    var toMajorAxisEndPoint = CadGeometryTransform.MirrorVector(fromMajorAxisEndPoint, axisStart, axisEnd);
                    var toRadiusRatio = fromRadiusRatio;
                    var toStartParameter = fromStartParameter;
                    var toEndParameter = fromEndParameter;
                    var toNormal = CadGeometryTransform.MirrorVector(fromNormal, axisStart, axisEnd);

                    forward.Add(CadOperationPayloadCodec.TransformEllipse(
                        id,
                        fromCenter,
                        fromMajorAxisEndPoint,
                        fromRadiusRatio,
                        fromStartParameter,
                        fromEndParameter,
                        fromNormal,
                        toCenter,
                        toMajorAxisEndPoint,
                        toRadiusRatio,
                        toStartParameter,
                        toEndParameter,
                        toNormal));
                    inverse.Add(CadOperationPayloadCodec.TransformEllipse(
                        id,
                        toCenter,
                        toMajorAxisEndPoint,
                        toRadiusRatio,
                        toStartParameter,
                        toEndParameter,
                        toNormal,
                        fromCenter,
                        fromMajorAxisEndPoint,
                        fromRadiusRatio,
                        fromStartParameter,
                        fromEndParameter,
                        fromNormal));
                    break;
                }
                case Spline spline:
                {
                    var fromDegree = spline.Degree;
                    var fromClosed = spline.IsClosed;
                    var fromIsPeriodic = spline.IsPeriodic;
                    var fromFitPoints = spline.FitPoints.ToArray();
                    var fromControlPoints = spline.ControlPoints.ToArray();
                    var fromKnots = spline.Knots.ToArray();
                    var fromWeights = spline.Weights.ToArray();
                    var fromStartTangent = spline.StartTangent;
                    var fromEndTangent = spline.EndTangent;
                    var fromNormal = spline.Normal;

                    var toDegree = fromDegree;
                    var toClosed = fromClosed;
                    var toIsPeriodic = fromIsPeriodic;
                    var toFitPoints = fromFitPoints
                        .Select(point => CadGeometryTransform.MirrorAcrossLine(point, axisStart, axisEnd))
                        .ToArray();
                    var toControlPoints = fromControlPoints
                        .Select(point => CadGeometryTransform.MirrorAcrossLine(point, axisStart, axisEnd))
                        .ToArray();
                    var toKnots = fromKnots;
                    var toWeights = fromWeights;
                    var toStartTangent = CadGeometryTransform.MirrorVector(fromStartTangent, axisStart, axisEnd);
                    var toEndTangent = CadGeometryTransform.MirrorVector(fromEndTangent, axisStart, axisEnd);
                    var toNormal = CadGeometryTransform.MirrorVector(fromNormal, axisStart, axisEnd);

                    forward.Add(CadOperationPayloadCodec.TransformSpline(
                        id,
                        fromDegree,
                        fromClosed,
                        fromIsPeriodic,
                        fromFitPoints,
                        fromControlPoints,
                        fromKnots,
                        fromWeights,
                        fromStartTangent,
                        fromEndTangent,
                        fromNormal,
                        toDegree,
                        toClosed,
                        toIsPeriodic,
                        toFitPoints,
                        toControlPoints,
                        toKnots,
                        toWeights,
                        toStartTangent,
                        toEndTangent,
                        toNormal));
                    inverse.Add(CadOperationPayloadCodec.TransformSpline(
                        id,
                        toDegree,
                        toClosed,
                        toIsPeriodic,
                        toFitPoints,
                        toControlPoints,
                        toKnots,
                        toWeights,
                        toStartTangent,
                        toEndTangent,
                        toNormal,
                        fromDegree,
                        fromClosed,
                        fromIsPeriodic,
                        fromFitPoints,
                        fromControlPoints,
                        fromKnots,
                        fromWeights,
                        fromStartTangent,
                        fromEndTangent,
                        fromNormal));
                    break;
                }
                case Hatch hatch:
                {
                    if (!CadHatchGeometry.TryGetLoops(hatch, out var fromLoops, out var loopError))
                    {
                        return ValueTask.FromResult(CadCommandResult.Fail($"MIRROR could not transform HATCH: {loopError}"));
                    }

                    var toLoops = CadHatchGeometry.MirrorLoops(fromLoops, axisStart, axisEnd);
                    var patternName = CadHatchGeometry.ResolvePatternName(hatch);
                    forward.Add(CadOperationPayloadCodec.TransformHatch(
                        id,
                        fromLoops,
                        toLoops,
                        hatch.IsSolid,
                        patternName,
                        hatch.Normal));
                    inverse.Add(CadOperationPayloadCodec.TransformHatch(
                        id,
                        toLoops,
                        fromLoops,
                        hatch.IsSolid,
                        patternName,
                        hatch.Normal));
                    break;
                }
                case Insert insert:
                {
                    if (insert.Block is null)
                    {
                        return ValueTask.FromResult(CadCommandResult.Fail("MIRROR cannot transform INSERT without a block reference."));
                    }

                    var fromInsertPoint = insert.InsertPoint;
                    var fromXScale = insert.XScale;
                    var fromYScale = insert.YScale;
                    var fromZScale = insert.ZScale;
                    var fromRotation = insert.Rotation;
                    var fromNormal = insert.Normal;
                    var fromDirection = new CSMath.XYZ(Math.Cos(fromRotation), Math.Sin(fromRotation), 0.0);
                    var toInsertPoint = CadGeometryTransform.MirrorAcrossLine(fromInsertPoint, axisStart, axisEnd);
                    var toDirection = CadGeometryTransform.MirrorDirection(fromDirection, axisStart, axisEnd);
                    var toXScale = fromXScale;
                    var toYScale = fromYScale;
                    var toZScale = fromZScale;
                    var toRotation = CadGeometryTransform.NormalizeAngle(Math.Atan2(toDirection.Y, toDirection.X));
                    var toNormal = fromNormal;

                    forward.Add(CadOperationPayloadCodec.TransformInsert(
                        id,
                        insert.Block.Name,
                        fromInsertPoint,
                        fromXScale,
                        fromYScale,
                        fromZScale,
                        fromRotation,
                        fromNormal,
                        toInsertPoint,
                        toXScale,
                        toYScale,
                        toZScale,
                        toRotation,
                        toNormal));
                    inverse.Add(CadOperationPayloadCodec.TransformInsert(
                        id,
                        insert.Block.Name,
                        toInsertPoint,
                        toXScale,
                        toYScale,
                        toZScale,
                        toRotation,
                        toNormal,
                        fromInsertPoint,
                        fromXScale,
                        fromYScale,
                        fromZScale,
                        fromRotation,
                        fromNormal));
                    break;
                }
                default:
                    return ValueTask.FromResult(CadCommandResult.Fail($"MIRROR does not support entity type '{target.GetType().Name}' yet."));
            }
        }

        var actorId = session.SessionId.Value;
        var forwardBatch = session.NextBatch(actorId, forward);
        var inverseBatch = session.NextBatch(actorId, inverse.AsEnumerable().Reverse().ToArray());

        session.Apply(forwardBatch);
        session.UndoRedo.Record(forwardBatch, inverseBatch);

        return ValueTask.FromResult(CadCommandResult.Ok($"Mirrored {targets.Count} entity(s).", forward));
    }
}
