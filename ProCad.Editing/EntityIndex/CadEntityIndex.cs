using ProCad.Editing.Identifiers;
using ACadSharp.Entities;

namespace ProCad.Editing.EntityIndex;

public sealed class CadEntityIndex : ICadEntityIndex
{
    private readonly Dictionary<CadEntityId, Entity> _byId = new();
    private readonly Dictionary<Entity, CadEntityId> _byEntity = new(System.Collections.Generic.ReferenceEqualityComparer.Instance);
    private readonly Dictionary<ulong, CadEntityId> _byHandle = new();

    public int Count => _byId.Count;

    public CadEntityId Register(Entity entity, CadEntityId? preferredId = null)
    {
        ArgumentNullException.ThrowIfNull(entity);

        if (_byEntity.TryGetValue(entity, out var existingId))
        {
            return existingId;
        }

        var id = preferredId.GetValueOrDefault();
        if (id.IsEmpty)
        {
            id = CadEntityId.New();
        }

        while (_byId.ContainsKey(id))
        {
            id = CadEntityId.New();
        }

        _byId[id] = entity;
        _byEntity[entity] = id;
        if (entity.Handle != 0)
        {
            _byHandle[entity.Handle] = id;
        }

        return id;
    }

    public bool Unregister(Entity entity)
    {
        ArgumentNullException.ThrowIfNull(entity);

        if (!_byEntity.TryGetValue(entity, out var id))
        {
            return false;
        }

        _byEntity.Remove(entity);
        _byId.Remove(id);
        if (entity.Handle != 0)
        {
            _byHandle.Remove(entity.Handle);
        }

        return true;
    }

    public bool TryGetEntity(CadEntityId id, out Entity entity)
    {
        return _byId.TryGetValue(id, out entity!);
    }

    public bool TryGetId(Entity entity, out CadEntityId id)
    {
        return _byEntity.TryGetValue(entity, out id);
    }

    public bool TryGetByHandle(ulong handle, out Entity entity, out CadEntityId id)
    {
        if (_byHandle.TryGetValue(handle, out id) && _byId.TryGetValue(id, out entity!))
        {
            return true;
        }

        entity = null!;
        id = default;
        return false;
    }

    public void Clear()
    {
        _byId.Clear();
        _byEntity.Clear();
        _byHandle.Clear();
    }
}
