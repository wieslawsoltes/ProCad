using ACadInspector.Editing.Clipboard;
using ACadSharp.Entities;
using CSMath;

namespace ACadInspector.Editing.Commands;

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

    public ValueTask<CadCommandResult> ExecuteAsync(CadCommandContext context)
    {
        if (!CadCommandSessionHelper.TryGetSession(context, out var session, out var error))
        {
            return ValueTask.FromResult(error);
        }

        if (!CadCommandTargetResolver.TryResolve(session, context.Arguments, out var targets, out var targetError))
        {
            return ValueTask.FromResult(CadCommandResult.Fail(targetError!));
        }

        var clipboardEntities = new List<CadClipboardEntity>(targets.Count);
        XYZ? basePoint = null;
        foreach (var target in targets)
        {
            if (target is not Entity entity)
            {
                return ValueTask.FromResult(CadCommandResult.Fail("COPYCLIP target is not a CAD entity."));
            }

            if (!CadClipboardEntityCodec.TryEncode(entity, out var clipboardEntity, out var encodeError))
            {
                return ValueTask.FromResult(CadCommandResult.Fail(encodeError!));
            }

            clipboardEntities.Add(clipboardEntity);
            basePoint ??= clipboardEntity.ReferencePoint;
        }

        _clipboardService.SetPayload(new CadClipboardPayload(
            clipboardEntities,
            basePoint ?? XYZ.Zero));

        return ValueTask.FromResult(CadCommandResult.Ok($"Copied {clipboardEntities.Count} entity(s) to clipboard."));
    }
}
