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
}
