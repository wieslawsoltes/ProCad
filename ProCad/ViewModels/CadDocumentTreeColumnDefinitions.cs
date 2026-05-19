using System;
using Avalonia.Controls;
using Avalonia.Controls.DataGridHierarchical;

namespace ProCad.ViewModels;

internal static class CadDocumentTreeColumnDefinitions
{
    public static DataGridColumnDefinitionList Create()
    {
        var columns = new DataGridColumnDefinitionList
        {
            CreateHierarchyColumn("Name", nameof(CadDocumentTreeNode.Name), static node => GetItem(node).Name),
            CreateTextColumn("Kind", nameof(CadDocumentTreeNode.Kind), static node => GetItem(node).Kind),
            CreateTextColumn("Type", nameof(CadDocumentTreeNode.TypeName), static node => GetItem(node).TypeName),
            CreateTextColumn("Handle", nameof(CadDocumentTreeNode.HandleText), static node => GetItem(node).HandleText),
            CreateTextColumn("Children", nameof(CadDocumentTreeNode.ChildCountText), static node => GetItem(node).ChildCountText)
        };

        return columns;
    }

    private static DataGridHierarchicalColumnDefinition CreateHierarchyColumn(
        object header,
        string propertyName,
        Func<HierarchicalNode, string> getter)
    {
        var binding = DataGridBindingFactory.CreateBinding(propertyName, getter);
        var accessor = DataGridBindingFactory.CreateValueAccessor(getter);
        var column = new DataGridHierarchicalColumnDefinition
        {
            Header = header,
            Binding = binding,
            ColumnKey = propertyName,
            SortMemberPath = $"Item.{propertyName}",
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

    private static DataGridTextColumnDefinition CreateTextColumn(
        object header,
        string propertyName,
        Func<HierarchicalNode, string> getter)
    {
        var binding = DataGridBindingFactory.CreateBinding(propertyName, getter);
        var accessor = DataGridBindingFactory.CreateValueAccessor(getter);
        var column = new DataGridTextColumnDefinition
        {
            Header = header,
            Binding = binding,
            ColumnKey = propertyName,
            SortMemberPath = $"Item.{propertyName}",
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

    private static CadDocumentTreeNode GetItem(HierarchicalNode node)
    {
        return (CadDocumentTreeNode)node.Item;
    }
}
