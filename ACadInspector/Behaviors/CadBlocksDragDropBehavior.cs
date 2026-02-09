using System;
using ACadInspector.Services;
using ACadInspector.ViewModels;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Avalonia.Xaml.Interactivity;

namespace ACadInspector.Behaviors;

public sealed class CadBlocksDragDropBehavior : Behavior<DataGrid>
{
    private Point _dragStart;
    private string? _pendingBlockName;
    private bool _isDragging;

    protected override void OnAttached()
    {
        base.OnAttached();
        if (AssociatedObject is null)
        {
            return;
        }

        AssociatedObject.AddHandler(
            InputElement.PointerPressedEvent,
            OnPointerPressed,
            RoutingStrategies.Tunnel | RoutingStrategies.Bubble,
            handledEventsToo: true);
        AssociatedObject.AddHandler(
            InputElement.PointerMovedEvent,
            OnPointerMoved,
            RoutingStrategies.Tunnel | RoutingStrategies.Bubble,
            handledEventsToo: true);
        AssociatedObject.AddHandler(
            InputElement.PointerReleasedEvent,
            OnPointerReleased,
            RoutingStrategies.Tunnel | RoutingStrategies.Bubble,
            handledEventsToo: true);
    }

    protected override void OnDetaching()
    {
        if (AssociatedObject is not null)
        {
            AssociatedObject.RemoveHandler(
                InputElement.PointerPressedEvent,
                OnPointerPressed);
            AssociatedObject.RemoveHandler(
                InputElement.PointerMovedEvent,
                OnPointerMoved);
            AssociatedObject.RemoveHandler(
                InputElement.PointerReleasedEvent,
                OnPointerReleased);
        }

        base.OnDetaching();
    }

    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (AssociatedObject is null)
        {
            return;
        }

        var point = e.GetCurrentPoint(AssociatedObject);
        if (!point.Properties.IsLeftButtonPressed)
        {
            _pendingBlockName = null;
            return;
        }

        _dragStart = point.Position;
        _pendingBlockName = ResolveBlockName(e.Source) ?? ResolveBlockName(AssociatedObject.SelectedItem);
    }

    private async void OnPointerMoved(object? sender, PointerEventArgs e)
    {
        if (AssociatedObject is null ||
            string.IsNullOrWhiteSpace(_pendingBlockName))
        {
            return;
        }

        if (_isDragging)
        {
            return;
        }

        var point = e.GetCurrentPoint(AssociatedObject);
        if (!point.Properties.IsLeftButtonPressed)
        {
            _pendingBlockName = null;
            return;
        }

        var delta = point.Position - _dragStart;
        if (Math.Abs(delta.X) < 4.0 && Math.Abs(delta.Y) < 4.0)
        {
            return;
        }

        var blockName = _pendingBlockName;
        _pendingBlockName = null;
        if (string.IsNullOrWhiteSpace(blockName))
        {
            return;
        }

        _isDragging = true;
        try
        {
            var transfer = new DataTransfer();
            transfer.Add(DataTransferItem.Create(CadInsertDragDropFormats.BlockNameFormat, blockName));
            transfer.Add(DataTransferItem.Create(CadInsertDragDropFormats.BlockNamePlatformFormat, blockName));
            transfer.Add(DataTransferItem.CreateText(blockName));
            await DragDrop.DoDragDropAsync(e, transfer, DragDropEffects.Copy);
        }
        finally
        {
            _isDragging = false;
        }
    }

    private void OnPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        _pendingBlockName = null;
        _isDragging = false;
    }

    private static string? ResolveBlockName(object? source)
    {
        if (source is CadBlockRowViewModel rowViewModel)
        {
            return rowViewModel.Name;
        }

        if (source is not Visual visual)
        {
            return null;
        }

        var current = visual;
        while (current is not null)
        {
            if (current is StyledElement element &&
                element.DataContext is CadBlockRowViewModel row)
            {
                return row.Name;
            }

            current = current.GetVisualParent();
        }

        return null;
    }
}
