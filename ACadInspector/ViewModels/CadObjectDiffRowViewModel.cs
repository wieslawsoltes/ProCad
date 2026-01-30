using System.Globalization;
using ACadInspector.Core;

namespace ACadInspector.ViewModels;

public sealed class CadObjectDiffRowViewModel
{
    public CadObjectDiffRowViewModel(CadObjectDiff diff)
    {
        Diff = diff;
        Path = diff.Path;
        Kind = diff.Kind;
        TypeName = diff.TypeName;
        DiffKindText = diff.DiffKind.ToString();
        PropertyDiffCountText = diff.PropertyDiffs.Count.ToString(CultureInfo.InvariantCulture);
    }

    public CadObjectDiff Diff { get; }

    public string Path { get; }

    public string Kind { get; }

    public string TypeName { get; }

    public string DiffKindText { get; }

    public string PropertyDiffCountText { get; }
}
