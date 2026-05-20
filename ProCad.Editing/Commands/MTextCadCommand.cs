using ProCad.Editing.Identifiers;
using ProCad.Editing.Operations;
using ProCad.Editing.Selection;
using CSMath;

namespace ProCad.Editing.Commands;

public sealed class MTextCadCommand : ICadCommandHandler
{
    public string Name => "MTEXT";
    public IReadOnlyList<string> Aliases => ["MT"];

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

        if (!CadCommandParsing.TryParseMTextArguments(
                context.Arguments,
                out var insertPoint,
                out var height,
                out var width,
                out var rotation,
                out var value,
                out var parseError))
        {
            return ValueTask.FromResult(CadCommandResult.Fail(parseError!));
        }

        var id = CadEntityId.New();
        var textDirection = new XYZ(Math.Cos(rotation), Math.Sin(rotation), 0.0);
        var normal = XYZ.AxisZ;

        var forwardOperations = new[]
        {
            CadOperationPayloadCodec.CreateMText(
                id,
                insertPoint,
                textDirection,
                height,
                width,
                value,
                normal)
                .WithCurrentProperties(session.Document)
        };
        var inverseOperations = new[]
        {
            CadOperationPayloadCodec.DeleteMText(
                id,
                insertPoint,
                textDirection,
                height,
                width,
                value,
                normal)
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

        return ValueTask.FromResult(CadCommandResult.Ok($"Created MTEXT [{id}].", forwardOperations));
    }
}
