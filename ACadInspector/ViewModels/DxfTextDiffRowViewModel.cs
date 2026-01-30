using System.Globalization;
using ACadInspector.Core;

namespace ACadInspector.ViewModels;

public sealed class DxfTextDiffRowViewModel
{
    public DxfTextDiffRowViewModel(DxfTextDiffLine line)
    {
        Change = line.Kind.ToString();
        LeftLine = line.LeftLineNumber?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;
        RightLine = line.RightLineNumber?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;
        LeftText = line.LeftText;
        RightText = line.RightText;
    }

    public string Change { get; }

    public string LeftLine { get; }

    public string RightLine { get; }

    public string LeftText { get; }

    public string RightText { get; }
}
