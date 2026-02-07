using ACadInspector.Editing.Identifiers;
using ACadInspector.Editing.Operations;
using ACadInspector.Editing.Selection;

namespace ACadInspector.Editing.Commands;

public sealed class LineCadCommand : ICadCommandHandler
{
    public string Name => "LINE";
    public IReadOnlyList<string> Aliases => ["L"];

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

        if (!CadCommandParsing.TryParseLineArguments(context.Arguments, out var start, out var end, out var parseError))
        {
            return ValueTask.FromResult(CadCommandResult.Fail(parseError!));
        }

        var id = CadEntityId.New();
        var forwardOperations = new[]
        {
            CadOperationPayloadCodec.CreateLine(id, start, end).WithCurrentProperties(session.Document)
        };
        var inverseOperations = new[] { CadOperationPayloadCodec.DeleteLine(id, start, end) };

        var actorId = session.SessionId.Value;
        var forward = session.NextBatch(actorId, forwardOperations);
        var inverse = session.NextBatch(actorId, inverseOperations);

        session.Apply(forward);
        session.UndoRedo.Record(forward, inverse);

        if (session.EntityIndex.TryGetEntity(id, out var created))
        {
            session.SetSelection([created], CadSelectionMode.Replace);
        }

        return ValueTask.FromResult(CadCommandResult.Ok($"Created LINE [{id}].", forwardOperations));
    }
}
