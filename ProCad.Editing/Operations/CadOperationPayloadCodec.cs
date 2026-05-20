using System.Globalization;
using ProCad.Editing.Identifiers;
using CSMath;

namespace ProCad.Editing.Operations;

public static class CadOperationPayloadCodec
{
    public const string EntityTypeKey = "entityType";
    public const string EntityTypeLine = "LINE";
    public const string EntityTypeCircle = "CIRCLE";
    public const string EntityTypeArc = "ARC";
    public const string EntityTypeLwPolyline = "LWPOLYLINE";
    public const string EntityTypePoint = "POINT";
    public const string EntityTypeXLine = "XLINE";
    public const string EntityTypeRay = "RAY";
    public const string EntityTypeEllipse = "ELLIPSE";
    public const string EntityTypeSpline = "SPLINE";
    public const string EntityTypeText = "TEXT";
    public const string EntityTypeMText = "MTEXT";
    public const string EntityTypeHatch = "HATCH";
    public const string EntityTypeInsert = "INSERT";

    public const string StartKey = "start";
    public const string EndKey = "end";
    public const string CenterKey = "center";
    public const string RadiusKey = "radius";
    public const string StartAngleKey = "startAngle";
    public const string EndAngleKey = "endAngle";
    public const string VerticesKey = "vertices";
    public const string ClosedKey = "closed";
    public const string LocationKey = "location";
    public const string FirstPointKey = "firstPoint";
    public const string DirectionKey = "direction";
    public const string RayStartKey = "rayStart";
    public const string MajorAxisEndPointKey = "majorAxisEndPoint";
    public const string RadiusRatioKey = "radiusRatio";
    public const string StartParameterKey = "startParameter";
    public const string EndParameterKey = "endParameter";
    public const string NormalKey = "normal";
    public const string FitPointsKey = "fitPoints";
    public const string ControlPointsKey = "controlPoints";
    public const string KnotsKey = "knots";
    public const string WeightsKey = "weights";
    public const string DegreeKey = "degree";
    public const string IsPeriodicKey = "isPeriodic";
    public const string StartTangentKey = "startTangent";
    public const string EndTangentKey = "endTangent";
    public const string InsertPointKey = "insertPoint";
    public const string AlignmentPointKey = "alignmentPoint";
    public const string HeightKey = "height";
    public const string RotationKey = "rotation";
    public const string TextValueKey = "textValue";
    public const string RectangleWidthKey = "rectangleWidth";
    public const string TextDirectionKey = "textDirection";
    public const string FromInsertPointKey = "fromInsertPoint";
    public const string ToInsertPointKey = "toInsertPoint";
    public const string FromAlignmentPointKey = "fromAlignmentPoint";
    public const string ToAlignmentPointKey = "toAlignmentPoint";
    public const string FromHeightKey = "fromHeight";
    public const string ToHeightKey = "toHeight";
    public const string FromRotationKey = "fromRotation";
    public const string ToRotationKey = "toRotation";
    public const string FromRectangleWidthKey = "fromRectangleWidth";
    public const string ToRectangleWidthKey = "toRectangleWidth";
    public const string FromTextDirectionKey = "fromTextDirection";
    public const string ToTextDirectionKey = "toTextDirection";
    public const string HatchLoopsKey = "hatchLoops";
    public const string FromHatchLoopsKey = "fromHatchLoops";
    public const string ToHatchLoopsKey = "toHatchLoops";
    public const string PatternNameKey = "patternName";
    public const string IsSolidKey = "isSolid";
    public const string BlockNameKey = "blockName";
    public const string XScaleKey = "xScale";
    public const string YScaleKey = "yScale";
    public const string ZScaleKey = "zScale";
    public const string InsertRotationKey = "insertRotation";
    public const string FromXScaleKey = "fromXScale";
    public const string ToXScaleKey = "toXScale";
    public const string FromYScaleKey = "fromYScale";
    public const string ToYScaleKey = "toYScale";
    public const string FromZScaleKey = "fromZScale";
    public const string ToZScaleKey = "toZScale";
    public const string FromInsertRotationKey = "fromInsertRotation";
    public const string ToInsertRotationKey = "toInsertRotation";

    public const string FromStartKey = "fromStart";
    public const string FromEndKey = "fromEnd";
    public const string ToStartKey = "toStart";
    public const string ToEndKey = "toEnd";
    public const string FromCenterKey = "fromCenter";
    public const string ToCenterKey = "toCenter";
    public const string FromRadiusKey = "fromRadius";
    public const string ToRadiusKey = "toRadius";
    public const string FromStartAngleKey = "fromStartAngle";
    public const string ToStartAngleKey = "toStartAngle";
    public const string FromEndAngleKey = "fromEndAngle";
    public const string ToEndAngleKey = "toEndAngle";
    public const string FromVerticesKey = "fromVertices";
    public const string ToVerticesKey = "toVertices";
    public const string FromClosedKey = "fromClosed";
    public const string ToClosedKey = "toClosed";
    public const string FromLocationKey = "fromLocation";
    public const string ToLocationKey = "toLocation";
    public const string FromFirstPointKey = "fromFirstPoint";
    public const string ToFirstPointKey = "toFirstPoint";
    public const string FromDirectionKey = "fromDirection";
    public const string ToDirectionKey = "toDirection";
    public const string FromRayStartKey = "fromRayStart";
    public const string ToRayStartKey = "toRayStart";
    public const string FromMajorAxisEndPointKey = "fromMajorAxisEndPoint";
    public const string ToMajorAxisEndPointKey = "toMajorAxisEndPoint";
    public const string FromRadiusRatioKey = "fromRadiusRatio";
    public const string ToRadiusRatioKey = "toRadiusRatio";
    public const string FromStartParameterKey = "fromStartParameter";
    public const string ToStartParameterKey = "toStartParameter";
    public const string FromEndParameterKey = "fromEndParameter";
    public const string ToEndParameterKey = "toEndParameter";
    public const string FromNormalKey = "fromNormal";
    public const string ToNormalKey = "toNormal";
    public const string FromDegreeKey = "fromDegree";
    public const string ToDegreeKey = "toDegree";
    public const string FromIsPeriodicKey = "fromIsPeriodic";
    public const string ToIsPeriodicKey = "toIsPeriodic";
    public const string FromFitPointsKey = "fromFitPoints";
    public const string ToFitPointsKey = "toFitPoints";
    public const string FromControlPointsKey = "fromControlPoints";
    public const string ToControlPointsKey = "toControlPoints";
    public const string FromKnotsKey = "fromKnots";
    public const string ToKnotsKey = "toKnots";
    public const string FromWeightsKey = "fromWeights";
    public const string ToWeightsKey = "toWeights";
    public const string FromStartTangentKey = "fromStartTangent";
    public const string ToStartTangentKey = "toStartTangent";
    public const string FromEndTangentKey = "fromEndTangent";
    public const string ToEndTangentKey = "toEndTangent";
    public const string PropertyNameKey = "propertyName";
    public const string FromValueKey = "fromValue";
    public const string ToValueKey = "toValue";
    public const string CreateLayerKey = "create.layer";
    public const string CreateLineTypeKey = "create.lineType";
    public const string CreateColorKey = "create.color";
    public const string CreateLineWeightKey = "create.lineWeight";
    public const string CreateLineTypeScaleKey = "create.lineTypeScale";
    public const string CreateInvisibleKey = "create.isInvisible";
    public const string CreateTransparencyKey = "create.transparency";
    public const string CreateTextStyleKey = "create.textStyle";
    public const string CreateDimensionStyleKey = "create.dimensionStyle";

    public static CadOperation CreateLine(CadEntityId entityId, XYZ start, XYZ end)
    {
        var payload = CreatePayload(EntityTypeLine);
        payload[StartKey] = FormatPoint(start);
        payload[EndKey] = FormatPoint(end);
        return new CadOperation(CadOperationKind.CreateEntity, entityId, payload);
    }

    public static CadOperation DeleteLine(CadEntityId entityId, XYZ start, XYZ end)
    {
        var payload = CreatePayload(EntityTypeLine);
        payload[StartKey] = FormatPoint(start);
        payload[EndKey] = FormatPoint(end);
        return new CadOperation(CadOperationKind.DeleteEntity, entityId, payload);
    }

    public static CadOperation TransformLine(CadEntityId entityId, XYZ fromStart, XYZ fromEnd, XYZ toStart, XYZ toEnd)
    {
        var payload = CreatePayload(EntityTypeLine);
        payload[FromStartKey] = FormatPoint(fromStart);
        payload[FromEndKey] = FormatPoint(fromEnd);
        payload[ToStartKey] = FormatPoint(toStart);
        payload[ToEndKey] = FormatPoint(toEnd);
        return new CadOperation(CadOperationKind.TransformEntity, entityId, payload);
    }

    public static CadOperation CreateCircle(CadEntityId entityId, XYZ center, double radius)
    {
        var payload = CreatePayload(EntityTypeCircle);
        payload[CenterKey] = FormatPoint(center);
        payload[RadiusKey] = FormatDouble(radius);
        return new CadOperation(CadOperationKind.CreateEntity, entityId, payload);
    }

    public static CadOperation DeleteCircle(CadEntityId entityId, XYZ center, double radius)
    {
        var payload = CreatePayload(EntityTypeCircle);
        payload[CenterKey] = FormatPoint(center);
        payload[RadiusKey] = FormatDouble(radius);
        return new CadOperation(CadOperationKind.DeleteEntity, entityId, payload);
    }

    public static CadOperation TransformCircle(CadEntityId entityId, XYZ fromCenter, double fromRadius, XYZ toCenter, double toRadius)
    {
        var payload = CreatePayload(EntityTypeCircle);
        payload[FromCenterKey] = FormatPoint(fromCenter);
        payload[FromRadiusKey] = FormatDouble(fromRadius);
        payload[ToCenterKey] = FormatPoint(toCenter);
        payload[ToRadiusKey] = FormatDouble(toRadius);
        return new CadOperation(CadOperationKind.TransformEntity, entityId, payload);
    }

    public static CadOperation CreateArc(CadEntityId entityId, XYZ center, double radius, double startAngle, double endAngle)
    {
        var payload = CreatePayload(EntityTypeArc);
        payload[CenterKey] = FormatPoint(center);
        payload[RadiusKey] = FormatDouble(radius);
        payload[StartAngleKey] = FormatDouble(startAngle);
        payload[EndAngleKey] = FormatDouble(endAngle);
        return new CadOperation(CadOperationKind.CreateEntity, entityId, payload);
    }

    public static CadOperation DeleteArc(CadEntityId entityId, XYZ center, double radius, double startAngle, double endAngle)
    {
        var payload = CreatePayload(EntityTypeArc);
        payload[CenterKey] = FormatPoint(center);
        payload[RadiusKey] = FormatDouble(radius);
        payload[StartAngleKey] = FormatDouble(startAngle);
        payload[EndAngleKey] = FormatDouble(endAngle);
        return new CadOperation(CadOperationKind.DeleteEntity, entityId, payload);
    }

    public static CadOperation TransformArc(
        CadEntityId entityId,
        XYZ fromCenter,
        double fromRadius,
        double fromStartAngle,
        double fromEndAngle,
        XYZ toCenter,
        double toRadius,
        double toStartAngle,
        double toEndAngle)
    {
        var payload = CreatePayload(EntityTypeArc);
        payload[FromCenterKey] = FormatPoint(fromCenter);
        payload[FromRadiusKey] = FormatDouble(fromRadius);
        payload[FromStartAngleKey] = FormatDouble(fromStartAngle);
        payload[FromEndAngleKey] = FormatDouble(fromEndAngle);
        payload[ToCenterKey] = FormatPoint(toCenter);
        payload[ToRadiusKey] = FormatDouble(toRadius);
        payload[ToStartAngleKey] = FormatDouble(toStartAngle);
        payload[ToEndAngleKey] = FormatDouble(toEndAngle);
        return new CadOperation(CadOperationKind.TransformEntity, entityId, payload);
    }

    public static CadOperation CreateLwPolyline(CadEntityId entityId, IReadOnlyList<XYZ> vertices, bool isClosed)
    {
        var payload = CreatePayload(EntityTypeLwPolyline);
        payload[VerticesKey] = FormatVertices(vertices);
        payload[ClosedKey] = FormatBoolean(isClosed);
        return new CadOperation(CadOperationKind.CreateEntity, entityId, payload);
    }

    public static CadOperation DeleteLwPolyline(CadEntityId entityId, IReadOnlyList<XYZ> vertices, bool isClosed)
    {
        var payload = CreatePayload(EntityTypeLwPolyline);
        payload[VerticesKey] = FormatVertices(vertices);
        payload[ClosedKey] = FormatBoolean(isClosed);
        return new CadOperation(CadOperationKind.DeleteEntity, entityId, payload);
    }

    public static CadOperation TransformLwPolyline(
        CadEntityId entityId,
        IReadOnlyList<XYZ> fromVertices,
        bool fromClosed,
        IReadOnlyList<XYZ> toVertices,
        bool toClosed)
    {
        var payload = CreatePayload(EntityTypeLwPolyline);
        payload[FromVerticesKey] = FormatVertices(fromVertices);
        payload[FromClosedKey] = FormatBoolean(fromClosed);
        payload[ToVerticesKey] = FormatVertices(toVertices);
        payload[ToClosedKey] = FormatBoolean(toClosed);
        return new CadOperation(CadOperationKind.TransformEntity, entityId, payload);
    }

    public static CadOperation CreatePoint(CadEntityId entityId, XYZ location)
    {
        var payload = CreatePayload(EntityTypePoint);
        payload[LocationKey] = FormatPoint(location);
        return new CadOperation(CadOperationKind.CreateEntity, entityId, payload);
    }

    public static CadOperation DeletePoint(CadEntityId entityId, XYZ location)
    {
        var payload = CreatePayload(EntityTypePoint);
        payload[LocationKey] = FormatPoint(location);
        return new CadOperation(CadOperationKind.DeleteEntity, entityId, payload);
    }

    public static CadOperation TransformPoint(CadEntityId entityId, XYZ fromLocation, XYZ toLocation)
    {
        var payload = CreatePayload(EntityTypePoint);
        payload[FromLocationKey] = FormatPoint(fromLocation);
        payload[ToLocationKey] = FormatPoint(toLocation);
        return new CadOperation(CadOperationKind.TransformEntity, entityId, payload);
    }

    public static CadOperation CreateXLine(CadEntityId entityId, XYZ firstPoint, XYZ direction)
    {
        var payload = CreatePayload(EntityTypeXLine);
        payload[FirstPointKey] = FormatPoint(firstPoint);
        payload[DirectionKey] = FormatPoint(direction);
        return new CadOperation(CadOperationKind.CreateEntity, entityId, payload);
    }

    public static CadOperation DeleteXLine(CadEntityId entityId, XYZ firstPoint, XYZ direction)
    {
        var payload = CreatePayload(EntityTypeXLine);
        payload[FirstPointKey] = FormatPoint(firstPoint);
        payload[DirectionKey] = FormatPoint(direction);
        return new CadOperation(CadOperationKind.DeleteEntity, entityId, payload);
    }

    public static CadOperation TransformXLine(
        CadEntityId entityId,
        XYZ fromFirstPoint,
        XYZ fromDirection,
        XYZ toFirstPoint,
        XYZ toDirection)
    {
        var payload = CreatePayload(EntityTypeXLine);
        payload[FromFirstPointKey] = FormatPoint(fromFirstPoint);
        payload[FromDirectionKey] = FormatPoint(fromDirection);
        payload[ToFirstPointKey] = FormatPoint(toFirstPoint);
        payload[ToDirectionKey] = FormatPoint(toDirection);
        return new CadOperation(CadOperationKind.TransformEntity, entityId, payload);
    }

    public static CadOperation CreateRay(CadEntityId entityId, XYZ startPoint, XYZ direction)
    {
        var payload = CreatePayload(EntityTypeRay);
        payload[RayStartKey] = FormatPoint(startPoint);
        payload[DirectionKey] = FormatPoint(direction);
        return new CadOperation(CadOperationKind.CreateEntity, entityId, payload);
    }

    public static CadOperation DeleteRay(CadEntityId entityId, XYZ startPoint, XYZ direction)
    {
        var payload = CreatePayload(EntityTypeRay);
        payload[RayStartKey] = FormatPoint(startPoint);
        payload[DirectionKey] = FormatPoint(direction);
        return new CadOperation(CadOperationKind.DeleteEntity, entityId, payload);
    }

    public static CadOperation TransformRay(
        CadEntityId entityId,
        XYZ fromStartPoint,
        XYZ fromDirection,
        XYZ toStartPoint,
        XYZ toDirection)
    {
        var payload = CreatePayload(EntityTypeRay);
        payload[FromRayStartKey] = FormatPoint(fromStartPoint);
        payload[FromDirectionKey] = FormatPoint(fromDirection);
        payload[ToRayStartKey] = FormatPoint(toStartPoint);
        payload[ToDirectionKey] = FormatPoint(toDirection);
        return new CadOperation(CadOperationKind.TransformEntity, entityId, payload);
    }

    public static CadOperation CreateEllipse(
        CadEntityId entityId,
        XYZ center,
        XYZ majorAxisEndPoint,
        double radiusRatio,
        double startParameter,
        double endParameter,
        XYZ normal)
    {
        var payload = CreatePayload(EntityTypeEllipse);
        payload[CenterKey] = FormatPoint(center);
        payload[MajorAxisEndPointKey] = FormatPoint(majorAxisEndPoint);
        payload[RadiusRatioKey] = FormatDouble(radiusRatio);
        payload[StartParameterKey] = FormatDouble(startParameter);
        payload[EndParameterKey] = FormatDouble(endParameter);
        payload[NormalKey] = FormatPoint(normal);
        return new CadOperation(CadOperationKind.CreateEntity, entityId, payload);
    }

    public static CadOperation DeleteEllipse(
        CadEntityId entityId,
        XYZ center,
        XYZ majorAxisEndPoint,
        double radiusRatio,
        double startParameter,
        double endParameter,
        XYZ normal)
    {
        var payload = CreatePayload(EntityTypeEllipse);
        payload[CenterKey] = FormatPoint(center);
        payload[MajorAxisEndPointKey] = FormatPoint(majorAxisEndPoint);
        payload[RadiusRatioKey] = FormatDouble(radiusRatio);
        payload[StartParameterKey] = FormatDouble(startParameter);
        payload[EndParameterKey] = FormatDouble(endParameter);
        payload[NormalKey] = FormatPoint(normal);
        return new CadOperation(CadOperationKind.DeleteEntity, entityId, payload);
    }

    public static CadOperation TransformEllipse(
        CadEntityId entityId,
        XYZ fromCenter,
        XYZ fromMajorAxisEndPoint,
        double fromRadiusRatio,
        double fromStartParameter,
        double fromEndParameter,
        XYZ fromNormal,
        XYZ toCenter,
        XYZ toMajorAxisEndPoint,
        double toRadiusRatio,
        double toStartParameter,
        double toEndParameter,
        XYZ toNormal)
    {
        var payload = CreatePayload(EntityTypeEllipse);
        payload[FromCenterKey] = FormatPoint(fromCenter);
        payload[FromMajorAxisEndPointKey] = FormatPoint(fromMajorAxisEndPoint);
        payload[FromRadiusRatioKey] = FormatDouble(fromRadiusRatio);
        payload[FromStartParameterKey] = FormatDouble(fromStartParameter);
        payload[FromEndParameterKey] = FormatDouble(fromEndParameter);
        payload[FromNormalKey] = FormatPoint(fromNormal);
        payload[ToCenterKey] = FormatPoint(toCenter);
        payload[ToMajorAxisEndPointKey] = FormatPoint(toMajorAxisEndPoint);
        payload[ToRadiusRatioKey] = FormatDouble(toRadiusRatio);
        payload[ToStartParameterKey] = FormatDouble(toStartParameter);
        payload[ToEndParameterKey] = FormatDouble(toEndParameter);
        payload[ToNormalKey] = FormatPoint(toNormal);
        return new CadOperation(CadOperationKind.TransformEntity, entityId, payload);
    }

    public static CadOperation CreateSpline(
        CadEntityId entityId,
        int degree,
        bool isClosed,
        bool isPeriodic,
        IReadOnlyList<XYZ> fitPoints,
        IReadOnlyList<XYZ> controlPoints,
        IReadOnlyList<double> knots,
        IReadOnlyList<double> weights,
        XYZ startTangent,
        XYZ endTangent,
        XYZ normal)
    {
        var payload = CreatePayload(EntityTypeSpline);
        payload[DegreeKey] = degree.ToString(CultureInfo.InvariantCulture);
        payload[ClosedKey] = FormatBoolean(isClosed);
        payload[IsPeriodicKey] = FormatBoolean(isPeriodic);
        payload[FitPointsKey] = FormatVertices(fitPoints);
        payload[ControlPointsKey] = FormatVertices(controlPoints);
        payload[KnotsKey] = FormatDoubleList(knots);
        payload[WeightsKey] = FormatDoubleList(weights);
        payload[StartTangentKey] = FormatPoint(startTangent);
        payload[EndTangentKey] = FormatPoint(endTangent);
        payload[NormalKey] = FormatPoint(normal);
        return new CadOperation(CadOperationKind.CreateEntity, entityId, payload);
    }

    public static CadOperation DeleteSpline(
        CadEntityId entityId,
        int degree,
        bool isClosed,
        bool isPeriodic,
        IReadOnlyList<XYZ> fitPoints,
        IReadOnlyList<XYZ> controlPoints,
        IReadOnlyList<double> knots,
        IReadOnlyList<double> weights,
        XYZ startTangent,
        XYZ endTangent,
        XYZ normal)
    {
        var payload = CreatePayload(EntityTypeSpline);
        payload[DegreeKey] = degree.ToString(CultureInfo.InvariantCulture);
        payload[ClosedKey] = FormatBoolean(isClosed);
        payload[IsPeriodicKey] = FormatBoolean(isPeriodic);
        payload[FitPointsKey] = FormatVertices(fitPoints);
        payload[ControlPointsKey] = FormatVertices(controlPoints);
        payload[KnotsKey] = FormatDoubleList(knots);
        payload[WeightsKey] = FormatDoubleList(weights);
        payload[StartTangentKey] = FormatPoint(startTangent);
        payload[EndTangentKey] = FormatPoint(endTangent);
        payload[NormalKey] = FormatPoint(normal);
        return new CadOperation(CadOperationKind.DeleteEntity, entityId, payload);
    }

    public static CadOperation TransformSpline(
        CadEntityId entityId,
        int fromDegree,
        bool fromClosed,
        bool fromIsPeriodic,
        IReadOnlyList<XYZ> fromFitPoints,
        IReadOnlyList<XYZ> fromControlPoints,
        IReadOnlyList<double> fromKnots,
        IReadOnlyList<double> fromWeights,
        XYZ fromStartTangent,
        XYZ fromEndTangent,
        XYZ fromNormal,
        int toDegree,
        bool toClosed,
        bool toIsPeriodic,
        IReadOnlyList<XYZ> toFitPoints,
        IReadOnlyList<XYZ> toControlPoints,
        IReadOnlyList<double> toKnots,
        IReadOnlyList<double> toWeights,
        XYZ toStartTangent,
        XYZ toEndTangent,
        XYZ toNormal)
    {
        var payload = CreatePayload(EntityTypeSpline);
        payload[FromDegreeKey] = fromDegree.ToString(CultureInfo.InvariantCulture);
        payload[FromClosedKey] = FormatBoolean(fromClosed);
        payload[FromIsPeriodicKey] = FormatBoolean(fromIsPeriodic);
        payload[FromFitPointsKey] = FormatVertices(fromFitPoints);
        payload[FromControlPointsKey] = FormatVertices(fromControlPoints);
        payload[FromKnotsKey] = FormatDoubleList(fromKnots);
        payload[FromWeightsKey] = FormatDoubleList(fromWeights);
        payload[FromStartTangentKey] = FormatPoint(fromStartTangent);
        payload[FromEndTangentKey] = FormatPoint(fromEndTangent);
        payload[FromNormalKey] = FormatPoint(fromNormal);
        payload[ToDegreeKey] = toDegree.ToString(CultureInfo.InvariantCulture);
        payload[ToClosedKey] = FormatBoolean(toClosed);
        payload[ToIsPeriodicKey] = FormatBoolean(toIsPeriodic);
        payload[ToFitPointsKey] = FormatVertices(toFitPoints);
        payload[ToControlPointsKey] = FormatVertices(toControlPoints);
        payload[ToKnotsKey] = FormatDoubleList(toKnots);
        payload[ToWeightsKey] = FormatDoubleList(toWeights);
        payload[ToStartTangentKey] = FormatPoint(toStartTangent);
        payload[ToEndTangentKey] = FormatPoint(toEndTangent);
        payload[ToNormalKey] = FormatPoint(toNormal);
        return new CadOperation(CadOperationKind.TransformEntity, entityId, payload);
    }

    public static CadOperation CreateText(
        CadEntityId entityId,
        XYZ insertPoint,
        XYZ alignmentPoint,
        double height,
        double rotation,
        string value,
        XYZ normal)
    {
        var payload = CreatePayload(EntityTypeText);
        payload[InsertPointKey] = FormatPoint(insertPoint);
        payload[AlignmentPointKey] = FormatPoint(alignmentPoint);
        payload[HeightKey] = FormatDouble(height);
        payload[RotationKey] = FormatDouble(rotation);
        payload[TextValueKey] = value ?? string.Empty;
        payload[NormalKey] = FormatPoint(normal);
        return new CadOperation(CadOperationKind.CreateEntity, entityId, payload);
    }

    public static CadOperation DeleteText(
        CadEntityId entityId,
        XYZ insertPoint,
        XYZ alignmentPoint,
        double height,
        double rotation,
        string value,
        XYZ normal)
    {
        var payload = CreatePayload(EntityTypeText);
        payload[InsertPointKey] = FormatPoint(insertPoint);
        payload[AlignmentPointKey] = FormatPoint(alignmentPoint);
        payload[HeightKey] = FormatDouble(height);
        payload[RotationKey] = FormatDouble(rotation);
        payload[TextValueKey] = value ?? string.Empty;
        payload[NormalKey] = FormatPoint(normal);
        return new CadOperation(CadOperationKind.DeleteEntity, entityId, payload);
    }

    public static CadOperation TransformText(
        CadEntityId entityId,
        XYZ fromInsertPoint,
        XYZ fromAlignmentPoint,
        double fromHeight,
        double fromRotation,
        XYZ toInsertPoint,
        XYZ toAlignmentPoint,
        double toHeight,
        double toRotation)
    {
        var payload = CreatePayload(EntityTypeText);
        payload[FromInsertPointKey] = FormatPoint(fromInsertPoint);
        payload[FromAlignmentPointKey] = FormatPoint(fromAlignmentPoint);
        payload[FromHeightKey] = FormatDouble(fromHeight);
        payload[FromRotationKey] = FormatDouble(fromRotation);
        payload[ToInsertPointKey] = FormatPoint(toInsertPoint);
        payload[ToAlignmentPointKey] = FormatPoint(toAlignmentPoint);
        payload[ToHeightKey] = FormatDouble(toHeight);
        payload[ToRotationKey] = FormatDouble(toRotation);
        return new CadOperation(CadOperationKind.TransformEntity, entityId, payload);
    }

    public static CadOperation CreateMText(
        CadEntityId entityId,
        XYZ insertPoint,
        XYZ textDirection,
        double height,
        double rectangleWidth,
        string value,
        XYZ normal)
    {
        var payload = CreatePayload(EntityTypeMText);
        payload[InsertPointKey] = FormatPoint(insertPoint);
        payload[TextDirectionKey] = FormatPoint(textDirection);
        payload[HeightKey] = FormatDouble(height);
        payload[RectangleWidthKey] = FormatDouble(rectangleWidth);
        payload[TextValueKey] = value ?? string.Empty;
        payload[NormalKey] = FormatPoint(normal);
        return new CadOperation(CadOperationKind.CreateEntity, entityId, payload);
    }

    public static CadOperation DeleteMText(
        CadEntityId entityId,
        XYZ insertPoint,
        XYZ textDirection,
        double height,
        double rectangleWidth,
        string value,
        XYZ normal)
    {
        var payload = CreatePayload(EntityTypeMText);
        payload[InsertPointKey] = FormatPoint(insertPoint);
        payload[TextDirectionKey] = FormatPoint(textDirection);
        payload[HeightKey] = FormatDouble(height);
        payload[RectangleWidthKey] = FormatDouble(rectangleWidth);
        payload[TextValueKey] = value ?? string.Empty;
        payload[NormalKey] = FormatPoint(normal);
        return new CadOperation(CadOperationKind.DeleteEntity, entityId, payload);
    }

    public static CadOperation TransformMText(
        CadEntityId entityId,
        XYZ fromInsertPoint,
        XYZ fromTextDirection,
        double fromHeight,
        double fromRectangleWidth,
        XYZ toInsertPoint,
        XYZ toTextDirection,
        double toHeight,
        double toRectangleWidth)
    {
        var payload = CreatePayload(EntityTypeMText);
        payload[FromInsertPointKey] = FormatPoint(fromInsertPoint);
        payload[FromTextDirectionKey] = FormatPoint(fromTextDirection);
        payload[FromHeightKey] = FormatDouble(fromHeight);
        payload[FromRectangleWidthKey] = FormatDouble(fromRectangleWidth);
        payload[ToInsertPointKey] = FormatPoint(toInsertPoint);
        payload[ToTextDirectionKey] = FormatPoint(toTextDirection);
        payload[ToHeightKey] = FormatDouble(toHeight);
        payload[ToRectangleWidthKey] = FormatDouble(toRectangleWidth);
        return new CadOperation(CadOperationKind.TransformEntity, entityId, payload);
    }

    public static CadOperation CreateHatch(
        CadEntityId entityId,
        IReadOnlyList<IReadOnlyList<XYZ>> loops,
        bool isSolid,
        string patternName,
        XYZ normal)
    {
        var payload = CreatePayload(EntityTypeHatch);
        payload[HatchLoopsKey] = FormatLoops(loops);
        payload[IsSolidKey] = FormatBoolean(isSolid);
        payload[PatternNameKey] = patternName ?? string.Empty;
        payload[NormalKey] = FormatPoint(normal);
        return new CadOperation(CadOperationKind.CreateEntity, entityId, payload);
    }

    public static CadOperation DeleteHatch(
        CadEntityId entityId,
        IReadOnlyList<IReadOnlyList<XYZ>> loops,
        bool isSolid,
        string patternName,
        XYZ normal)
    {
        var payload = CreatePayload(EntityTypeHatch);
        payload[HatchLoopsKey] = FormatLoops(loops);
        payload[IsSolidKey] = FormatBoolean(isSolid);
        payload[PatternNameKey] = patternName ?? string.Empty;
        payload[NormalKey] = FormatPoint(normal);
        return new CadOperation(CadOperationKind.DeleteEntity, entityId, payload);
    }

    public static CadOperation TransformHatch(
        CadEntityId entityId,
        IReadOnlyList<IReadOnlyList<XYZ>> fromLoops,
        IReadOnlyList<IReadOnlyList<XYZ>> toLoops,
        bool isSolid,
        string patternName,
        XYZ normal)
    {
        var payload = CreatePayload(EntityTypeHatch);
        payload[FromHatchLoopsKey] = FormatLoops(fromLoops);
        payload[ToHatchLoopsKey] = FormatLoops(toLoops);
        payload[IsSolidKey] = FormatBoolean(isSolid);
        payload[PatternNameKey] = patternName ?? string.Empty;
        payload[NormalKey] = FormatPoint(normal);
        return new CadOperation(CadOperationKind.TransformEntity, entityId, payload);
    }

    public static CadOperation CreateInsert(
        CadEntityId entityId,
        string blockName,
        XYZ insertPoint,
        double xScale,
        double yScale,
        double zScale,
        double rotation,
        XYZ normal)
    {
        var payload = CreatePayload(EntityTypeInsert);
        payload[BlockNameKey] = blockName ?? string.Empty;
        payload[InsertPointKey] = FormatPoint(insertPoint);
        payload[XScaleKey] = FormatDouble(xScale);
        payload[YScaleKey] = FormatDouble(yScale);
        payload[ZScaleKey] = FormatDouble(zScale);
        payload[InsertRotationKey] = FormatDouble(rotation);
        payload[NormalKey] = FormatPoint(normal);
        return new CadOperation(CadOperationKind.CreateEntity, entityId, payload);
    }

    public static CadOperation DeleteInsert(
        CadEntityId entityId,
        string blockName,
        XYZ insertPoint,
        double xScale,
        double yScale,
        double zScale,
        double rotation,
        XYZ normal)
    {
        var payload = CreatePayload(EntityTypeInsert);
        payload[BlockNameKey] = blockName ?? string.Empty;
        payload[InsertPointKey] = FormatPoint(insertPoint);
        payload[XScaleKey] = FormatDouble(xScale);
        payload[YScaleKey] = FormatDouble(yScale);
        payload[ZScaleKey] = FormatDouble(zScale);
        payload[InsertRotationKey] = FormatDouble(rotation);
        payload[NormalKey] = FormatPoint(normal);
        return new CadOperation(CadOperationKind.DeleteEntity, entityId, payload);
    }

    public static CadOperation TransformInsert(
        CadEntityId entityId,
        string blockName,
        XYZ fromInsertPoint,
        double fromXScale,
        double fromYScale,
        double fromZScale,
        double fromRotation,
        XYZ fromNormal,
        XYZ toInsertPoint,
        double toXScale,
        double toYScale,
        double toZScale,
        double toRotation,
        XYZ toNormal)
    {
        var payload = CreatePayload(EntityTypeInsert);
        payload[BlockNameKey] = blockName ?? string.Empty;
        payload[FromInsertPointKey] = FormatPoint(fromInsertPoint);
        payload[FromXScaleKey] = FormatDouble(fromXScale);
        payload[FromYScaleKey] = FormatDouble(fromYScale);
        payload[FromZScaleKey] = FormatDouble(fromZScale);
        payload[FromInsertRotationKey] = FormatDouble(fromRotation);
        payload[FromNormalKey] = FormatPoint(fromNormal);
        payload[ToInsertPointKey] = FormatPoint(toInsertPoint);
        payload[ToXScaleKey] = FormatDouble(toXScale);
        payload[ToYScaleKey] = FormatDouble(toYScale);
        payload[ToZScaleKey] = FormatDouble(toZScale);
        payload[ToInsertRotationKey] = FormatDouble(toRotation);
        payload[ToNormalKey] = FormatPoint(toNormal);
        return new CadOperation(CadOperationKind.TransformEntity, entityId, payload);
    }

    public static CadOperation UpdateEntityProperty(
        CadEntityId entityId,
        string propertyName,
        string fromValue,
        string toValue)
    {
        var payload = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [PropertyNameKey] = propertyName,
            [FromValueKey] = fromValue,
            [ToValueKey] = toValue
        };

        return new CadOperation(CadOperationKind.UpdateProperty, entityId, payload);
    }

    public static CadOperation WithCreateProperties(CadOperation operation, in CadEntityCreateProperties properties)
    {
        var payload = operation.Payload is null
            ? new Dictionary<string, string>(StringComparer.Ordinal)
            : new Dictionary<string, string>(operation.Payload, StringComparer.Ordinal);

        payload[CreateLayerKey] = properties.LayerName;
        payload[CreateLineTypeKey] = properties.LineTypeName;
        payload[CreateColorKey] = CadEntityPropertyCodec.SerializeColor(properties.Color);
        payload[CreateLineWeightKey] = CadEntityPropertyCodec.SerializeLineWeight(properties.LineWeight);
        payload[CreateLineTypeScaleKey] = CadEntityPropertyCodec.SerializeLineTypeScale(properties.LineTypeScale);
        payload[CreateInvisibleKey] = CadEntityPropertyCodec.SerializeBoolean(properties.IsInvisible);
        payload[CreateTransparencyKey] = CadEntityPropertyCodec.SerializeTransparency(properties.Transparency);

        if (string.IsNullOrWhiteSpace(properties.TextStyleName))
        {
            payload.Remove(CreateTextStyleKey);
        }
        else
        {
            payload[CreateTextStyleKey] = properties.TextStyleName!;
        }

        if (string.IsNullOrWhiteSpace(properties.DimensionStyleName))
        {
            payload.Remove(CreateDimensionStyleKey);
        }
        else
        {
            payload[CreateDimensionStyleKey] = properties.DimensionStyleName!;
        }

        return operation with
        {
            Payload = payload
        };
    }

    public static CadOperation CopyCreateProperties(CadOperation source, CadOperation destination)
    {
        return TryGetCreateProperties(source, out var properties)
            ? WithCreateProperties(destination, properties)
            : destination;
    }

    public static bool TryGetCreateProperties(CadOperation operation, out CadEntityCreateProperties properties)
    {
        properties = default;
        if (operation.Payload is null ||
            !TryReadString(operation.Payload, CreateLayerKey, out var layerName) ||
            !TryReadString(operation.Payload, CreateLineTypeKey, out var lineTypeName) ||
            !TryReadString(operation.Payload, CreateColorKey, out var colorToken) ||
            !CadEntityPropertyCodec.TryDeserializeColor(colorToken, out var color) ||
            !TryReadString(operation.Payload, CreateLineWeightKey, out var lineWeightToken) ||
            !CadEntityPropertyCodec.TryDeserializeLineWeight(lineWeightToken, out var lineWeight) ||
            !TryReadString(operation.Payload, CreateLineTypeScaleKey, out var lineTypeScaleToken) ||
            !CadEntityPropertyCodec.TryDeserializeLineTypeScale(lineTypeScaleToken, out var lineTypeScale) ||
            !TryReadString(operation.Payload, CreateInvisibleKey, out var invisibleToken) ||
            !CadEntityPropertyCodec.TryDeserializeBoolean(invisibleToken, out var isInvisible) ||
            !TryReadString(operation.Payload, CreateTransparencyKey, out var transparencyToken) ||
            !CadEntityPropertyCodec.TryDeserializeTransparency(transparencyToken, out var transparency))
        {
            return false;
        }

        operation.Payload.TryGetValue(CreateTextStyleKey, out var textStyleName);
        operation.Payload.TryGetValue(CreateDimensionStyleKey, out var dimensionStyleName);
        properties = new CadEntityCreateProperties(
            LayerName: layerName,
            LineTypeName: lineTypeName,
            Color: color,
            LineWeight: lineWeight,
            LineTypeScale: lineTypeScale,
            IsInvisible: isInvisible,
            Transparency: transparency,
            TextStyleName: string.IsNullOrWhiteSpace(textStyleName) ? null : textStyleName,
            DimensionStyleName: string.IsNullOrWhiteSpace(dimensionStyleName) ? null : dimensionStyleName);
        return true;
    }

    public static bool TryGetEntityType(CadOperation operation, out string entityType)
    {
        if (operation.Payload is not null &&
            operation.Payload.TryGetValue(EntityTypeKey, out var token) &&
            !string.IsNullOrWhiteSpace(token))
        {
            entityType = token;
            return true;
        }

        entityType = string.Empty;
        return false;
    }

    public static bool TryGetCreateLine(CadOperation operation, out XYZ start, out XYZ end)
    {
        start = XYZ.Zero;
        end = XYZ.Zero;
        return TryReadEntityType(operation.Payload, EntityTypeLine) &&
               TryReadPoint(operation.Payload!, StartKey, out start) &&
               TryReadPoint(operation.Payload!, EndKey, out end);
    }

    public static bool TryGetTransformLine(CadOperation operation, out XYZ toStart, out XYZ toEnd)
    {
        toStart = XYZ.Zero;
        toEnd = XYZ.Zero;
        return TryReadEntityType(operation.Payload, EntityTypeLine) &&
               TryReadPoint(operation.Payload!, ToStartKey, out toStart) &&
               TryReadPoint(operation.Payload!, ToEndKey, out toEnd);
    }

    public static bool TryGetCreateCircle(CadOperation operation, out XYZ center, out double radius)
    {
        center = XYZ.Zero;
        radius = 0.0;
        return TryReadEntityType(operation.Payload, EntityTypeCircle) &&
               TryReadPoint(operation.Payload!, CenterKey, out center) &&
               TryReadDouble(operation.Payload!, RadiusKey, out radius);
    }

    public static bool TryGetTransformCircle(CadOperation operation, out XYZ toCenter, out double toRadius)
    {
        toCenter = XYZ.Zero;
        toRadius = 0.0;
        return TryReadEntityType(operation.Payload, EntityTypeCircle) &&
               TryReadPoint(operation.Payload!, ToCenterKey, out toCenter) &&
               TryReadDouble(operation.Payload!, ToRadiusKey, out toRadius);
    }

    public static bool TryGetCreateArc(
        CadOperation operation,
        out XYZ center,
        out double radius,
        out double startAngle,
        out double endAngle)
    {
        center = XYZ.Zero;
        radius = 0.0;
        startAngle = 0.0;
        endAngle = 0.0;
        return TryReadEntityType(operation.Payload, EntityTypeArc) &&
               TryReadPoint(operation.Payload!, CenterKey, out center) &&
               TryReadDouble(operation.Payload!, RadiusKey, out radius) &&
               TryReadDouble(operation.Payload!, StartAngleKey, out startAngle) &&
               TryReadDouble(operation.Payload!, EndAngleKey, out endAngle);
    }

    public static bool TryGetTransformArc(
        CadOperation operation,
        out XYZ toCenter,
        out double toRadius,
        out double toStartAngle,
        out double toEndAngle)
    {
        toCenter = XYZ.Zero;
        toRadius = 0.0;
        toStartAngle = 0.0;
        toEndAngle = 0.0;
        return TryReadEntityType(operation.Payload, EntityTypeArc) &&
               TryReadPoint(operation.Payload!, ToCenterKey, out toCenter) &&
               TryReadDouble(operation.Payload!, ToRadiusKey, out toRadius) &&
               TryReadDouble(operation.Payload!, ToStartAngleKey, out toStartAngle) &&
               TryReadDouble(operation.Payload!, ToEndAngleKey, out toEndAngle);
    }

    public static bool TryGetCreateLwPolyline(CadOperation operation, out IReadOnlyList<XYZ> vertices, out bool isClosed)
    {
        vertices = Array.Empty<XYZ>();
        isClosed = false;
        return TryReadEntityType(operation.Payload, EntityTypeLwPolyline) &&
               TryReadVertices(operation.Payload!, VerticesKey, out vertices) &&
               TryReadBoolean(operation.Payload!, ClosedKey, out isClosed);
    }

    public static bool TryGetTransformLwPolyline(CadOperation operation, out IReadOnlyList<XYZ> vertices, out bool isClosed)
    {
        vertices = Array.Empty<XYZ>();
        isClosed = false;
        return TryReadEntityType(operation.Payload, EntityTypeLwPolyline) &&
               TryReadVertices(operation.Payload!, ToVerticesKey, out vertices) &&
               TryReadBoolean(operation.Payload!, ToClosedKey, out isClosed);
    }

    public static bool TryGetCreatePoint(CadOperation operation, out XYZ location)
    {
        location = XYZ.Zero;
        return TryReadEntityType(operation.Payload, EntityTypePoint) &&
               TryReadPoint(operation.Payload!, LocationKey, out location);
    }

    public static bool TryGetTransformPoint(CadOperation operation, out XYZ location)
    {
        location = XYZ.Zero;
        return TryReadEntityType(operation.Payload, EntityTypePoint) &&
               TryReadPoint(operation.Payload!, ToLocationKey, out location);
    }

    public static bool TryGetCreateXLine(CadOperation operation, out XYZ firstPoint, out XYZ direction)
    {
        firstPoint = XYZ.Zero;
        direction = XYZ.Zero;
        return TryReadEntityType(operation.Payload, EntityTypeXLine) &&
               TryReadPoint(operation.Payload!, FirstPointKey, out firstPoint) &&
               TryReadPoint(operation.Payload!, DirectionKey, out direction);
    }

    public static bool TryGetTransformXLine(CadOperation operation, out XYZ firstPoint, out XYZ direction)
    {
        firstPoint = XYZ.Zero;
        direction = XYZ.Zero;
        return TryReadEntityType(operation.Payload, EntityTypeXLine) &&
               TryReadPoint(operation.Payload!, ToFirstPointKey, out firstPoint) &&
               TryReadPoint(operation.Payload!, ToDirectionKey, out direction);
    }

    public static bool TryGetCreateRay(CadOperation operation, out XYZ startPoint, out XYZ direction)
    {
        startPoint = XYZ.Zero;
        direction = XYZ.Zero;
        return TryReadEntityType(operation.Payload, EntityTypeRay) &&
               TryReadPoint(operation.Payload!, RayStartKey, out startPoint) &&
               TryReadPoint(operation.Payload!, DirectionKey, out direction);
    }

    public static bool TryGetTransformRay(CadOperation operation, out XYZ startPoint, out XYZ direction)
    {
        startPoint = XYZ.Zero;
        direction = XYZ.Zero;
        return TryReadEntityType(operation.Payload, EntityTypeRay) &&
               TryReadPoint(operation.Payload!, ToRayStartKey, out startPoint) &&
               TryReadPoint(operation.Payload!, ToDirectionKey, out direction);
    }

    public static bool TryGetCreateEllipse(
        CadOperation operation,
        out XYZ center,
        out XYZ majorAxisEndPoint,
        out double radiusRatio,
        out double startParameter,
        out double endParameter,
        out XYZ normal)
    {
        center = XYZ.Zero;
        majorAxisEndPoint = XYZ.Zero;
        radiusRatio = 0.0;
        startParameter = 0.0;
        endParameter = 0.0;
        normal = XYZ.Zero;
        return TryReadEntityType(operation.Payload, EntityTypeEllipse) &&
               TryReadPoint(operation.Payload!, CenterKey, out center) &&
               TryReadPoint(operation.Payload!, MajorAxisEndPointKey, out majorAxisEndPoint) &&
               TryReadDouble(operation.Payload!, RadiusRatioKey, out radiusRatio) &&
               TryReadDouble(operation.Payload!, StartParameterKey, out startParameter) &&
               TryReadDouble(operation.Payload!, EndParameterKey, out endParameter) &&
               TryReadPoint(operation.Payload!, NormalKey, out normal);
    }

    public static bool TryGetTransformEllipse(
        CadOperation operation,
        out XYZ toCenter,
        out XYZ toMajorAxisEndPoint,
        out double toRadiusRatio,
        out double toStartParameter,
        out double toEndParameter,
        out XYZ toNormal)
    {
        toCenter = XYZ.Zero;
        toMajorAxisEndPoint = XYZ.Zero;
        toRadiusRatio = 0.0;
        toStartParameter = 0.0;
        toEndParameter = 0.0;
        toNormal = XYZ.Zero;
        return TryReadEntityType(operation.Payload, EntityTypeEllipse) &&
               TryReadPoint(operation.Payload!, ToCenterKey, out toCenter) &&
               TryReadPoint(operation.Payload!, ToMajorAxisEndPointKey, out toMajorAxisEndPoint) &&
               TryReadDouble(operation.Payload!, ToRadiusRatioKey, out toRadiusRatio) &&
               TryReadDouble(operation.Payload!, ToStartParameterKey, out toStartParameter) &&
               TryReadDouble(operation.Payload!, ToEndParameterKey, out toEndParameter) &&
               TryReadPoint(operation.Payload!, ToNormalKey, out toNormal);
    }

    public static bool TryGetCreateSpline(
        CadOperation operation,
        out int degree,
        out bool isClosed,
        out bool isPeriodic,
        out IReadOnlyList<XYZ> fitPoints,
        out IReadOnlyList<XYZ> controlPoints,
        out IReadOnlyList<double> knots,
        out IReadOnlyList<double> weights,
        out XYZ startTangent,
        out XYZ endTangent,
        out XYZ normal)
    {
        degree = 0;
        isClosed = false;
        isPeriodic = false;
        fitPoints = Array.Empty<XYZ>();
        controlPoints = Array.Empty<XYZ>();
        knots = Array.Empty<double>();
        weights = Array.Empty<double>();
        startTangent = XYZ.Zero;
        endTangent = XYZ.Zero;
        normal = XYZ.Zero;
        return TryReadEntityType(operation.Payload, EntityTypeSpline) &&
               TryReadInt(operation.Payload!, DegreeKey, out degree) &&
               TryReadBoolean(operation.Payload!, ClosedKey, out isClosed) &&
               TryReadBoolean(operation.Payload!, IsPeriodicKey, out isPeriodic) &&
               TryReadVerticesOptional(operation.Payload!, FitPointsKey, out fitPoints) &&
               TryReadVertices(operation.Payload!, ControlPointsKey, out controlPoints) &&
               TryReadDoubleList(operation.Payload!, KnotsKey, out knots) &&
               TryReadDoubleList(operation.Payload!, WeightsKey, out weights) &&
               TryReadPoint(operation.Payload!, StartTangentKey, out startTangent) &&
               TryReadPoint(operation.Payload!, EndTangentKey, out endTangent) &&
               TryReadPoint(operation.Payload!, NormalKey, out normal);
    }

    public static bool TryGetTransformSpline(
        CadOperation operation,
        out int toDegree,
        out bool toClosed,
        out bool toIsPeriodic,
        out IReadOnlyList<XYZ> toFitPoints,
        out IReadOnlyList<XYZ> toControlPoints,
        out IReadOnlyList<double> toKnots,
        out IReadOnlyList<double> toWeights,
        out XYZ toStartTangent,
        out XYZ toEndTangent,
        out XYZ toNormal)
    {
        toDegree = 0;
        toClosed = false;
        toIsPeriodic = false;
        toFitPoints = Array.Empty<XYZ>();
        toControlPoints = Array.Empty<XYZ>();
        toKnots = Array.Empty<double>();
        toWeights = Array.Empty<double>();
        toStartTangent = XYZ.Zero;
        toEndTangent = XYZ.Zero;
        toNormal = XYZ.Zero;
        return TryReadEntityType(operation.Payload, EntityTypeSpline) &&
               TryReadInt(operation.Payload!, ToDegreeKey, out toDegree) &&
               TryReadBoolean(operation.Payload!, ToClosedKey, out toClosed) &&
               TryReadBoolean(operation.Payload!, ToIsPeriodicKey, out toIsPeriodic) &&
               TryReadVerticesOptional(operation.Payload!, ToFitPointsKey, out toFitPoints) &&
               TryReadVertices(operation.Payload!, ToControlPointsKey, out toControlPoints) &&
               TryReadDoubleList(operation.Payload!, ToKnotsKey, out toKnots) &&
               TryReadDoubleList(operation.Payload!, ToWeightsKey, out toWeights) &&
               TryReadPoint(operation.Payload!, ToStartTangentKey, out toStartTangent) &&
               TryReadPoint(operation.Payload!, ToEndTangentKey, out toEndTangent) &&
               TryReadPoint(operation.Payload!, ToNormalKey, out toNormal);
    }

    public static bool TryGetCreateText(
        CadOperation operation,
        out XYZ insertPoint,
        out XYZ alignmentPoint,
        out double height,
        out double rotation,
        out string value,
        out XYZ normal)
    {
        insertPoint = XYZ.Zero;
        alignmentPoint = XYZ.Zero;
        height = 0.0;
        rotation = 0.0;
        value = string.Empty;
        normal = XYZ.Zero;

        if (!TryReadEntityType(operation.Payload, EntityTypeText) ||
            !TryReadPoint(operation.Payload!, InsertPointKey, out insertPoint) ||
            !TryReadPoint(operation.Payload!, AlignmentPointKey, out alignmentPoint) ||
            !TryReadDouble(operation.Payload!, HeightKey, out height) ||
            !TryReadDouble(operation.Payload!, RotationKey, out rotation) ||
            !operation.Payload!.TryGetValue(TextValueKey, out var textValue) ||
            !TryReadPoint(operation.Payload!, NormalKey, out normal))
        {
            return false;
        }

        value = textValue ?? string.Empty;
        return true;
    }

    public static bool TryGetTransformText(
        CadOperation operation,
        out XYZ insertPoint,
        out XYZ alignmentPoint,
        out double height,
        out double rotation)
    {
        insertPoint = XYZ.Zero;
        alignmentPoint = XYZ.Zero;
        height = 0.0;
        rotation = 0.0;

        return TryReadEntityType(operation.Payload, EntityTypeText) &&
               TryReadPoint(operation.Payload!, ToInsertPointKey, out insertPoint) &&
               TryReadPoint(operation.Payload!, ToAlignmentPointKey, out alignmentPoint) &&
               TryReadDouble(operation.Payload!, ToHeightKey, out height) &&
               TryReadDouble(operation.Payload!, ToRotationKey, out rotation);
    }

    public static bool TryGetCreateMText(
        CadOperation operation,
        out XYZ insertPoint,
        out XYZ textDirection,
        out double height,
        out double rectangleWidth,
        out string value,
        out XYZ normal)
    {
        insertPoint = XYZ.Zero;
        textDirection = XYZ.Zero;
        height = 0.0;
        rectangleWidth = 0.0;
        value = string.Empty;
        normal = XYZ.Zero;

        if (!TryReadEntityType(operation.Payload, EntityTypeMText) ||
            !TryReadPoint(operation.Payload!, InsertPointKey, out insertPoint) ||
            !TryReadPoint(operation.Payload!, TextDirectionKey, out textDirection) ||
            !TryReadDouble(operation.Payload!, HeightKey, out height) ||
            !TryReadDouble(operation.Payload!, RectangleWidthKey, out rectangleWidth) ||
            !operation.Payload!.TryGetValue(TextValueKey, out var textValue) ||
            !TryReadPoint(operation.Payload!, NormalKey, out normal))
        {
            return false;
        }

        value = textValue ?? string.Empty;
        return true;
    }

    public static bool TryGetTransformMText(
        CadOperation operation,
        out XYZ insertPoint,
        out XYZ textDirection,
        out double height,
        out double rectangleWidth)
    {
        insertPoint = XYZ.Zero;
        textDirection = XYZ.Zero;
        height = 0.0;
        rectangleWidth = 0.0;

        return TryReadEntityType(operation.Payload, EntityTypeMText) &&
               TryReadPoint(operation.Payload!, ToInsertPointKey, out insertPoint) &&
               TryReadPoint(operation.Payload!, ToTextDirectionKey, out textDirection) &&
               TryReadDouble(operation.Payload!, ToHeightKey, out height) &&
               TryReadDouble(operation.Payload!, ToRectangleWidthKey, out rectangleWidth);
    }

    public static bool TryGetCreateHatch(
        CadOperation operation,
        out IReadOnlyList<IReadOnlyList<XYZ>> loops,
        out bool isSolid,
        out string patternName,
        out XYZ normal)
    {
        loops = Array.Empty<IReadOnlyList<XYZ>>();
        isSolid = true;
        patternName = string.Empty;
        normal = XYZ.Zero;

        if (!TryReadEntityType(operation.Payload, EntityTypeHatch) ||
            !TryReadLoops(operation.Payload!, HatchLoopsKey, out loops) ||
            !TryReadBoolean(operation.Payload!, IsSolidKey, out isSolid) ||
            !operation.Payload!.TryGetValue(PatternNameKey, out patternName!) ||
            !TryReadPoint(operation.Payload!, NormalKey, out normal))
        {
            return false;
        }

        patternName ??= string.Empty;
        return true;
    }

    public static bool TryGetTransformHatch(
        CadOperation operation,
        out IReadOnlyList<IReadOnlyList<XYZ>> loops)
    {
        loops = Array.Empty<IReadOnlyList<XYZ>>();
        return TryReadEntityType(operation.Payload, EntityTypeHatch) &&
               TryReadLoops(operation.Payload!, ToHatchLoopsKey, out loops);
    }

    public static bool TryGetCreateInsert(
        CadOperation operation,
        out string blockName,
        out XYZ insertPoint,
        out double xScale,
        out double yScale,
        out double zScale,
        out double rotation,
        out XYZ normal)
    {
        blockName = string.Empty;
        insertPoint = XYZ.Zero;
        xScale = 1.0;
        yScale = 1.0;
        zScale = 1.0;
        rotation = 0.0;
        normal = XYZ.AxisZ;

        if (!TryReadEntityType(operation.Payload, EntityTypeInsert) ||
            !TryReadString(operation.Payload!, BlockNameKey, out blockName) ||
            string.IsNullOrWhiteSpace(blockName) ||
            !TryReadPoint(operation.Payload!, InsertPointKey, out insertPoint) ||
            !TryReadDouble(operation.Payload!, XScaleKey, out xScale) ||
            !TryReadDouble(operation.Payload!, YScaleKey, out yScale) ||
            !TryReadDouble(operation.Payload!, ZScaleKey, out zScale) ||
            !TryReadDouble(operation.Payload!, InsertRotationKey, out rotation) ||
            !TryReadPoint(operation.Payload!, NormalKey, out normal))
        {
            return false;
        }

        return true;
    }

    public static bool TryGetTransformInsert(
        CadOperation operation,
        out XYZ insertPoint,
        out double xScale,
        out double yScale,
        out double zScale,
        out double rotation,
        out XYZ normal)
    {
        insertPoint = XYZ.Zero;
        xScale = 1.0;
        yScale = 1.0;
        zScale = 1.0;
        rotation = 0.0;
        normal = XYZ.AxisZ;

        return TryReadEntityType(operation.Payload, EntityTypeInsert) &&
               TryReadPoint(operation.Payload!, ToInsertPointKey, out insertPoint) &&
               TryReadDouble(operation.Payload!, ToXScaleKey, out xScale) &&
               TryReadDouble(operation.Payload!, ToYScaleKey, out yScale) &&
               TryReadDouble(operation.Payload!, ToZScaleKey, out zScale) &&
               TryReadDouble(operation.Payload!, ToInsertRotationKey, out rotation) &&
               TryReadPoint(operation.Payload!, ToNormalKey, out normal);
    }

    public static bool TryGetUpdateProperty(CadOperation operation, out string propertyName, out string toValue)
    {
        propertyName = string.Empty;
        toValue = string.Empty;
        return operation.Payload is not null &&
               TryReadString(operation.Payload, PropertyNameKey, out propertyName) &&
               TryReadString(operation.Payload, ToValueKey, out toValue);
    }

    public static string FormatPoint(XYZ point)
    {
        return string.Format(CultureInfo.InvariantCulture, "{0},{1},{2}", FormatDouble(point.X), FormatDouble(point.Y), FormatDouble(point.Z));
    }

    public static bool TryParsePoint(string token, out XYZ point)
    {
        point = XYZ.Zero;
        if (string.IsNullOrWhiteSpace(token))
        {
            return false;
        }

        var parts = token.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length is not 2 and not 3)
        {
            return false;
        }

        if (!double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var x) ||
            !double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var y))
        {
            return false;
        }

        double z = 0.0;
        if (parts.Length == 3 &&
            !double.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out z))
        {
            return false;
        }

        point = new XYZ(x, y, z);
        return true;
    }

    public static string FormatVertices(IReadOnlyList<XYZ> vertices)
    {
        ArgumentNullException.ThrowIfNull(vertices);
        return string.Join("|", vertices.Select(FormatPoint));
    }

    public static bool TryParseVertices(string token, out IReadOnlyList<XYZ> vertices)
    {
        vertices = Array.Empty<XYZ>();
        if (string.IsNullOrWhiteSpace(token))
        {
            return false;
        }

        var parsed = new List<XYZ>();
        var points = token.Split('|', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        foreach (var item in points)
        {
            if (!TryParsePoint(item, out var point))
            {
                vertices = Array.Empty<XYZ>();
                return false;
            }

            parsed.Add(point);
        }

        if (parsed.Count == 0)
        {
            return false;
        }

        vertices = parsed;
        return true;
    }

    public static string FormatDouble(double value)
    {
        return value.ToString("R", CultureInfo.InvariantCulture);
    }

    public static string FormatDoubleList(IReadOnlyList<double> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        return string.Join("|", values.Select(FormatDouble));
    }

    public static bool TryParseDoubleList(string token, out IReadOnlyList<double> values)
    {
        values = Array.Empty<double>();
        if (string.IsNullOrWhiteSpace(token))
        {
            return false;
        }

        var parts = token.Split('|', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
        {
            return false;
        }

        var parsed = new double[parts.Length];
        for (var i = 0; i < parts.Length; i++)
        {
            if (!double.TryParse(parts[i], NumberStyles.Float, CultureInfo.InvariantCulture, out parsed[i]))
            {
                values = Array.Empty<double>();
                return false;
            }
        }

        values = parsed;
        return true;
    }

    public static string FormatLoops(IReadOnlyList<IReadOnlyList<XYZ>> loops)
    {
        ArgumentNullException.ThrowIfNull(loops);
        return string.Join(";", loops.Select(loop => FormatVertices(loop.ToArray())));
    }

    public static bool TryParseLoops(string token, out IReadOnlyList<IReadOnlyList<XYZ>> loops)
    {
        loops = Array.Empty<IReadOnlyList<XYZ>>();
        if (string.IsNullOrWhiteSpace(token))
        {
            return false;
        }

        var parts = token.Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
        {
            return false;
        }

        var parsed = new List<IReadOnlyList<XYZ>>(parts.Length);
        foreach (var part in parts)
        {
            if (!TryParseVertices(part, out var vertices) || vertices.Count < 3)
            {
                loops = Array.Empty<IReadOnlyList<XYZ>>();
                return false;
            }

            parsed.Add(vertices);
        }

        loops = parsed;
        return true;
    }

    private static string FormatBoolean(bool value)
    {
        return value ? "1" : "0";
    }

    private static Dictionary<string, string> CreatePayload(string entityType)
    {
        return new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [EntityTypeKey] = entityType
        };
    }

    private static bool TryReadEntityType(IReadOnlyDictionary<string, string>? payload, string expected)
    {
        return payload is not null &&
               payload.TryGetValue(EntityTypeKey, out var type) &&
               string.Equals(type, expected, StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryReadPoint(IReadOnlyDictionary<string, string> payload, string key, out XYZ point)
    {
        point = XYZ.Zero;
        return payload.TryGetValue(key, out var value) &&
               TryParsePoint(value, out point);
    }

    private static bool TryReadDouble(IReadOnlyDictionary<string, string> payload, string key, out double value)
    {
        value = 0.0;
        return payload.TryGetValue(key, out var token) &&
               double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
    }

    private static bool TryReadVertices(IReadOnlyDictionary<string, string> payload, string key, out IReadOnlyList<XYZ> vertices)
    {
        vertices = Array.Empty<XYZ>();
        return payload.TryGetValue(key, out var token) &&
               TryParseVertices(token, out vertices);
    }

    private static bool TryReadVerticesOptional(IReadOnlyDictionary<string, string> payload, string key, out IReadOnlyList<XYZ> vertices)
    {
        vertices = Array.Empty<XYZ>();
        if (!payload.TryGetValue(key, out var token))
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(token))
        {
            vertices = Array.Empty<XYZ>();
            return true;
        }

        return TryParseVertices(token, out vertices);
    }

    private static bool TryReadDoubleList(IReadOnlyDictionary<string, string> payload, string key, out IReadOnlyList<double> values)
    {
        values = Array.Empty<double>();
        return payload.TryGetValue(key, out var token) &&
               TryParseDoubleList(token, out values);
    }

    private static bool TryReadLoops(IReadOnlyDictionary<string, string> payload, string key, out IReadOnlyList<IReadOnlyList<XYZ>> loops)
    {
        loops = Array.Empty<IReadOnlyList<XYZ>>();
        return payload.TryGetValue(key, out var token) &&
               TryParseLoops(token, out loops);
    }

    private static bool TryReadBoolean(IReadOnlyDictionary<string, string> payload, string key, out bool value)
    {
        value = false;
        if (!payload.TryGetValue(key, out var token))
        {
            return false;
        }

        if (token == "1")
        {
            value = true;
            return true;
        }

        if (token == "0")
        {
            value = false;
            return true;
        }

        return bool.TryParse(token, out value);
    }

    private static bool TryReadInt(IReadOnlyDictionary<string, string> payload, string key, out int value)
    {
        value = 0;
        return payload.TryGetValue(key, out var token) &&
               int.TryParse(token, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
    }

    private static bool TryReadString(IReadOnlyDictionary<string, string> payload, string key, out string value)
    {
        if (!payload.TryGetValue(key, out var token) ||
            string.IsNullOrWhiteSpace(token))
        {
            value = string.Empty;
            return false;
        }

        value = token;
        return true;
    }
}
