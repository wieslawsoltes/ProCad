using System.Globalization;

namespace ACadInspector.ViewModels;

public sealed class CadDiffSummaryRowViewModel
{
    public CadDiffSummaryRowViewModel(string category, int count)
    {
        Category = category;
        CountText = count.ToString(CultureInfo.InvariantCulture);
    }

    public string Category { get; }

    public string CountText { get; }
}
