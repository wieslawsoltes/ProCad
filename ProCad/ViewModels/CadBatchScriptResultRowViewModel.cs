using System;
using ProCad.Core;
using ProCad.Scripting;

namespace ProCad.ViewModels;

public sealed class CadBatchScriptResultRowViewModel
{
    public CadBatchScriptResultRowViewModel(
        string documentName,
        string? documentPath,
        CadFileFormat format,
        CadScriptExecutionResult result)
    {
        Document = documentName;
        DocumentPath = documentPath ?? string.Empty;
        Format = format.ToString();
        Status = BuildStatus(result);
        Duration = FormatDuration(result.Duration);
        Output = result.Output;
        Error = result.Error ?? string.Empty;
        Message = BuildMessage(result);
    }

    public string Document { get; }

    public string DocumentPath { get; }

    public string Format { get; }

    public string Status { get; }

    public string Duration { get; }

    public string Message { get; }

    public string Output { get; }

    public string Error { get; }

    private static string BuildStatus(CadScriptExecutionResult result)
    {
        if (result.Success)
        {
            return "Success";
        }

        if (!string.IsNullOrWhiteSpace(result.Error) &&
            result.Error.Contains("cancel", StringComparison.OrdinalIgnoreCase))
        {
            return "Cancelled";
        }

        return "Failed";
    }

    private static string BuildMessage(CadScriptExecutionResult result)
    {
        if (result.Success)
        {
            return string.IsNullOrWhiteSpace(result.Output)
                ? "Completed"
                : TrimLine(result.Output);
        }

        return string.IsNullOrWhiteSpace(result.Error) ? "Script failed" : TrimLine(result.Error);
    }

    private static string TrimLine(string text)
    {
        var lineEnd = text.IndexOfAny(new[] { '\r', '\n' });
        return lineEnd >= 0 ? text.Substring(0, lineEnd) : text;
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
