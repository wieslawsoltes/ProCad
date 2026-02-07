using System;
using Avalonia.Controls;
using Avalonia.Controls.DataGridHierarchical;

namespace ACadInspector.ViewModels;

internal static class DataGridColumnDefinitionThreadSafety
{
    private static readonly object OptionsGate = new();

    public static void SetOptions(
        DataGridTextColumnDefinition column,
        DataGridColumnDefinitionOptions options)
    {
        SetOptionsCore(column, options);
    }

    public static void SetOptions(
        DataGridCheckBoxColumnDefinition column,
        DataGridColumnDefinitionOptions options)
    {
        SetOptionsCore(column, options);
    }

    public static void SetOptions(
        DataGridImageColumnDefinition column,
        DataGridColumnDefinitionOptions options)
    {
        SetOptionsCore(column, options);
    }

    public static void SetOptions(
        DataGridHierarchicalColumnDefinition column,
        DataGridColumnDefinitionOptions options)
    {
        SetOptionsCore(column, options);
    }

    private static void SetOptionsCore(
        DataGridColumnDefinition column,
        DataGridColumnDefinitionOptions options)
    {
        ArgumentNullException.ThrowIfNull(column);
        ArgumentNullException.ThrowIfNull(options);

        lock (OptionsGate)
        {
            column.Options = options;
        }
    }
}
