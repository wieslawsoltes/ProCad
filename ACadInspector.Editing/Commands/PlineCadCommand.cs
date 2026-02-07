using ACadInspector.Editing.Identifiers;
using ACadInspector.Editing.Operations;
using ACadInspector.Editing.Selection;

namespace ACadInspector.Editing.Commands;

public sealed class PlineCadCommand : ICadCommandHandler
{
    public string Name => "PLINE";
    public IReadOnlyList<string> Aliases => ["PL", "POLYLINE"];

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

        if (!CadCommandParsing.TryParsePolylineArguments(context.Arguments, out var vertices, out var isClosed, out var parseError))
        {
            return ValueTask.FromResult(CadCommandResult.Fail(parseError!));
        }

        var id = CadEntityId.New();
        var forwardOperations = new[]
        {
            CadOperationPayloadCodec.CreateLwPolyline(id, vertices, isClosed).WithCurrentProperties(session.Document)
        };
        var inverseOperations = new[] { CadOperationPayloadCodec.DeleteLwPolyline(id, vertices, isClosed) };

        var actorId = session.SessionId.Value;
        var forward = session.NextBatch(actorId, forwardOperations);
        var inverse = session.NextBatch(actorId, inverseOperations);

        session.Apply(forward);
        session.UndoRedo.Record(forward, inverse);

        if (session.EntityIndex.TryGetEntity(id, out var created))
        {
            session.SetSelection([created], CadSelectionMode.Replace);
        }

        return ValueTask.FromResult(CadCommandResult.Ok($"Created PLINE [{id}] with {vertices.Count} vertices.", forwardOperations));
    }
}
