using ACadInspector.Editing.Identifiers;
using ACadInspector.Editing.Operations;
using ACadInspector.Editing.Selection;
using ACadSharp.Entities;
using CSMath;

namespace ACadInspector.Editing.Commands;

public sealed class ArrayCadCommand : ICadCommandHandler
{
    public string Name => "ARRAY";
    public IReadOnlyList<string> Aliases => ["AR"];

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

        if (!TryParseArrayMode(context.Arguments, out var mode, out var spec, out var consumed, out var parseError))
        {
            return ValueTask.FromResult(CadCommandResult.Fail(parseError!));
        }

        var targetTokens = context.Arguments.Skip(consumed).ToArray();
        if (!CadCommandTargetResolver.TryResolve(session, targetTokens, out var targets, out var targetError))
        {
            return ValueTask.FromResult(CadCommandResult.Fail(targetError!));
        }

        var copyCount = spec.CopyCount;
        var forward = new List<CadOperation>(targets.Count * copyCount);
        var inverse = new List<CadOperation>(targets.Count * copyCount);
        var createdIds = new List<CadEntityId>(targets.Count * copyCount);

        if (mode == CadArrayMode.Rectangular)
        {
            for (var row = 0; row < spec.Rows; row++)
            {
                for (var column = 0; column < spec.Columns; column++)
                {
                    if (row == 0 && column == 0)
                    {
                        continue;
                    }

                    var delta = new XYZ(column * spec.ColumnSpacing, row * spec.RowSpacing, 0.0);
                    foreach (var target in targets)
                    {
                        var newId = CadEntityId.New();
                        if (!TryAppendCreateWithTransform(
                                target,
                                newId,
                                point => CadGeometryTransform.Translate(point, delta),
                                angleDeltaRadians: 0.0,
                                modeName: "rectangular",
                                forward,
                                inverse,
                                out var createError))
                        {
                            return ValueTask.FromResult(CadCommandResult.Fail(createError!));
                        }

                        createdIds.Add(newId);
                    }
                }
            }
        }
        else if (mode == CadArrayMode.Polar)
        {
            for (var item = 1; item < spec.ItemCount; item++)
            {
                var angle = item * spec.AngleStepRadians;
                foreach (var target in targets)
                {
                    var newId = CadEntityId.New();
                    if (!TryAppendCreateWithTransform(
                            target,
                            newId,
                            point => CadGeometryTransform.RotateAroundZ(point, spec.Center, angle),
                            angleDeltaRadians: angle,
                            modeName: "polar",
                            forward,
                            inverse,
                            out var createError))
                    {
                        return ValueTask.FromResult(CadCommandResult.Fail(createError!));
                    }

                    createdIds.Add(newId);
                }
            }
        }
        else
        {
            if (!session.EntityIndex.TryGetByHandle(spec.PathHandle, out var pathEntityObject, out _) ||
                pathEntityObject is not Entity pathEntity)
            {
                return ValueTask.FromResult(CadCommandResult.Fail($"ARRAY PATH could not resolve path handle '{spec.PathHandle:X}'."));
            }

            if (!TryBuildPathPoints(pathEntity, spec.ItemCount, out var pathPoints, out var pathError))
            {
                return ValueTask.FromResult(CadCommandResult.Fail(pathError!));
            }

            var pathOrigin = pathPoints[0];
            for (var item = 1; item < pathPoints.Length; item++)
            {
                var delta = new XYZ(
                    pathPoints[item].X - pathOrigin.X,
                    pathPoints[item].Y - pathOrigin.Y,
                    pathPoints[item].Z - pathOrigin.Z);
                foreach (var target in targets)
                {
                    var newId = CadEntityId.New();
                    if (!TryAppendCreateWithTransform(
                            target,
                            newId,
                            point => CadGeometryTransform.Translate(point, delta),
                            angleDeltaRadians: 0.0,
                            modeName: "path",
                            forward,
                            inverse,
                            out var createError))
                    {
                        return ValueTask.FromResult(CadCommandResult.Fail(createError!));
                    }

                    createdIds.Add(newId);
                }
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
            if (session.EntityIndex.TryGetEntity(id, out var entity))
            {
                createdEntities.Add(entity);
            }
        }

        if (createdEntities.Count > 0)
        {
            session.SetSelection(createdEntities, CadSelectionMode.Replace);
        }

        return ValueTask.FromResult(CadCommandResult.Ok($"Created {forward.Count} {mode.ToString().ToLowerInvariant()} array entity instance(s).", forward));
    }

    private static bool TryParseArrayMode(
        IReadOnlyList<string> args,
        out CadArrayMode mode,
        out CadArraySpec spec,
        out int consumed,
        out string? error)
    {
        mode = CadArrayMode.Rectangular;
        spec = default;
        consumed = 0;
        error = null;

        if (args.Count == 0)
        {
            error = "Usage: ARRAY rows columns rowSpacing columnSpacing [handles...] | ARRAY POLAR itemCount angleStepDeg center [handles...] | ARRAY PATH itemCount pathHandle [handles...]";
            return false;
        }

        var firstToken = args[0];
        if (firstToken.Equals("POLAR", StringComparison.OrdinalIgnoreCase) ||
            firstToken.Equals("P", StringComparison.OrdinalIgnoreCase))
        {
            var polarArgs = args.Skip(1).ToArray();
            if (!CadCommandParsing.TryParseArrayPolarArguments(
                    polarArgs,
                    out var itemCount,
                    out var angleStepRadians,
                    out var center,
                    out var consumedPolar,
                    out error))
            {
                return false;
            }

            mode = CadArrayMode.Polar;
            spec = CadArraySpec.CreatePolar(itemCount, angleStepRadians, center);
            consumed = 1 + consumedPolar;
            return true;
        }

        if (firstToken.Equals("PATH", StringComparison.OrdinalIgnoreCase))
        {
            var pathArgs = args.Skip(1).ToArray();
            if (!TryParseArrayPathArguments(
                    pathArgs,
                    out var itemCount,
                    out var pathHandle,
                    out var consumedPath,
                    out error))
            {
                return false;
            }

            mode = CadArrayMode.Path;
            spec = CadArraySpec.CreatePath(itemCount, pathHandle);
            consumed = 1 + consumedPath;
            return true;
        }

        if (!CadCommandParsing.TryParseArrayRectangularArguments(
                args,
                out var rows,
                out var columns,
                out var rowSpacing,
                out var columnSpacing,
                out consumed,
                out error))
        {
            return false;
        }

        mode = CadArrayMode.Rectangular;
        spec = CadArraySpec.CreateRectangular(rows, columns, rowSpacing, columnSpacing);
        return true;
    }

    private static bool TryParseArrayPathArguments(
        IReadOnlyList<string> args,
        out int itemCount,
        out ulong pathHandle,
        out int consumed,
        out string? error)
    {
        itemCount = 0;
        pathHandle = 0;
        consumed = 0;
        error = null;

        if (args.Count < 2 ||
            !int.TryParse(args[0], out itemCount) ||
            itemCount < 2 ||
            !CadCommandParsing.TryParseHandle(args[1], out pathHandle))
        {
            error = "Usage: ARRAY PATH itemCount(>=2) pathHandle [handles...]";
            return false;
        }

        consumed = 2;
        return true;
    }

    private static bool TryBuildPathPoints(
        Entity pathEntity,
        int itemCount,
        out XYZ[] points,
        out string? error)
    {
        points = Array.Empty<XYZ>();
        error = null;
        if (itemCount < 2)
        {
            error = "ARRAY PATH requires itemCount >= 2.";
            return false;
        }

        switch (pathEntity)
        {
            case Line line:
                return TryBuildLinePathPoints(line, itemCount, out points, out error);
            case LwPolyline polyline:
                return TryBuildPolylinePathPoints(polyline, itemCount, out points, out error);
            default:
                error = $"ARRAY PATH supports LINE or LWPOLYLINE path entities, got '{pathEntity.GetType().Name}'.";
                return false;
        }
    }

    private static bool TryBuildLinePathPoints(
        Line line,
        int itemCount,
        out XYZ[] points,
        out string? error)
    {
        points = Array.Empty<XYZ>();
        error = null;
        var start = line.StartPoint;
        var end = line.EndPoint;
        var length = Distance(start, end);
        if (length <= 1e-9)
        {
            error = "ARRAY PATH requires a non-degenerate line path.";
            return false;
        }

        points = new XYZ[itemCount];
        for (var index = 0; index < itemCount; index++)
        {
            var t = index / (double)(itemCount - 1);
            points[index] = new XYZ(
                start.X + ((end.X - start.X) * t),
                start.Y + ((end.Y - start.Y) * t),
                start.Z + ((end.Z - start.Z) * t));
        }

        return true;
    }

    private static bool TryBuildPolylinePathPoints(
        LwPolyline polyline,
        int itemCount,
        out XYZ[] points,
        out string? error)
    {
        points = Array.Empty<XYZ>();
        error = null;
        var vertices = CadGeometryTransform.ToVertices(polyline).ToList();
        if (vertices.Count < 2)
        {
            error = "ARRAY PATH requires polyline path with at least two vertices.";
            return false;
        }

        if (polyline.IsClosed &&
            (vertices[0].X != vertices[^1].X ||
             vertices[0].Y != vertices[^1].Y ||
             vertices[0].Z != vertices[^1].Z))
        {
            vertices.Add(vertices[0]);
        }

        var cumulative = new double[vertices.Count];
        var totalLength = 0.0;
        for (var index = 1; index < vertices.Count; index++)
        {
            totalLength += Distance(vertices[index - 1], vertices[index]);
            cumulative[index] = totalLength;
        }

        if (totalLength <= 1e-9)
        {
            error = "ARRAY PATH requires non-degenerate polyline segments.";
            return false;
        }

        points = new XYZ[itemCount];
        for (var item = 0; item < itemCount; item++)
        {
            var t = item / (double)(itemCount - 1);
            var targetDistance = totalLength * t;
            var segmentIndex = 1;
            while (segmentIndex < cumulative.Length && cumulative[segmentIndex] < targetDistance)
            {
                segmentIndex++;
            }

            segmentIndex = Math.Clamp(segmentIndex, 1, vertices.Count - 1);
            var segmentStart = vertices[segmentIndex - 1];
            var segmentEnd = vertices[segmentIndex];
            var segmentStartDistance = cumulative[segmentIndex - 1];
            var segmentLength = Math.Max(1e-9, cumulative[segmentIndex] - segmentStartDistance);
            var localT = (targetDistance - segmentStartDistance) / segmentLength;
            points[item] = new XYZ(
                segmentStart.X + ((segmentEnd.X - segmentStart.X) * localT),
                segmentStart.Y + ((segmentEnd.Y - segmentStart.Y) * localT),
                segmentStart.Z + ((segmentEnd.Z - segmentStart.Z) * localT));
        }

        return true;
    }

    private static double Distance(XYZ first, XYZ second)
    {
        var dx = second.X - first.X;
        var dy = second.Y - first.Y;
        var dz = second.Z - first.Z;
        return Math.Sqrt((dx * dx) + (dy * dy) + (dz * dz));
    }

    private static bool TryAppendCreateWithTransform(
        Entity target,
        CadEntityId newId,
        Func<XYZ, XYZ> transformPoint,
        double angleDeltaRadians,
        string modeName,
        ICollection<CadOperation> forward,
        ICollection<CadOperation> inverse,
        out string? error)
    {
        error = null;

        switch (target)
        {
            case Line line:
            {
                var copiedStart = transformPoint(line.StartPoint);
                var copiedEnd = transformPoint(line.EndPoint);
                forward.Add(CadOperationPayloadCodec.CreateLine(newId, copiedStart, copiedEnd).WithSourceProperties(target));
                inverse.Add(CadOperationPayloadCodec.DeleteLine(newId, copiedStart, copiedEnd));
                return true;
            }
            case Arc arc:
            {
                var copiedCenter = transformPoint(arc.Center);
                var copiedStartAngle = CadGeometryTransform.NormalizeAngle(arc.StartAngle + angleDeltaRadians);
                var copiedEndAngle = CadGeometryTransform.NormalizeAngle(arc.EndAngle + angleDeltaRadians);
                forward.Add(CadOperationPayloadCodec.CreateArc(newId, copiedCenter, arc.Radius, copiedStartAngle, copiedEndAngle).WithSourceProperties(target));
                inverse.Add(CadOperationPayloadCodec.DeleteArc(newId, copiedCenter, arc.Radius, copiedStartAngle, copiedEndAngle));
                return true;
            }
            case Circle circle:
            {
                var copiedCenter = transformPoint(circle.Center);
                forward.Add(CadOperationPayloadCodec.CreateCircle(newId, copiedCenter, circle.Radius).WithSourceProperties(target));
                inverse.Add(CadOperationPayloadCodec.DeleteCircle(newId, copiedCenter, circle.Radius));
                return true;
            }
            case LwPolyline polyline:
            {
                var copiedVertices = CadGeometryTransform.ToVertices(polyline).Select(transformPoint).ToArray();
                var copiedClosed = polyline.IsClosed;
                forward.Add(CadOperationPayloadCodec.CreateLwPolyline(newId, copiedVertices, copiedClosed).WithSourceProperties(target));
                inverse.Add(CadOperationPayloadCodec.DeleteLwPolyline(newId, copiedVertices, copiedClosed));
                return true;
            }
            case Point point:
            {
                var copiedLocation = transformPoint(point.Location);
                forward.Add(CadOperationPayloadCodec.CreatePoint(newId, copiedLocation).WithSourceProperties(target));
                inverse.Add(CadOperationPayloadCodec.DeletePoint(newId, copiedLocation));
                return true;
            }
            case XLine xline:
            {
                var copiedFirstPoint = transformPoint(xline.FirstPoint);
                var copiedDirection = Math.Abs(angleDeltaRadians) > 1e-12
                    ? CadGeometryTransform.RotateDirectionAroundZ(CadGeometryTransform.NormalizeDirection(xline.Direction), angleDeltaRadians)
                    : CadGeometryTransform.NormalizeDirection(xline.Direction);
                forward.Add(CadOperationPayloadCodec.CreateXLine(newId, copiedFirstPoint, copiedDirection).WithSourceProperties(target));
                inverse.Add(CadOperationPayloadCodec.DeleteXLine(newId, copiedFirstPoint, copiedDirection));
                return true;
            }
            case Ray ray:
            {
                var copiedStartPoint = transformPoint(ray.StartPoint);
                var copiedDirection = Math.Abs(angleDeltaRadians) > 1e-12
                    ? CadGeometryTransform.RotateDirectionAroundZ(CadGeometryTransform.NormalizeDirection(ray.Direction), angleDeltaRadians)
                    : CadGeometryTransform.NormalizeDirection(ray.Direction);
                forward.Add(CadOperationPayloadCodec.CreateRay(newId, copiedStartPoint, copiedDirection).WithSourceProperties(target));
                inverse.Add(CadOperationPayloadCodec.DeleteRay(newId, copiedStartPoint, copiedDirection));
                return true;
            }
            case TextEntity text:
            {
                var copiedInsertPoint = transformPoint(text.InsertPoint);
                var copiedAlignmentPoint = transformPoint(text.AlignmentPoint);
                var copiedRotation = Math.Abs(angleDeltaRadians) > 1e-12
                    ? CadGeometryTransform.NormalizeAngle(text.Rotation + angleDeltaRadians)
                    : text.Rotation;
                forward.Add(CadOperationPayloadCodec.CreateText(
                        newId,
                        copiedInsertPoint,
                        copiedAlignmentPoint,
                        text.Height,
                        copiedRotation,
                        text.Value,
                        text.Normal)
                    .WithSourceProperties(target));
                inverse.Add(CadOperationPayloadCodec.DeleteText(
                    newId,
                    copiedInsertPoint,
                    copiedAlignmentPoint,
                    text.Height,
                    copiedRotation,
                    text.Value,
                    text.Normal));
                return true;
            }
            case MText mtext:
            {
                var copiedInsertPoint = transformPoint(mtext.InsertPoint);
                var copiedTextDirection = Math.Abs(angleDeltaRadians) > 1e-12
                    ? CadGeometryTransform.RotateDirectionAroundZ(CadGeometryTransform.NormalizeDirection(mtext.AlignmentPoint), angleDeltaRadians)
                    : CadGeometryTransform.NormalizeDirection(mtext.AlignmentPoint);
                forward.Add(CadOperationPayloadCodec.CreateMText(
                        newId,
                        copiedInsertPoint,
                        copiedTextDirection,
                        mtext.Height,
                        mtext.RectangleWidth,
                        mtext.Value,
                        mtext.Normal)
                    .WithSourceProperties(target));
                inverse.Add(CadOperationPayloadCodec.DeleteMText(
                    newId,
                    copiedInsertPoint,
                    copiedTextDirection,
                    mtext.Height,
                    mtext.RectangleWidth,
                    mtext.Value,
                    mtext.Normal));
                return true;
            }
            case Ellipse ellipse:
            {
                var copiedCenter = transformPoint(ellipse.Center);
                var copiedMajorAxisEndPoint = Math.Abs(angleDeltaRadians) > 1e-12
                    ? CadGeometryTransform.RotateVectorAroundZ(ellipse.MajorAxisEndPoint, angleDeltaRadians)
                    : ellipse.MajorAxisEndPoint;
                var copiedNormal = Math.Abs(angleDeltaRadians) > 1e-12
                    ? CadGeometryTransform.RotateVectorAroundZ(ellipse.Normal, angleDeltaRadians)
                    : ellipse.Normal;
                forward.Add(CadOperationPayloadCodec.CreateEllipse(
                        newId,
                        copiedCenter,
                        copiedMajorAxisEndPoint,
                        ellipse.RadiusRatio,
                        ellipse.StartParameter,
                        ellipse.EndParameter,
                        copiedNormal)
                    .WithSourceProperties(target));
                inverse.Add(CadOperationPayloadCodec.DeleteEllipse(
                    newId,
                    copiedCenter,
                    copiedMajorAxisEndPoint,
                    ellipse.RadiusRatio,
                    ellipse.StartParameter,
                    ellipse.EndParameter,
                    copiedNormal));
                return true;
            }
            case Spline spline:
            {
                var copiedFitPoints = spline.FitPoints.Select(transformPoint).ToArray();
                var copiedControlPoints = spline.ControlPoints.Select(transformPoint).ToArray();
                var copiedStartTangent = Math.Abs(angleDeltaRadians) > 1e-12
                    ? CadGeometryTransform.RotateVectorAroundZ(spline.StartTangent, angleDeltaRadians)
                    : spline.StartTangent;
                var copiedEndTangent = Math.Abs(angleDeltaRadians) > 1e-12
                    ? CadGeometryTransform.RotateVectorAroundZ(spline.EndTangent, angleDeltaRadians)
                    : spline.EndTangent;
                var copiedNormal = Math.Abs(angleDeltaRadians) > 1e-12
                    ? CadGeometryTransform.RotateVectorAroundZ(spline.Normal, angleDeltaRadians)
                    : spline.Normal;
                var copiedKnots = spline.Knots.ToArray();
                var copiedWeights = spline.Weights.ToArray();
                forward.Add(CadOperationPayloadCodec.CreateSpline(
                        newId,
                        spline.Degree,
                        spline.IsClosed,
                        spline.IsPeriodic,
                        copiedFitPoints,
                        copiedControlPoints,
                        copiedKnots,
                        copiedWeights,
                        copiedStartTangent,
                        copiedEndTangent,
                        copiedNormal)
                    .WithSourceProperties(target));
                inverse.Add(CadOperationPayloadCodec.DeleteSpline(
                    newId,
                    spline.Degree,
                    spline.IsClosed,
                    spline.IsPeriodic,
                    copiedFitPoints,
                    copiedControlPoints,
                    copiedKnots,
                    copiedWeights,
                    copiedStartTangent,
                    copiedEndTangent,
                    copiedNormal));
                return true;
            }
            case Hatch hatch:
            {
                if (!CadHatchGeometry.TryGetLoops(hatch, out var loops, out var loopError))
                {
                    error = $"ARRAY {modeName} could not copy HATCH: {loopError}";
                    return false;
                }

                var copiedLoops = loops
                    .Select(loop => loop.Select(transformPoint).ToArray())
                    .ToArray();
                var patternName = CadHatchGeometry.ResolvePatternName(hatch);
                forward.Add(CadOperationPayloadCodec.CreateHatch(
                        newId,
                        copiedLoops,
                        hatch.IsSolid,
                        patternName,
                        hatch.Normal)
                    .WithSourceProperties(target));
                inverse.Add(CadOperationPayloadCodec.DeleteHatch(
                    newId,
                    copiedLoops,
                    hatch.IsSolid,
                    patternName,
                    hatch.Normal));
                return true;
            }
            default:
                error = $"ARRAY {modeName} does not support entity type '{target.GetType().Name}' yet.";
                return false;
        }
    }

    private enum CadArrayMode
    {
        Rectangular,
        Polar,
        Path
    }

    private readonly record struct CadArraySpec(
        int Rows,
        int Columns,
        double RowSpacing,
        double ColumnSpacing,
        int ItemCount,
        double AngleStepRadians,
        XYZ Center,
        ulong PathHandle)
    {
        public int CopyCount => Rows > 0
            ? (Rows * Columns) - 1
            : ItemCount - 1;

        public static CadArraySpec CreateRectangular(int rows, int columns, double rowSpacing, double columnSpacing)
        {
            return new CadArraySpec(
                Rows: rows,
                Columns: columns,
                RowSpacing: rowSpacing,
                ColumnSpacing: columnSpacing,
                ItemCount: 0,
                AngleStepRadians: 0.0,
                Center: XYZ.Zero,
                PathHandle: 0);
        }

        public static CadArraySpec CreatePolar(int itemCount, double angleStepRadians, XYZ center)
        {
            return new CadArraySpec(
                Rows: 0,
                Columns: 0,
                RowSpacing: 0.0,
                ColumnSpacing: 0.0,
                ItemCount: itemCount,
                AngleStepRadians: angleStepRadians,
                Center: center,
                PathHandle: 0);
        }

        public static CadArraySpec CreatePath(int itemCount, ulong pathHandle)
        {
            return new CadArraySpec(
                Rows: 0,
                Columns: 0,
                RowSpacing: 0.0,
                ColumnSpacing: 0.0,
                ItemCount: itemCount,
                AngleStepRadians: 0.0,
                Center: XYZ.Zero,
                PathHandle: pathHandle);
        }
    }
}
