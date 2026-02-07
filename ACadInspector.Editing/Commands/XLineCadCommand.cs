using ACadInspector.Editing.Identifiers;
using ACadInspector.Editing.Operations;
using ACadInspector.Editing.Selection;

namespace ACadInspector.Editing.Commands;

public sealed class XLineCadCommand : ICadCommandHandler
{
    public string Name => "XLINE";
    public IReadOnlyList<string> Aliases => ["XL"];

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

        if (!CadCommandParsing.TryParseXLineArguments(context.Arguments, out var firstPoint, out var direction, out var parseError))
        {
            return ValueTask.FromResult(CadCommandResult.Fail(parseError!));
        }

        var id = CadEntityId.New();
        var forwardOperations = new[]
        {
            CadOperationPayloadCodec.CreateXLine(id, firstPoint, direction).WithCurrentProperties(session.Document)
        };
        var inverseOperations = new[] { CadOperationPayloadCodec.DeleteXLine(id, firstPoint, direction) };

        var actorId = session.SessionId.Value;
        var forward = session.NextBatch(actorId, forwardOperations);
        var inverse = session.NextBatch(actorId, inverseOperations);

        session.Apply(forward);
        session.UndoRedo.Record(forward, inverse);

        if (session.EntityIndex.TryGetEntity(id, out var created))
        {
            session.SetSelection([created], CadSelectionMode.Replace);
        }

        return ValueTask.FromResult(CadCommandResult.Ok($"Created XLINE [{id}].", forwardOperations));
    }
}
