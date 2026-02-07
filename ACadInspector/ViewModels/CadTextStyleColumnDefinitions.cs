using System;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;

namespace ACadInspector.ViewModels;

internal static class CadTextStyleColumnDefinitions
{
    public static DataGridColumnDefinitionList Create()
    {
        var columns = new DataGridColumnDefinitionList
        {
            CreateImageColumn("Preview", nameof(CadTextStyleRowViewModel.Preview), static row => row.Preview),
            CreateCheckBoxColumn("Current", nameof(CadTextStyleRowViewModel.IsCurrent), static row => row.IsCurrent),
            CreateTextColumn("Name", nameof(CadTextStyleRowViewModel.Name), static row => row.Name),
            CreateTextColumn("Handle", nameof(CadTextStyleRowViewModel.Handle), static row => row.Handle),
            CreateTextColumn("Font", nameof(CadTextStyleRowViewModel.Font), static row => row.Font),
            CreateTextColumn("Big Font", nameof(CadTextStyleRowViewModel.BigFont), static row => row.BigFont),
            CreateTextColumn("Height", nameof(CadTextStyleRowViewModel.Height), static row => row.Height),
            CreateTextColumn("Width", nameof(CadTextStyleRowViewModel.Width), static row => row.Width),
            CreateTextColumn("Last Height", nameof(CadTextStyleRowViewModel.LastHeight), static row => row.LastHeight),
            CreateTextColumn("Oblique°", nameof(CadTextStyleRowViewModel.ObliqueAngle), static row => row.ObliqueAngle),
            CreateCheckBoxColumn("Shape", nameof(CadTextStyleRowViewModel.IsShapeFile), static row => row.IsShapeFile),
            CreateCheckBoxColumn("Vertical", nameof(CadTextStyleRowViewModel.IsVertical), static row => row.IsVertical),
            CreateCheckBoxColumn("Backward", nameof(CadTextStyleRowViewModel.IsMirrorBackward), static row => row.IsMirrorBackward),
            CreateCheckBoxColumn("UpsideDown", nameof(CadTextStyleRowViewModel.IsMirrorUpsideDown), static row => row.IsMirrorUpsideDown),
            CreateCheckBoxColumn("Bold", nameof(CadTextStyleRowViewModel.IsBold), static row => row.IsBold),
            CreateCheckBoxColumn("Italic", nameof(CadTextStyleRowViewModel.IsItalic), static row => row.IsItalic)
        };

        return columns;
    }

    private static DataGridImageColumnDefinition CreateImageColumn(
        object header,
        string propertyName,
        Func<CadTextStyleRowViewModel, Bitmap?> getter)
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
        Func<CadTextStyleRowViewModel, string> getter)
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
        Func<CadTextStyleRowViewModel, bool> getter)
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
