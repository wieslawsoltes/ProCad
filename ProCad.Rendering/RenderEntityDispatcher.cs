using System;
using System.Collections.Generic;
using ACadSharp.Entities;
using CSMath;

namespace ProCad.Rendering;

public sealed class RenderEntityDispatcher : IRenderEntityDispatcher
{
    private readonly IRenderEntityHandler[] _handlers;

    public RenderEntityDispatcher(IEnumerable<IRenderEntityHandler> handlers)
    {
        if (handlers is null)
        {
            throw new ArgumentNullException(nameof(handlers));
        }

        _handlers = ToArray(handlers);
    }

    public void Append(Entity entity, Transform transform, RenderBuildContext context)
    {
        using var scope = context.SelectionContext.EnterEntity(entity);
        for (var i = 0; i < _handlers.Length; i++)
        {
            var handler = _handlers[i];
            if (handler.CanHandle(entity))
            {
                handler.Append(entity, transform, context);
                return;
            }
        }
    }

    private static IRenderEntityHandler[] ToArray(IEnumerable<IRenderEntityHandler> handlers)
    {
        if (handlers is ICollection<IRenderEntityHandler> collection)
        {
            var array = new IRenderEntityHandler[collection.Count];
            collection.CopyTo(array, 0);
            return array;
        }

        var list = new List<IRenderEntityHandler>();
        foreach (var handler in handlers)
        {
            list.Add(handler);
        }

        return list.ToArray();
    }
}
