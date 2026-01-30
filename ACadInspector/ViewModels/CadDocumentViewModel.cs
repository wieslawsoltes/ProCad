using ACadInspector.Core;
using ACadSharp;
using Dock.Model.ReactiveUI.Controls;
using ReactiveUI.SourceGenerators;

namespace ACadInspector.ViewModels;

public sealed partial class CadDocumentViewModel : Document
{
    public CadDocument Document { get; }
    public CadRenderViewModel Render { get; }

    [Reactive]
    public partial CadFileFormat Format { get; set; }

    [Reactive]
    public partial string? Path { get; set; }

    public CadDocumentViewModel(
        CadDocument document,
        CadFileFormat format,
        string? path,
        string displayName,
        CadRenderViewModel render)
    {
        Document = document;
        Format = format;
        Path = path;
        Title = displayName;
        Render = render;
    }

    public void UpdateLocation(CadFileFormat format, string? path, string displayName)
    {
        Format = format;
        Path = path;
        Title = displayName;
    }
}
