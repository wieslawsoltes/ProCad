using ProCad.Editing.Identifiers;
using ProCad.Editing.Operations;
using ProCad.Editing.Selection;
using CSMath;

namespace ProCad.Editing.Commands;

public sealed class RectangCadCommand : ICadCommandHandler
{
    public string Name => "RECTANG";
    public IReadOnlyList<string> Aliases => ["REC", "RECTANGLE"];

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

        if (!CadCommandParsing.TryParseRectangArguments(context.Arguments, out var firstCorner, out var secondCorner, out var parseError))
        {
            return ValueTask.FromResult(CadCommandResult.Fail(parseError!));
        }

        if (Math.Abs(firstCorner.X - secondCorner.X) < double.Epsilon &&
            Math.Abs(firstCorner.Y - secondCorner.Y) < double.Epsilon)
        {
            return ValueTask.FromResult(CadCommandResult.Fail("RECTANG requires two distinct corners."));
        }

        var z = firstCorner.Z;
        var vertices = new[]
        {
            new XYZ(firstCorner.X, firstCorner.Y, z),
            new XYZ(secondCorner.X, firstCorner.Y, z),
            new XYZ(secondCorner.X, secondCorner.Y, z),
            new XYZ(firstCorner.X, secondCorner.Y, z)
        };

        var id = CadEntityId.New();
        var forwardOperations = new[]
        {
            CadOperationPayloadCodec.CreateLwPolyline(id, vertices, isClosed: true).WithCurrentProperties(session.Document)
        };
        var inverseOperations = new[] { CadOperationPayloadCodec.DeleteLwPolyline(id, vertices, isClosed: true) };

        var actorId = session.SessionId.Value;
        var forward = session.NextBatch(actorId, forwardOperations);
        var inverse = session.NextBatch(actorId, inverseOperations);

        session.Apply(forward);
        session.UndoRedo.Record(forward, inverse);

        if (session.EntityIndex.TryGetEntity(id, out var created))
        {
            session.SetSelection([created], CadSelectionMode.Replace);
        }

        return ValueTask.FromResult(CadCommandResult.Ok($"Created RECTANG [{id}].", forwardOperations));
    }
}
