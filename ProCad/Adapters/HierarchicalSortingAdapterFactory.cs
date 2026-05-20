using System;
using System.Collections.Generic;
using Avalonia.Controls;
using Avalonia.Controls.DataGridSorting;

namespace ProCad.Adapters;

public sealed class HierarchicalSortingAdapter : DataGridSortingAdapter
{
    public HierarchicalSortingAdapter(
        ISortingModel model,
        Func<IEnumerable<DataGridColumn>> columnProvider,
        Action? beforeViewRefresh,
        Action? afterViewRefresh)
        : base(model, columnProvider, beforeViewRefresh, afterViewRefresh)
    {
    }

    protected override bool TryApplyModelToView(
        IReadOnlyList<SortingDescriptor> descriptors,
        IReadOnlyList<SortingDescriptor> previousDescriptors,
        out bool changed)
    {
        changed = true;
        return true;
    }
}

public sealed class HierarchicalSortingAdapterFactory : IDataGridSortingAdapterFactory
{
    public DataGridSortingAdapter Create(DataGrid grid, ISortingModel model)
    {
        return new HierarchicalSortingAdapter(
            model,
            () => grid.ColumnDefinitions,
            null,
            null);
    }
}
