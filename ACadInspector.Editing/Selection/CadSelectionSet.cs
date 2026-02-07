namespace ACadInspector.Editing.Selection;

public enum CadSelectionMode
{
    Replace,
    Add,
    Remove,
    Toggle
}

public sealed class CadSelectionSet
{
    private readonly HashSet<object> _items = new(System.Collections.Generic.ReferenceEqualityComparer.Instance);

    public IReadOnlyCollection<object> Items => _items;
    public int Count => _items.Count;

    public bool Contains(object item)
    {
        return _items.Contains(item);
    }

    public bool Clear()
    {
        if (_items.Count == 0)
        {
            return false;
        }

        _items.Clear();
        return true;
    }

    public bool Apply(IEnumerable<object?> items, CadSelectionMode mode)
    {
        ArgumentNullException.ThrowIfNull(items);

        var changed = false;
        if (mode == CadSelectionMode.Replace)
        {
            changed = Clear();
        }

        foreach (var item in items)
        {
            if (item is null)
            {
                continue;
            }

            switch (mode)
            {
                case CadSelectionMode.Replace:
                case CadSelectionMode.Add:
                    changed |= _items.Add(item);
                    break;
                case CadSelectionMode.Remove:
                    changed |= _items.Remove(item);
                    break;
                case CadSelectionMode.Toggle:
                    if (!_items.Remove(item))
                    {
                        changed |= _items.Add(item);
                    }
                    else
                    {
                        changed = true;
                    }
                    break;
            }
        }

        return changed;
    }
}
