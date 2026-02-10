using System;
using System.Collections.Generic;
using ACadInspector.Rendering;
using ACadSharp.Entities;
using ReactiveUI;
using ReactiveUI.SourceGenerators;

namespace ACadInspector.Services;

public sealed partial class CadSelectionAnnotationService : ReactiveObject
{
    private const int MaxAnnotationPrimitives = 2048;
    private Dictionary<Entity, RenderBounds>? _entityBounds;
    private Dictionary<Entity, List<IRenderPrimitive>>? _entityGeometry;

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
            _entityGeometry = null;
            HoverAnnotation = null;
            SelectionAnnotation = null;
            HoveredObject = null;
            return;
        }

        var map = new Dictionary<Entity, RenderBounds>(ReferenceEqualityComparer.Instance);
        var geometry = new Dictionary<Entity, List<IRenderPrimitive>>(ReferenceEqualityComparer.Instance);
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

            if (!geometry.TryGetValue(entity, out var list))
            {
                list = new List<IRenderPrimitive>();
                geometry[entity] = list;
            }

            list.Add(kvp.Key);
        }

        _entityBounds = map;
        _entityGeometry = geometry;
    }

    public void ClearHover()
    {
        HoverAnnotation = null;
        HoveredObject = null;
    }

    public void UpdateHover(Entity? entity, RenderBounds? fallbackBounds)
    {
        HoveredObject = entity;
        HoverAnnotation = BuildAnnotation(
            RenderAnnotationKind.Hover,
            entity,
            fallbackBounds,
            geometryOverride: null,
            useSceneGeometry: true);
    }

    public void UpdateSelection(object? selected, RenderBounds? fallbackBounds)
    {
        UpdateSelection(selected, fallbackBounds, useSceneGeometry: true);
    }

    public void UpdateSelection(object? selected, RenderBounds? fallbackBounds, bool useSceneGeometry)
    {
        var entity = selected as Entity;
        SelectionAnnotation = BuildAnnotation(
            RenderAnnotationKind.Selection,
            entity,
            fallbackBounds,
            geometryOverride: null,
            useSceneGeometry);
    }

    public void UpdateHover(Entity? entity, RenderBounds? fallbackBounds, IRenderPrimitive? primitive)
    {
        HoveredObject = entity;
        var geometryOverride = primitive is null ? null : new[] { primitive };
        HoverAnnotation = BuildAnnotation(
            RenderAnnotationKind.Hover,
            entity,
            fallbackBounds,
            geometryOverride,
            useSceneGeometry: true);
    }

    public void UpdateSelection(Entity? entity, RenderBounds? fallbackBounds, IRenderPrimitive? primitive)
    {
        UpdateSelection(entity, fallbackBounds, primitive, useSceneGeometry: true);
    }

    public void UpdateSelection(Entity? entity, RenderBounds? fallbackBounds, IRenderPrimitive? primitive, bool useSceneGeometry)
    {
        var geometryOverride = primitive is null ? null : new[] { primitive };
        SelectionAnnotation = BuildAnnotation(
            RenderAnnotationKind.Selection,
            entity,
            fallbackBounds,
            geometryOverride,
            useSceneGeometry);
    }

    public bool TryGetBounds(Entity entity, out RenderBounds bounds)
    {
        if (_entityBounds is not null && _entityBounds.TryGetValue(entity, out bounds))
        {
            return true;
        }

        bounds = default;
        return false;
    }

    private RenderAnnotation? BuildAnnotation(
        RenderAnnotationKind kind,
        Entity? entity,
        RenderBounds? fallbackBounds,
        IReadOnlyList<IRenderPrimitive>? geometryOverride,
        bool useSceneGeometry)
    {
        if (entity is null)
        {
            return null;
        }

        var bounds = ResolveEntityBounds(entity, fallbackBounds, preferFallbackBounds: !useSceneGeometry);
        if (!bounds.HasValue || bounds.Value.IsEmpty)
        {
            return null;
        }

        var label = BuildLabel(entity);
        var style = kind == RenderAnnotationKind.Selection
            ? RenderAnnotationStyle.Selection
            : RenderAnnotationStyle.Hover;
        var geometry = geometryOverride ?? (useSceneGeometry ? ResolveEntityGeometry(entity) : null);
        return new RenderAnnotation(kind, bounds.Value, label, style, geometry);
    }

    private RenderBounds? ResolveEntityBounds(Entity entity, RenderBounds? fallback, bool preferFallbackBounds)
    {
        if (preferFallbackBounds && fallback.HasValue)
        {
            return fallback;
        }

        if (_entityBounds is not null && _entityBounds.TryGetValue(entity, out var bounds))
        {
            return bounds;
        }

        return fallback;
    }

    private IReadOnlyList<IRenderPrimitive>? ResolveEntityGeometry(Entity entity)
    {
        if (_entityGeometry is null || !_entityGeometry.TryGetValue(entity, out var list))
        {
            return null;
        }

        if (list.Count == 0 || list.Count > MaxAnnotationPrimitives)
        {
            return null;
        }

        return list;
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
