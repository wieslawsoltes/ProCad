using System.Globalization;
using ACadInspector.Editing.Operations;
using CSMath;

namespace ACadInspector.Editing.Commands;

internal static class CadCommandParsing
{
    public static bool TryParseLineArguments(
        IReadOnlyList<string> args,
        out XYZ start,
        out XYZ end,
        out string? error)
    {
        start = XYZ.Zero;
        end = XYZ.Zero;
        error = null;

        if (args.Count == 2 &&
            CadOperationPayloadCodec.TryParsePoint(args[0], out start) &&
            CadOperationPayloadCodec.TryParsePoint(args[1], out end))
        {
            return true;
        }

        if (TryParseCoordinateVector(args, 0, out start, out var consumedStart) &&
            TryParseCoordinateVector(args, consumedStart, out end, out var consumedEnd) &&
            consumedEnd == args.Count)
        {
            return true;
        }

        error = "Usage: LINE x1,y1[,z1] x2,y2[,z2]";
        return false;
    }

    public static bool TryParseXLineArguments(
        IReadOnlyList<string> args,
        out XYZ firstPoint,
        out XYZ direction,
        out string? error)
    {
        return TryParseDirectedLineArguments(
            args,
            "Usage: XLINE x1,y1[,z1] x2,y2[,z2]",
            out firstPoint,
            out direction,
            out error);
    }

    public static bool TryParseRayArguments(
        IReadOnlyList<string> args,
        out XYZ startPoint,
        out XYZ direction,
        out string? error)
    {
        return TryParseDirectedLineArguments(
            args,
            "Usage: RAY x1,y1[,z1] x2,y2[,z2]",
            out startPoint,
            out direction,
            out error);
    }

    public static bool TryParseTranslation(
        IReadOnlyList<string> args,
        out XYZ vector,
        out int consumed,
        out string? error)
    {
        vector = XYZ.Zero;
        consumed = 0;
        error = null;

        if (!TryParseCoordinateVector(args, 0, out vector, out consumed))
        {
            error = "Usage: MOVE dx,dy[,dz] [handles...] or COPY dx,dy[,dz] [handles...]";
            return false;
        }

        return true;
    }

    public static bool TryParseCircleArguments(
        IReadOnlyList<string> args,
        out XYZ center,
        out double radius,
        out string? error)
    {
        center = XYZ.Zero;
        radius = 0.0;
        error = null;

        if (!TryParseCoordinateVector(args, 0, out center, out var consumed))
        {
            error = "Usage: CIRCLE centerX,centerY[,centerZ] radius";
            return false;
        }

        if (consumed >= args.Count ||
            !double.TryParse(args[consumed], NumberStyles.Float, CultureInfo.InvariantCulture, out radius) ||
            radius <= 0.0 ||
            consumed + 1 != args.Count)
        {
            error = "Usage: CIRCLE centerX,centerY[,centerZ] radius";
            return false;
        }

        return true;
    }

    public static bool TryParseArcArguments(
        IReadOnlyList<string> args,
        out XYZ center,
        out double radius,
        out double startAngleRadians,
        out double endAngleRadians,
        out string? error)
    {
        center = XYZ.Zero;
        radius = 0.0;
        startAngleRadians = 0.0;
        endAngleRadians = 0.0;
        error = null;

        if (!TryParseCoordinateVector(args, 0, out center, out var consumed))
        {
            error = "Usage: ARC centerX,centerY[,centerZ] radius startAngleDeg endAngleDeg";
            return false;
        }

        if (consumed + 2 >= args.Count ||
            !double.TryParse(args[consumed], NumberStyles.Float, CultureInfo.InvariantCulture, out radius) ||
            !double.TryParse(args[consumed + 1], NumberStyles.Float, CultureInfo.InvariantCulture, out var startAngleDegrees) ||
            !double.TryParse(args[consumed + 2], NumberStyles.Float, CultureInfo.InvariantCulture, out var endAngleDegrees) ||
            radius <= 0.0 ||
            consumed + 3 != args.Count)
        {
            error = "Usage: ARC centerX,centerY[,centerZ] radius startAngleDeg endAngleDeg";
            return false;
        }

        startAngleRadians = DegreesToRadians(startAngleDegrees);
        endAngleRadians = DegreesToRadians(endAngleDegrees);
        return true;
    }

    public static bool TryParseEllipseArguments(
        IReadOnlyList<string> args,
        out XYZ center,
        out XYZ majorAxisEndPoint,
        out double radiusRatio,
        out double startParameter,
        out double endParameter,
        out string? error)
    {
        center = XYZ.Zero;
        majorAxisEndPoint = XYZ.Zero;
        radiusRatio = 0.0;
        startParameter = 0.0;
        endParameter = Math.PI * 2.0;
        error = null;

        if (!TryParseCoordinateVector(args, 0, out center, out var consumedCenter) ||
            !TryParseCoordinateVector(args, consumedCenter, out var majorAxisEndAbsolute, out var consumedMajorAxis) ||
            consumedMajorAxis >= args.Count ||
            !double.TryParse(args[consumedMajorAxis], NumberStyles.Float, CultureInfo.InvariantCulture, out radiusRatio) ||
            radiusRatio <= 0.0 ||
            radiusRatio > 1.0)
        {
            error = "Usage: ELLIPSE centerX,centerY[,z] majorAxisEndX,majorAxisEndY[,z] ratio(0,1] [startDeg endDeg]";
            return false;
        }

        majorAxisEndPoint = new XYZ(
            majorAxisEndAbsolute.X - center.X,
            majorAxisEndAbsolute.Y - center.Y,
            majorAxisEndAbsolute.Z - center.Z);
        if ((majorAxisEndPoint.X * majorAxisEndPoint.X) +
            (majorAxisEndPoint.Y * majorAxisEndPoint.Y) +
            (majorAxisEndPoint.Z * majorAxisEndPoint.Z) <= 1e-12)
        {
            error = "ELLIPSE major axis endpoint must be distinct from center.";
            return false;
        }

        consumedMajorAxis++;
        if (consumedMajorAxis == args.Count)
        {
            return true;
        }

        if (consumedMajorAxis + 1 >= args.Count ||
            !double.TryParse(args[consumedMajorAxis], NumberStyles.Float, CultureInfo.InvariantCulture, out var startDegrees) ||
            !double.TryParse(args[consumedMajorAxis + 1], NumberStyles.Float, CultureInfo.InvariantCulture, out var endDegrees) ||
            consumedMajorAxis + 2 != args.Count)
        {
            error = "Usage: ELLIPSE centerX,centerY[,z] majorAxisEndX,majorAxisEndY[,z] ratio(0,1] [startDeg endDeg]";
            return false;
        }

        startParameter = DegreesToRadians(startDegrees);
        endParameter = DegreesToRadians(endDegrees);
        return true;
    }

    public static bool TryParseSplineArguments(
        IReadOnlyList<string> args,
        out IReadOnlyList<XYZ> fitPoints,
        out bool isClosed,
        out string? error)
    {
        fitPoints = Array.Empty<XYZ>();
        isClosed = false;
        error = null;

        if (args.Count < 2)
        {
            error = "Usage: SPLINE p1 p2 [p3 ...] [CLOSE]";
            return false;
        }

        var points = new List<XYZ>();
        var index = 0;
        while (index < args.Count)
        {
            var token = args[index];
            if (token.Equals("CLOSE", StringComparison.OrdinalIgnoreCase) ||
                token.Equals("C", StringComparison.OrdinalIgnoreCase))
            {
                isClosed = true;
                index++;
                continue;
            }

            if (!TryParseCoordinateVector(args, index, out var point, out var consumedTo) ||
                consumedTo <= index)
            {
                error = "Usage: SPLINE p1 p2 [p3 ...] [CLOSE]";
                return false;
            }

            points.Add(point);
            index = consumedTo;
        }

        if (points.Count < 2)
        {
            error = "SPLINE requires at least two fit points.";
            return false;
        }

        fitPoints = points;
        return true;
    }

    public static bool TryParsePolylineArguments(
        IReadOnlyList<string> args,
        out IReadOnlyList<XYZ> vertices,
        out bool isClosed,
        out string? error)
    {
        vertices = Array.Empty<XYZ>();
        isClosed = false;
        error = null;

        if (args.Count < 2)
        {
            error = "Usage: PLINE p1 p2 [p3 ...] [CLOSE]";
            return false;
        }

        var endIndex = args.Count;
        var last = args[^1];
        if (string.Equals(last, "C", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(last, "CLOSE", StringComparison.OrdinalIgnoreCase))
        {
            isClosed = true;
            endIndex--;
        }

        var points = new List<XYZ>();
        var index = 0;
        while (index < endIndex)
        {
            if (!TryParseCoordinateVector(args, index, out var point, out var consumedTo) ||
                consumedTo <= index)
            {
                error = "Usage: PLINE p1 p2 [p3 ...] [CLOSE]";
                return false;
            }

            points.Add(point);
            index = consumedTo;
        }

        if (points.Count < 2)
        {
            error = "PLINE requires at least two points.";
            return false;
        }

        vertices = points;
        return true;
    }

    public static bool TryParsePointArgument(
        IReadOnlyList<string> args,
        out XYZ location,
        out string? error)
    {
        location = XYZ.Zero;
        error = null;

        if (!TryParseCoordinateVector(args, 0, out location, out var consumed) || consumed != args.Count)
        {
            error = "Usage: POINT x,y[,z]";
            return false;
        }

        return true;
    }

    public static bool TryParseInsertArguments(
        IReadOnlyList<string> args,
        out string blockName,
        out XYZ insertPoint,
        out double scale,
        out double rotationRadians,
        out string? error)
    {
        blockName = string.Empty;
        insertPoint = XYZ.Zero;
        scale = 1.0;
        rotationRadians = 0.0;
        error = null;

        if (args.Count < 2)
        {
            error = "Usage: INSERT blockName x,y[,z] [scale] [rotationDeg]";
            return false;
        }

        blockName = args[0].Trim();
        if (string.IsNullOrWhiteSpace(blockName))
        {
            error = "INSERT block name cannot be empty.";
            return false;
        }

        if (!TryParseCoordinateVector(args, 1, out insertPoint, out var consumed))
        {
            error = "Usage: INSERT blockName x,y[,z] [scale] [rotationDeg]";
            return false;
        }

        if (consumed < args.Count)
        {
            if (!double.TryParse(args[consumed], NumberStyles.Float, CultureInfo.InvariantCulture, out scale) ||
                scale <= 0.0)
            {
                error = "Usage: INSERT blockName x,y[,z] [scale] [rotationDeg]";
                return false;
            }

            consumed++;
        }

        if (consumed < args.Count)
        {
            if (!double.TryParse(args[consumed], NumberStyles.Float, CultureInfo.InvariantCulture, out var rotationDegrees))
            {
                error = "Usage: INSERT blockName x,y[,z] [scale] [rotationDeg]";
                return false;
            }

            rotationRadians = DegreesToRadians(rotationDegrees);
            consumed++;
        }

        if (consumed != args.Count)
        {
            error = "Usage: INSERT blockName x,y[,z] [scale] [rotationDeg]";
            return false;
        }

        return true;
    }

    public static bool TryParseTextArguments(
        IReadOnlyList<string> args,
        out XYZ insertPoint,
        out double height,
        out double rotationRadians,
        out string value,
        out string? error)
    {
        insertPoint = XYZ.Zero;
        height = 0.0;
        rotationRadians = 0.0;
        value = string.Empty;
        error = null;

        if (!TryParseCoordinateVector(args, 0, out insertPoint, out var consumedPoint) ||
            consumedPoint >= args.Count ||
            !double.TryParse(args[consumedPoint], NumberStyles.Float, CultureInfo.InvariantCulture, out height) ||
            height <= 0.0)
        {
            error = "Usage: TEXT x,y[,z] height [rotationDeg] value";
            return false;
        }

        var index = consumedPoint + 1;
        if (args.Count - index >= 2 &&
            double.TryParse(args[index], NumberStyles.Float, CultureInfo.InvariantCulture, out var rotationDegrees))
        {
            rotationRadians = DegreesToRadians(rotationDegrees);
            index++;
        }

        if (index >= args.Count)
        {
            error = "Usage: TEXT x,y[,z] height [rotationDeg] value";
            return false;
        }

        value = string.Join(' ', args.Skip(index));
        if (string.IsNullOrWhiteSpace(value))
        {
            error = "TEXT value cannot be empty.";
            return false;
        }

        return true;
    }

    public static bool TryParseMTextArguments(
        IReadOnlyList<string> args,
        out XYZ insertPoint,
        out double height,
        out double width,
        out double rotationRadians,
        out string value,
        out string? error)
    {
        insertPoint = XYZ.Zero;
        height = 0.0;
        width = 0.0;
        rotationRadians = 0.0;
        value = string.Empty;
        error = null;

        if (!TryParseCoordinateVector(args, 0, out insertPoint, out var consumedPoint) ||
            consumedPoint + 1 >= args.Count ||
            !double.TryParse(args[consumedPoint], NumberStyles.Float, CultureInfo.InvariantCulture, out height) ||
            !double.TryParse(args[consumedPoint + 1], NumberStyles.Float, CultureInfo.InvariantCulture, out width) ||
            height <= 0.0 ||
            width <= 0.0)
        {
            error = "Usage: MTEXT x,y[,z] height width [rotationDeg] value";
            return false;
        }

        var index = consumedPoint + 2;
        if (args.Count - index >= 2 &&
            double.TryParse(args[index], NumberStyles.Float, CultureInfo.InvariantCulture, out var rotationDegrees))
        {
            rotationRadians = DegreesToRadians(rotationDegrees);
            index++;
        }

        if (index >= args.Count)
        {
            error = "Usage: MTEXT x,y[,z] height width [rotationDeg] value";
            return false;
        }

        value = string.Join(' ', args.Skip(index));
        if (string.IsNullOrWhiteSpace(value))
        {
            error = "MTEXT value cannot be empty.";
            return false;
        }

        return true;
    }

    public static bool TryParsePolygonArguments(
        IReadOnlyList<string> args,
        out int sides,
        out XYZ center,
        out double radius,
        out bool circumscribed,
        out string? error)
    {
        sides = 0;
        center = XYZ.Zero;
        radius = 0.0;
        circumscribed = false;
        error = null;

        if (args.Count < 3 ||
            !int.TryParse(args[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out sides) ||
            sides < 3)
        {
            error = "Usage: POLYGON sides(>=3) centerX,centerY[,z] radius [INSCRIBED|CIRCUMSCRIBED]";
            return false;
        }

        if (!TryParseCoordinateVector(args, 1, out center, out var consumed) ||
            consumed >= args.Count ||
            !double.TryParse(args[consumed], NumberStyles.Float, CultureInfo.InvariantCulture, out radius) ||
            radius <= 0.0)
        {
            error = "Usage: POLYGON sides(>=3) centerX,centerY[,z] radius [INSCRIBED|CIRCUMSCRIBED]";
            return false;
        }

        consumed++;
        if (consumed == args.Count)
        {
            return true;
        }

        if (consumed + 1 != args.Count)
        {
            error = "Usage: POLYGON sides(>=3) centerX,centerY[,z] radius [INSCRIBED|CIRCUMSCRIBED]";
            return false;
        }

        var mode = args[consumed];
        if (mode.Equals("C", StringComparison.OrdinalIgnoreCase) ||
            mode.Equals("CIRCUMSCRIBED", StringComparison.OrdinalIgnoreCase))
        {
            circumscribed = true;
            return true;
        }

        if (mode.Equals("I", StringComparison.OrdinalIgnoreCase) ||
            mode.Equals("INSCRIBED", StringComparison.OrdinalIgnoreCase))
        {
            circumscribed = false;
            return true;
        }

        error = "Usage: POLYGON sides(>=3) centerX,centerY[,z] radius [INSCRIBED|CIRCUMSCRIBED]";
        return false;
    }

    public static bool TryParseRectangArguments(
        IReadOnlyList<string> args,
        out XYZ firstCorner,
        out XYZ secondCorner,
        out string? error)
    {
        firstCorner = XYZ.Zero;
        secondCorner = XYZ.Zero;
        error = null;

        if (!TryParseCoordinateVector(args, 0, out firstCorner, out var consumedFirst) ||
            !TryParseCoordinateVector(args, consumedFirst, out secondCorner, out var consumedSecond) ||
            consumedSecond != args.Count)
        {
            error = "Usage: RECTANG x1,y1[,z1] x2,y2[,z2]";
            return false;
        }

        return true;
    }

    public static bool TryParseRotateArguments(
        IReadOnlyList<string> args,
        out double angleRadians,
        out XYZ center,
        out int consumed,
        out string? error)
    {
        angleRadians = 0.0;
        center = XYZ.Zero;
        consumed = 0;
        error = null;

        if (args.Count == 0 ||
            !double.TryParse(args[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var angleDegrees))
        {
            error = "Usage: ROTATE angleDeg [centerX,centerY[,z]] [handles...]";
            return false;
        }

        angleRadians = DegreesToRadians(angleDegrees);
        consumed = 1;

        if (args.Count > 1 && args[1].Contains(',', StringComparison.Ordinal))
        {
            if (!CadOperationPayloadCodec.TryParsePoint(args[1], out center))
            {
                error = "Usage: ROTATE angleDeg [centerX,centerY[,z]] [handles...]";
                return false;
            }

            consumed = 2;
        }

        return true;
    }

    public static bool TryParseScaleArguments(
        IReadOnlyList<string> args,
        out double factor,
        out XYZ center,
        out int consumed,
        out string? error)
    {
        factor = 0.0;
        center = XYZ.Zero;
        consumed = 0;
        error = null;

        if (args.Count == 0 ||
            !double.TryParse(args[0], NumberStyles.Float, CultureInfo.InvariantCulture, out factor) ||
            factor <= 0.0)
        {
            error = "Usage: SCALE factor(>0) [centerX,centerY[,z]] [handles...]";
            return false;
        }

        consumed = 1;
        if (args.Count > 1 && args[1].Contains(',', StringComparison.Ordinal))
        {
            if (!CadOperationPayloadCodec.TryParsePoint(args[1], out center))
            {
                error = "Usage: SCALE factor(>0) [centerX,centerY[,z]] [handles...]";
                return false;
            }

            consumed = 2;
        }

        return true;
    }

    public static bool TryParseMirrorArguments(
        IReadOnlyList<string> args,
        out XYZ axisStart,
        out XYZ axisEnd,
        out int consumed,
        out string? error)
    {
        axisStart = XYZ.Zero;
        axisEnd = XYZ.Zero;
        consumed = 0;
        error = null;

        if (!TryParseCoordinateVector(args, 0, out axisStart, out var consumedStart) ||
            !TryParseCoordinateVector(args, consumedStart, out axisEnd, out var consumedEnd))
        {
            error = "Usage: MIRROR x1,y1[,z1] x2,y2[,z2] [handles...]";
            return false;
        }

        var dx = axisEnd.X - axisStart.X;
        var dy = axisEnd.Y - axisStart.Y;
        var dz = axisEnd.Z - axisStart.Z;
        if ((dx * dx) + (dy * dy) + (dz * dz) <= double.Epsilon)
        {
            error = "MIRROR axis points must be distinct.";
            return false;
        }

        consumed = consumedEnd;
        return true;
    }

    public static bool TryParseOffsetArguments(
        IReadOnlyList<string> args,
        out double distance,
        out int sideSign,
        out int consumed,
        out string? error)
    {
        distance = 0.0;
        sideSign = 1;
        consumed = 0;
        error = null;

        if (args.Count == 0 ||
            !double.TryParse(args[0], NumberStyles.Float, CultureInfo.InvariantCulture, out distance) ||
            distance <= 0.0)
        {
            error = "Usage: OFFSET distance(>0) [LEFT|RIGHT|OUTER|INNER] [handles...]";
            return false;
        }

        consumed = 1;
        if (args.Count == 1)
        {
            return true;
        }

        var sideToken = args[1];
        if (sideToken.Equals("LEFT", StringComparison.OrdinalIgnoreCase) ||
            sideToken.Equals("L", StringComparison.OrdinalIgnoreCase) ||
            sideToken.Equals("OUTER", StringComparison.OrdinalIgnoreCase) ||
            sideToken.Equals("O", StringComparison.OrdinalIgnoreCase))
        {
            sideSign = 1;
            consumed = 2;
            return true;
        }

        if (sideToken.Equals("RIGHT", StringComparison.OrdinalIgnoreCase) ||
            sideToken.Equals("R", StringComparison.OrdinalIgnoreCase) ||
            sideToken.Equals("INNER", StringComparison.OrdinalIgnoreCase) ||
            sideToken.Equals("I", StringComparison.OrdinalIgnoreCase))
        {
            sideSign = -1;
            consumed = 2;
            return true;
        }

        return true;
    }

    public static bool TryParseTrimExtendArguments(
        IReadOnlyList<string> args,
        out ulong boundaryHandle,
        out ulong targetHandle,
        out CadLineEndpoint endpoint,
        out string? error)
    {
        boundaryHandle = 0;
        targetHandle = 0;
        endpoint = CadLineEndpoint.End;
        error = null;

        if (args.Count < 2 ||
            !TryParseHandle(args[0], out boundaryHandle) ||
            !TryParseHandle(args[1], out targetHandle))
        {
            error = "Usage: TRIM/EXTEND boundaryHandle targetHandle [START|END]";
            return false;
        }

        if (args.Count == 2)
        {
            return true;
        }

        if (args.Count != 3)
        {
            error = "Usage: TRIM/EXTEND boundaryHandle targetHandle [START|END]";
            return false;
        }

        var token = args[2];
        if (token.Equals("START", StringComparison.OrdinalIgnoreCase) ||
            token.Equals("S", StringComparison.OrdinalIgnoreCase))
        {
            endpoint = CadLineEndpoint.Start;
            return true;
        }

        if (token.Equals("END", StringComparison.OrdinalIgnoreCase) ||
            token.Equals("E", StringComparison.OrdinalIgnoreCase))
        {
            endpoint = CadLineEndpoint.End;
            return true;
        }

        error = "Usage: TRIM/EXTEND boundaryHandle targetHandle [START|END]";
        return false;
    }

    public static bool TryParseBreakArguments(
        IReadOnlyList<string> args,
        out ulong targetHandle,
        out XYZ firstBreakPoint,
        out XYZ secondBreakPoint,
        out bool hasSecondBreakPoint,
        out string? error)
    {
        targetHandle = 0;
        firstBreakPoint = XYZ.Zero;
        secondBreakPoint = XYZ.Zero;
        hasSecondBreakPoint = false;
        error = null;

        if (args.Count is not (2 or 3) ||
            !TryParseHandle(args[0], out targetHandle) ||
            !TryParsePointToken(args[1], out firstBreakPoint))
        {
            error = "Usage: BREAK targetHandle firstPoint [secondPoint]";
            return false;
        }

        if (args.Count == 3)
        {
            if (!TryParsePointToken(args[2], out secondBreakPoint))
            {
                error = "Usage: BREAK targetHandle firstPoint [secondPoint]";
                return false;
            }

            hasSecondBreakPoint = true;
        }

        return true;
    }

    public static bool TryParseFilletArguments(
        IReadOnlyList<string> args,
        out double radius,
        out int consumed,
        out string? error)
    {
        radius = 0.0;
        consumed = 0;
        error = null;

        if (args.Count == 0 ||
            !double.TryParse(args[0], NumberStyles.Float, CultureInfo.InvariantCulture, out radius) ||
            radius <= 0.0)
        {
            error = "Usage: FILLET radius(>0) [entityHandle1 entityHandle2]";
            return false;
        }

        consumed = 1;
        return true;
    }

    public static bool TryParseChamferArguments(
        IReadOnlyList<string> args,
        out double firstDistance,
        out double secondDistance,
        out int consumed,
        out string? error)
    {
        firstDistance = 0.0;
        secondDistance = 0.0;
        consumed = 0;
        error = null;

        if (args.Count == 0 ||
            !double.TryParse(args[0], NumberStyles.Float, CultureInfo.InvariantCulture, out firstDistance) ||
            firstDistance <= 0.0)
        {
            error = "Usage: CHAMFER distance1(>0) [distance2(>0)] [entityHandle1 entityHandle2]";
            return false;
        }

        secondDistance = firstDistance;
        consumed = 1;

        var canHaveExplicitSecondDistance =
            args.Count == 2 ||
            (args.Count > 3 && args.Count - 2 >= 2);

        if (canHaveExplicitSecondDistance &&
            double.TryParse(args[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var parsedSecondDistance))
        {
            if (parsedSecondDistance <= 0.0)
            {
                error = "Usage: CHAMFER distance1(>0) [distance2(>0)] [entityHandle1 entityHandle2]";
                return false;
            }

            secondDistance = parsedSecondDistance;
            consumed = 2;
        }

        return true;
    }

    public static bool TryParseStretchArguments(
        IReadOnlyList<string> args,
        out XYZ delta,
        out XYZ gripPoint,
        out int consumed,
        out string? error)
    {
        delta = XYZ.Zero;
        gripPoint = XYZ.Zero;
        consumed = 0;
        error = null;

        if (!TryParseCoordinateVector(args, 0, out delta, out var consumedDelta) ||
            !TryParseCoordinateVector(args, consumedDelta, out gripPoint, out var consumedGrip))
        {
            error = "Usage: STRETCH dx,dy[,dz] gripX,gripY[,z] [handles...]";
            return false;
        }

        consumed = consumedGrip;
        return true;
    }

    public static bool TryParseArrayRectangularArguments(
        IReadOnlyList<string> args,
        out int rows,
        out int columns,
        out double rowSpacing,
        out double columnSpacing,
        out int consumed,
        out string? error)
    {
        rows = 0;
        columns = 0;
        rowSpacing = 0.0;
        columnSpacing = 0.0;
        consumed = 0;
        error = null;

        if (args.Count < 4 ||
            !int.TryParse(args[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out rows) ||
            !int.TryParse(args[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out columns) ||
            !double.TryParse(args[2], NumberStyles.Float, CultureInfo.InvariantCulture, out rowSpacing) ||
            !double.TryParse(args[3], NumberStyles.Float, CultureInfo.InvariantCulture, out columnSpacing) ||
            rows < 1 ||
            columns < 1 ||
            (rows == 1 && columns == 1))
        {
            error = "Usage: ARRAY rows(>=1) columns(>=1) rowSpacing columnSpacing [handles...]";
            return false;
        }

        consumed = 4;
        return true;
    }

    public static bool TryParseArrayPolarArguments(
        IReadOnlyList<string> args,
        out int itemCount,
        out double angleStepRadians,
        out XYZ center,
        out int consumed,
        out string? error)
    {
        itemCount = 0;
        angleStepRadians = 0.0;
        center = XYZ.Zero;
        consumed = 0;
        error = null;

        if (args.Count < 3 ||
            !int.TryParse(args[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out itemCount) ||
            itemCount < 2 ||
            !double.TryParse(args[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var angleStepDegrees))
        {
            error = "Usage: ARRAY POLAR itemCount(>=2) angleStepDeg centerX,centerY[,z] [handles...]";
            return false;
        }

        if (!TryParseCoordinateVector(args, 2, out center, out consumed))
        {
            error = "Usage: ARRAY POLAR itemCount(>=2) angleStepDeg centerX,centerY[,z] [handles...]";
            return false;
        }

        angleStepRadians = DegreesToRadians(angleStepDegrees);
        return true;
    }

    public static bool TryParseHatchArguments(
        IReadOnlyList<string> args,
        out string patternName,
        out bool isSolid,
        out int consumed,
        out string? error)
    {
        patternName = "SOLID";
        isSolid = true;
        consumed = 0;
        error = null;

        if (args.Count == 0)
        {
            return true;
        }

        var firstToken = args[0];
        if (TryParseHandle(firstToken, out _))
        {
            return true;
        }

        if (string.IsNullOrWhiteSpace(firstToken))
        {
            error = "Usage: HATCH [SOLID|patternName] [handles...]";
            return false;
        }

        consumed = 1;
        if (firstToken.Equals("SOLID", StringComparison.OrdinalIgnoreCase))
        {
            patternName = "SOLID";
            isSolid = true;
            return true;
        }

        patternName = firstToken;
        isSolid = false;
        return true;
    }

    public static bool TryParseAlignArguments(
        IReadOnlyList<string> args,
        out XYZ sourceFirstPoint,
        out XYZ destinationFirstPoint,
        out XYZ sourceSecondPoint,
        out XYZ destinationSecondPoint,
        out bool hasSecondPair,
        out int consumed,
        out string? error)
    {
        sourceFirstPoint = XYZ.Zero;
        destinationFirstPoint = XYZ.Zero;
        sourceSecondPoint = XYZ.Zero;
        destinationSecondPoint = XYZ.Zero;
        hasSecondPair = false;
        consumed = 0;
        error = null;

        if (!TryParseCoordinateVector(args, 0, out sourceFirstPoint, out var consumedSourceFirst) ||
            !TryParseCoordinateVector(args, consumedSourceFirst, out destinationFirstPoint, out var consumedDestinationFirst))
        {
            error = "Usage: ALIGN source1X,source1Y[,z] dest1X,dest1Y[,z] [source2X,source2Y[,z] dest2X,dest2Y[,z]] [handles...]";
            return false;
        }

        consumed = consumedDestinationFirst;

        if (args.Count - consumed >= 2 &&
            args[consumed].Contains(',', StringComparison.Ordinal) &&
            TryParseCoordinateVector(args, consumed, out sourceSecondPoint, out var consumedSourceSecond) &&
            TryParseCoordinateVector(args, consumedSourceSecond, out destinationSecondPoint, out var consumedDestinationSecond))
        {
            consumed = consumedDestinationSecond;
            hasSecondPair = true;
        }

        return true;
    }

    public static bool TryParseMatchPropArguments(
        IReadOnlyList<string> args,
        out ulong sourceHandle,
        out int consumed,
        out string? error)
    {
        sourceHandle = 0;
        consumed = 0;
        error = null;

        if (args.Count == 0 || !TryParseHandle(args[0], out sourceHandle))
        {
            error = "Usage: MATCHPROP sourceHandle [targetHandles...]";
            return false;
        }

        consumed = 1;
        return true;
    }

    public static bool TryParseHandle(string token, out ulong handle)
    {
        handle = 0;
        if (string.IsNullOrWhiteSpace(token))
        {
            return false;
        }

        var candidate = token.Trim();
        if (candidate.StartsWith("H:", StringComparison.OrdinalIgnoreCase))
        {
            candidate = candidate[2..];
        }

        if (candidate.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            candidate = candidate[2..];
        }

        return ulong.TryParse(candidate, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out handle) ||
               ulong.TryParse(candidate, NumberStyles.Integer, CultureInfo.InvariantCulture, out handle);
    }

    private static bool TryParseCoordinateVector(IReadOnlyList<string> args, int startIndex, out XYZ point, out int consumedTo)
    {
        point = XYZ.Zero;
        consumedTo = startIndex;
        if (startIndex >= args.Count)
        {
            return false;
        }

        if (CadOperationPayloadCodec.TryParsePoint(args[startIndex], out point))
        {
            consumedTo = startIndex + 1;
            return true;
        }

        if (!double.TryParse(args[startIndex], NumberStyles.Float, CultureInfo.InvariantCulture, out var x) ||
            startIndex + 1 >= args.Count ||
            !double.TryParse(args[startIndex + 1], NumberStyles.Float, CultureInfo.InvariantCulture, out var y))
        {
            return false;
        }

        var z = 0.0;
        var consumed = 2;
        if (startIndex + 2 < args.Count &&
            double.TryParse(args[startIndex + 2], NumberStyles.Float, CultureInfo.InvariantCulture, out var parsedZ))
        {
            z = parsedZ;
            consumed = 3;
        }

        point = new XYZ(x, y, z);
        consumedTo = startIndex + consumed;
        return true;
    }

    private static bool TryParsePointToken(string token, out XYZ point)
    {
        if (CadOperationPayloadCodec.TryParsePoint(token, out point))
        {
            return true;
        }

        point = XYZ.Zero;
        return false;
    }

    private static bool TryParseDirectedLineArguments(
        IReadOnlyList<string> args,
        string usage,
        out XYZ startPoint,
        out XYZ direction,
        out string? error)
    {
        startPoint = XYZ.Zero;
        direction = XYZ.Zero;
        error = null;

        if (!TryParseLineArguments(args, out startPoint, out var directionPoint, out _))
        {
            error = usage;
            return false;
        }

        direction = new XYZ(
            directionPoint.X - startPoint.X,
            directionPoint.Y - startPoint.Y,
            directionPoint.Z - startPoint.Z);

        var length = Math.Sqrt(
            (direction.X * direction.X) +
            (direction.Y * direction.Y) +
            (direction.Z * direction.Z));
        if (length <= double.Epsilon)
        {
            error = "Direction cannot be zero-length.";
            return false;
        }

        direction = new XYZ(
            direction.X / length,
            direction.Y / length,
            direction.Z / length);
        return true;
    }

    private static double DegreesToRadians(double degrees)
    {
        return degrees * (Math.PI / 180.0);
    }
}
