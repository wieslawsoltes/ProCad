using ProCad.Editing.Dependencies;
using ProCad.Editing.EntityIndex;
using ProCad.Editing.Identifiers;
using ProCad.Editing.Operations;
using ProCad.Editing.Constraints;
using ProCad.Editing.Selection;
using ProCad.Editing.Transactions;
using ProCad.Editing.Undo;
using ACadSharp;
using ACadSharp.Entities;
using CSMath;

namespace ProCad.Editing.Sessions;

public sealed class CadDocumentSession : ICadEditorSession
{
    private long _sequence;
    private readonly ICadGeometricConstraintSolver _constraintSolver;

    public CadDocumentSessionId SessionId { get; }
    public CadDocument Document { get; }
    public CadSelectionSet SelectionSet { get; } = new();
    public ICadEntityIndex EntityIndex { get; }
    public ICadUndoRedoService UndoRedo { get; }
    public ICadTransactionService TransactionService { get; }
    public ICadDependencyResolver DependencyResolver { get; }
    public ICadConstraintService Constraints { get; }

    public long Revision { get; private set; }
    public bool IsDirty { get; private set; }

    public CadDocumentSession(
        CadDocument document,
        ICadEntityIndex entityIndex,
        ICadUndoRedoService undoRedo,
        ICadTransactionService transactionService,
        ICadDependencyResolver dependencyResolver,
        ICadConstraintService constraints,
        ICadGeometricConstraintSolver constraintSolver)
    {
        Document = document ?? throw new ArgumentNullException(nameof(document));
        EntityIndex = entityIndex ?? throw new ArgumentNullException(nameof(entityIndex));
        UndoRedo = undoRedo ?? throw new ArgumentNullException(nameof(undoRedo));
        TransactionService = transactionService ?? throw new ArgumentNullException(nameof(transactionService));
        DependencyResolver = dependencyResolver ?? throw new ArgumentNullException(nameof(dependencyResolver));
        Constraints = constraints ?? throw new ArgumentNullException(nameof(constraints));
        _constraintSolver = constraintSolver ?? throw new ArgumentNullException(nameof(constraintSolver));
        SessionId = CadDocumentSessionId.New();

        RebuildEntityIndex();
    }

    public CadOperationBatch Apply(CadOperationBatch batch)
    {
        ArgumentNullException.ThrowIfNull(batch);
        var dirtyEntityIds = new HashSet<CadEntityId>();

        foreach (var operation in batch.Operations)
        {
            CollectEntityIds(operation, dirtyEntityIds);
            ApplyOperation(operation);
        }

        if (Constraints.GetConstraints().Count > 0)
        {
            _constraintSolver.Solve(
                Constraints,
                EntityIndex,
                dirtyEntityIds.Count == 0 ? null : dirtyEntityIds);
        }

        Revision += Math.Max(1, batch.Operations.Count);
        IsDirty = true;
        _sequence = Math.Max(_sequence + 1, batch.Sequence);
        return batch;
    }

    public bool TryUndo(Guid actorId, out CadOperationBatch undoBatch)
    {
        if (!UndoRedo.TryPopUndoUnit(
                unit => unit.Metadata.ActorId == actorId &&
                        unit.Metadata.Source != CadUndoSource.CollabReplay,
                out var undoUnit))
        {
            undoBatch = null!;
            return false;
        }

        undoBatch = undoUnit.Inverse;
        Apply(undoBatch);
        return true;
    }

    public bool TryRedo(Guid actorId, out CadOperationBatch redoBatch)
    {
        if (!UndoRedo.TryPopRedoUnit(
                unit => unit.Metadata.ActorId == actorId &&
                        unit.Metadata.Source != CadUndoSource.CollabReplay,
                out var redoUnit))
        {
            redoBatch = null!;
            return false;
        }

        redoBatch = redoUnit.Forward;
        Apply(redoBatch);
        return true;
    }

    public bool SetSelection(IEnumerable<object?> selection, CadSelectionMode mode)
    {
        return SelectionSet.Apply(selection, mode);
    }

    public CadOperationBatch NextBatch(Guid actorId, IReadOnlyList<CadOperation> operations)
    {
        return CadOperationBatch.Create(actorId, Revision, ++_sequence, operations);
    }

    public byte[] ExportConstraintPayload()
    {
        return Constraints.ExportPayload();
    }

    public bool ImportConstraintPayload(ReadOnlySpan<byte> payload)
    {
        return Constraints.ImportPayload(payload);
    }

    public void MarkExternalMutation(bool rebuildEntityIndex = false)
    {
        if (rebuildEntityIndex)
        {
            RebuildEntityIndex();
        }

        Revision += 1;
        IsDirty = true;
        _sequence += 1;
    }

    private void ApplyOperation(CadOperation operation)
    {
        switch (operation.Kind)
        {
            case CadOperationKind.CreateEntity:
                ApplyCreate(operation);
                break;
            case CadOperationKind.DeleteEntity:
                ApplyDelete(operation);
                break;
            case CadOperationKind.TransformEntity:
                ApplyTransform(operation);
                break;
            case CadOperationKind.UpdateProperty:
                ApplyUpdateProperty(operation);
                break;
            case CadOperationKind.Composite:
                ApplyComposite(operation);
                break;
            default:
                // Unsupported operation kinds are intentionally ignored at this stage.
                break;
        }
    }

    private void ApplyComposite(CadOperation operation)
    {
        if (operation.Children is null || operation.Children.Count == 0)
        {
            return;
        }

        foreach (var child in operation.Children)
        {
            ApplyOperation(child);
        }
    }

    private void ApplyCreate(CadOperation operation)
    {
        if (!TryGetEntityId(operation, out var id))
        {
            return;
        }

        if (EntityIndex.TryGetEntity(id, out _))
        {
            return;
        }

        if (!CadOperationPayloadCodec.TryGetEntityType(operation, out var entityType))
        {
            return;
        }

        switch (entityType.ToUpperInvariant())
        {
            case CadOperationPayloadCodec.EntityTypeLine:
            {
                if (!CadOperationPayloadCodec.TryGetCreateLine(operation, out var start, out var end))
                {
                    return;
                }

                var line = new Line(start, end);
                RegisterCreatedEntity(line, id, operation);
                break;
            }
            case CadOperationPayloadCodec.EntityTypeCircle:
            {
                if (!CadOperationPayloadCodec.TryGetCreateCircle(operation, out var center, out var radius))
                {
                    return;
                }

                var circle = new Circle
                {
                    Center = center,
                    Radius = radius
                };
                RegisterCreatedEntity(circle, id, operation);
                break;
            }
            case CadOperationPayloadCodec.EntityTypeArc:
            {
                if (!CadOperationPayloadCodec.TryGetCreateArc(operation, out var center, out var radius, out var startAngle, out var endAngle))
                {
                    return;
                }

                var arc = new Arc
                {
                    Center = center,
                    Radius = radius,
                    StartAngle = startAngle,
                    EndAngle = endAngle
                };
                RegisterCreatedEntity(arc, id, operation);
                break;
            }
            case CadOperationPayloadCodec.EntityTypeLwPolyline:
            {
                if (!CadOperationPayloadCodec.TryGetCreateLwPolyline(operation, out var vertices, out var isClosed))
                {
                    return;
                }

                var polyline = new LwPolyline
                {
                    IsClosed = isClosed
                };

                if (vertices.Count > 0)
                {
                    polyline.Elevation = vertices[0].Z;
                }

                foreach (var vertex in vertices)
                {
                    polyline.Vertices.Add(new LwPolyline.Vertex(new XY(vertex.X, vertex.Y)));
                }

                RegisterCreatedEntity(polyline, id, operation);
                break;
            }
            case CadOperationPayloadCodec.EntityTypePoint:
            {
                if (!CadOperationPayloadCodec.TryGetCreatePoint(operation, out var location))
                {
                    return;
                }

                var point = new Point
                {
                    Location = location
                };

                RegisterCreatedEntity(point, id, operation);
                break;
            }
            case CadOperationPayloadCodec.EntityTypeXLine:
            {
                if (!CadOperationPayloadCodec.TryGetCreateXLine(operation, out var firstPoint, out var direction))
                {
                    return;
                }

                var xline = new XLine
                {
                    FirstPoint = firstPoint,
                    Direction = direction
                };

                RegisterCreatedEntity(xline, id, operation);
                break;
            }
            case CadOperationPayloadCodec.EntityTypeRay:
            {
                if (!CadOperationPayloadCodec.TryGetCreateRay(operation, out var startPoint, out var direction))
                {
                    return;
                }

                var ray = new Ray
                {
                    StartPoint = startPoint,
                    Direction = direction
                };

                RegisterCreatedEntity(ray, id, operation);
                break;
            }
            case CadOperationPayloadCodec.EntityTypeEllipse:
            {
                if (!CadOperationPayloadCodec.TryGetCreateEllipse(
                        operation,
                        out var center,
                        out var majorAxisEndPoint,
                        out var radiusRatio,
                        out var startParameter,
                        out var endParameter,
                        out var normal))
                {
                    return;
                }

                var ellipse = new Ellipse
                {
                    Center = center,
                    MajorAxisEndPoint = majorAxisEndPoint,
                    RadiusRatio = radiusRatio,
                    StartParameter = startParameter,
                    EndParameter = endParameter,
                    Normal = normal
                };

                RegisterCreatedEntity(ellipse, id, operation);
                break;
            }
            case CadOperationPayloadCodec.EntityTypeSpline:
            {
                if (!CadOperationPayloadCodec.TryGetCreateSpline(
                        operation,
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
                    return;
                }

                var spline = new Spline
                {
                    Degree = degree,
                    IsClosed = isClosed,
                    IsPeriodic = isPeriodic,
                    StartTangent = startTangent,
                    EndTangent = endTangent,
                    Normal = normal
                };

                foreach (var point in fitPoints)
                {
                    spline.FitPoints.Add(point);
                }

                foreach (var point in controlPoints)
                {
                    spline.ControlPoints.Add(point);
                }

                foreach (var knot in knots)
                {
                    spline.Knots.Add(knot);
                }

                foreach (var weight in weights)
                {
                    spline.Weights.Add(weight);
                }

                RegisterCreatedEntity(spline, id, operation);
                break;
            }
            case CadOperationPayloadCodec.EntityTypeText:
            {
                if (!CadOperationPayloadCodec.TryGetCreateText(
                        operation,
                        out var insertPoint,
                        out var alignmentPoint,
                        out var height,
                        out var rotation,
                        out var value,
                        out var normal))
                {
                    return;
                }

                var text = new TextEntity
                {
                    InsertPoint = insertPoint,
                    AlignmentPoint = alignmentPoint,
                    Height = height,
                    Rotation = rotation,
                    Value = value,
                    Normal = normal
                };

                RegisterCreatedEntity(text, id, operation);
                break;
            }
            case CadOperationPayloadCodec.EntityTypeMText:
            {
                if (!CadOperationPayloadCodec.TryGetCreateMText(
                        operation,
                        out var insertPoint,
                        out var textDirection,
                        out var height,
                        out var rectangleWidth,
                        out var value,
                        out var normal))
                {
                    return;
                }

                var mtext = new MText
                {
                    InsertPoint = insertPoint,
                    AlignmentPoint = textDirection,
                    Height = height,
                    RectangleWidth = rectangleWidth,
                    Value = value,
                    Normal = normal
                };

                RegisterCreatedEntity(mtext, id, operation);
                break;
            }
            case CadOperationPayloadCodec.EntityTypeHatch:
            {
                if (!CadOperationPayloadCodec.TryGetCreateHatch(
                        operation,
                        out var loops,
                        out var isSolid,
                        out var patternName,
                        out var normal))
                {
                    return;
                }

                var hatch = new Hatch
                {
                    IsSolid = isSolid,
                    Normal = normal
                };

                if (!isSolid)
                {
                    hatch.Pattern = new HatchPattern(string.IsNullOrWhiteSpace(patternName) ? "ANSI31" : patternName);
                }

                for (var index = 0; index < loops.Count; index++)
                {
                    var loop = loops[index];
                    var path = new Hatch.BoundaryPath
                    {
                        Flags = index == 0 ? BoundaryPathFlags.External : BoundaryPathFlags.Default
                    };
                    path.Edges.Add(new Hatch.BoundaryPath.Polyline(loop, isClosed: true));
                    hatch.Paths.Add(path);
                }

                RegisterCreatedEntity(hatch, id, operation);
                break;
            }
            case CadOperationPayloadCodec.EntityTypeInsert:
            {
                if (!CadOperationPayloadCodec.TryGetCreateInsert(
                        operation,
                        out var blockName,
                        out var insertPoint,
                        out var xScale,
                        out var yScale,
                        out var zScale,
                        out var rotation,
                        out var normal))
                {
                    return;
                }

                if (Document.BlockRecords is null ||
                    !Document.BlockRecords.TryGetValue(blockName, out var block))
                {
                    return;
                }

                var insert = new Insert(block)
                {
                    InsertPoint = insertPoint,
                    XScale = xScale,
                    YScale = yScale,
                    ZScale = zScale,
                    Rotation = rotation,
                    Normal = normal
                };

                RegisterCreatedEntity(insert, id, operation);
                break;
            }
        }
    }

    private void ApplyDelete(CadOperation operation)
    {
        if (!TryGetEntityId(operation, out var id))
        {
            return;
        }

        if (!EntityIndex.TryGetEntity(id, out var entity))
        {
            return;
        }

        if (entity.Owner is ACadSharp.Tables.BlockRecord block)
        {
            block.Entities.Remove(entity);
        }

        EntityIndex.Unregister(entity);
        SelectionSet.Apply([entity], CadSelectionMode.Remove);
    }

    private void ApplyTransform(CadOperation operation)
    {
        if (!TryGetEntityId(operation, out var id))
        {
            return;
        }

        if (!EntityIndex.TryGetEntity(id, out var entity))
        {
            return;
        }

        switch (entity)
        {
            case Line line when CadOperationPayloadCodec.TryGetTransformLine(operation, out var toStart, out var toEnd):
                line.StartPoint = toStart;
                line.EndPoint = toEnd;
                break;
            case Circle circle when CadOperationPayloadCodec.TryGetTransformCircle(operation, out var toCenter, out var toRadius):
                circle.Center = toCenter;
                circle.Radius = toRadius;
                break;
            case Arc arc when CadOperationPayloadCodec.TryGetTransformArc(operation, out var toArcCenter, out var toArcRadius, out var toStartAngle, out var toEndAngle):
                arc.Center = toArcCenter;
                arc.Radius = toArcRadius;
                arc.StartAngle = toStartAngle;
                arc.EndAngle = toEndAngle;
                break;
            case LwPolyline polyline when CadOperationPayloadCodec.TryGetTransformLwPolyline(operation, out var vertices, out var isClosed):
            {
                polyline.Vertices.Clear();
                foreach (var vertex in vertices)
                {
                    polyline.Vertices.Add(new LwPolyline.Vertex(new XY(vertex.X, vertex.Y)));
                }

                if (vertices.Count > 0)
                {
                    polyline.Elevation = vertices[0].Z;
                }

                polyline.IsClosed = isClosed;
                break;
            }
            case Point point when CadOperationPayloadCodec.TryGetTransformPoint(operation, out var toLocation):
                point.Location = toLocation;
                break;
            case XLine xline when CadOperationPayloadCodec.TryGetTransformXLine(operation, out var toFirstPoint, out var toDirection):
                xline.FirstPoint = toFirstPoint;
                xline.Direction = toDirection;
                break;
            case Ray ray when CadOperationPayloadCodec.TryGetTransformRay(operation, out var toStartPoint, out var toRayDirection):
                ray.StartPoint = toStartPoint;
                ray.Direction = toRayDirection;
                break;
            case TextEntity text when CadOperationPayloadCodec.TryGetTransformText(operation, out var toInsertPoint, out var toAlignmentPoint, out var toHeight, out var toRotation):
                text.InsertPoint = toInsertPoint;
                text.AlignmentPoint = toAlignmentPoint;
                text.Height = toHeight;
                text.Rotation = toRotation;
                break;
            case MText mtext when CadOperationPayloadCodec.TryGetTransformMText(operation, out var toMTextInsertPoint, out var toTextDirection, out var toMTextHeight, out var toRectangleWidth):
                mtext.InsertPoint = toMTextInsertPoint;
                mtext.AlignmentPoint = toTextDirection;
                mtext.Height = toMTextHeight;
                mtext.RectangleWidth = toRectangleWidth;
                break;
            case Ellipse ellipse when CadOperationPayloadCodec.TryGetTransformEllipse(
                operation,
                out var toEllipseCenter,
                out var toMajorAxisEndPoint,
                out var toRadiusRatio,
                out var toStartParameter,
                out var toEndParameter,
                out var toEllipseNormal):
                ellipse.Center = toEllipseCenter;
                ellipse.MajorAxisEndPoint = toMajorAxisEndPoint;
                ellipse.RadiusRatio = toRadiusRatio;
                ellipse.StartParameter = toStartParameter;
                ellipse.EndParameter = toEndParameter;
                ellipse.Normal = toEllipseNormal;
                break;
            case Spline spline when CadOperationPayloadCodec.TryGetTransformSpline(
                operation,
                out var toDegree,
                out var toClosed,
                out var toIsPeriodic,
                out var toFitPoints,
                out var toControlPoints,
                out var toKnots,
                out var toWeights,
                out var toStartTangent,
                out var toEndTangent,
                out var toSplineNormal):
                spline.Degree = toDegree;
                spline.IsClosed = toClosed;
                spline.IsPeriodic = toIsPeriodic;
                spline.FitPoints.Clear();
                foreach (var point in toFitPoints)
                {
                    spline.FitPoints.Add(point);
                }

                spline.ControlPoints.Clear();
                foreach (var point in toControlPoints)
                {
                    spline.ControlPoints.Add(point);
                }

                spline.Knots.Clear();
                foreach (var knot in toKnots)
                {
                    spline.Knots.Add(knot);
                }

                spline.Weights.Clear();
                foreach (var weight in toWeights)
                {
                    spline.Weights.Add(weight);
                }

                spline.StartTangent = toStartTangent;
                spline.EndTangent = toEndTangent;
                spline.Normal = toSplineNormal;
                break;
            case Hatch hatch when CadOperationPayloadCodec.TryGetTransformHatch(operation, out var toLoops):
                ApplyHatchLoops(hatch, toLoops);
                break;
            case Insert insert when CadOperationPayloadCodec.TryGetTransformInsert(
                operation,
                out var toInsertPoint,
                out var toXScale,
                out var toYScale,
                out var toZScale,
                out var toRotation,
                out var toNormal):
                insert.InsertPoint = toInsertPoint;
                insert.XScale = toXScale;
                insert.YScale = toYScale;
                insert.ZScale = toZScale;
                insert.Rotation = toRotation;
                insert.Normal = toNormal;
                break;
        }
    }

    private void ApplyUpdateProperty(CadOperation operation)
    {
        if (!TryGetEntityId(operation, out var id))
        {
            return;
        }

        if (!EntityIndex.TryGetEntity(id, out var entity) ||
            entity is not Entity cadEntity)
        {
            return;
        }

        if (!CadOperationPayloadCodec.TryGetUpdateProperty(operation, out var propertyName, out var toValue))
        {
            return;
        }

        switch (propertyName)
        {
            case CadEntityPropertyCodec.Layer:
                cadEntity.Layer = CadEntityPropertyCodec.ResolveLayer(Document, toValue);
                break;
            case CadEntityPropertyCodec.LineType:
                cadEntity.LineType = CadEntityPropertyCodec.ResolveLineType(Document, toValue);
                break;
            case CadEntityPropertyCodec.Color:
                if (CadEntityPropertyCodec.TryDeserializeColor(toValue, out var color))
                {
                    cadEntity.Color = color;
                }

                break;
            case CadEntityPropertyCodec.LineWeight:
                if (CadEntityPropertyCodec.TryDeserializeLineWeight(toValue, out var lineWeight))
                {
                    cadEntity.LineWeight = lineWeight;
                }

                break;
            case CadEntityPropertyCodec.LineTypeScale:
                if (CadEntityPropertyCodec.TryDeserializeLineTypeScale(toValue, out var lineTypeScale))
                {
                    cadEntity.LineTypeScale = lineTypeScale;
                }

                break;
            case CadEntityPropertyCodec.IsInvisible:
                if (CadEntityPropertyCodec.TryDeserializeBoolean(toValue, out var isInvisible))
                {
                    cadEntity.IsInvisible = isInvisible;
                }

                break;
            case CadEntityPropertyCodec.Transparency:
                if (CadEntityPropertyCodec.TryDeserializeTransparency(toValue, out var transparency))
                {
                    cadEntity.Transparency = transparency;
                }

                break;
        }
    }

    private static bool TryGetEntityId(CadOperation operation, out CadEntityId id)
    {
        if (operation.EntityId is not { } entityId || entityId.IsEmpty)
        {
            id = default;
            return false;
        }

        id = entityId;
        return true;
    }

    private static void CollectEntityIds(CadOperation operation, HashSet<CadEntityId> ids)
    {
        if (operation.EntityId is { } id && !id.IsEmpty)
        {
            ids.Add(id);
        }

        if (operation.Children is null || operation.Children.Count == 0)
        {
            return;
        }

        foreach (var child in operation.Children)
        {
            CollectEntityIds(child, ids);
        }
    }

    private void RegisterCreatedEntity(Entity entity, CadEntityId id, CadOperation operation)
    {
        ArgumentNullException.ThrowIfNull(entity);

        var properties = CadOperationPayloadCodec.TryGetCreateProperties(operation, out var payloadProperties)
            ? payloadProperties
            : CadEntityCreateProperties.FromHeader(Document);
        ApplyCreateProperties(entity, properties);
        Document.Entities.Add(entity);
        EntityIndex.Register(entity, id);
    }

    private void ApplyCreateProperties(Entity entity, in CadEntityCreateProperties properties)
    {
        entity.Layer = CadEntityPropertyCodec.ResolveLayer(Document, properties.LayerName);
        entity.LineType = CadEntityPropertyCodec.ResolveLineType(Document, properties.LineTypeName);
        entity.Color = properties.Color;
        entity.LineWeight = properties.LineWeight;
        entity.LineTypeScale = properties.LineTypeScale;
        entity.IsInvisible = properties.IsInvisible;
        entity.Transparency = properties.Transparency;

        if (!string.IsNullOrWhiteSpace(properties.TextStyleName))
        {
            var textStyle = CadEntityPropertyCodec.ResolveTextStyle(Document, properties.TextStyleName);
            if (entity is IText text)
            {
                text.Style = textStyle;
            }
            else if (entity is MultiLeader multiLeader)
            {
                multiLeader.TextStyle = textStyle;
            }
        }

        if (string.IsNullOrWhiteSpace(properties.DimensionStyleName))
        {
            return;
        }

        var dimensionStyle = CadEntityPropertyCodec.ResolveDimensionStyle(Document, properties.DimensionStyleName);
        if (entity is Dimension dimension)
        {
            dimension.Style = dimensionStyle;
        }
        else if (entity is Leader leader)
        {
            leader.Style = dimensionStyle;
        }
    }

    private void RebuildEntityIndex()
    {
        EntityIndex.Clear();
        foreach (var entity in EnumerateEntities(Document))
        {
            EntityIndex.Register(entity);
        }
    }

    private static void ApplyHatchLoops(Hatch hatch, IReadOnlyList<IReadOnlyList<XYZ>> loops)
    {
        hatch.Paths.Clear();
        for (var index = 0; index < loops.Count; index++)
        {
            var loop = loops[index];
            if (loop.Count < 3)
            {
                continue;
            }

            var path = new Hatch.BoundaryPath
            {
                Flags = index == 0 ? BoundaryPathFlags.External : BoundaryPathFlags.Default
            };
            path.Edges.Add(new Hatch.BoundaryPath.Polyline(loop, isClosed: true));
            hatch.Paths.Add(path);
        }
    }

    private static IEnumerable<Entity> EnumerateEntities(CadDocument document)
    {
        foreach (var entity in document.Entities)
        {
            yield return entity;
        }

        if (document.BlockRecords is null)
        {
            yield break;
        }

        foreach (var record in document.BlockRecords)
        {
            foreach (var entity in record.Entities)
            {
                yield return entity;
            }
        }
    }
}
