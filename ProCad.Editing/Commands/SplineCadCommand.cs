using ProCad.Editing.Identifiers;
using ProCad.Editing.Operations;
using ProCad.Editing.Selection;
using ACadSharp.Entities;
using CSMath;

namespace ProCad.Editing.Commands;

public sealed class SplineCadCommand : ICadCommandHandler
{
    public string Name => "SPLINE";
    public IReadOnlyList<string> Aliases => ["SPL"];

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

        if (!CadCommandParsing.TryParseSplineArguments(context.Arguments, out var fitPoints, out var isClosed, out var parseError))
        {
            return ValueTask.FromResult(CadCommandResult.Fail(parseError!));
        }

        var prototype = new Spline
        {
            Degree = 3,
            IsClosed = isClosed,
            KnotParametrization = KnotParametrization.Chord,
            Normal = XYZ.AxisZ
        };

        foreach (var point in fitPoints)
        {
            prototype.FitPoints.Add(point);
        }

        if (!prototype.UpdateFromFitPoints())
        {
            return ValueTask.FromResult(CadCommandResult.Fail("SPLINE failed to build a valid curve from fit points."));
        }

        var id = CadEntityId.New();
        var fitPointsSnapshot = prototype.FitPoints.ToArray();
        var controlPointsSnapshot = prototype.ControlPoints.ToArray();
        var knotsSnapshot = prototype.Knots.ToArray();
        var weightsSnapshot = prototype.Weights.ToArray();

        var forwardOperations = new[]
        {
            CadOperationPayloadCodec.CreateSpline(
                id,
                prototype.Degree,
                prototype.IsClosed,
                prototype.IsPeriodic,
                fitPointsSnapshot,
                controlPointsSnapshot,
                knotsSnapshot,
                weightsSnapshot,
                prototype.StartTangent,
                prototype.EndTangent,
                prototype.Normal)
                .WithCurrentProperties(session.Document)
        };

        var inverseOperations = new[]
        {
            CadOperationPayloadCodec.DeleteSpline(
                id,
                prototype.Degree,
                prototype.IsClosed,
                prototype.IsPeriodic,
                fitPointsSnapshot,
                controlPointsSnapshot,
                knotsSnapshot,
                weightsSnapshot,
                prototype.StartTangent,
                prototype.EndTangent,
                prototype.Normal)
        };

        var actorId = session.SessionId.Value;
        var forward = session.NextBatch(actorId, forwardOperations);
        var inverse = session.NextBatch(actorId, inverseOperations);
        session.Apply(forward);
        session.UndoRedo.Record(forward, inverse);

        if (session.EntityIndex.TryGetEntity(id, out var created))
        {
            session.SetSelection([created], CadSelectionMode.Replace);
        }

        return ValueTask.FromResult(CadCommandResult.Ok($"Created SPLINE [{id}] with {fitPoints.Count} fit point(s).", forwardOperations));
    }
}
