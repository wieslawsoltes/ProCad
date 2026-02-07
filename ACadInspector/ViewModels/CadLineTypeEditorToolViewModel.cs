using System;

namespace ACadInspector.ViewModels;

public sealed class CadLineTypeEditorToolViewModel : CadToolViewModelBase
{
    public CadLineTypeEditorToolViewModel(CadLineTypeToolViewModel lineTypeTool)
    {
        Source = lineTypeTool ?? throw new ArgumentNullException(nameof(lineTypeTool));
    }

    public CadLineTypeToolViewModel Source { get; }
}
