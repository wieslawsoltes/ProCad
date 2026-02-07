using ACadInspector.Editing.Operations;
using ACadSharp.Entities;

namespace ACadInspector.Editing.Commands;

public sealed class MatchPropCadCommand : ICadCommandHandler
{
    public string Name => "MATCHPROP";
    public IReadOnlyList<string> Aliases => ["MA"];

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

        if (!CadCommandParsing.TryParseMatchPropArguments(context.Arguments, out var sourceHandle, out var consumed, out var parseError))
        {
            return ValueTask.FromResult(CadCommandResult.Fail(parseError!));
        }

        if (!session.EntityIndex.TryGetByHandle(sourceHandle, out var sourceEntity, out _) ||
            sourceEntity is not Entity source)
        {
            return ValueTask.FromResult(CadCommandResult.Fail($"Source handle '{sourceHandle:X}' was not found."));
        }

        var targetTokens = context.Arguments.Skip(consumed).ToArray();
        if (!CadCommandTargetResolver.TryResolve(session, targetTokens, out var resolvedTargets, out var targetError))
        {
            return ValueTask.FromResult(CadCommandResult.Fail(targetError!));
        }

        var targets = resolvedTargets
            .OfType<Entity>()
            .Where(target => !ReferenceEquals(target, source))
            .Distinct()
            .ToArray();

        if (targets.Length == 0)
        {
            return ValueTask.FromResult(CadCommandResult.Fail("MATCHPROP requires at least one target entity different from source."));
        }

        var forward = new List<CadOperation>(targets.Length * 6);
        var inverse = new List<CadOperation>(targets.Length * 6);
        foreach (var target in targets)
        {
            if (!session.EntityIndex.TryGetId(target, out var id))
            {
                id = session.EntityIndex.Register(target);
            }

            AppendPropertyChange(
                id,
                CadEntityPropertyCodec.Layer,
                target.Layer.Name,
                source.Layer.Name,
                forward,
                inverse,
                StringComparer.OrdinalIgnoreCase);

            AppendPropertyChange(
                id,
                CadEntityPropertyCodec.LineType,
                target.LineType.Name,
                source.LineType.Name,
                forward,
                inverse,
                StringComparer.OrdinalIgnoreCase);

            AppendPropertyChange(
                id,
                CadEntityPropertyCodec.Color,
                CadEntityPropertyCodec.SerializeColor(target.Color),
                CadEntityPropertyCodec.SerializeColor(source.Color),
                forward,
                inverse,
                StringComparer.Ordinal);

            AppendPropertyChange(
                id,
                CadEntityPropertyCodec.LineWeight,
                CadEntityPropertyCodec.SerializeLineWeight(target.LineWeight),
                CadEntityPropertyCodec.SerializeLineWeight(source.LineWeight),
                forward,
                inverse,
                StringComparer.Ordinal);

            AppendPropertyChange(
                id,
                CadEntityPropertyCodec.LineTypeScale,
                CadEntityPropertyCodec.SerializeLineTypeScale(target.LineTypeScale),
                CadEntityPropertyCodec.SerializeLineTypeScale(source.LineTypeScale),
                forward,
                inverse,
                StringComparer.Ordinal);

            AppendPropertyChange(
                id,
                CadEntityPropertyCodec.IsInvisible,
                CadEntityPropertyCodec.SerializeBoolean(target.IsInvisible),
                CadEntityPropertyCodec.SerializeBoolean(source.IsInvisible),
                forward,
                inverse,
                StringComparer.Ordinal);

            AppendPropertyChange(
                id,
                CadEntityPropertyCodec.Transparency,
                CadEntityPropertyCodec.SerializeTransparency(target.Transparency),
                CadEntityPropertyCodec.SerializeTransparency(source.Transparency),
                forward,
                inverse,
                StringComparer.Ordinal);
        }

        if (forward.Count == 0)
        {
            return ValueTask.FromResult(CadCommandResult.Fail("MATCHPROP found no differing properties to apply."));
        }

        var actorId = session.SessionId.Value;
        var forwardBatch = session.NextBatch(actorId, forward);
        var inverseBatch = session.NextBatch(actorId, inverse.AsEnumerable().Reverse().ToArray());
        session.Apply(forwardBatch);
        session.UndoRedo.Record(forwardBatch, inverseBatch);

        return ValueTask.FromResult(CadCommandResult.Ok($"Matched properties to {targets.Length} entity(s).", forward));
    }

    private static void AppendPropertyChange(
        ACadInspector.Editing.Identifiers.CadEntityId entityId,
        string propertyName,
        string fromValue,
        string toValue,
        ICollection<CadOperation> forward,
        ICollection<CadOperation> inverse,
        StringComparer comparer)
    {
        if (comparer.Equals(fromValue, toValue))
        {
            return;
        }

        forward.Add(CadOperationPayloadCodec.UpdateEntityProperty(entityId, propertyName, fromValue, toValue));
        inverse.Add(CadOperationPayloadCodec.UpdateEntityProperty(entityId, propertyName, toValue, fromValue));
    }
}
