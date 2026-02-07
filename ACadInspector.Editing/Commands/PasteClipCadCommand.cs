using ACadInspector.Editing.Clipboard;
using ACadInspector.Editing.Identifiers;
using ACadInspector.Editing.Operations;
using ACadInspector.Editing.Selection;
using CSMath;

namespace ACadInspector.Editing.Commands;

public sealed class PasteClipCadCommand : ICadCommandHandler
{
    private readonly ICadClipboardService _clipboardService;

    public PasteClipCadCommand(ICadClipboardService clipboardService)
    {
        _clipboardService = clipboardService;
    }

    public string Name => "PASTECLIP";
    public IReadOnlyList<string> Aliases => ["PASTE", "PASTEORIG", "PO"];

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

        if (!_clipboardService.TryGetPayload(out var payload) || payload.Entities.Count == 0)
        {
            return ValueTask.FromResult(CadCommandResult.Fail("Clipboard is empty."));
        }

        var pasteOrig = context.CommandName.Equals("PASTEORIG", StringComparison.OrdinalIgnoreCase) ||
                        context.CommandName.Equals("PO", StringComparison.OrdinalIgnoreCase);

        var insertionPoint = payload.BasePoint;
        if (pasteOrig)
        {
            if (context.Arguments.Count > 0)
            {
                return ValueTask.FromResult(CadCommandResult.Fail("Usage: PASTEORIG"));
            }
        }
        else if (context.Arguments.Count > 0)
        {
            string? parseError = null;
            if (context.Arguments.Count != 1 ||
                !CadCommandParsing.TryParsePointArgument(context.Arguments, out insertionPoint, out parseError))
            {
                return ValueTask.FromResult(CadCommandResult.Fail(parseError ?? "Usage: PASTECLIP [x,y[,z]]"));
            }
        }

        var translation = new XYZ(
            insertionPoint.X - payload.BasePoint.X,
            insertionPoint.Y - payload.BasePoint.Y,
            insertionPoint.Z - payload.BasePoint.Z);

        var forward = new List<CadOperation>(payload.Entities.Count);
        var inverse = new List<CadOperation>(payload.Entities.Count);
        var createdIds = new List<CadEntityId>(payload.Entities.Count);

        foreach (var item in payload.Entities)
        {
            var id = CadEntityId.New();
            if (!CadClipboardEntityCodec.TryDecodeCreateOperation(item, id, translation, out var createOperation, out var decodeError))
            {
                return ValueTask.FromResult(CadCommandResult.Fail(decodeError!));
            }

            forward.Add(createOperation);
            if (!TryCreateDeleteOperation(createOperation, out var deleteOperation))
            {
                return ValueTask.FromResult(CadCommandResult.Fail("Unable to create inverse operation for pasted entity."));
            }

            inverse.Add(deleteOperation);
            createdIds.Add(id);
        }

        var actorId = session.SessionId.Value;
        var forwardBatch = session.NextBatch(actorId, forward);
        var inverseBatch = session.NextBatch(actorId, inverse.AsEnumerable().Reverse().ToArray());

        session.Apply(forwardBatch);
        session.UndoRedo.Record(forwardBatch, inverseBatch);

        var createdEntities = new List<object?>(createdIds.Count);
        foreach (var createdId in createdIds)
        {
            if (session.EntityIndex.TryGetEntity(createdId, out var created))
            {
                createdEntities.Add(created);
            }
        }

        if (createdEntities.Count > 0)
        {
            session.SetSelection(createdEntities, CadSelectionMode.Replace);
        }

        return ValueTask.FromResult(CadCommandResult.Ok($"Pasted {forward.Count} entity(s).", forward));
    }

    private static bool TryCreateDeleteOperation(CadOperation createOperation, out CadOperation deleteOperation)
    {
        var entityId = createOperation.EntityId;
        if (entityId is null)
        {
            deleteOperation = null!;
            return false;
        }

        if (!CadOperationPayloadCodec.TryGetEntityType(createOperation, out var entityType))
        {
            deleteOperation = null!;
            return false;
        }

        switch (entityType.ToUpperInvariant())
        {
            case CadOperationPayloadCodec.EntityTypeLine:
                if (CadOperationPayloadCodec.TryGetCreateLine(createOperation, out var lineStart, out var lineEnd))
                {
                    deleteOperation = CadOperationPayloadCodec.DeleteLine(entityId.Value, lineStart, lineEnd);
                    return true;
                }
                break;
            case CadOperationPayloadCodec.EntityTypeArc:
                if (CadOperationPayloadCodec.TryGetCreateArc(createOperation, out var arcCenter, out var arcRadius, out var arcStartAngle, out var arcEndAngle))
                {
                    deleteOperation = CadOperationPayloadCodec.DeleteArc(entityId.Value, arcCenter, arcRadius, arcStartAngle, arcEndAngle);
                    return true;
                }
                break;
            case CadOperationPayloadCodec.EntityTypeCircle:
                if (CadOperationPayloadCodec.TryGetCreateCircle(createOperation, out var circleCenter, out var circleRadius))
                {
                    deleteOperation = CadOperationPayloadCodec.DeleteCircle(entityId.Value, circleCenter, circleRadius);
                    return true;
                }
                break;
            case CadOperationPayloadCodec.EntityTypeLwPolyline:
                if (CadOperationPayloadCodec.TryGetCreateLwPolyline(createOperation, out var vertices, out var isClosed))
                {
                    deleteOperation = CadOperationPayloadCodec.DeleteLwPolyline(entityId.Value, vertices, isClosed);
                    return true;
                }
                break;
            case CadOperationPayloadCodec.EntityTypePoint:
                if (CadOperationPayloadCodec.TryGetCreatePoint(createOperation, out var location))
                {
                    deleteOperation = CadOperationPayloadCodec.DeletePoint(entityId.Value, location);
                    return true;
                }
                break;
            case CadOperationPayloadCodec.EntityTypeXLine:
                if (CadOperationPayloadCodec.TryGetCreateXLine(createOperation, out var firstPoint, out var xlineDirection))
                {
                    deleteOperation = CadOperationPayloadCodec.DeleteXLine(entityId.Value, firstPoint, xlineDirection);
                    return true;
                }
                break;
            case CadOperationPayloadCodec.EntityTypeRay:
                if (CadOperationPayloadCodec.TryGetCreateRay(createOperation, out var rayStart, out var rayDirection))
                {
                    deleteOperation = CadOperationPayloadCodec.DeleteRay(entityId.Value, rayStart, rayDirection);
                    return true;
                }
                break;
            case CadOperationPayloadCodec.EntityTypeEllipse:
                if (CadOperationPayloadCodec.TryGetCreateEllipse(
                        createOperation,
                        out var ellipseCenter,
                        out var majorAxisEndPoint,
                        out var radiusRatio,
                        out var startParameter,
                        out var endParameter,
                        out var ellipseNormal))
                {
                    deleteOperation = CadOperationPayloadCodec.DeleteEllipse(
                        entityId.Value,
                        ellipseCenter,
                        majorAxisEndPoint,
                        radiusRatio,
                        startParameter,
                        endParameter,
                        ellipseNormal);
                    return true;
                }
                break;
            case CadOperationPayloadCodec.EntityTypeSpline:
                if (CadOperationPayloadCodec.TryGetCreateSpline(
                        createOperation,
                        out var degree,
                        out var splineIsClosed,
                        out var splineIsPeriodic,
                        out var fitPoints,
                        out var controlPoints,
                        out var knots,
                        out var weights,
                        out var startTangent,
                        out var endTangent,
                        out var splineNormal))
                {
                    deleteOperation = CadOperationPayloadCodec.DeleteSpline(
                        entityId.Value,
                        degree,
                        splineIsClosed,
                        splineIsPeriodic,
                        fitPoints,
                        controlPoints,
                        knots,
                        weights,
                        startTangent,
                        endTangent,
                        splineNormal);
                    return true;
                }
                break;
            case CadOperationPayloadCodec.EntityTypeText:
                if (CadOperationPayloadCodec.TryGetCreateText(
                        createOperation,
                        out var insertPoint,
                        out var alignmentPoint,
                        out var height,
                        out var rotation,
                        out var value,
                        out var normal))
                {
                    deleteOperation = CadOperationPayloadCodec.DeleteText(
                        entityId.Value,
                        insertPoint,
                        alignmentPoint,
                        height,
                        rotation,
                        value,
                        normal);
                    return true;
                }
                break;
            case CadOperationPayloadCodec.EntityTypeMText:
                if (CadOperationPayloadCodec.TryGetCreateMText(
                        createOperation,
                        out var mtextInsertPoint,
                        out var textDirection,
                        out var mtextHeight,
                        out var rectangleWidth,
                        out var mtextValue,
                        out var mtextNormal))
                {
                    deleteOperation = CadOperationPayloadCodec.DeleteMText(
                        entityId.Value,
                        mtextInsertPoint,
                        textDirection,
                        mtextHeight,
                        rectangleWidth,
                        mtextValue,
                        mtextNormal);
                    return true;
                }
                break;
            case CadOperationPayloadCodec.EntityTypeHatch:
                if (CadOperationPayloadCodec.TryGetCreateHatch(
                        createOperation,
                        out var loops,
                        out var isSolid,
                        out var patternName,
                        out var hatchNormal))
                {
                    deleteOperation = CadOperationPayloadCodec.DeleteHatch(
                        entityId.Value,
                        loops,
                        isSolid,
                        patternName,
                        hatchNormal);
                    return true;
                }
                break;
            case CadOperationPayloadCodec.EntityTypeInsert:
                if (CadOperationPayloadCodec.TryGetCreateInsert(
                        createOperation,
                        out var blockName,
                        out var insertLocation,
                        out var xScale,
                        out var yScale,
                        out var zScale,
                        out var insertRotation,
                        out var insertNormal))
                {
                    deleteOperation = CadOperationPayloadCodec.DeleteInsert(
                        entityId.Value,
                        blockName,
                        insertLocation,
                        xScale,
                        yScale,
                        zScale,
                        insertRotation,
                        insertNormal);
                    return true;
                }
                break;
        }

        deleteOperation = null!;
        return false;
    }
}
