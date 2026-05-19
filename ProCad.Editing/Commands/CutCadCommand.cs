using ProCad.Editing.Clipboard;
using ProCad.Editing.Operations;
using ProCad.Editing.Selection;
using ACadSharp.Entities;
using CSMath;

namespace ProCad.Editing.Commands;

public sealed class CutCadCommand : ICadCommandHandler
{
    private readonly ICadClipboardService _clipboardService;

    public CutCadCommand(ICadClipboardService clipboardService)
    {
        _clipboardService = clipboardService;
    }

    public string Name => "CUT";
    public IReadOnlyList<string> Aliases => ["CU"];

    public bool CanExecute(CadCommandContext context)
    {
        return context.Session is not null;
    }

    public async ValueTask<CadCommandResult> ExecuteAsync(CadCommandContext context)
    {
        if (!CadCommandSessionHelper.TryGetSession(context, out var session, out var error))
        {
            return error;
        }

        if (!CadCommandTargetResolver.TryResolve(session, context.Arguments, out var targets, out var targetError))
        {
            return CadCommandResult.Fail(targetError!);
        }

        var clipboardEntities = new List<CadClipboardEntity>(targets.Count);
        var sourceEntities = new List<Entity>(targets.Count);
        XYZ? basePoint = null;
        var forward = new List<CadOperation>(targets.Count);
        var inverse = new List<CadOperation>(targets.Count);

        foreach (var target in targets)
        {
            if (!session.EntityIndex.TryGetId(target, out var id))
            {
                id = session.EntityIndex.Register(target);
            }

            if (!CadClipboardEntityCodec.TryEncode(target, out var clipboardEntity, out var encodeError))
            {
                return CadCommandResult.Fail(encodeError!);
            }

            clipboardEntities.Add(clipboardEntity);
            sourceEntities.Add(target);
            basePoint ??= clipboardEntity.ReferencePoint;

            switch (target)
            {
                case Line line:
                    forward.Add(CadOperationPayloadCodec.DeleteLine(id, line.StartPoint, line.EndPoint));
                    inverse.Add(CadOperationPayloadCodec.CreateLine(id, line.StartPoint, line.EndPoint).WithSourceProperties(target));
                    break;
                case Arc arc:
                    forward.Add(CadOperationPayloadCodec.DeleteArc(id, arc.Center, arc.Radius, arc.StartAngle, arc.EndAngle));
                    inverse.Add(CadOperationPayloadCodec.CreateArc(id, arc.Center, arc.Radius, arc.StartAngle, arc.EndAngle).WithSourceProperties(target));
                    break;
                case Circle circle:
                    forward.Add(CadOperationPayloadCodec.DeleteCircle(id, circle.Center, circle.Radius));
                    inverse.Add(CadOperationPayloadCodec.CreateCircle(id, circle.Center, circle.Radius).WithSourceProperties(target));
                    break;
                case LwPolyline polyline:
                {
                    var vertices = CadGeometryTransform.ToVertices(polyline);
                    forward.Add(CadOperationPayloadCodec.DeleteLwPolyline(id, vertices, polyline.IsClosed));
                    inverse.Add(CadOperationPayloadCodec.CreateLwPolyline(id, vertices, polyline.IsClosed).WithSourceProperties(target));
                    break;
                }
                case Point point:
                    forward.Add(CadOperationPayloadCodec.DeletePoint(id, point.Location));
                    inverse.Add(CadOperationPayloadCodec.CreatePoint(id, point.Location).WithSourceProperties(target));
                    break;
                case XLine xline:
                {
                    var direction = CadGeometryTransform.NormalizeDirection(xline.Direction);
                    forward.Add(CadOperationPayloadCodec.DeleteXLine(id, xline.FirstPoint, direction));
                    inverse.Add(CadOperationPayloadCodec.CreateXLine(id, xline.FirstPoint, direction).WithSourceProperties(target));
                    break;
                }
                case Ray ray:
                {
                    var direction = CadGeometryTransform.NormalizeDirection(ray.Direction);
                    forward.Add(CadOperationPayloadCodec.DeleteRay(id, ray.StartPoint, direction));
                    inverse.Add(CadOperationPayloadCodec.CreateRay(id, ray.StartPoint, direction).WithSourceProperties(target));
                    break;
                }
                case Ellipse ellipse:
                    forward.Add(CadOperationPayloadCodec.DeleteEllipse(
                        id,
                        ellipse.Center,
                        ellipse.MajorAxisEndPoint,
                        ellipse.RadiusRatio,
                        ellipse.StartParameter,
                        ellipse.EndParameter,
                        ellipse.Normal));
                    inverse.Add(CadOperationPayloadCodec.CreateEllipse(
                            id,
                            ellipse.Center,
                            ellipse.MajorAxisEndPoint,
                            ellipse.RadiusRatio,
                            ellipse.StartParameter,
                            ellipse.EndParameter,
                            ellipse.Normal)
                        .WithSourceProperties(target));
                    break;
                case Spline spline:
                    forward.Add(CadOperationPayloadCodec.DeleteSpline(
                        id,
                        spline.Degree,
                        spline.IsClosed,
                        spline.IsPeriodic,
                        spline.FitPoints.ToArray(),
                        spline.ControlPoints.ToArray(),
                        spline.Knots.ToArray(),
                        spline.Weights.ToArray(),
                        spline.StartTangent,
                        spline.EndTangent,
                        spline.Normal));
                    inverse.Add(CadOperationPayloadCodec.CreateSpline(
                            id,
                            spline.Degree,
                            spline.IsClosed,
                            spline.IsPeriodic,
                            spline.FitPoints.ToArray(),
                            spline.ControlPoints.ToArray(),
                            spline.Knots.ToArray(),
                            spline.Weights.ToArray(),
                            spline.StartTangent,
                            spline.EndTangent,
                            spline.Normal)
                        .WithSourceProperties(target));
                    break;
                case TextEntity text:
                    forward.Add(CadOperationPayloadCodec.DeleteText(
                        id,
                        text.InsertPoint,
                        text.AlignmentPoint,
                        text.Height,
                        text.Rotation,
                        text.Value,
                        text.Normal));
                    inverse.Add(CadOperationPayloadCodec.CreateText(
                            id,
                            text.InsertPoint,
                            text.AlignmentPoint,
                            text.Height,
                            text.Rotation,
                            text.Value,
                            text.Normal)
                        .WithSourceProperties(target));
                    break;
                case MText mtext:
                    forward.Add(CadOperationPayloadCodec.DeleteMText(
                        id,
                        mtext.InsertPoint,
                        CadGeometryTransform.NormalizeDirection(mtext.AlignmentPoint),
                        mtext.Height,
                        mtext.RectangleWidth,
                        mtext.Value,
                        mtext.Normal));
                    inverse.Add(CadOperationPayloadCodec.CreateMText(
                            id,
                            mtext.InsertPoint,
                            CadGeometryTransform.NormalizeDirection(mtext.AlignmentPoint),
                            mtext.Height,
                            mtext.RectangleWidth,
                            mtext.Value,
                            mtext.Normal)
                        .WithSourceProperties(target));
                    break;
                case Hatch hatch:
                {
                    if (!CadHatchGeometry.TryGetLoops(hatch, out var loops, out var loopError))
                    {
                        return CadCommandResult.Fail($"CUT could not process HATCH: {loopError}");
                    }

                    var patternName = CadHatchGeometry.ResolvePatternName(hatch);
                    forward.Add(CadOperationPayloadCodec.DeleteHatch(id, loops, hatch.IsSolid, patternName, hatch.Normal));
                    inverse.Add(CadOperationPayloadCodec.CreateHatch(
                            id,
                            loops,
                            hatch.IsSolid,
                            patternName,
                            hatch.Normal)
                        .WithSourceProperties(target));
                    break;
                }
                case Insert insert:
                {
                    if (insert.Block is null)
                    {
                        return CadCommandResult.Fail("CUT cannot process INSERT without a block reference.");
                    }

                    forward.Add(CadOperationPayloadCodec.DeleteInsert(
                        id,
                        insert.Block.Name,
                        insert.InsertPoint,
                        insert.XScale,
                        insert.YScale,
                        insert.ZScale,
                        insert.Rotation,
                        insert.Normal));
                    inverse.Add(CadOperationPayloadCodec.CreateInsert(
                            id,
                            insert.Block.Name,
                            insert.InsertPoint,
                            insert.XScale,
                            insert.YScale,
                            insert.ZScale,
                            insert.Rotation,
                            insert.Normal)
                        .WithSourceProperties(target));
                    break;
                }
                default:
                    return CadCommandResult.Fail($"CUT does not support entity type '{target.GetType().Name}' yet.");
            }
        }

        var payload = new CadClipboardPayload(
            clipboardEntities,
            basePoint ?? XYZ.Zero,
            Dependencies: CadClipboardDependencyGraphBuilder.Build(session.Document, sourceEntities));
        _clipboardService.SetPayload(payload);
        if (_clipboardService is ICadSystemClipboardSync systemClipboardSync)
        {
            await systemClipboardSync.PublishAsync(payload, context.CancellationToken).ConfigureAwait(false);
        }

        var actorId = session.SessionId.Value;
        var forwardBatch = session.NextBatch(actorId, forward);
        var inverseBatch = session.NextBatch(actorId, inverse.AsEnumerable().Reverse().ToArray());

        session.Apply(forwardBatch);
        session.UndoRedo.Record(forwardBatch, inverseBatch);
        session.SetSelection(Array.Empty<object?>(), CadSelectionMode.Replace);

        return CadCommandResult.Ok($"Cut {forward.Count} entity(s) to clipboard.", forward);
    }
}
