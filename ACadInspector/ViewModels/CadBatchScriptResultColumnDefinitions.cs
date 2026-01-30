using System;
using Avalonia.Controls;

namespace ACadInspector.ViewModels;

internal static class CadBatchScriptResultColumnDefinitions
{
    public static DataGridColumnDefinitionList Create()
    {
        var columns = new DataGridColumnDefinitionList
        {
            CreateTextColumn("Document", nameof(CadBatchScriptResultRowViewModel.Document), static row => row.Document),
            CreateTextColumn("Format", nameof(CadBatchScriptResultRowViewModel.Format), static row => row.Format),
            CreateTextColumn("Status", nameof(CadBatchScriptResultRowViewModel.Status), static row => row.Status),
            CreateTextColumn("Duration", nameof(CadBatchScriptResultRowViewModel.Duration), static row => row.Duration),
            CreateTextColumn("Message", nameof(CadBatchScriptResultRowViewModel.Message), static row => row.Message),
            CreateTextColumn("Document Path", nameof(CadBatchScriptResultRowViewModel.DocumentPath), static row => row.DocumentPath)
        };

        return columns;
    }

    private static DataGridTextColumnDefinition CreateTextColumn(
        object header,
        string propertyName,
        Func<CadBatchScriptResultRowViewModel, string> getter)
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
            CanUserReorder = true,
            CanUserResize = true
        };

        column.Options = new DataGridColumnDefinitionOptions
        {
            IsSearchable = true,
            FilterValueAccessor = accessor
        };

        return column;
    }
}
