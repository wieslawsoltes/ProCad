using System.Collections.Generic;
using ACadSharp.Entities;
using ACadSharp.Objects.Evaluations;
using ACadSharp.Tables;
using ACadSharp.XData;

namespace ACadInspector.Rendering;

internal sealed class DynamicBlockVisibilityFilter
{
    private readonly HashSet<ulong> _controlledKeys;
    private readonly HashSet<ulong> _stateKeys;
    private readonly HashSet<Entity> _controlledEntities;
    private readonly HashSet<Entity> _stateEntities;

    public DynamicBlockVisibilityFilter(
        BlockVisibilityParameter parameter,
        BlockVisibilityParameter.State state)
    {
        _controlledKeys = new HashSet<ulong>();
        _stateKeys = new HashSet<ulong>();
        _controlledEntities = new HashSet<Entity>(ReferenceEqualityComparer.Instance);
        _stateEntities = new HashSet<Entity>(ReferenceEqualityComparer.Instance);

        foreach (var entity in parameter.Entities)
        {
            var key = ResolveEntityKey(entity);
            if (key != 0)
            {
                _controlledKeys.Add(key);
            }
            else
            {
                _controlledEntities.Add(entity);
            }
        }

        foreach (var entity in state.Entities)
        {
            var key = ResolveEntityKey(entity);
            if (key != 0)
            {
                _stateKeys.Add(key);
            }
            else
            {
                _stateEntities.Add(entity);
            }
        }
    }

    public bool IsVisible(Entity entity)
    {
        if (entity is null)
        {
            return false;
        }

        var key = ResolveEntityKey(entity);
        if (key != 0)
        {
            if (!_controlledKeys.Contains(key))
            {
                return true;
            }

            return _stateKeys.Contains(key);
        }

        if (!_controlledEntities.Contains(entity))
        {
            return true;
        }

        return _stateEntities.Contains(entity);
    }

    private static ulong ResolveEntityKey(Entity entity)
    {
        if (TryGetBlockRepHandle(entity, out var handle))
        {
            return handle;
        }

        return entity.Handle;
    }

    internal static bool TryGetBlockRepHandle(Entity entity, out ulong handle)
    {
        handle = 0;
        if (entity.ExtendedData is null)
        {
            return false;
        }

        if (!entity.ExtendedData.TryGet(AppId.BlockRepETag, out var data))
        {
            return false;
        }

        foreach (var record in data.Records)
        {
            if (record is ExtendedDataHandle handleRecord)
            {
                handle = handleRecord.Value;
                return handle != 0;
            }
        }

        return false;
    }
}
