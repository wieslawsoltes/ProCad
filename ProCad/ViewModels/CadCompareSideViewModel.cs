using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using ProCad.Core;
using ACadSharp;
using ReactiveUI;
using ReactiveUI.SourceGenerators;

namespace ProCad.ViewModels;

public sealed partial class CadCompareSideViewModel : ReactiveObject
{
    public CadCompareSideViewModel(string label)
    {
        Label = label;
    }

    public string Label { get; }

    [Reactive]
    public partial CadDocument? Document { get; set; }

    [Reactive]
    public partial string DisplayName { get; set; } = "No document";

    [Reactive]
    public partial string FormatText { get; set; } = "Unknown";

    [Reactive]
    public partial string PathText { get; set; } = "Not loaded";

    [Reactive]
    public partial bool IsLoaded { get; set; }

    public CadFileFormat? Format { get; private set; }

    public string? Path { get; private set; }

    public Func<CancellationToken, ValueTask<Stream>>? OpenRead { get; private set; }

    public void UpdateFrom(
        CadDocument? document,
        CadFileFormat? format,
        string? path,
        string? displayName,
        Func<CancellationToken, ValueTask<Stream>>? openRead = null)
    {
        if (document is null)
        {
            DisplayName = "No document";
            FormatText = "Unknown";
            PathText = "Not loaded";
            IsLoaded = false;
            Document = null;
            Format = null;
            Path = null;
            OpenRead = null;
            return;
        }

        DisplayName = string.IsNullOrWhiteSpace(displayName) ? "Untitled" : displayName;
        Format = format;
        Path = path;
        OpenRead = openRead;
        FormatText = Format?.ToString() ?? "Unknown";
        PathText = string.IsNullOrWhiteSpace(Path) ? "In-memory" : Path;
        IsLoaded = true;
        Document = document;
    }
}
