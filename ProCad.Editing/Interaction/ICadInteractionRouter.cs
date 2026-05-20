using System.Numerics;
using ProCad.Editing.Prompt;
using ProCad.Editing.Sessions;

namespace ProCad.Editing.Interaction;

public readonly record struct CadInteractionContext(
    ICadEditorSession? Session,
    string? ActiveCommand);

public sealed record CadToolVisualHint(
    string Kind,
    Vector2 Anchor,
    Vector2? SecondaryAnchor,
    string? Text,
    string? Color = null,
    Vector2? TertiaryAnchor = null,
    float? Scalar = null);

public sealed record CadToolVisualSnapshot(
    bool Handled,
    string? Prompt,
    string? Status,
    IReadOnlyList<CadToolVisualHint> Hints)
{
    public static readonly CadToolVisualSnapshot Empty = new(
        Handled: false,
        Prompt: null,
        Status: null,
        Hints: Array.Empty<CadToolVisualHint>());
}

public interface ICadInteractionRouter
{
    ValueTask<CadToolVisualSnapshot> RouteAsync(
        CadInteractionEvent interactionEvent,
        CadInteractionContext context,
        CancellationToken cancellationToken = default);
}

public interface ICadEditorTool
{
    string Id { get; }
    string DisplayName { get; }

    CadToolVisualSnapshot HandleInteraction(in CadInteractionEvent interactionEvent, in CadInteractionContext context);
}

public interface ICadInteractiveCommandAdapter
{
    string CommandName { get; }
    ValueTask<CadPromptResolution> SubmitAsync(
        ICadCommandRuntime runtime,
        CadPromptToken token,
        ICadEditorSession? session,
        bool commit = false,
        CancellationToken cancellationToken = default);
}

public sealed record CadInteractiveCommandPreview(
    string CommandName,
    string? Prompt,
    string? Status,
    IReadOnlyList<CadToolVisualHint> Hints);

public interface ICadInteractiveCommandPreviewProvider
{
    bool TryBuildPreview(
        ICadEditorSession? session,
        Vector2 cursorPoint,
        string? fallbackPrompt,
        string? fallbackStatus,
        out CadInteractiveCommandPreview preview);

    void ResetPreview(ICadEditorSession? session);
}
