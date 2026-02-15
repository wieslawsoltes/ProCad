using ACadInspector.Editing.Operations;
using ACadSharp.Entities;
using CSMath;

namespace ACadInspector.Editing.Commands;

public sealed class TrimCadCommand : ICadCommandHandler
{
    public string Name => "TRIM";
    public IReadOnlyList<string> Aliases => ["TR"];

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

        if (!CadCommandParsing.TryParseTrimExtendArguments(context.Arguments, out var boundaryHandle, out var targetHandle, out var endpoint, out var parseError))
        {
            return ValueTask.FromResult(CadCommandResult.Fail(parseError!));
        }

        if (!session.EntityIndex.TryGetByHandle(boundaryHandle, out var boundary, out _) ||
            boundary is not Entity boundaryEntity)
        {
            return ValueTask.FromResult(CadCommandResult.Fail($"Boundary handle '{boundaryHandle:X}' was not found."));
        }

        if (!session.EntityIndex.TryGetByHandle(targetHandle, out var target, out _) ||
            target is not Entity targetEntity)
        {
            return ValueTask.FromResult(CadCommandResult.Fail($"Target handle '{targetHandle:X}' was not found."));
        }

        if (targetEntity is Line targetLine)
        {
            if (!CadTrimExtendGeometry.TryComputeTrimmedLine(targetLine, boundaryEntity, endpoint, out var newStart, out var newEnd, out var geometryError))
            {
                return ValueTask.FromResult(CadCommandResult.Fail(geometryError!));
            }

            if (!session.EntityIndex.TryGetId(targetLine, out var targetId))
            {
                targetId = session.EntityIndex.Register(targetLine);
            }

            var forwardOperation = CadOperationPayloadCodec.TransformLine(targetId, targetLine.StartPoint, targetLine.EndPoint, newStart, newEnd);
            var inverseOperation = CadOperationPayloadCodec.TransformLine(targetId, newStart, newEnd, targetLine.StartPoint, targetLine.EndPoint);

            var actorId = session.SessionId.Value;
            var forwardBatch = session.NextBatch(actorId, [forwardOperation]);
            var inverseBatch = session.NextBatch(actorId, [inverseOperation]);

            session.Apply(forwardBatch);
            session.UndoRedo.Record(forwardBatch, inverseBatch);

            return ValueTask.FromResult(CadCommandResult.Ok("Trim completed.", [forwardOperation]));
        }

        if (targetEntity is not LwPolyline targetPolyline)
        {
            return ValueTask.FromResult(CadCommandResult.Fail(
                $"Target handle '{targetHandle:X}' must resolve to a LINE or open LWPOLYLINE."));
        }

        if (targetPolyline.IsClosed)
        {
            return ValueTask.FromResult(CadCommandResult.Fail("TRIM target polyline must be open."));
        }

        if (!CadPolylineEditValidation.TryValidateLinearPolyline(targetPolyline, "TRIM", out var unsupportedError))
        {
            return ValueTask.FromResult(CadCommandResult.Fail(unsupportedError!));
        }

        var fromVertices = CadGeometryTransform.ToVertices(targetPolyline);
        if (fromVertices.Count < 2)
        {
            return ValueTask.FromResult(CadCommandResult.Fail("TRIM target polyline must have at least two vertices."));
        }

        if (!TryTrimPolylineEndpoint(fromVertices, boundaryEntity, endpoint, out var toVertices, out var polylineError))
        {
            return ValueTask.FromResult(CadCommandResult.Fail(polylineError!));
        }

        if (!session.EntityIndex.TryGetId(targetPolyline, out var polylineId))
        {
            polylineId = session.EntityIndex.Register(targetPolyline);
        }

        var forwardPolyline = CadOperationPayloadCodec.TransformLwPolyline(
            polylineId,
            fromVertices,
            fromClosed: false,
            toVertices,
            toClosed: false);
        var inversePolyline = CadOperationPayloadCodec.TransformLwPolyline(
            polylineId,
            toVertices,
            fromClosed: false,
            fromVertices,
            toClosed: false);

        var polylineActorId = session.SessionId.Value;
        var polylineForwardBatch = session.NextBatch(polylineActorId, [forwardPolyline]);
        var polylineInverseBatch = session.NextBatch(polylineActorId, [inversePolyline]);

        session.Apply(polylineForwardBatch);
        session.UndoRedo.Record(polylineForwardBatch, polylineInverseBatch);

        return ValueTask.FromResult(CadCommandResult.Ok("Trim completed.", [forwardPolyline]));
    }

    private static bool TryTrimPolylineEndpoint(
        IReadOnlyList<XYZ> fromVertices,
        Entity boundaryEntity,
        CadLineEndpoint endpoint,
        out IReadOnlyList<XYZ> toVertices,
        out string? error)
    {
        error = null;
        toVertices = Array.Empty<XYZ>();

        var vertices = fromVertices.ToArray();
        var endpointIndex = endpoint == CadLineEndpoint.End ? vertices.Length - 1 : 0;
        var anchorIndex = endpoint == CadLineEndpoint.End ? vertices.Length - 2 : 1;
        var targetSegment = new Line
        {
            StartPoint = vertices[anchorIndex],
            EndPoint = vertices[endpointIndex]
        };

        if (!CadTrimExtendGeometry.TryComputeTrimmedLine(
                targetSegment,
                boundaryEntity,
                endpoint: CadLineEndpoint.End,
                out _,
                out var trimmedEndpoint,
                out error))
        {
            return false;
        }

        vertices[endpointIndex] = trimmedEndpoint;
        toVertices = vertices;
        return true;
    }
}
