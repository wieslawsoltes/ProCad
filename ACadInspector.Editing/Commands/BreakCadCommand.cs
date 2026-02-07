using ACadInspector.Editing.Identifiers;
using ACadInspector.Editing.Operations;
using ACadInspector.Editing.Selection;
using ACadSharp.Entities;
using CSMath;

namespace ACadInspector.Editing.Commands;

public sealed class BreakCadCommand : ICadCommandHandler
{
    public string Name => "BREAK";
    public IReadOnlyList<string> Aliases => ["BR"];

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

        if (!CadCommandParsing.TryParseBreakArguments(context.Arguments, out var targetHandle, out var breakPoint, out var parseError))
        {
            return ValueTask.FromResult(CadCommandResult.Fail(parseError!));
        }

        if (!session.EntityIndex.TryGetByHandle(targetHandle, out var target, out _) ||
            target is not Line line)
        {
            return ValueTask.FromResult(CadCommandResult.Fail($"BREAK target handle '{targetHandle:X}' must resolve to a LINE."));
        }

        if (!TryValidateBreakPoint(line, breakPoint, out var validationError))
        {
            return ValueTask.FromResult(CadCommandResult.Fail(validationError!));
        }

        if (!session.EntityIndex.TryGetId(line, out var targetId))
        {
            targetId = session.EntityIndex.Register(line);
        }

        var newId = CadEntityId.New();
        var forwardOperations = new CadOperation[]
        {
            CadOperationPayloadCodec.TransformLine(targetId, line.StartPoint, line.EndPoint, line.StartPoint, breakPoint),
            CadOperationPayloadCodec.CreateLine(newId, breakPoint, line.EndPoint).WithSourceProperties(line)
        };

        var inverseOperations = new CadOperation[]
        {
            CadOperationPayloadCodec.DeleteLine(newId, breakPoint, line.EndPoint),
            CadOperationPayloadCodec.TransformLine(targetId, line.StartPoint, breakPoint, line.StartPoint, line.EndPoint)
        };

        var actorId = session.SessionId.Value;
        var forwardBatch = session.NextBatch(actorId, forwardOperations);
        var inverseBatch = session.NextBatch(actorId, inverseOperations);

        session.Apply(forwardBatch);
        session.UndoRedo.Record(forwardBatch, inverseBatch);

        var created = session.EntityIndex.TryGetEntity(newId, out var createdEntity)
            ? createdEntity
            : null;
        session.SetSelection(
            created is null ? [line] : [line, created],
            CadSelectionMode.Replace);

        return ValueTask.FromResult(CadCommandResult.Ok("Break completed.", forwardOperations));
    }

    private static bool TryValidateBreakPoint(Line line, XYZ point, out string? error)
    {
        error = null;
        var dx = line.EndPoint.X - line.StartPoint.X;
        var dy = line.EndPoint.Y - line.StartPoint.Y;
        var lengthSquared = (dx * dx) + (dy * dy);
        if (lengthSquared <= 1e-10)
        {
            error = "Cannot break a zero-length line.";
            return false;
        }

        var px = point.X - line.StartPoint.X;
        var py = point.Y - line.StartPoint.Y;
        var cross = Math.Abs((dx * py) - (dy * px));
        if (cross > 1e-6 * Math.Sqrt(lengthSquared))
        {
            error = "Break point is not on the target line.";
            return false;
        }

        var t = ((px * dx) + (py * dy)) / lengthSquared;
        if (t <= 1e-6 || t >= 1.0 - 1e-6)
        {
            error = "Break point must be between line endpoints.";
            return false;
        }

        return true;
    }
}
