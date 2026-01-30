using ReactiveUI;
using ReactiveUI.SourceGenerators;

namespace ACadInspector.Services;

public sealed partial class CadScriptWorkspaceService : ReactiveObject
{
    [Reactive]
    public partial string ScriptText { get; set; } = string.Empty;
}
