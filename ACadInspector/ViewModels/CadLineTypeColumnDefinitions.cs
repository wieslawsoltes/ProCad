using System;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;

namespace ACadInspector.ViewModels;

internal static class CadLineTypeColumnDefinitions
{
    public static DataGridColumnDefinitionList Create()
    {
        var columns = new DataGridColumnDefinitionList
        {
            CreateImageColumn("Preview", nameof(CadLineTypeRowViewModel.Preview), static row => row.Preview),
            CreateTextColumn("Name", nameof(CadLineTypeRowViewModel.Name), static row => row.Name),
            CreateTextColumn("Handle", nameof(CadLineTypeRowViewModel.Handle), static row => row.Handle),
            CreateTextColumn("Description", nameof(CadLineTypeRowViewModel.Description), static row => row.Description),
            CreateTextColumn("Pattern", nameof(CadLineTypeRowViewModel.PatternLength), static row => row.PatternLength),
            CreateTextColumn("Segments", nameof(CadLineTypeRowViewModel.SegmentCount), static row => row.SegmentCount),
            CreateCheckBoxColumn("Complex", nameof(CadLineTypeRowViewModel.IsComplex), static row => row.IsComplex),
            CreateCheckBoxColumn("Shapes", nameof(CadLineTypeRowViewModel.HasShapes), static row => row.HasShapes)
        };

        return columns;
    }

    private static DataGridImageColumnDefinition CreateImageColumn(
        object header,
        string propertyName,
        Func<CadLineTypeRowViewModel, Bitmap?> getter)
    {
        var binding = DataGridBindingFactory.CreateBinding(propertyName, getter);
        var accessor = DataGridBindingFactory.CreateValueAccessor(getter, setter: null);
        var column = new DataGridImageColumnDefinition
        {
            Header = header,
            Binding = binding,
            ColumnKey = propertyName,
            CanUserSort = false,
            CanUserReorder = true,
            CanUserResize = true,
            ImageWidth = 48,
            ImageHeight = 48,
            Stretch = Stretch.Uniform
        };

        column.Options = new DataGridColumnDefinitionOptions
        {
            IsSearchable = false,
            FilterValueAccessor = accessor
        };

        return column;
    }

    private static DataGridTextColumnDefinition CreateTextColumn(
        object header,
        string propertyName,
        Func<CadLineTypeRowViewModel, string> getter)
    {
        var binding = DataGridBindingFactory.CreateBinding(propertyName, getter);
        var accessor = DataGridBindingFactory.CreateValueAccessor(getter, setter: null);
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

    private static DataGridCheckBoxColumnDefinition CreateCheckBoxColumn(
        object header,
        string propertyName,
        Func<CadLineTypeRowViewModel, bool> getter)
    {
        var binding = DataGridBindingFactory.CreateBinding(propertyName, getter);
        var accessor = DataGridBindingFactory.CreateValueAccessor(getter, setter: null);
        var column = new DataGridCheckBoxColumnDefinition
        {
            Header = header,
            Binding = binding,
            ColumnKey = propertyName,
            SortMemberPath = propertyName,
            CanUserSort = true,
            CanUserReorder = true,
            CanUserResize = true,
            IsReadOnly = true
        };

        column.Options = new DataGridColumnDefinitionOptions
        {
            IsSearchable = true,
            FilterValueAccessor = accessor
        };

        return column;
    }
}
