using ReactiveUI;
using ReactiveUI.SourceGenerators;

namespace ACadInspector.Services;

public sealed partial class CadScriptWorkspaceService : ReactiveObject
{
    [Reactive]
    public partial string ScriptText { get; set; } = string.Empty;

    [Reactive]
    public partial string CommandScriptText { get; set; } = string.Empty;

    [Reactive]
    public partial string RecordingSavePath { get; set; } = string.Empty;

    [Reactive]
    public partial string CommandStartLine { get; set; } = "1";

    [Reactive]
    public partial string CommandMaxCommands { get; set; } = string.Empty;

    [Reactive]
    public partial string MacroCatalogJson { get; set; } = string.Empty;

    [Reactive]
    public partial string LastMacroName { get; set; } = string.Empty;
}
