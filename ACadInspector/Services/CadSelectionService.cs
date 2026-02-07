using System.Collections.Generic;
using System.Linq;
using ACadInspector.Editing.Selection;
using ReactiveUI;

namespace ACadInspector.Services;

public sealed class CadSelectionService : ReactiveObject
{
    private readonly CadSelectionSet _selectionSet = new();
    private object? _selectedObject;
    private long _selectionStamp;

    public object? SelectedObject
    {
        get => _selectedObject;
        set
        {
            if (EqualityComparer<object?>.Default.Equals(_selectedObject, value))
            {
                return;
            }

            this.RaiseAndSetIfChanged(ref _selectedObject, value);
            if (value is null)
            {
                if (_selectionSet.Clear())
                {
                    AdvanceSelection();
                }
                return;
            }

            if (_selectionSet.Apply(new[] { value }, CadSelectionMode.Replace))
            {
                AdvanceSelection();
            }
        }
    }

    public IReadOnlyCollection<object> SelectedObjects => _selectionSet.Items;

    public long SelectionStamp
    {
        get => _selectionStamp;
        private set => this.RaiseAndSetIfChanged(ref _selectionStamp, value);
    }

    public bool ApplySelection(IEnumerable<object?> selection, CadSelectionMode mode)
    {
        if (!_selectionSet.Apply(selection, mode))
        {
            return false;
        }

        _selectedObject = _selectionSet.Items.Count == 1
            ? _selectionSet.Items.FirstOrDefault()
            : null;
        this.RaisePropertyChanged(nameof(SelectedObject));
        AdvanceSelection();
        return true;
    }

    public bool Contains(object? selected)
    {
        return selected is not null && _selectionSet.Contains(selected);
    }

    public bool SetPrimarySelection(object? selected)
    {
        if (selected is null || !_selectionSet.Contains(selected))
        {
            return false;
        }

        if (ReferenceEquals(_selectedObject, selected))
        {
            return false;
        }

        _selectedObject = selected;
        this.RaisePropertyChanged(nameof(SelectedObject));
        AdvanceSelection();
        return true;
    }

    public void ClearSelection()
    {
        if (!_selectionSet.Clear())
        {
            if (_selectedObject is not null)
            {
                _selectedObject = null;
                this.RaisePropertyChanged(nameof(SelectedObject));
            }
            return;
        }

        _selectedObject = null;
        this.RaisePropertyChanged(nameof(SelectedObject));
        AdvanceSelection();
    }

    private void AdvanceSelection()
    {
        SelectionStamp++;
        this.RaisePropertyChanged(nameof(SelectedObjects));
    }
}
