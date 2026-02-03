using System;
using System.Collections.Generic;
using ACadInspector.Rendering;
using ACadSharp.Entities;
using ReactiveUI;
using ReactiveUI.SourceGenerators;

namespace ACadInspector.Services;

public sealed partial class CadSelectionAnnotationService : ReactiveObject
{
    private Dictionary<Entity, RenderBounds>? _entityBounds;

    [Reactive]
    public partial RenderAnnotation? HoverAnnotation { get; private set; }

    [Reactive]
    public partial RenderAnnotation? SelectionAnnotation { get; private set; }

    [Reactive]
    public partial object? HoveredObject { get; private set; }

    public void UpdateScene(RenderScene? scene)
    {
        if (scene is null)
        {
            _entityBounds = null;
            HoverAnnotation = null;
            SelectionAnnotation = null;
            HoveredObject = null;
            return;
        }

        var map = new Dictionary<Entity, RenderBounds>(ReferenceEqualityComparer.Instance);
        foreach (var kvp in scene.PrimitiveMetadata)
        {
            var entity = kvp.Value.OwnerEntity ?? kvp.Value.SourceEntity;
            if (entity is null)
            {
                continue;
            }

            if (map.TryGetValue(entity, out var bounds))
            {
                map[entity] = bounds.Expand(kvp.Key.Bounds);
            }
            else
            {
                map[entity] = kvp.Key.Bounds;
            }
        }

        _entityBounds = map;
    }

    public void ClearHover()
    {
        HoverAnnotation = null;
        HoveredObject = null;
    }

    public void UpdateHover(Entity? entity, RenderBounds? fallbackBounds)
    {
        HoveredObject = entity;
        HoverAnnotation = BuildAnnotation(RenderAnnotationKind.Hover, entity, fallbackBounds);
    }

    public void UpdateSelection(object? selected, RenderBounds? fallbackBounds)
    {
        var entity = selected as Entity;
        SelectionAnnotation = BuildAnnotation(RenderAnnotationKind.Selection, entity, fallbackBounds);
    }

    private RenderAnnotation? BuildAnnotation(RenderAnnotationKind kind, Entity? entity, RenderBounds? fallbackBounds)
    {
        if (entity is null)
        {
            return null;
        }

        var bounds = ResolveEntityBounds(entity, fallbackBounds);
        if (!bounds.HasValue || bounds.Value.IsEmpty)
        {
            return null;
        }

        var label = BuildLabel(entity);
        var style = kind == RenderAnnotationKind.Selection
            ? RenderAnnotationStyle.Selection
            : RenderAnnotationStyle.Hover;
        return new RenderAnnotation(kind, bounds.Value, label, style);
    }

    private RenderBounds? ResolveEntityBounds(Entity entity, RenderBounds? fallback)
    {
        if (_entityBounds is not null && _entityBounds.TryGetValue(entity, out var bounds))
        {
            return bounds;
        }

        return fallback;
    }

    private static string BuildLabel(Entity entity)
    {
        var name = string.IsNullOrWhiteSpace(entity.ObjectName)
            ? entity.GetType().Name
            : entity.ObjectName;
        var handle = entity.Handle.ToString("X");
        return string.IsNullOrWhiteSpace(handle)
            ? name
            : $"{name} [{handle}]";
    }
}
