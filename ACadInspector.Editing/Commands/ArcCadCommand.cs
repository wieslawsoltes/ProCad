using ACadInspector.Editing.Identifiers;
using ACadInspector.Editing.Operations;
using ACadInspector.Editing.Selection;

namespace ACadInspector.Editing.Commands;

public sealed class ArcCadCommand : ICadCommandHandler
{
    public string Name => "ARC";
    public IReadOnlyList<string> Aliases => ["A"];

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

        if (!CadCommandParsing.TryParseArcArguments(
                context.Arguments,
                out var center,
                out var radius,
                out var startAngleRadians,
                out var endAngleRadians,
                out var parseError))
        {
            return ValueTask.FromResult(CadCommandResult.Fail(parseError!));
        }

        var id = CadEntityId.New();
        var forwardOperations = new[]
        {
            CadOperationPayloadCodec.CreateArc(id, center, radius, startAngleRadians, endAngleRadians)
                .WithCurrentProperties(session.Document)
        };
        var inverseOperations = new[]
        {
            CadOperationPayloadCodec.DeleteArc(id, center, radius, startAngleRadians, endAngleRadians)
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

        return ValueTask.FromResult(CadCommandResult.Ok($"Created ARC [{id}].", forwardOperations));
    }
}
