using ProCad.Editing.Identifiers;
using ProCad.Editing.Operations;
using ProCad.Editing.Selection;
using CSMath;

namespace ProCad.Editing.Commands;

public sealed class PolygonCadCommand : ICadCommandHandler
{
    public string Name => "POLYGON";
    public IReadOnlyList<string> Aliases => ["POL"];

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

        if (!CadCommandParsing.TryParsePolygonArguments(
                context.Arguments,
                out var sides,
                out var center,
                out var radius,
                out var circumscribed,
                out var parseError))
        {
            return ValueTask.FromResult(CadCommandResult.Fail(parseError!));
        }

        var polygonRadius = radius;
        if (circumscribed)
        {
            polygonRadius = radius / Math.Cos(Math.PI / sides);
        }

        var vertices = new XYZ[sides];
        for (var i = 0; i < sides; i++)
        {
            var angle = (2.0 * Math.PI * i) / sides;
            vertices[i] = new XYZ(
                center.X + polygonRadius * Math.Cos(angle),
                center.Y + polygonRadius * Math.Sin(angle),
                center.Z);
        }

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

        return ValueTask.FromResult(CadCommandResult.Ok($"Created POLYGON [{id}] with {sides} sides.", forwardOperations));
    }
}
