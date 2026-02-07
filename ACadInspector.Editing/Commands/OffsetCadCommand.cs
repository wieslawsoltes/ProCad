using ACadInspector.Editing.Identifiers;
using ACadInspector.Editing.Operations;
using ACadInspector.Editing.Selection;
using ACadSharp.Entities;
using CSMath;

namespace ACadInspector.Editing.Commands;

public sealed class OffsetCadCommand : ICadCommandHandler
{
    public string Name => "OFFSET";
    public IReadOnlyList<string> Aliases => ["O"];

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

        if (!CadCommandParsing.TryParseOffsetArguments(context.Arguments, out var distance, out var sideSign, out var consumed, out var parseError))
        {
            return ValueTask.FromResult(CadCommandResult.Fail(parseError!));
        }

        var sideMode = ResolveSideMode(context.Arguments, consumed);

        var targetTokens = context.Arguments.Skip(consumed).ToArray();
        if (!CadCommandTargetResolver.TryResolve(session, targetTokens, out var targets, out var targetError))
        {
            return ValueTask.FromResult(CadCommandResult.Fail(targetError!));
        }

        var forward = new List<CadOperation>(targets.Count);
        var inverse = new List<CadOperation>(targets.Count);
        var createdIds = new List<CadEntityId>(targets.Count);

        foreach (var target in targets)
        {
            var id = CadEntityId.New();
            createdIds.Add(id);

            switch (target)
            {
                case Line line:
                {
                    var dx = line.EndPoint.X - line.StartPoint.X;
                    var dy = line.EndPoint.Y - line.StartPoint.Y;
                    var length = Math.Sqrt((dx * dx) + (dy * dy));
                    if (length <= double.Epsilon)
                    {
                        return ValueTask.FromResult(CadCommandResult.Fail("OFFSET cannot process zero-length LINE."));
                    }

                    var normalX = -dy / length;
                    var normalY = dx / length;
                    var offset = sideSign * distance;
                    var offsetVector = new XYZ(normalX * offset, normalY * offset, 0.0);

                    var start = CadGeometryTransform.Translate(line.StartPoint, offsetVector);
                    var end = CadGeometryTransform.Translate(line.EndPoint, offsetVector);
                    forward.Add(CadOperationPayloadCodec.CreateLine(id, start, end).WithSourceProperties(target));
                    inverse.Add(CadOperationPayloadCodec.DeleteLine(id, start, end));
                    break;
                }
                case Arc arc:
                {
                    var radius = arc.Radius + sideSign * distance;
                    if (radius <= double.Epsilon)
                    {
                        return ValueTask.FromResult(CadCommandResult.Fail("OFFSET would produce a non-positive ARC radius."));
                    }

                    forward.Add(CadOperationPayloadCodec.CreateArc(id, arc.Center, radius, arc.StartAngle, arc.EndAngle).WithSourceProperties(target));
                    inverse.Add(CadOperationPayloadCodec.DeleteArc(id, arc.Center, radius, arc.StartAngle, arc.EndAngle));
                    break;
                }
                case Circle circle:
                {
                    var radius = circle.Radius + sideSign * distance;
                    if (radius <= double.Epsilon)
                    {
                        return ValueTask.FromResult(CadCommandResult.Fail("OFFSET would produce a non-positive CIRCLE radius."));
                    }

                    forward.Add(CadOperationPayloadCodec.CreateCircle(id, circle.Center, radius).WithSourceProperties(target));
                    inverse.Add(CadOperationPayloadCodec.DeleteCircle(id, circle.Center, radius));
                    break;
                }
                case LwPolyline polyline:
                {
                    var sourceVertices = CadGeometryTransform.ToVertices(polyline);
                    if (sourceVertices.Count < 2)
                    {
                        return ValueTask.FromResult(CadCommandResult.Fail("OFFSET requires at least two polyline vertices."));
                    }

                    var effectiveSideSign = ResolvePolylineSideSign(sourceVertices, polyline.IsClosed, sideSign, sideMode);
                    if (!TryOffsetPolylineVertices(
                            sourceVertices,
                            polyline.IsClosed,
                            distance,
                            effectiveSideSign,
                            out var offsetVertices,
                            out var polylineError))
                    {
                        return ValueTask.FromResult(CadCommandResult.Fail(polylineError!));
                    }

                    forward.Add(CadOperationPayloadCodec.CreateLwPolyline(id, offsetVertices, polyline.IsClosed).WithSourceProperties(target));
                    inverse.Add(CadOperationPayloadCodec.DeleteLwPolyline(id, offsetVertices, polyline.IsClosed));
                    break;
                }
                case XLine xline:
                {
                    var direction = CadGeometryTransform.NormalizeDirection(xline.Direction);
                    var planarLength = Math.Sqrt(direction.X * direction.X + direction.Y * direction.Y);
                    if (planarLength <= double.Epsilon)
                    {
                        return ValueTask.FromResult(CadCommandResult.Fail("OFFSET cannot process XLINE with zero planar direction."));
                    }

                    var normal = new XYZ(-direction.Y / planarLength, direction.X / planarLength, 0.0);
                    var offsetVector = new XYZ(
                        normal.X * sideSign * distance,
                        normal.Y * sideSign * distance,
                        0.0);
                    var firstPoint = CadGeometryTransform.Translate(xline.FirstPoint, offsetVector);
                    forward.Add(CadOperationPayloadCodec.CreateXLine(id, firstPoint, direction).WithSourceProperties(target));
                    inverse.Add(CadOperationPayloadCodec.DeleteXLine(id, firstPoint, direction));
                    break;
                }
                case Ray ray:
                {
                    var direction = CadGeometryTransform.NormalizeDirection(ray.Direction);
                    var planarLength = Math.Sqrt(direction.X * direction.X + direction.Y * direction.Y);
                    if (planarLength <= double.Epsilon)
                    {
                        return ValueTask.FromResult(CadCommandResult.Fail("OFFSET cannot process RAY with zero planar direction."));
                    }

                    var normal = new XYZ(-direction.Y / planarLength, direction.X / planarLength, 0.0);
                    var offsetVector = new XYZ(
                        normal.X * sideSign * distance,
                        normal.Y * sideSign * distance,
                        0.0);
                    var startPoint = CadGeometryTransform.Translate(ray.StartPoint, offsetVector);
                    forward.Add(CadOperationPayloadCodec.CreateRay(id, startPoint, direction).WithSourceProperties(target));
                    inverse.Add(CadOperationPayloadCodec.DeleteRay(id, startPoint, direction));
                    break;
                }
                case Ellipse ellipse:
                {
                    var majorLength = Math.Sqrt(
                        ellipse.MajorAxisEndPoint.X * ellipse.MajorAxisEndPoint.X +
                        ellipse.MajorAxisEndPoint.Y * ellipse.MajorAxisEndPoint.Y +
                        ellipse.MajorAxisEndPoint.Z * ellipse.MajorAxisEndPoint.Z);
                    if (majorLength <= double.Epsilon)
                    {
                        return ValueTask.FromResult(CadCommandResult.Fail("OFFSET cannot process ELLIPSE with zero-length major axis."));
                    }

                    var minorLength = majorLength * Math.Max(Math.Abs(ellipse.RadiusRatio), 1e-9);
                    var offset = sideSign * distance;
                    var newMajorLength = majorLength + offset;
                    var newMinorLength = minorLength + offset;
                    if (newMajorLength <= double.Epsilon || newMinorLength <= double.Epsilon)
                    {
                        return ValueTask.FromResult(CadCommandResult.Fail("OFFSET would produce a non-positive ELLIPSE axis length."));
                    }

                    var scale = newMajorLength / majorLength;
                    var newMajorAxisEndPoint = CadGeometryTransform.ScaleVector(ellipse.MajorAxisEndPoint, scale);
                    var newRadiusRatio = newMinorLength / newMajorLength;
                    forward.Add(CadOperationPayloadCodec.CreateEllipse(
                            id,
                            ellipse.Center,
                            newMajorAxisEndPoint,
                            newRadiusRatio,
                            ellipse.StartParameter,
                            ellipse.EndParameter,
                            ellipse.Normal)
                        .WithSourceProperties(target));
                    inverse.Add(CadOperationPayloadCodec.DeleteEllipse(
                        id,
                        ellipse.Center,
                        newMajorAxisEndPoint,
                        newRadiusRatio,
                        ellipse.StartParameter,
                        ellipse.EndParameter,
                        ellipse.Normal));
                    break;
                }
                default:
                    return ValueTask.FromResult(CadCommandResult.Fail($"OFFSET does not support entity type '{target.GetType().Name}' yet."));
            }
        }

        var actorId = session.SessionId.Value;
        var forwardBatch = session.NextBatch(actorId, forward);
        var inverseBatch = session.NextBatch(actorId, inverse.AsEnumerable().Reverse().ToArray());

        session.Apply(forwardBatch);
        session.UndoRedo.Record(forwardBatch, inverseBatch);

        var createdEntities = new List<object?>(createdIds.Count);
        foreach (var id in createdIds)
        {
            if (session.EntityIndex.TryGetEntity(id, out var created))
            {
                createdEntities.Add(created);
            }
        }

        if (createdEntities.Count > 0)
        {
            session.SetSelection(createdEntities, CadSelectionMode.Replace);
        }

        return ValueTask.FromResult(CadCommandResult.Ok($"Created {forward.Count} offset entity(s).", forward));
    }

    private static int ResolvePolylineSideSign(
        IReadOnlyList<XYZ> vertices,
        bool isClosed,
        int requestedSideSign,
        CadOffsetSideMode sideMode)
    {
        if (!isClosed || sideMode is CadOffsetSideMode.Left or CadOffsetSideMode.Right)
        {
            return requestedSideSign;
        }

        var signedArea = ComputeSignedArea(vertices);
        var interiorSign = signedArea >= 0.0 ? 1 : -1;

        return sideMode == CadOffsetSideMode.Outer
            ? -interiorSign
            : interiorSign;
    }

    private static CadOffsetSideMode ResolveSideMode(IReadOnlyList<string> args, int consumed)
    {
        if (consumed < 2 || args.Count < 2)
        {
            return CadOffsetSideMode.Left;
        }

        var token = args[1];
        if (token.Equals("OUTER", StringComparison.OrdinalIgnoreCase) ||
            token.Equals("O", StringComparison.OrdinalIgnoreCase))
        {
            return CadOffsetSideMode.Outer;
        }

        if (token.Equals("INNER", StringComparison.OrdinalIgnoreCase) ||
            token.Equals("I", StringComparison.OrdinalIgnoreCase))
        {
            return CadOffsetSideMode.Inner;
        }

        if (token.Equals("RIGHT", StringComparison.OrdinalIgnoreCase) ||
            token.Equals("R", StringComparison.OrdinalIgnoreCase))
        {
            return CadOffsetSideMode.Right;
        }

        return CadOffsetSideMode.Left;
    }

    private static bool TryOffsetPolylineVertices(
        IReadOnlyList<XYZ> vertices,
        bool isClosed,
        double distance,
        int sideSign,
        out IReadOnlyList<XYZ> offsetVertices,
        out string? error)
    {
        offsetVertices = Array.Empty<XYZ>();
        error = null;

        var count = vertices.Count;
        if (count < 2)
        {
            error = "OFFSET polyline requires at least two vertices.";
            return false;
        }

        var segmentCount = isClosed ? count : count - 1;
        var normals = new XYZ[segmentCount];
        for (var i = 0; i < segmentCount; i++)
        {
            var start = vertices[i];
            var end = vertices[(i + 1) % count];
            var dx = end.X - start.X;
            var dy = end.Y - start.Y;
            var length = Math.Sqrt((dx * dx) + (dy * dy));
            if (length <= double.Epsilon)
            {
                error = "OFFSET polyline contains zero-length segments.";
                return false;
            }

            var nx = sideSign * (-dy / length);
            var ny = sideSign * (dx / length);
            normals[i] = new XYZ(nx, ny, 0.0);
        }

        var result = new XYZ[count];
        for (var i = 0; i < count; i++)
        {
            if (!TryGetVertexOffsetVector(vertices, isClosed, normals, distance, i, out var offsetVector))
            {
                error = "OFFSET polyline failed to resolve a stable corner offset.";
                return false;
            }

            result[i] = CadGeometryTransform.Translate(vertices[i], offsetVector);
        }

        offsetVertices = result;
        return true;
    }

    private static bool TryGetVertexOffsetVector(
        IReadOnlyList<XYZ> vertices,
        bool isClosed,
        IReadOnlyList<XYZ> normals,
        double distance,
        int vertexIndex,
        out XYZ offset)
    {
        offset = XYZ.Zero;
        var count = vertices.Count;

        if (!isClosed)
        {
            if (vertexIndex == 0)
            {
                offset = new XYZ(normals[0].X * distance, normals[0].Y * distance, 0.0);
                return true;
            }

            if (vertexIndex == count - 1)
            {
                var endNormal = normals[^1];
                offset = new XYZ(endNormal.X * distance, endNormal.Y * distance, 0.0);
                return true;
            }
        }

        var prevSegmentIndex = (vertexIndex - 1 + normals.Count) % normals.Count;
        var nextSegmentIndex = vertexIndex % normals.Count;
        var prevNormal = normals[prevSegmentIndex];
        var nextNormal = normals[nextSegmentIndex];

        var bisector = new XYZ(
            prevNormal.X + nextNormal.X,
            prevNormal.Y + nextNormal.Y,
            0.0);

        var bisectorLength = Math.Sqrt((bisector.X * bisector.X) + (bisector.Y * bisector.Y));
        if (bisectorLength <= 1e-9)
        {
            // Near 180-degree corner; fallback to next-segment normal offset.
            offset = new XYZ(nextNormal.X * distance, nextNormal.Y * distance, 0.0);
            return true;
        }

        bisector = new XYZ(bisector.X / bisectorLength, bisector.Y / bisectorLength, 0.0);
        var denom = (bisector.X * nextNormal.X) + (bisector.Y * nextNormal.Y);
        if (Math.Abs(denom) <= 1e-6)
        {
            return false;
        }

        var scale = distance / denom;
        if (Math.Abs(scale) > distance * 20.0)
        {
            // Avoid explosive miters on near-collinear turns.
            offset = new XYZ(nextNormal.X * distance, nextNormal.Y * distance, 0.0);
            return true;
        }

        offset = new XYZ(bisector.X * scale, bisector.Y * scale, 0.0);
        return true;
    }

    private static double ComputeSignedArea(IReadOnlyList<XYZ> vertices)
    {
        var area2 = 0.0;
        for (var i = 0; i < vertices.Count; i++)
        {
            var a = vertices[i];
            var b = vertices[(i + 1) % vertices.Count];
            area2 += (a.X * b.Y) - (b.X * a.Y);
        }

        return area2 * 0.5;
    }

    private enum CadOffsetSideMode
    {
        Left,
        Right,
        Outer,
        Inner
    }
}
