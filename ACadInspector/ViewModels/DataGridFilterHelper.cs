using System;
using System.Globalization;
using Avalonia.Controls;
using Avalonia.Controls.DataGridFiltering;
using Avalonia.Controls.DataGridSearching;

namespace ACadInspector.ViewModels;

internal static class DataGridFilterHelper
{
    private const string GlobalFilterId = "__global__";

    public static void ApplySearch(SearchModel model, string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            model.Clear();
            return;
        }

        var descriptor = new SearchDescriptor(
            text.Trim(),
            matchMode: SearchMatchMode.Contains,
            termMode: SearchTermCombineMode.Any,
            scope: SearchScope.VisibleColumns,
            comparison: StringComparison.OrdinalIgnoreCase);

        model.SetOrUpdate(descriptor);
    }

    public static void ApplyFilter(
        FilteringModel model,
        DataGridColumnDefinitionList columns,
        string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            model.Remove(GlobalFilterId);
            return;
        }

        var trimmed = text.Trim();
        model.SetOrUpdate(new FilteringDescriptor(
            columnId: GlobalFilterId,
            @operator: FilteringOperator.Custom,
            propertyPath: GlobalFilterId,
            predicate: item => MatchesAnyColumn(item, columns, trimmed)));
    }

    private static bool MatchesAnyColumn(
        object item,
        DataGridColumnDefinitionList columns,
        string text)
    {
        foreach (var column in columns)
        {
            var options = column.Options;
            if (options?.IsSearchable == false)
            {
                continue;
            }

            var accessor = options?.FilterValueAccessor;
            if (accessor is null)
            {
                continue;
            }

            var value = accessor.GetValue(item);
            if (value is null)
            {
                continue;
            }

            var stringValue = value switch
            {
                string s => s,
                IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
                _ => value.ToString() ?? string.Empty
            };

            if (stringValue.Length == 0)
            {
                continue;
            }

            if (stringValue.Contains(text, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
