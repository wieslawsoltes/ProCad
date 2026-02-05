using System.Globalization;
using ACadInspector.Rendering;
using ACadSharp.Entities;
using ACadSharp.Objects;
using CSMath;

namespace ACadInspector.ViewModels;

public sealed class CadViewportRowViewModel : ViewModelBase
{
    public Viewport Viewport { get; }
    public string LayoutName { get; }
    public string Handle { get; }
    public string Id { get; }
    public string LayerName { get; }
    public bool IsPaper { get; }
    public string Center { get; }
    public string Size { get; }
    public string ViewCenter { get; }
    public string ViewSize { get; }
    public string ScaleFactor { get; }
    public string TwistAngle { get; }
    public string Status { get; }

    public CadViewportRowViewModel(Layout layout, Viewport viewport)
    {
        Viewport = viewport;
        LayoutName = layout.Name;
        Handle = viewport.Handle == 0 ? string.Empty : viewport.Handle.ToString("X", CultureInfo.InvariantCulture);
        Id = viewport.Id.ToString(CultureInfo.InvariantCulture);
        LayerName = viewport.Layer?.Name ?? string.Empty;
        IsPaper = ViewportRenderUtils.IsPaperViewport(viewport);
        Center = FormatPoint(viewport.Center);
        Size = FormatSize(viewport.Width, viewport.Height);
        ViewCenter = FormatPoint(viewport.ViewCenter);
        ViewSize = FormatSize(viewport.ViewWidth, viewport.ViewHeight);
        ScaleFactor = viewport.ScaleFactor.ToString("0.###", CultureInfo.InvariantCulture);
        TwistAngle = viewport.TwistAngle.ToString("0.###", CultureInfo.InvariantCulture);
        Status = viewport.Status.ToString();
    }

    private static string FormatPoint(XYZ point)
    {
        return string.Create(CultureInfo.InvariantCulture, $"{point.X:0.###}, {point.Y:0.###}");
    }

    private static string FormatPoint(XY point)
    {
        return string.Create(CultureInfo.InvariantCulture, $"{point.X:0.###}, {point.Y:0.###}");
    }

    private static string FormatSize(double width, double height)
    {
        return string.Create(CultureInfo.InvariantCulture, $"{width:0.###} x {height:0.###}");
    }
}
