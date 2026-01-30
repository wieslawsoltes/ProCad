using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using ACadInspector.Core;
using ACadSharp;
using ReactiveUI;
using ReactiveUI.SourceGenerators;

namespace ACadInspector.ViewModels;

public sealed partial class CadBatchItemViewModel : ReactiveObject
{
    private readonly Stopwatch _stopwatch = new();

    public CadBatchItemViewModel(CadOpenFileResult result)
    {
        FileName = result.FileName;
        Path = result.Path;
        PathText = string.IsNullOrWhiteSpace(result.Path) ? "(virtual path)" : result.Path;
        Format = result.Format;
        FormatText = result.Format.ToString();
        OpenRead = result.OpenReadAsync;
        Status = CadBatchItemStatus.Pending;
        StatusText = Status.ToString();
        Message = "Queued";
        DurationText = "";
    }

    public string FileName { get; }

    public string? Path { get; }

    public string PathText { get; }

    public CadFileFormat Format { get; }

    public string FormatText { get; }

    public Func<CancellationToken, ValueTask<Stream>>? OpenRead { get; }

    public CadDocument? Document { get; private set; }

    [Reactive]
    public partial CadBatchItemStatus Status { get; set; }

    [Reactive]
    public partial string StatusText { get; set; }

    [Reactive]
    public partial string Message { get; set; }

    [Reactive]
    public partial string DurationText { get; set; }

    public void MarkPending(string? message = null)
    {
        SetStatus(CadBatchItemStatus.Pending, message ?? "Queued");
    }

    public void MarkLoading()
    {
        _stopwatch.Restart();
        SetStatus(CadBatchItemStatus.Loading, "Loading");
    }

    public void MarkLoaded(CadDocument document)
    {
        _stopwatch.Stop();
        Document = document;
        SetStatus(CadBatchItemStatus.Loaded, "Loaded");
        DurationText = FormatDuration(_stopwatch.Elapsed);
    }

    public void MarkFailed(string message)
    {
        _stopwatch.Stop();
        SetStatus(CadBatchItemStatus.Failed, message);
        DurationText = FormatDuration(_stopwatch.Elapsed);
    }

    public void MarkCancelled()
    {
        _stopwatch.Stop();
        SetStatus(CadBatchItemStatus.Cancelled, "Cancelled");
        DurationText = FormatDuration(_stopwatch.Elapsed);
    }

    public void MarkSkipped(string message)
    {
        SetStatus(CadBatchItemStatus.Skipped, message);
    }

    private void SetStatus(CadBatchItemStatus status, string message)
    {
        Status = status;
        StatusText = status.ToString();
        Message = message;
    }

    private static string FormatDuration(TimeSpan elapsed)
    {
        if (elapsed == TimeSpan.Zero)
        {
            return string.Empty;
        }

        if (elapsed.TotalSeconds < 1)
        {
            return $"{elapsed.TotalMilliseconds:F0} ms";
        }

        return elapsed.TotalSeconds < 60
            ? $"{elapsed.TotalSeconds:F2} s"
            : $"{elapsed.TotalMinutes:F2} min";
    }
}
