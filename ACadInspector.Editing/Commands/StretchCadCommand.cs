using ACadInspector.Editing.Operations;
using ACadSharp.Entities;
using CSMath;

namespace ACadInspector.Editing.Commands;

public sealed class StretchCadCommand : ICadCommandHandler
{
    public string Name => "STRETCH";
    public IReadOnlyList<string> Aliases => ["S"];

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

        if (!CadCommandParsing.TryParseStretchArguments(
                context.Arguments,
                out var delta,
                out var gripPoint,
                out var consumed,
                out var parseError))
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
                    var moveStart = DistanceSquared(line.StartPoint, gripPoint) <= DistanceSquared(line.EndPoint, gripPoint);
                    var fromStart = line.StartPoint;
                    var fromEnd = line.EndPoint;
                    var toStart = moveStart ? CadGeometryTransform.Translate(fromStart, delta) : fromStart;
                    var toEnd = moveStart ? fromEnd : CadGeometryTransform.Translate(fromEnd, delta);

                    forward.Add(CadOperationPayloadCodec.TransformLine(id, fromStart, fromEnd, toStart, toEnd));
                    inverse.Add(CadOperationPayloadCodec.TransformLine(id, toStart, toEnd, fromStart, fromEnd));
                    break;
                }
                case LwPolyline polyline:
                {
                    var fromVertices = CadGeometryTransform.ToVertices(polyline).ToArray();
                    if (fromVertices.Length == 0)
                    {
                        return ValueTask.FromResult(CadCommandResult.Fail("STRETCH cannot process an empty polyline."));
                    }

                    var fromClosed = polyline.IsClosed;
                    var toVertices = fromVertices.ToArray();
                    var nearestIndex = FindNearestVertexIndex(fromVertices, gripPoint);
                    toVertices[nearestIndex] = CadGeometryTransform.Translate(toVertices[nearestIndex], delta);

                    forward.Add(CadOperationPayloadCodec.TransformLwPolyline(id, fromVertices, fromClosed, toVertices, fromClosed));
                    inverse.Add(CadOperationPayloadCodec.TransformLwPolyline(id, toVertices, fromClosed, fromVertices, fromClosed));
                    break;
                }
                case Point point:
                {
                    var fromLocation = point.Location;
                    var toLocation = CadGeometryTransform.Translate(fromLocation, delta);
                    forward.Add(CadOperationPayloadCodec.TransformPoint(id, fromLocation, toLocation));
                    inverse.Add(CadOperationPayloadCodec.TransformPoint(id, toLocation, fromLocation));
                    break;
                }
                case Circle circle when circle is not Arc:
                {
                    var fromCenter = circle.Center;
                    var fromRadius = circle.Radius;
                    var fromRadiusGrip = ResolveCircleRadiusGrip(circle.Center, circle.Radius, gripPoint);
                    var centerDistance = DistanceSquared(fromCenter, gripPoint);
                    var radiusDistance = DistanceSquared(fromRadiusGrip, gripPoint);

                    var toCenter = fromCenter;
                    var toRadius = fromRadius;
                    if (centerDistance <= radiusDistance)
                    {
                        toCenter = CadGeometryTransform.Translate(fromCenter, delta);
                    }
                    else
                    {
                        var movedRadiusGrip = CadGeometryTransform.Translate(fromRadiusGrip, delta);
                        toRadius = Math.Sqrt(DistanceSquared(fromCenter, movedRadiusGrip));
                        if (toRadius <= 1e-9)
                        {
                            return ValueTask.FromResult(CadCommandResult.Fail("STRETCH circle radius must remain greater than zero."));
                        }
                    }

                    forward.Add(CadOperationPayloadCodec.TransformCircle(id, fromCenter, fromRadius, toCenter, toRadius));
                    inverse.Add(CadOperationPayloadCodec.TransformCircle(id, toCenter, toRadius, fromCenter, fromRadius));
                    break;
                }
                case Arc arc:
                {
                    var fromCenter = arc.Center;
                    var fromRadius = arc.Radius;
                    var fromStartAngle = arc.StartAngle;
                    var fromEndAngle = arc.EndAngle;
                    var startPoint = ToArcPoint(fromCenter, fromRadius, fromStartAngle);
                    var endPoint = ToArcPoint(fromCenter, fromRadius, fromEndAngle);
                    var centerDistance = DistanceSquared(fromCenter, gripPoint);
                    var startDistance = DistanceSquared(startPoint, gripPoint);
                    var endDistance = DistanceSquared(endPoint, gripPoint);

                    var toCenter = fromCenter;
                    var toRadius = fromRadius;
                    var toStartAngle = fromStartAngle;
                    var toEndAngle = fromEndAngle;

                    if (centerDistance <= startDistance && centerDistance <= endDistance)
                    {
                        toCenter = CadGeometryTransform.Translate(fromCenter, delta);
                    }
                    else if (startDistance <= endDistance)
                    {
                        var movedStart = CadGeometryTransform.Translate(startPoint, delta);
                        toRadius = Math.Sqrt(DistanceSquared(fromCenter, movedStart));
                        if (toRadius <= 1e-9)
                        {
                            return ValueTask.FromResult(CadCommandResult.Fail("STRETCH arc radius must remain greater than zero."));
                        }

                        toStartAngle = CadGeometryTransform.NormalizeAngle(Math.Atan2(
                            movedStart.Y - fromCenter.Y,
                            movedStart.X - fromCenter.X));
                    }
                    else
                    {
                        var movedEnd = CadGeometryTransform.Translate(endPoint, delta);
                        toRadius = Math.Sqrt(DistanceSquared(fromCenter, movedEnd));
                        if (toRadius <= 1e-9)
                        {
                            return ValueTask.FromResult(CadCommandResult.Fail("STRETCH arc radius must remain greater than zero."));
                        }

                        toEndAngle = CadGeometryTransform.NormalizeAngle(Math.Atan2(
                            movedEnd.Y - fromCenter.Y,
                            movedEnd.X - fromCenter.X));
                    }

                    forward.Add(CadOperationPayloadCodec.TransformArc(
                        id,
                        fromCenter,
                        fromRadius,
                        fromStartAngle,
                        fromEndAngle,
                        toCenter,
                        toRadius,
                        toStartAngle,
                        toEndAngle));
                    inverse.Add(CadOperationPayloadCodec.TransformArc(
                        id,
                        toCenter,
                        toRadius,
                        toStartAngle,
                        toEndAngle,
                        fromCenter,
                        fromRadius,
                        fromStartAngle,
                        fromEndAngle));
                    break;
                }
                case XLine xline:
                {
                    var fromFirstPoint = xline.FirstPoint;
                    var fromDirection = CadGeometryTransform.NormalizeDirection(xline.Direction);
                    var toFirstPoint = CadGeometryTransform.Translate(fromFirstPoint, delta);
                    forward.Add(CadOperationPayloadCodec.TransformXLine(id, fromFirstPoint, fromDirection, toFirstPoint, fromDirection));
                    inverse.Add(CadOperationPayloadCodec.TransformXLine(id, toFirstPoint, fromDirection, fromFirstPoint, fromDirection));
                    break;
                }
                case Ray ray:
                {
                    var fromStartPoint = ray.StartPoint;
                    var fromDirection = CadGeometryTransform.NormalizeDirection(ray.Direction);
                    var toStartPoint = CadGeometryTransform.Translate(fromStartPoint, delta);
                    forward.Add(CadOperationPayloadCodec.TransformRay(id, fromStartPoint, fromDirection, toStartPoint, fromDirection));
                    inverse.Add(CadOperationPayloadCodec.TransformRay(id, toStartPoint, fromDirection, fromStartPoint, fromDirection));
                    break;
                }
                case TextEntity text:
                {
                    var fromInsertPoint = text.InsertPoint;
                    var fromAlignmentPoint = text.AlignmentPoint;
                    var fromHeight = text.Height;
                    var fromRotation = text.Rotation;
                    var toInsertPoint = CadGeometryTransform.Translate(fromInsertPoint, delta);
                    var toAlignmentPoint = CadGeometryTransform.Translate(fromAlignmentPoint, delta);
                    forward.Add(CadOperationPayloadCodec.TransformText(
                        id,
                        fromInsertPoint,
                        fromAlignmentPoint,
                        fromHeight,
                        fromRotation,
                        toInsertPoint,
                        toAlignmentPoint,
                        fromHeight,
                        fromRotation));
                    inverse.Add(CadOperationPayloadCodec.TransformText(
                        id,
                        toInsertPoint,
                        toAlignmentPoint,
                        fromHeight,
                        fromRotation,
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
                    var toInsertPoint = CadGeometryTransform.Translate(fromInsertPoint, delta);
                    forward.Add(CadOperationPayloadCodec.TransformMText(
                        id,
                        fromInsertPoint,
                        fromTextDirection,
                        fromHeight,
                        fromRectangleWidth,
                        toInsertPoint,
                        fromTextDirection,
                        fromHeight,
                        fromRectangleWidth));
                    inverse.Add(CadOperationPayloadCodec.TransformMText(
                        id,
                        toInsertPoint,
                        fromTextDirection,
                        fromHeight,
                        fromRectangleWidth,
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
                    var majorGrip = new XYZ(
                        fromCenter.X + fromMajorAxisEndPoint.X,
                        fromCenter.Y + fromMajorAxisEndPoint.Y,
                        fromCenter.Z + fromMajorAxisEndPoint.Z);
                    var centerDistance = DistanceSquared(fromCenter, gripPoint);
                    var majorDistance = DistanceSquared(majorGrip, gripPoint);

                    var toCenter = fromCenter;
                    var toMajorAxisEndPoint = fromMajorAxisEndPoint;
                    if (centerDistance <= majorDistance)
                    {
                        toCenter = CadGeometryTransform.Translate(fromCenter, delta);
                    }
                    else
                    {
                        var movedMajorGrip = CadGeometryTransform.Translate(majorGrip, delta);
                        toMajorAxisEndPoint = new XYZ(
                            movedMajorGrip.X - fromCenter.X,
                            movedMajorGrip.Y - fromCenter.Y,
                            movedMajorGrip.Z - fromCenter.Z);
                        if (DistanceSquared(XYZ.Zero, toMajorAxisEndPoint) <= 1e-12)
                        {
                            return ValueTask.FromResult(CadCommandResult.Fail("STRETCH ellipse major axis must remain non-zero."));
                        }
                    }

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
                        fromRadiusRatio,
                        fromStartParameter,
                        fromEndParameter,
                        fromNormal));
                    inverse.Add(CadOperationPayloadCodec.TransformEllipse(
                        id,
                        toCenter,
                        toMajorAxisEndPoint,
                        fromRadiusRatio,
                        fromStartParameter,
                        fromEndParameter,
                        fromNormal,
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

                    var toFitPoints = fromFitPoints.ToArray();
                    var toControlPoints = fromControlPoints.ToArray();
                    if (toFitPoints.Length > 0)
                    {
                        var nearestIndex = FindNearestVertexIndex(toFitPoints, gripPoint);
                        toFitPoints[nearestIndex] = CadGeometryTransform.Translate(toFitPoints[nearestIndex], delta);
                    }
                    else if (toControlPoints.Length > 0)
                    {
                        var nearestIndex = FindNearestVertexIndex(toControlPoints, gripPoint);
                        toControlPoints[nearestIndex] = CadGeometryTransform.Translate(toControlPoints[nearestIndex], delta);
                    }
                    else
                    {
                        return ValueTask.FromResult(CadCommandResult.Fail("STRETCH cannot process a spline without fit/control points."));
                    }

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
                        fromDegree,
                        fromClosed,
                        fromIsPeriodic,
                        toFitPoints,
                        toControlPoints,
                        fromKnots,
                        fromWeights,
                        fromStartTangent,
                        fromEndTangent,
                        fromNormal));
                    inverse.Add(CadOperationPayloadCodec.TransformSpline(
                        id,
                        fromDegree,
                        fromClosed,
                        fromIsPeriodic,
                        toFitPoints,
                        toControlPoints,
                        fromKnots,
                        fromWeights,
                        fromStartTangent,
                        fromEndTangent,
                        fromNormal,
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
                        return ValueTask.FromResult(CadCommandResult.Fail($"STRETCH could not process HATCH: {loopError}"));
                    }

                    var toLoops = fromLoops
                        .Select(loop => loop.ToArray())
                        .ToArray();
                    var nearest = FindNearestLoopVertexIndex(toLoops, gripPoint);
                    if (nearest.LoopIndex < 0)
                    {
                        return ValueTask.FromResult(CadCommandResult.Fail("STRETCH cannot process empty HATCH loops."));
                    }

                    toLoops[nearest.LoopIndex][nearest.VertexIndex] =
                        CadGeometryTransform.Translate(toLoops[nearest.LoopIndex][nearest.VertexIndex], delta);
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
                        return ValueTask.FromResult(CadCommandResult.Fail("STRETCH cannot process INSERT without a block reference."));
                    }

                    var fromInsertPoint = insert.InsertPoint;
                    var fromXScale = insert.XScale;
                    var fromYScale = insert.YScale;
                    var fromZScale = insert.ZScale;
                    var fromRotation = insert.Rotation;
                    var fromNormal = insert.Normal;
                    var toInsertPoint = CadGeometryTransform.Translate(fromInsertPoint, delta);
                    var toXScale = fromXScale;
                    var toYScale = fromYScale;
                    var toZScale = fromZScale;
                    var toRotation = fromRotation;
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
                    return ValueTask.FromResult(CadCommandResult.Fail($"STRETCH does not support entity type '{target.GetType().Name}' yet."));
            }
        }

        var actorId = session.SessionId.Value;
        var forwardBatch = session.NextBatch(actorId, forward);
        var inverseBatch = session.NextBatch(actorId, inverse.AsEnumerable().Reverse().ToArray());
        session.Apply(forwardBatch);
        session.UndoRedo.Record(forwardBatch, inverseBatch);

        return ValueTask.FromResult(CadCommandResult.Ok($"Stretched {forward.Count} entity(s).", forward));
    }

    private static int FindNearestVertexIndex(IReadOnlyList<XYZ> vertices, XYZ gripPoint)
    {
        var bestIndex = 0;
        var bestDistance = DistanceSquared(vertices[0], gripPoint);
        for (var i = 1; i < vertices.Count; i++)
        {
            var distance = DistanceSquared(vertices[i], gripPoint);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                bestIndex = i;
            }
        }

        return bestIndex;
    }

    private static (int LoopIndex, int VertexIndex) FindNearestLoopVertexIndex(
        IReadOnlyList<IReadOnlyList<XYZ>> loops,
        XYZ gripPoint)
    {
        var bestLoopIndex = -1;
        var bestVertexIndex = -1;
        var bestDistance = double.MaxValue;

        for (var loopIndex = 0; loopIndex < loops.Count; loopIndex++)
        {
            var loop = loops[loopIndex];
            for (var vertexIndex = 0; vertexIndex < loop.Count; vertexIndex++)
            {
                var distance = DistanceSquared(loop[vertexIndex], gripPoint);
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    bestLoopIndex = loopIndex;
                    bestVertexIndex = vertexIndex;
                }
            }
        }

        return (bestLoopIndex, bestVertexIndex);
    }

    private static XYZ ResolveCircleRadiusGrip(XYZ center, double radius, XYZ gripPoint)
    {
        var direction = new XYZ(
            gripPoint.X - center.X,
            gripPoint.Y - center.Y,
            gripPoint.Z - center.Z);
        direction = CadGeometryTransform.NormalizeDirection(direction);
        if (DistanceSquared(direction, XYZ.Zero) <= 1e-12)
        {
            direction = new XYZ(1.0, 0.0, 0.0);
        }

        return new XYZ(
            center.X + (direction.X * radius),
            center.Y + (direction.Y * radius),
            center.Z + (direction.Z * radius));
    }

    private static XYZ ToArcPoint(XYZ center, double radius, double angleRadians)
    {
        return new XYZ(
            center.X + (radius * Math.Cos(angleRadians)),
            center.Y + (radius * Math.Sin(angleRadians)),
            center.Z);
    }

    private static double DistanceSquared(XYZ first, XYZ second)
    {
        var dx = first.X - second.X;
        var dy = first.Y - second.Y;
        var dz = first.Z - second.Z;
        return (dx * dx) + (dy * dy) + (dz * dz);
    }
}
