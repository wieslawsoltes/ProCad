using System;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;

namespace ACadInspector.ViewModels;

internal static class CadBlockColumnDefinitions
{
    public static DataGridColumnDefinitionList Create()
    {
        var columns = new DataGridColumnDefinitionList
        {
            CreateImageColumn("Preview", nameof(CadBlockRowViewModel.Preview), static row => row.Preview),
            CreateTextColumn("Name", nameof(CadBlockRowViewModel.Name), static row => row.Name),
            CreateTextColumn("Handle", nameof(CadBlockRowViewModel.Handle), static row => row.Handle),
            CreateTextColumn("Layout", nameof(CadBlockRowViewModel.LayoutName), static row => row.LayoutName),
            CreateTextColumn("Entities", nameof(CadBlockRowViewModel.EntityCount), static row => row.EntityCount),
            CreateCheckBoxColumn("XRef", nameof(CadBlockRowViewModel.IsXRef), static row => row.IsXRef),
            CreateCheckBoxColumn("Overlay", nameof(CadBlockRowViewModel.IsXRefOverlay), static row => row.IsXRefOverlay),
            CreateCheckBoxColumn("Anonymous", nameof(CadBlockRowViewModel.IsAnonymous), static row => row.IsAnonymous),
            CreateCheckBoxColumn("Dynamic", nameof(CadBlockRowViewModel.IsDynamic), static row => row.IsDynamic),
            CreateCheckBoxColumn("Layout", nameof(CadBlockRowViewModel.IsLayout), static row => row.IsLayout),
            CreateCheckBoxColumn("Attrs", nameof(CadBlockRowViewModel.HasAttributes), static row => row.HasAttributes)
        };

        return columns;
    }

    private static DataGridImageColumnDefinition CreateImageColumn(
        object header,
        string propertyName,
        Func<CadBlockRowViewModel, Bitmap?> getter)
    {
        var binding = DataGridBindingFactory.CreateBinding(propertyName, getter);
        var accessor = DataGridBindingFactory.CreateValueAccessor(getter, setter: null);
        var column = new DataGridImageColumnDefinition
        {
            Header = header,
            Binding = binding,
            ColumnKey = propertyName,
            CanUserSort = false,
            CanUserReorder = false,
            CanUserResize = true,
            ImageWidth = 48,
            ImageHeight = 48,
            Stretch = Stretch.Uniform
        };

        DataGridColumnDefinitionThreadSafety.SetOptions(column, new DataGridColumnDefinitionOptions
        {
            IsSearchable = false,
            FilterValueAccessor = accessor
        });

        return column;
    }

    private static DataGridTextColumnDefinition CreateTextColumn(
        object header,
        string propertyName,
        Func<CadBlockRowViewModel, string> getter)
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

    private static DataGridCheckBoxColumnDefinition CreateCheckBoxColumn(
        object header,
        string propertyName,
        Func<CadBlockRowViewModel, bool> getter)
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
            CanUserReorder = false,
            CanUserResize = true,
            IsReadOnly = true
        };

        DataGridColumnDefinitionThreadSafety.SetOptions(column, new DataGridColumnDefinitionOptions
        {
            IsSearchable = true,
            FilterValueAccessor = accessor
        });

        return column;
    }
}
