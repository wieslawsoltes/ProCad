using System.Globalization;
using ACadSharp.Tables;
using Avalonia.Media.Imaging;
using ReactiveUI.SourceGenerators;

namespace ACadInspector.ViewModels;

public sealed partial class CadLineTypeRowViewModel : ViewModelBase
{
    public LineType LineType { get; }
    public string Name { get; }
    public string Handle { get; }
    public string Description { get; }
    public string PatternLength { get; }
    public string SegmentCount { get; }
    public bool IsComplex { get; }
    public bool HasShapes { get; }

    [Reactive]
    public partial Bitmap? Preview { get; set; }

    public CadLineTypeRowViewModel(LineType lineType)
    {
        LineType = lineType;
        Name = lineType.Name;
        Handle = lineType.Handle == 0 ? string.Empty : lineType.Handle.ToString("X", CultureInfo.InvariantCulture);
        Description = lineType.Description ?? string.Empty;
        PatternLength = lineType.PatternLength.ToString("0.###", CultureInfo.InvariantCulture);
        var count = 0;
        foreach (var _ in lineType.Segments)
        {
            count++;
        }
        SegmentCount = count.ToString(CultureInfo.InvariantCulture);
        IsComplex = lineType.IsComplex;
        HasShapes = lineType.HasShapes;
    }
}
