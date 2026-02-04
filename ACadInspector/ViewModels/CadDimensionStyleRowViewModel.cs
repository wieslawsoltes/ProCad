using System.Globalization;
using ACadSharp.Tables;
using Avalonia.Media.Imaging;
using ReactiveUI.SourceGenerators;

namespace ACadInspector.ViewModels;

public sealed partial class CadDimensionStyleRowViewModel : ViewModelBase
{
    public DimensionStyle Style { get; }
    public string Name { get; }
    public string Handle { get; }
    public string TextHeight { get; }
    public string ArrowSize { get; }
    public string DecimalPlaces { get; }
    public string ScaleFactor { get; }
    public string TextStyle { get; }
    public bool AlternateUnits { get; }

    [Reactive]
    public partial Bitmap? Preview { get; set; }

    public CadDimensionStyleRowViewModel(DimensionStyle style)
    {
        Style = style;
        Name = style.Name;
        Handle = style.Handle == 0 ? string.Empty : style.Handle.ToString("X", CultureInfo.InvariantCulture);
        TextHeight = style.TextHeight.ToString("0.###", CultureInfo.InvariantCulture);
        ArrowSize = style.ArrowSize.ToString("0.###", CultureInfo.InvariantCulture);
        DecimalPlaces = style.DecimalPlaces.ToString(CultureInfo.InvariantCulture);
        ScaleFactor = style.ScaleFactor.ToString("0.###", CultureInfo.InvariantCulture);
        TextStyle = style.Style?.Name ?? string.Empty;
        AlternateUnits = style.AlternateUnitDimensioning;
    }
}
