using System;
using System.Collections.Generic;
using ACadSharp.Entities;
using ACadSharp.Objects;
using ACadSharp.Tables;

namespace ProCad.Rendering;

public sealed class DefaultRenderEntityOrderResolver : IRenderEntityOrderResolver
{
    public IReadOnlyList<Entity> OrderEntities(IEnumerable<Entity> entities, BlockRecord? block)
    {
        if (entities is null)
        {
            return Array.Empty<Entity>();
        }

        var list = entities as IList<Entity> ?? new List<Entity>(entities);
        if (list.Count <= 1)
        {
            return list as IReadOnlyList<Entity> ?? new List<Entity>(list);
        }

        var sortTable = block?.SortEntitiesTable;
        var items = new EntityOrder[list.Count];
        if (sortTable is null)
        {
            for (var i = 0; i < list.Count; i++)
            {
                var entity = list[i];
                items[i] = new EntityOrder(entity, i, entity?.Handle ?? 0);
            }
        }
        else
        {
            for (var i = 0; i < list.Count; i++)
            {
                var entity = list[i];
                var key = entity is null ? 0 : sortTable.GetSorterHandle(entity);
                items[i] = new EntityOrder(entity, i, key);
            }
        }

        Array.Sort(items, static (left, right) =>
        {
            var order = left.SortKey.CompareTo(right.SortKey);
            return order != 0 ? order : left.Index.CompareTo(right.Index);
        });

        var ordered = new List<Entity>(items.Length);
        for (var i = 0; i < items.Length; i++)
        {
            var entity = items[i].Entity;
            if (entity is not null)
            {
                ordered.Add(entity);
            }
        }

        return ordered;
    }

    private readonly struct EntityOrder
    {
        public Entity? Entity { get; }
        public int Index { get; }
        public ulong SortKey { get; }

        public EntityOrder(Entity? entity, int index, ulong sortKey)
        {
            Entity = entity;
            Index = index;
            SortKey = sortKey;
        }
    }
}
