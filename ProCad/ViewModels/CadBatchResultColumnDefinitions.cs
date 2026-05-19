using System;
using Avalonia.Controls;

namespace ProCad.ViewModels;

internal static class CadBatchResultColumnDefinitions
{
    public static DataGridColumnDefinitionList Create()
    {
        var columns = new DataGridColumnDefinitionList
        {
            CreateTextColumn("Document", nameof(CadBatchResultRowViewModel.Document), static row => row.Document),
            CreateTextColumn("Format", nameof(CadBatchResultRowViewModel.Format), static row => row.Format),
            CreateTextColumn("Object Path", nameof(CadBatchResultRowViewModel.ObjectPath), static row => row.ObjectPath),
            CreateTextColumn("Kind", nameof(CadBatchResultRowViewModel.Kind), static row => row.Kind),
            CreateTextColumn("Type", nameof(CadBatchResultRowViewModel.TypeName), static row => row.TypeName),
            CreateTextColumn("Name", nameof(CadBatchResultRowViewModel.Name), static row => row.Name),
            CreateTextColumn("Handle", nameof(CadBatchResultRowViewModel.Handle), static row => row.Handle),
            CreateTextColumn("Match", nameof(CadBatchResultRowViewModel.Match), static row => row.Match),
            CreateTextColumn("Document Path", nameof(CadBatchResultRowViewModel.DocumentPath), static row => row.DocumentPath)
        };

        return columns;
    }

    private static DataGridTextColumnDefinition CreateTextColumn(
        object header,
        string propertyName,
        Func<CadBatchResultRowViewModel, string> getter)
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
