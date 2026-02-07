using ACadInspector.Editing.Identifiers;
using ACadInspector.Editing.Operations;
using ACadInspector.Editing.Selection;
using ACadSharp.Entities;

namespace ACadInspector.Editing.Commands;

public sealed class CopyCadCommand : ICadCommandHandler
{
    public string Name => "COPY";
    public IReadOnlyList<string> Aliases => ["CO", "CP"];

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

        if (!CadCommandParsing.TryParseTranslation(context.Arguments, out var delta, out var consumed, out var parseError))
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
        var createdIds = new List<CadEntityId>(targets.Count);

        foreach (var target in targets)
        {
            var newId = CadEntityId.New();
            createdIds.Add(newId);

            switch (target)
            {
                case Line line:
                {
                    var copiedStart = CadGeometryTransform.Translate(line.StartPoint, delta);
                    var copiedEnd = CadGeometryTransform.Translate(line.EndPoint, delta);
                    forward.Add(CadOperationPayloadCodec.CreateLine(newId, copiedStart, copiedEnd).WithSourceProperties(target));
                    inverse.Add(CadOperationPayloadCodec.DeleteLine(newId, copiedStart, copiedEnd));
                    break;
                }
                case Arc arc:
                {
                    var copiedCenter = CadGeometryTransform.Translate(arc.Center, delta);
                    forward.Add(CadOperationPayloadCodec.CreateArc(newId, copiedCenter, arc.Radius, arc.StartAngle, arc.EndAngle).WithSourceProperties(target));
                    inverse.Add(CadOperationPayloadCodec.DeleteArc(newId, copiedCenter, arc.Radius, arc.StartAngle, arc.EndAngle));
                    break;
                }
                case Circle circle:
                {
                    var copiedCenter = CadGeometryTransform.Translate(circle.Center, delta);
                    forward.Add(CadOperationPayloadCodec.CreateCircle(newId, copiedCenter, circle.Radius).WithSourceProperties(target));
                    inverse.Add(CadOperationPayloadCodec.DeleteCircle(newId, copiedCenter, circle.Radius));
                    break;
                }
                case LwPolyline polyline:
                {
                    var copiedVertices = CadGeometryTransform.TranslateVertices(CadGeometryTransform.ToVertices(polyline), delta);
                    var copiedClosed = polyline.IsClosed;
                    forward.Add(CadOperationPayloadCodec.CreateLwPolyline(newId, copiedVertices, copiedClosed).WithSourceProperties(target));
                    inverse.Add(CadOperationPayloadCodec.DeleteLwPolyline(newId, copiedVertices, copiedClosed));
                    break;
                }
                case Point point:
                {
                    var copiedLocation = CadGeometryTransform.Translate(point.Location, delta);
                    forward.Add(CadOperationPayloadCodec.CreatePoint(newId, copiedLocation).WithSourceProperties(target));
                    inverse.Add(CadOperationPayloadCodec.DeletePoint(newId, copiedLocation));
                    break;
                }
                case XLine xline:
                {
                    var copiedFirstPoint = CadGeometryTransform.Translate(xline.FirstPoint, delta);
                    var copiedDirection = CadGeometryTransform.NormalizeDirection(xline.Direction);
                    forward.Add(CadOperationPayloadCodec.CreateXLine(newId, copiedFirstPoint, copiedDirection).WithSourceProperties(target));
                    inverse.Add(CadOperationPayloadCodec.DeleteXLine(newId, copiedFirstPoint, copiedDirection));
                    break;
                }
                case Ray ray:
                {
                    var copiedStartPoint = CadGeometryTransform.Translate(ray.StartPoint, delta);
                    var copiedDirection = CadGeometryTransform.NormalizeDirection(ray.Direction);
                    forward.Add(CadOperationPayloadCodec.CreateRay(newId, copiedStartPoint, copiedDirection).WithSourceProperties(target));
                    inverse.Add(CadOperationPayloadCodec.DeleteRay(newId, copiedStartPoint, copiedDirection));
                    break;
                }
                case Ellipse ellipse:
                {
                    var copiedCenter = CadGeometryTransform.Translate(ellipse.Center, delta);
                    forward.Add(CadOperationPayloadCodec.CreateEllipse(
                            newId,
                            copiedCenter,
                            ellipse.MajorAxisEndPoint,
                            ellipse.RadiusRatio,
                            ellipse.StartParameter,
                            ellipse.EndParameter,
                            ellipse.Normal)
                        .WithSourceProperties(target));
                    inverse.Add(CadOperationPayloadCodec.DeleteEllipse(
                        newId,
                        copiedCenter,
                        ellipse.MajorAxisEndPoint,
                        ellipse.RadiusRatio,
                        ellipse.StartParameter,
                        ellipse.EndParameter,
                        ellipse.Normal));
                    break;
                }
                case Spline spline:
                {
                    var fitPoints = spline.FitPoints.Select(point => CadGeometryTransform.Translate(point, delta)).ToArray();
                    var controlPoints = spline.ControlPoints.Select(point => CadGeometryTransform.Translate(point, delta)).ToArray();
                    forward.Add(CadOperationPayloadCodec.CreateSpline(
                            newId,
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
                        .WithSourceProperties(target));
                    inverse.Add(CadOperationPayloadCodec.DeleteSpline(
                        newId,
                        spline.Degree,
                        spline.IsClosed,
                        spline.IsPeriodic,
                        fitPoints,
                        controlPoints,
                        spline.Knots.ToArray(),
                        spline.Weights.ToArray(),
                        spline.StartTangent,
                        spline.EndTangent,
                        spline.Normal));
                    break;
                }
                case TextEntity text:
                {
                    var copiedInsertPoint = CadGeometryTransform.Translate(text.InsertPoint, delta);
                    var copiedAlignmentPoint = CadGeometryTransform.Translate(text.AlignmentPoint, delta);
                    forward.Add(CadOperationPayloadCodec.CreateText(
                            newId,
                            copiedInsertPoint,
                            copiedAlignmentPoint,
                            text.Height,
                            text.Rotation,
                            text.Value,
                            text.Normal)
                        .WithSourceProperties(target));
                    inverse.Add(CadOperationPayloadCodec.DeleteText(
                        newId,
                        copiedInsertPoint,
                        copiedAlignmentPoint,
                        text.Height,
                        text.Rotation,
                        text.Value,
                        text.Normal));
                    break;
                }
                case MText mtext:
                {
                    var copiedInsertPoint = CadGeometryTransform.Translate(mtext.InsertPoint, delta);
                    var copiedTextDirection = CadGeometryTransform.NormalizeDirection(mtext.AlignmentPoint);
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
                    break;
                }
                case Hatch hatch:
                {
                    if (!CadHatchGeometry.TryGetLoops(hatch, out var loops, out var loopError))
                    {
                        return ValueTask.FromResult(CadCommandResult.Fail($"COPY could not copy HATCH: {loopError}"));
                    }

                    var copiedLoops = CadHatchGeometry.TranslateLoops(loops, delta);
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
                    break;
                }
                case Insert insert:
                {
                    if (insert.Block is null)
                    {
                        return ValueTask.FromResult(CadCommandResult.Fail("COPY cannot duplicate INSERT without a block reference."));
                    }

                    var copiedInsertPoint = CadGeometryTransform.Translate(insert.InsertPoint, delta);
                    forward.Add(CadOperationPayloadCodec.CreateInsert(
                            newId,
                            insert.Block.Name,
                            copiedInsertPoint,
                            insert.XScale,
                            insert.YScale,
                            insert.ZScale,
                            insert.Rotation,
                            insert.Normal)
                        .WithSourceProperties(target));
                    inverse.Add(CadOperationPayloadCodec.DeleteInsert(
                        newId,
                        insert.Block.Name,
                        copiedInsertPoint,
                        insert.XScale,
                        insert.YScale,
                        insert.ZScale,
                        insert.Rotation,
                        insert.Normal));
                    break;
                }
                default:
                    return ValueTask.FromResult(CadCommandResult.Fail($"COPY does not support entity type '{target.GetType().Name}' yet."));
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

        return ValueTask.FromResult(CadCommandResult.Ok($"Copied {forward.Count} entity(s).", forward));
    }
}
