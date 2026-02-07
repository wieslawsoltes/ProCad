using ACadInspector.Editing.Operations;
using ACadInspector.Editing.Selection;
using ACadSharp.Entities;

namespace ACadInspector.Editing.Commands;

public sealed class EraseCadCommand : ICadCommandHandler
{
    public string Name => "ERASE";
    public IReadOnlyList<string> Aliases => ["E"];

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

        if (!CadCommandTargetResolver.TryResolve(session, context.Arguments, out var targets, out var targetError))
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
                        return ValueTask.FromResult(CadCommandResult.Fail($"ERASE could not remove HATCH: {loopError}"));
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
                        return ValueTask.FromResult(CadCommandResult.Fail("ERASE cannot remove INSERT without a block reference."));
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
                    return ValueTask.FromResult(CadCommandResult.Fail($"ERASE does not support entity type '{target.GetType().Name}' yet."));
            }
        }

        var actorId = session.SessionId.Value;
        var forwardBatch = session.NextBatch(actorId, forward);
        var inverseBatch = session.NextBatch(actorId, inverse.AsEnumerable().Reverse().ToArray());

        session.Apply(forwardBatch);
        session.UndoRedo.Record(forwardBatch, inverseBatch);
        session.SetSelection(Array.Empty<object?>(), CadSelectionMode.Replace);

        return ValueTask.FromResult(CadCommandResult.Ok($"Erased {forward.Count} entity(s).", forward));
    }
}
