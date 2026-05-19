using ProCad.Editing.Clipboard;
using ACadSharp.Entities;
using CSMath;

namespace ProCad.Editing.Commands;

public sealed class CopyClipCadCommand : ICadCommandHandler
{
    private readonly ICadClipboardService _clipboardService;

    public CopyClipCadCommand(ICadClipboardService clipboardService)
    {
        _clipboardService = clipboardService;
    }

    public string Name => "COPYCLIP";
    public IReadOnlyList<string> Aliases => ["CC"];

    public bool CanExecute(CadCommandContext context)
    {
        return context.Session is not null;
    }

    public async ValueTask<CadCommandResult> ExecuteAsync(CadCommandContext context)
    {
        if (!CadCommandSessionHelper.TryGetSession(context, out var session, out var error))
        {
            return error;
        }

        if (!CadCommandTargetResolver.TryResolve(session, context.Arguments, out var targets, out var targetError))
        {
            return CadCommandResult.Fail(targetError!);
        }

        var clipboardEntities = new List<CadClipboardEntity>(targets.Count);
        var sourceEntities = new List<Entity>(targets.Count);
        XYZ? basePoint = null;
        foreach (var target in targets)
        {
            if (target is not Entity entity)
            {
                return CadCommandResult.Fail("COPYCLIP target is not a CAD entity.");
            }

            if (!CadClipboardEntityCodec.TryEncode(entity, out var clipboardEntity, out var encodeError))
            {
                return CadCommandResult.Fail(encodeError!);
            }

            clipboardEntities.Add(clipboardEntity);
            sourceEntities.Add(entity);
            basePoint ??= clipboardEntity.ReferencePoint;
        }

        var payload = new CadClipboardPayload(
            clipboardEntities,
            basePoint ?? XYZ.Zero,
            Dependencies: CadClipboardDependencyGraphBuilder.Build(session.Document, sourceEntities));
        _clipboardService.SetPayload(payload);
        if (_clipboardService is ICadSystemClipboardSync systemClipboardSync)
        {
            await systemClipboardSync.PublishAsync(payload, context.CancellationToken).ConfigureAwait(false);
        }

        return CadCommandResult.Ok($"Copied {clipboardEntities.Count} entity(s) to clipboard.");
    }
}
