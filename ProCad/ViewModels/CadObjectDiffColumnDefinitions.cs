using System;
using Avalonia.Controls;

namespace ProCad.ViewModels;

internal static class CadObjectDiffColumnDefinitions
{
    public static DataGridColumnDefinitionList Create()
    {
        var columns = new DataGridColumnDefinitionList
        {
            CreateTextColumn("Path", nameof(CadObjectDiffRowViewModel.Path), static row => row.Path),
            CreateTextColumn("Kind", nameof(CadObjectDiffRowViewModel.Kind), static row => row.Kind),
            CreateTextColumn("Type", nameof(CadObjectDiffRowViewModel.TypeName), static row => row.TypeName),
            CreateTextColumn("Change", nameof(CadObjectDiffRowViewModel.DiffKindText), static row => row.DiffKindText),
            CreateTextColumn("Property Diffs", nameof(CadObjectDiffRowViewModel.PropertyDiffCountText), static row => row.PropertyDiffCountText)
        };

        return columns;
    }

    private static DataGridTextColumnDefinition CreateTextColumn(
        object header,
        string propertyName,
        Func<CadObjectDiffRowViewModel, string> getter)
    {
        var binding = DataGridBindingFactory.CreateBinding(propertyName, getter);
        var accessor = DataGridBindingFactory.CreateValueAccessor(getter);
        var column = new DataGridTextColumnDefinition
        {
            Header = header,
            Binding = binding,
            ColumnKey = propertyName,
            SortMemberPath = propertyName,
            CanUserSort = true,
            CanUserReorder = false,
            CanUserResize = true
        };

        DataGridColumnDefinitionThreadSafety.SetOptions(column, new DataGridColumnDefinitionOptions
        {
            IsSearchable = true,
            FilterValueAccessor = accessor
        });

        return column;
    }
}
