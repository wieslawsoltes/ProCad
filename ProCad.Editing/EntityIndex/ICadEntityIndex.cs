using ProCad.Editing.Identifiers;
using ACadSharp.Entities;

namespace ProCad.Editing.EntityIndex;

public interface ICadEntityIndex
{
    int Count { get; }

    CadEntityId Register(Entity entity, CadEntityId? preferredId = null);
    bool Unregister(Entity entity);
    bool TryGetEntity(CadEntityId id, out Entity entity);
    bool TryGetId(Entity entity, out CadEntityId id);
    bool TryGetByHandle(ulong handle, out Entity entity, out CadEntityId id);
    void Clear();
}
