using System;

namespace ACadInspector.ViewModels;

public sealed class CadTextStyleEditorToolViewModel : CadToolViewModelBase
{
    public CadTextStyleEditorToolViewModel(CadTextStyleToolViewModel textStyleTool)
    {
        Source = textStyleTool ?? throw new ArgumentNullException(nameof(textStyleTool));
    }

    public CadTextStyleToolViewModel Source { get; }
}
