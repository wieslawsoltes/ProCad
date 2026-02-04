using System.Collections.Generic;
using ACadInspector.Rendering;
using ACadSharp.Entities;
using ACadSharp.Tables;
using ReactiveUI;
using ReactiveUI.SourceGenerators;

namespace ACadInspector.Services;

public sealed partial class CadDynamicBlockOverrideService : ReactiveObject, IDynamicBlockOverrideProvider
{
    private readonly Dictionary<Insert, DynamicBlockOverrideSet> _overrides =
        new(ReferenceEqualityComparer.Instance);

    [Reactive]
    public partial int ChangeStamp { get; private set; }

    public DynamicBlockOverrideSet? GetBlockOverrides(BlockRecord block)
    {
        return null;
    }

    public DynamicBlockOverrideSet? GetInsertOverrides(Insert insert, BlockRecord block)
    {
        if (_overrides.TryGetValue(insert, out var overrides))
        {
            return overrides;
        }

        return null;
    }

    public DynamicBlockOverrideSet GetOrCreateOverrides(Insert insert)
    {
        if (!_overrides.TryGetValue(insert, out var overrides))
        {
            overrides = new DynamicBlockOverrideSet();
            _overrides[insert] = overrides;
        }

        return overrides;
    }

    public bool TryGetOverrides(Insert insert, out DynamicBlockOverrideSet overrides)
    {
        return _overrides.TryGetValue(insert, out overrides!);
    }

    public void ClearOverrides(Insert insert)
    {
        if (_overrides.Remove(insert))
        {
            NotifyChanged();
        }
    }

    public void ClearAll()
    {
        if (_overrides.Count == 0)
        {
            return;
        }

        _overrides.Clear();
        NotifyChanged();
    }

    public void NotifyChanged()
    {
        ChangeStamp++;
    }
}
