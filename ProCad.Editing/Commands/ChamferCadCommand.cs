using ProCad.Editing.Identifiers;
using ProCad.Editing.Operations;
using ProCad.Editing.Selection;
using ProCad.Editing.Sessions;
using ACadSharp.Entities;
using CSMath;

namespace ProCad.Editing.Commands;

public sealed class ChamferCadCommand : ICadCommandHandler
{
    private const double PolylineAnchorToleranceSquared = 1e-8;

    private readonly record struct ChamferTargetCandidate(
        Entity Entity,
        Line WorkingLine,
        bool IsPolyline,
        IReadOnlyList<XYZ> Vertices,
        int EndpointIndex,
        int AnchorIndex,
        XYZ EndpointPoint,
        XYZ AnchorPoint);

    public string Name => "CHAMFER";
    public IReadOnlyList<string> Aliases => ["CHA"];

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

        if (!CadCommandParsing.TryParseChamferArguments(
                context.Arguments,
                out var firstDistance,
                out var secondDistance,
                out var consumed,
                out var parseError))
        {
            return ValueTask.FromResult(CadCommandResult.Fail(parseError!));
        }

        var targetTokens = context.Arguments.Skip(consumed).ToArray();
        if (!TryResolveTwoTargets(session, targetTokens, out var firstTarget, out var secondTarget, out var targetError))
        {
            return ValueTask.FromResult(CadCommandResult.Fail(targetError!));
        }

        if (!TryBuildCandidates(firstTarget, out var firstCandidates, out var firstCandidateError))
        {
            return ValueTask.FromResult(CadCommandResult.Fail(firstCandidateError!));
        }

        if (!TryBuildCandidates(secondTarget, out var secondCandidates, out var secondCandidateError))
        {
            return ValueTask.FromResult(CadCommandResult.Fail(secondCandidateError!));
        }

        if (!TrySelectBestChamferGeometry(
                firstCandidates,
                secondCandidates,
                firstDistance,
                secondDistance,
                out var firstSelection,
                out var secondSelection,
                out var geometry,
                out var selectedFirstEndpoint,
                out var selectedSecondEndpoint,
                out var geometryError))
        {
            return ValueTask.FromResult(CadCommandResult.Fail(geometryError!));
        }

        var firstId = EnsureId(session, firstTarget);
        var secondId = EnsureId(session, secondTarget);
        var chamferId = CadEntityId.New();

        var forwardOperations = new List<CadOperation>(3);
        var inverseOperations = new List<CadOperation>(3);

        if (!TryCreateTargetTransformOperations(
                firstId,
                firstSelection,
                geometry.FirstNewStart,
                geometry.FirstNewEnd,
                selectedFirstEndpoint,
                out var firstForward,
                out var firstInverse,
                out var firstTransformError))
        {
            return ValueTask.FromResult(CadCommandResult.Fail(firstTransformError!));
        }

        if (!TryCreateTargetTransformOperations(
                secondId,
                secondSelection,
                geometry.SecondNewStart,
                geometry.SecondNewEnd,
                selectedSecondEndpoint,
                out var secondForward,
                out var secondInverse,
                out var secondTransformError))
        {
            return ValueTask.FromResult(CadCommandResult.Fail(secondTransformError!));
        }

        forwardOperations.Add(firstForward);
        forwardOperations.Add(secondForward);
        forwardOperations.Add(CadOperationPayloadCodec.CreateLine(chamferId, geometry.ChamferStart, geometry.ChamferEnd).WithSourceProperties(firstTarget));

        inverseOperations.Add(CadOperationPayloadCodec.DeleteLine(chamferId, geometry.ChamferStart, geometry.ChamferEnd));
        inverseOperations.Add(secondInverse);
        inverseOperations.Add(firstInverse);

        ApplyWithUndo(session, forwardOperations, inverseOperations);

        var selected = new List<object?>(3) { firstTarget, secondTarget };
        if (session.EntityIndex.TryGetEntity(chamferId, out var chamferEntity))
        {
            selected.Add(chamferEntity);
        }

        session.SetSelection(selected, CadSelectionMode.Replace);
        return ValueTask.FromResult(CadCommandResult.Ok("Chamfer completed.", forwardOperations));
    }

    private static bool TryResolveTwoTargets(
        CadDocumentSession session,
        IReadOnlyList<string> tokens,
        out Entity firstTarget,
        out Entity secondTarget,
        out string? error)
    {
        firstTarget = null!;
        secondTarget = null!;
        error = null;

        if (!CadCommandTargetResolver.TryResolve(session, tokens, out var targets, out error))
        {
            return false;
        }

        if (targets.Count != 2)
        {
            error = "CHAMFER requires exactly two target entities.";
            return false;
        }

        firstTarget = targets[0];
        secondTarget = targets[1];
        return true;
    }

    private static bool TryBuildCandidates(
        Entity target,
        out IReadOnlyList<ChamferTargetCandidate> candidates,
        out string? error)
    {
        error = null;
        candidates = Array.Empty<ChamferTargetCandidate>();

        if (target is Line line)
        {
            candidates =
            [
                new ChamferTargetCandidate(
                    Entity: line,
                    WorkingLine: new Line { StartPoint = line.StartPoint, EndPoint = line.EndPoint },
                    IsPolyline: false,
                    Vertices: Array.Empty<XYZ>(),
                    EndpointIndex: -1,
                    AnchorIndex: -1,
                    EndpointPoint: XYZ.Zero,
                    AnchorPoint: XYZ.Zero)
            ];
            return true;
        }

        if (target is not LwPolyline polyline)
        {
            error = $"CHAMFER does not support entity type '{target.GetType().Name}' yet.";
            return false;
        }

        if (polyline.IsClosed)
        {
            error = "CHAMFER currently supports only open LWPOLYLINE targets.";
            return false;
        }

        if (!CadPolylineEditValidation.TryValidateLinearPolyline(polyline, "CHAMFER", out error))
        {
            return false;
        }

        var vertices = CadGeometryTransform.ToVertices(polyline);
        if (vertices.Count < 2)
        {
            error = "CHAMFER target polyline must have at least two vertices.";
            return false;
        }

        var startEndpoint = vertices[0];
        var startAnchor = vertices[1];
        var endEndpoint = vertices[^1];
        var endAnchor = vertices[^2];

        candidates =
        [
            new ChamferTargetCandidate(
                Entity: polyline,
                WorkingLine: new Line { StartPoint = startEndpoint, EndPoint = startAnchor },
                IsPolyline: true,
                Vertices: vertices,
                EndpointIndex: 0,
                AnchorIndex: 1,
                EndpointPoint: startEndpoint,
                AnchorPoint: startAnchor),
            new ChamferTargetCandidate(
                Entity: polyline,
                WorkingLine: new Line { StartPoint = endEndpoint, EndPoint = endAnchor },
                IsPolyline: true,
                Vertices: vertices,
                EndpointIndex: vertices.Count - 1,
                AnchorIndex: vertices.Count - 2,
                EndpointPoint: endEndpoint,
                AnchorPoint: endAnchor)
        ];
        return true;
    }

    private static bool TrySelectBestChamferGeometry(
        IReadOnlyList<ChamferTargetCandidate> firstCandidates,
        IReadOnlyList<ChamferTargetCandidate> secondCandidates,
        double firstDistance,
        double secondDistance,
        out ChamferTargetCandidate selectedFirst,
        out ChamferTargetCandidate selectedSecond,
        out CadChamferGeometry geometry,
        out XYZ selectedFirstEndpoint,
        out XYZ selectedSecondEndpoint,
        out string? error)
    {
        selectedFirst = default;
        selectedSecond = default;
        geometry = default;
        selectedFirstEndpoint = XYZ.Zero;
        selectedSecondEndpoint = XYZ.Zero;
        error = "CHAMFER could not determine compatible target endpoints.";

        var hasResult = false;
        var bestMetric = double.MaxValue;
        string? firstFailure = null;
        foreach (var first in firstCandidates)
        {
            foreach (var second in secondCandidates)
            {
                if (!CadFilletChamferGeometry.TryComputeChamfer(first.WorkingLine, second.WorkingLine, firstDistance, secondDistance, out var candidateGeometry, out var candidateError))
                {
                    firstFailure ??= candidateError;
                    continue;
                }

                if (!TryResolveSelectedEndpoint(first, candidateGeometry.FirstNewStart, candidateGeometry.FirstNewEnd, out var firstEndpoint, out var firstMetric) ||
                    !TryResolveSelectedEndpoint(second, candidateGeometry.SecondNewStart, candidateGeometry.SecondNewEnd, out var secondEndpoint, out var secondMetric))
                {
                    continue;
                }

                var metric = firstMetric + secondMetric;
                if (metric >= bestMetric)
                {
                    continue;
                }

                bestMetric = metric;
                selectedFirst = first;
                selectedSecond = second;
                selectedFirstEndpoint = firstEndpoint;
                selectedSecondEndpoint = secondEndpoint;
                geometry = candidateGeometry;
                hasResult = true;
            }
        }

        if (!hasResult)
        {
            if (!string.IsNullOrWhiteSpace(firstFailure))
            {
                error = firstFailure;
            }

            return false;
        }

        error = null;
        return true;
    }

    private static bool TryResolveSelectedEndpoint(
        ChamferTargetCandidate candidate,
        XYZ updatedStart,
        XYZ updatedEnd,
        out XYZ selectedEndpoint,
        out double metric)
    {
        selectedEndpoint = XYZ.Zero;
        metric = 0.0;
        if (!candidate.IsPolyline)
        {
            return true;
        }

        var startToAnchor = DistanceSquared(updatedStart, candidate.AnchorPoint);
        var endToAnchor = DistanceSquared(updatedEnd, candidate.AnchorPoint);
        if (startToAnchor <= PolylineAnchorToleranceSquared &&
            endToAnchor > PolylineAnchorToleranceSquared)
        {
            selectedEndpoint = updatedEnd;
        }
        else if (endToAnchor <= PolylineAnchorToleranceSquared &&
                 startToAnchor > PolylineAnchorToleranceSquared)
        {
            selectedEndpoint = updatedStart;
        }
        else
        {
            return false;
        }

        if (DistanceSquared(selectedEndpoint, candidate.AnchorPoint) <= PolylineAnchorToleranceSquared)
        {
            return false;
        }

        metric = Math.Sqrt(DistanceSquared(candidate.EndpointPoint, selectedEndpoint));
        return true;
    }

    private static bool TryCreateTargetTransformOperations(
        CadEntityId id,
        ChamferTargetCandidate candidate,
        XYZ updatedStart,
        XYZ updatedEnd,
        XYZ selectedEndpoint,
        out CadOperation forward,
        out CadOperation inverse,
        out string? error)
    {
        error = null;
        forward = null!;
        inverse = null!;

        if (!candidate.IsPolyline &&
            candidate.Entity is Line line)
        {
            forward = CadOperationPayloadCodec.TransformLine(id, line.StartPoint, line.EndPoint, updatedStart, updatedEnd);
            inverse = CadOperationPayloadCodec.TransformLine(id, updatedStart, updatedEnd, line.StartPoint, line.EndPoint);
            return true;
        }

        var fromVertices = candidate.Vertices;
        var toVertices = fromVertices.ToArray();
        toVertices[candidate.EndpointIndex] = selectedEndpoint;
        if (DistanceSquared(toVertices[candidate.EndpointIndex], toVertices[candidate.AnchorIndex]) <= PolylineAnchorToleranceSquared)
        {
            error = "CHAMFER failed because the resulting polyline endpoint is degenerate.";
            return false;
        }

        forward = CadOperationPayloadCodec.TransformLwPolyline(id, fromVertices, fromClosed: false, toVertices, toClosed: false);
        inverse = CadOperationPayloadCodec.TransformLwPolyline(id, toVertices, fromClosed: false, fromVertices, toClosed: false);
        return true;
    }

    private static double DistanceSquared(XYZ a, XYZ b)
    {
        var dx = a.X - b.X;
        var dy = a.Y - b.Y;
        var dz = a.Z - b.Z;
        return (dx * dx) + (dy * dy) + (dz * dz);
    }

    private static CadEntityId EnsureId(CadDocumentSession session, Entity entity)
    {
        if (!session.EntityIndex.TryGetId(entity, out var id))
        {
            id = session.EntityIndex.Register(entity);
        }

        return id;
    }

    private static void ApplyWithUndo(
        CadDocumentSession session,
        IReadOnlyList<CadOperation> forwardOperations,
        IReadOnlyList<CadOperation> inverseOperations)
    {
        var actorId = session.SessionId.Value;
        var forwardBatch = session.NextBatch(actorId, forwardOperations);
        var inverseBatch = session.NextBatch(actorId, inverseOperations);
        session.Apply(forwardBatch);
        session.UndoRedo.Record(forwardBatch, inverseBatch);
    }
}
