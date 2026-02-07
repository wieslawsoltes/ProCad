using ACadInspector.Editing.Identifiers;
using ACadInspector.Editing.Operations;
using ACadInspector.Editing.Selection;
using CSMath;

namespace ACadInspector.Editing.Commands;

public sealed class EllipseCadCommand : ICadCommandHandler
{
    public string Name => "ELLIPSE";
    public IReadOnlyList<string> Aliases => ["EL"];

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

        if (!CadCommandParsing.TryParseEllipseArguments(
                context.Arguments,
                out var center,
                out var majorAxisEndPoint,
                out var radiusRatio,
                out var startParameter,
                out var endParameter,
                out var parseError))
        {
            return ValueTask.FromResult(CadCommandResult.Fail(parseError!));
        }

        var id = CadEntityId.New();
        var normal = XYZ.AxisZ;
        var forwardOperations = new[]
        {
            CadOperationPayloadCodec.CreateEllipse(
                    id,
                    center,
                    majorAxisEndPoint,
                    radiusRatio,
                    startParameter,
                    endParameter,
                    normal)
                .WithCurrentProperties(session.Document)
        };

        var inverseOperations = new[]
        {
            CadOperationPayloadCodec.DeleteEllipse(
                id,
                center,
                majorAxisEndPoint,
                radiusRatio,
                startParameter,
                endParameter,
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

        return ValueTask.FromResult(CadCommandResult.Ok($"Created ELLIPSE [{id}].", forwardOperations));
    }
}
