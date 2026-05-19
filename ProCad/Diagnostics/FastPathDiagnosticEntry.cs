using System;
using Avalonia.Controls;

namespace ProCad.Diagnostics;

public sealed class FastPathDiagnosticEntry
{
    public FastPathDiagnosticEntry(
        DateTimeOffset timestamp,
        string feature,
        string context,
        string column,
        string message)
    {
        Timestamp = timestamp;
        Feature = feature;
        Context = context;
        Column = column;
        Message = message;
        Summary = $"{feature} | {context} | {column}: {message}";
    }

    public DateTimeOffset Timestamp { get; }

    public string Feature { get; }

    public string Context { get; }

    public string Column { get; }

    public string Message { get; }

    public string Summary { get; }

    public static FastPathDiagnosticEntry Create(DataGridFastPathMissingAccessorEventArgs args, DataGrid grid)
    {
        var feature = args.Feature.ToString();
        var context = BuildContext(grid);
        var column = args.Column?.Header?.ToString() ?? "(unknown column)";
        var message = string.IsNullOrWhiteSpace(args.Message) ? "Missing fast-path accessor." : args.Message;
        return new FastPathDiagnosticEntry(DateTimeOffset.Now, feature, context, column, message);
    }

    private static string BuildContext(DataGrid grid)
    {
        if (!string.IsNullOrWhiteSpace(grid.Name))
        {
            return grid.Name;
        }

        var dataContext = grid.DataContext;
        if (dataContext is not null)
        {
            return dataContext.GetType().Name;
        }

        return grid.GetType().Name;
    }
}
