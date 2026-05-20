using ProCad.Editing.Identifiers;
using ProCad.Editing.Operations;
using ProCad.Editing.Selection;
using ACadSharp.Entities;
using CSMath;

namespace ProCad.Editing.Commands;

public sealed class TextCadCommand : ICadCommandHandler
{
    public string Name => "TEXT";
    public IReadOnlyList<string> Aliases => ["DT"];

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

        if (!CadCommandParsing.TryParseTextArguments(
                context.Arguments,
                out var insertPoint,
                out var height,
                out var rotation,
                out var value,
                out var parseError))
        {
            return ValueTask.FromResult(CadCommandResult.Fail(parseError!));
        }

        var id = CadEntityId.New();
        var alignmentPoint = insertPoint;
        var normal = XYZ.AxisZ;

        var forwardOperations = new[]
        {
            CadOperationPayloadCodec.CreateText(
                id,
                insertPoint,
                alignmentPoint,
                height,
                rotation,
                value,
                normal)
                .WithCurrentProperties(session.Document)
        };
        var inverseOperations = new[]
        {
            CadOperationPayloadCodec.DeleteText(
                id,
                insertPoint,
                alignmentPoint,
                height,
                rotation,
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

        return ValueTask.FromResult(CadCommandResult.Ok($"Created TEXT [{id}].", forwardOperations));
    }
}
