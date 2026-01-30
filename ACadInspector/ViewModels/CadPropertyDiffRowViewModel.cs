using ACadInspector.Core;

namespace ACadInspector.ViewModels;

public sealed class CadPropertyDiffRowViewModel
{
    public CadPropertyDiffRowViewModel(CadPropertyDiff diff)
    {
        Property = diff.Name;
        LeftValue = diff.LeftValue;
        RightValue = diff.RightValue;
    }

    public string Property { get; }

    public string LeftValue { get; }

    public string RightValue { get; }
}
