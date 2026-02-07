using ACadInspector.Editing.Identifiers;
using ACadInspector.Editing.Operations;
using ACadInspector.Editing.Selection;
using ACadInspector.Editing.Sessions;
using ACadSharp.Entities;

namespace ACadInspector.Editing.Commands;

public sealed class FilletCadCommand : ICadCommandHandler
{
    public string Name => "FILLET";
    public IReadOnlyList<string> Aliases => ["F"];

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

        if (!CadCommandParsing.TryParseFilletArguments(context.Arguments, out var radius, out var consumed, out var parseError))
        {
            return ValueTask.FromResult(CadCommandResult.Fail(parseError!));
        }

        var targetTokens = context.Arguments.Skip(consumed).ToArray();
        if (!TryResolveTwoLineTargets(session, targetTokens, out var firstLine, out var secondLine, out var targetError))
        {
            return ValueTask.FromResult(CadCommandResult.Fail(targetError!));
        }

        if (!CadFilletChamferGeometry.TryComputeFillet(firstLine, secondLine, radius, out var geometry, out var geometryError))
        {
            return ValueTask.FromResult(CadCommandResult.Fail(geometryError!));
        }

        var firstId = EnsureId(session, firstLine);
        var secondId = EnsureId(session, secondLine);
        var arcId = CadEntityId.New();

        var forwardOperations = new CadOperation[]
        {
            CadOperationPayloadCodec.TransformLine(firstId, firstLine.StartPoint, firstLine.EndPoint, geometry.FirstNewStart, geometry.FirstNewEnd),
            CadOperationPayloadCodec.TransformLine(secondId, secondLine.StartPoint, secondLine.EndPoint, geometry.SecondNewStart, geometry.SecondNewEnd),
            CadOperationPayloadCodec.CreateArc(arcId, geometry.ArcCenter, geometry.ArcRadius, geometry.ArcStartAngle, geometry.ArcEndAngle)
                .WithSourceProperties(firstLine)
        };

        var inverseOperations = new CadOperation[]
        {
            CadOperationPayloadCodec.DeleteArc(arcId, geometry.ArcCenter, geometry.ArcRadius, geometry.ArcStartAngle, geometry.ArcEndAngle),
            CadOperationPayloadCodec.TransformLine(secondId, geometry.SecondNewStart, geometry.SecondNewEnd, secondLine.StartPoint, secondLine.EndPoint),
            CadOperationPayloadCodec.TransformLine(firstId, geometry.FirstNewStart, geometry.FirstNewEnd, firstLine.StartPoint, firstLine.EndPoint)
        };

        ApplyWithUndo(session, forwardOperations, inverseOperations);

        var selected = new List<object?>(3) { firstLine, secondLine };
        if (session.EntityIndex.TryGetEntity(arcId, out var arcEntity))
        {
            selected.Add(arcEntity);
        }

        session.SetSelection(selected, CadSelectionMode.Replace);
        return ValueTask.FromResult(CadCommandResult.Ok("Fillet completed.", forwardOperations));
    }

    private static bool TryResolveTwoLineTargets(
        CadDocumentSession session,
        IReadOnlyList<string> tokens,
        out Line firstLine,
        out Line secondLine,
        out string? error)
    {
        firstLine = null!;
        secondLine = null!;
        error = null;

        if (!CadCommandTargetResolver.TryResolve(session, tokens, out var targets, out error))
        {
            return false;
        }

        if (targets.Count != 2)
        {
            error = "FILLET requires exactly two target entities.";
            return false;
        }

        if (targets[0] is not Line first || targets[1] is not Line second)
        {
            error = "FILLET currently supports only LINE targets.";
            return false;
        }

        firstLine = first;
        secondLine = second;
        return true;
    }

    private static CadEntityId EnsureId(CadDocumentSession session, Entity entity)
    {
        if (!session.EntityIndex.TryGetId(entity, out var id))
        {
            id = session.EntityIndex.Register(entity);
        }

        return id;
    }

    private static void ApplyWithUndo(
        CadDocumentSession session,
        IReadOnlyList<CadOperation> forwardOperations,
        IReadOnlyList<CadOperation> inverseOperations)
    {
        var actorId = session.SessionId.Value;
        var forwardBatch = session.NextBatch(actorId, forwardOperations);
        var inverseBatch = session.NextBatch(actorId, inverseOperations);
        session.Apply(forwardBatch);
        session.UndoRedo.Record(forwardBatch, inverseBatch);
    }
}
