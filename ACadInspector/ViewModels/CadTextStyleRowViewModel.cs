using System;
using System.Globalization;
using ACadSharp.Entities;
using ACadSharp.Tables;
using Avalonia.Media.Imaging;
using ReactiveUI.SourceGenerators;

namespace ACadInspector.ViewModels;

public sealed partial class CadTextStyleRowViewModel : ViewModelBase
{
    public TextStyle Style { get; }

    [Reactive]
    public partial string Name { get; set; } = string.Empty;

    [Reactive]
    public partial string Handle { get; set; } = string.Empty;

    [Reactive]
    public partial string Font { get; set; } = string.Empty;

    [Reactive]
    public partial string BigFont { get; set; } = string.Empty;

    [Reactive]
    public partial string Height { get; set; } = string.Empty;

    [Reactive]
    public partial string Width { get; set; } = string.Empty;

    [Reactive]
    public partial string LastHeight { get; set; } = string.Empty;

    [Reactive]
    public partial string ObliqueAngle { get; set; } = string.Empty;

    [Reactive]
    public partial bool IsShapeFile { get; set; }

    [Reactive]
    public partial bool IsVertical { get; set; }

    [Reactive]
    public partial bool IsMirrorBackward { get; set; }

    [Reactive]
    public partial bool IsMirrorUpsideDown { get; set; }

    [Reactive]
    public partial bool IsBold { get; set; }

    [Reactive]
    public partial bool IsItalic { get; set; }

    [Reactive]
    public partial bool IsCurrent { get; set; }

    [Reactive]
    public partial Bitmap? Preview { get; set; }

    public CadTextStyleRowViewModel(TextStyle style)
    {
        Style = style;
        RefreshFromStyle(currentStyleName: null);
    }

    public void RefreshFromStyle(string? currentStyleName)
    {
        Name = Style.Name;
        Handle = Style.Handle == 0 ? string.Empty : Style.Handle.ToString("X", CultureInfo.InvariantCulture);
        Font = Style.Filename ?? string.Empty;
        BigFont = Style.BigFontFilename ?? string.Empty;
        Height = Style.Height.ToString("0.###", CultureInfo.InvariantCulture);
        Width = Style.Width.ToString("0.###", CultureInfo.InvariantCulture);
        LastHeight = Style.LastHeight.ToString("0.###", CultureInfo.InvariantCulture);
        ObliqueAngle = (Style.ObliqueAngle * 180.0 / Math.PI).ToString("0.###", CultureInfo.InvariantCulture);
        IsShapeFile = Style.Flags.HasFlag(StyleFlags.IsShape);
        IsVertical = Style.Flags.HasFlag(StyleFlags.VerticalText);
        IsMirrorBackward = Style.MirrorFlag.HasFlag(TextMirrorFlag.Backward);
        IsMirrorUpsideDown = Style.MirrorFlag.HasFlag(TextMirrorFlag.UpsideDown);
        IsBold = Style.TrueType.HasFlag(FontFlags.Bold);
        IsItalic = Style.TrueType.HasFlag(FontFlags.Italic);
        IsCurrent = !string.IsNullOrWhiteSpace(currentStyleName) &&
                    string.Equals(currentStyleName, Style.Name, StringComparison.OrdinalIgnoreCase);
    }
}
