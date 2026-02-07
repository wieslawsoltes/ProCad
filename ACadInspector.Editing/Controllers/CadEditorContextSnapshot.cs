using ACadInspector.Editing.Identifiers;

namespace ACadInspector.Editing.Controllers;

public sealed record CadEditorContextSnapshot(
    CadDocumentSessionId SessionId,
    string Prompt,
    string? ActiveCommand,
    bool IsCommandActive,
    bool CanStartCommands,
    int UndoDepth,
    int RedoDepth,
    string? ParameterHelp = null,
    string? LastMessage = null,
    int ActiveParameterIndex = 0,
    int CompletionCount = 0);

public interface ICadEditorContextSnapshotProvider
{
    CadEditorContextSnapshot Current { get; }
    event EventHandler<CadEditorContextSnapshot>? SnapshotChanged;
}
