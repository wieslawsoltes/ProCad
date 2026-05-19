using ProCad.Editing.Identifiers;
using ProCad.Editing.Operations;
using ProCad.Editing.Selection;
using ACadSharp.Entities;
using CSMath;

namespace ProCad.Editing.Commands;

public sealed class InsertCadCommand : ICadCommandHandler
{
    public string Name => "INSERT";
    public IReadOnlyList<string> Aliases => ["I", "-INSERT"];

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

        if (!CadCommandParsing.TryParseInsertArguments(
                context.Arguments,
                out var blockName,
                out var insertPoint,
                out var scale,
                out var rotation,
                out var parseError))
        {
            return ValueTask.FromResult(CadCommandResult.Fail(parseError!));
        }

        if (session.Document.BlockRecords is null ||
            !session.Document.BlockRecords.TryGetValue(blockName, out var block))
        {
            return ValueTask.FromResult(CadCommandResult.Fail($"INSERT could not find block '{blockName}'."));
        }

        var id = CadEntityId.New();
        var normal = XYZ.AxisZ;

        var forwardOperations = new[]
        {
            CadOperationPayloadCodec.CreateInsert(
                    id,
                    block.Name,
                    insertPoint,
                    scale,
                    scale,
                    1.0,
                    rotation,
                    normal)
                .WithCurrentProperties(session.Document)
        };
        var inverseOperations = new[]
        {
            CadOperationPayloadCodec.DeleteInsert(
                id,
                block.Name,
                insertPoint,
                scale,
                scale,
                1.0,
                rotation,
                normal)
        };

        var actorId = session.SessionId.Value;
        var forwardBatch = session.NextBatch(actorId, forwardOperations);
        var inverseBatch = session.NextBatch(actorId, inverseOperations);
        session.Apply(forwardBatch);
        session.UndoRedo.Record(forwardBatch, inverseBatch);

        if (session.EntityIndex.TryGetEntity(id, out var created))
        {
            session.SetSelection([created], CadSelectionMode.Replace);
        }

        return ValueTask.FromResult(CadCommandResult.Ok($"Created INSERT [{id}] from '{block.Name}'.", forwardOperations));
    }
}
