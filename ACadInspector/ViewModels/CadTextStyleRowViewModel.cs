using System.Globalization;
using ACadSharp.Tables;
using Avalonia.Media.Imaging;
using ReactiveUI.SourceGenerators;

namespace ACadInspector.ViewModels;

public sealed partial class CadTextStyleRowViewModel : ViewModelBase
{
    public TextStyle Style { get; }
    public string Name { get; }
    public string Handle { get; }
    public string Font { get; }
    public string BigFont { get; }
    public string Height { get; }
    public string Width { get; }
    public string ObliqueAngle { get; }
    public bool IsShapeFile { get; }

    [Reactive]
    public partial Bitmap? Preview { get; set; }

    public CadTextStyleRowViewModel(TextStyle style)
    {
        Style = style;
        Name = style.Name;
        Handle = style.Handle == 0 ? string.Empty : style.Handle.ToString("X", CultureInfo.InvariantCulture);
        Font = style.Filename ?? string.Empty;
        BigFont = style.BigFontFilename ?? string.Empty;
        Height = style.Height.ToString("0.###", CultureInfo.InvariantCulture);
        Width = style.Width.ToString("0.###", CultureInfo.InvariantCulture);
        ObliqueAngle = style.ObliqueAngle.ToString("0.###", CultureInfo.InvariantCulture);
        IsShapeFile = style.IsShapeFile;
    }
}
