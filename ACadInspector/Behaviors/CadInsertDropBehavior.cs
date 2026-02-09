using System;
using System.Windows.Input;
using ACadInspector.Controls;
using ACadInspector.Services;
using Avalonia;
using Avalonia.Input;
using Avalonia.Xaml.Interactivity;

namespace ACadInspector.Behaviors;

public sealed class CadInsertDropBehavior : Behavior<CadRenderControl>
{
    public static readonly StyledProperty<ICommand?> DropCommandProperty =
        AvaloniaProperty.Register<CadInsertDropBehavior, ICommand?>(nameof(DropCommand));

    public ICommand? DropCommand
    {
        get => GetValue(DropCommandProperty);
        set => SetValue(DropCommandProperty, value);
    }

    protected override void OnAttached()
    {
        base.OnAttached();
        if (AssociatedObject is null)
        {
            return;
        }

        DragDrop.SetAllowDrop(AssociatedObject, true);
        AssociatedObject.AddHandler(DragDrop.DragOverEvent, OnDragOver);
        AssociatedObject.AddHandler(DragDrop.DropEvent, OnDrop);
    }

    protected override void OnDetaching()
    {
        if (AssociatedObject is not null)
        {
            AssociatedObject.RemoveHandler(DragDrop.DragOverEvent, OnDragOver);
            AssociatedObject.RemoveHandler(DragDrop.DropEvent, OnDrop);
        }

        base.OnDetaching();
    }

    private void OnDragOver(object? sender, DragEventArgs e)
    {
        if (!HasSupportedBlockPayload(e.DataTransfer))
        {
            e.DragEffects = DragDropEffects.None;
            e.Handled = true;
            return;
        }

        e.DragEffects = DragDropEffects.Copy;
        e.Handled = true;
    }

    private void OnDrop(object? sender, DragEventArgs e)
    {
        if (AssociatedObject is null ||
            !TryGetBlockName(e.DataTransfer, out var blockName))
        {
            return;
        }

        var command = DropCommand;
        if (command is null)
        {
            return;
        }

        var position = e.GetPosition(AssociatedObject);
        if (!AssociatedObject.TryScreenToWorld(position, out var worldPoint))
        {
            return;
        }

        var request = new CadInsertDropRequest(blockName, worldPoint);
        if (command.CanExecute(request))
        {
            command.Execute(request);
            e.Handled = true;
        }
    }

    private static bool TryGetBlockName(IDataTransfer data, out string blockName)
    {
        blockName = string.Empty;
        var token = data.TryGetValue(CadInsertDragDropFormats.BlockNameFormat) ??
                    data.TryGetValue(CadInsertDragDropFormats.BlockNamePlatformFormat);
        if (!string.IsNullOrWhiteSpace(token))
        {
            blockName = token.Trim();
            return true;
        }

        var text = data.TryGetText();
        if (!string.IsNullOrWhiteSpace(text))
        {
            blockName = text.Trim();
            return true;
        }

        return false;
    }

    private static bool HasSupportedBlockPayload(IDataTransfer data)
    {
        return !string.IsNullOrWhiteSpace(data.TryGetValue(CadInsertDragDropFormats.BlockNameFormat)) ||
               !string.IsNullOrWhiteSpace(data.TryGetValue(CadInsertDragDropFormats.BlockNamePlatformFormat)) ||
               !string.IsNullOrWhiteSpace(data.TryGetText());
    }
}
