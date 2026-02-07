using ACadInspector.Editing.Operations;
using ACadSharp.Entities;

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
            target is not Line targetLine)
        {
            return ValueTask.FromResult(CadCommandResult.Fail($"Target handle '{targetHandle:X}' must resolve to a LINE."));
        }

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
}
