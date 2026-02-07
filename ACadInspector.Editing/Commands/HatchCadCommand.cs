using ACadInspector.Editing.Identifiers;
using ACadInspector.Editing.Operations;
using ACadInspector.Editing.Selection;
using ACadSharp.Entities;
using CSMath;

namespace ACadInspector.Editing.Commands;

public sealed class HatchCadCommand : ICadCommandHandler
{
    public string Name => "HATCH";
    public IReadOnlyList<string> Aliases => ["H"];

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

        if (!CadCommandParsing.TryParseHatchArguments(
                context.Arguments,
                out var patternName,
                out var isSolid,
                out var consumed,
                out var parseError))
        {
            return ValueTask.FromResult(CadCommandResult.Fail(parseError!));
        }

        var targetTokens = context.Arguments.Skip(consumed).ToArray();
        if (!CadCommandTargetResolver.TryResolve(session, targetTokens, out var targets, out var targetError))
        {
            return ValueTask.FromResult(CadCommandResult.Fail(targetError!));
        }

        var forward = new List<CadOperation>(targets.Count);
        var inverse = new List<CadOperation>(targets.Count);
        var createdIds = new List<CadEntityId>(targets.Count);

        foreach (var target in targets)
        {
            if (target is not LwPolyline polyline)
            {
                return ValueTask.FromResult(CadCommandResult.Fail($"HATCH currently supports only closed LWPOLYLINE targets, got '{target.GetType().Name}'."));
            }

            if (!polyline.IsClosed)
            {
                return ValueTask.FromResult(CadCommandResult.Fail("HATCH requires a closed LWPOLYLINE boundary."));
            }

            var vertices = CadGeometryTransform.ToVertices(polyline).ToArray();
            if (vertices.Length < 3)
            {
                return ValueTask.FromResult(CadCommandResult.Fail("HATCH boundary must contain at least three vertices."));
            }

            var loops = new IReadOnlyList<XYZ>[] { vertices };
            var id = CadEntityId.New();
            var normalizedPatternName = isSolid ? "SOLID" : patternName;

            forward.Add(
                CadOperationPayloadCodec.CreateHatch(id, loops, isSolid, normalizedPatternName, XYZ.AxisZ)
                    .WithCurrentProperties(session.Document));
            inverse.Add(CadOperationPayloadCodec.DeleteHatch(id, loops, isSolid, normalizedPatternName, XYZ.AxisZ));
            createdIds.Add(id);
        }

        var actorId = session.SessionId.Value;
        var forwardBatch = session.NextBatch(actorId, forward);
        var inverseBatch = session.NextBatch(actorId, inverse.AsEnumerable().Reverse().ToArray());
        session.Apply(forwardBatch);
        session.UndoRedo.Record(forwardBatch, inverseBatch);

        var createdEntities = new List<object?>(createdIds.Count);
        foreach (var id in createdIds)
        {
            if (session.EntityIndex.TryGetEntity(id, out var entity))
            {
                createdEntities.Add(entity);
            }
        }

        if (createdEntities.Count > 0)
        {
            session.SetSelection(createdEntities, CadSelectionMode.Replace);
        }

        return ValueTask.FromResult(CadCommandResult.Ok($"Created {forward.Count} HATCH entity(s).", forward));
    }
}
