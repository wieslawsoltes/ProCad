using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using ACadSharp;
using CSMath;

namespace ProCad.Rendering;

public sealed class RenderCache : IRenderCache
{
    private readonly ConditionalWeakTable<CadDocument, DocumentCache> _documents = new();

    public bool TryGetGeometry(
        CadDocument document,
        RenderGeometryCacheKey key,
        out IReadOnlyList<XYZ> points)
    {
        points = null!;
        if (document is null)
        {
            return false;
        }

        var cache = _documents.GetOrCreateValue(document);
        if (!cache.Geometry.TryGetValue(key, out var cachedPoints) || cachedPoints is null)
        {
            return false;
        }

        points = cachedPoints;
        return true;
    }

    public void StoreGeometry(
        CadDocument document,
        RenderGeometryCacheKey key,
        IReadOnlyList<XYZ> points)
    {
        if (document is null || points is null)
        {
            return;
        }

        var cache = _documents.GetOrCreateValue(document);
        cache.Geometry[key] = points;
    }

    public bool TryGetTextLayout(
        CadDocument document,
        RenderTextLayoutCacheKey key,
        out RenderTextLayout layout)
    {
        layout = default;
        if (document is null)
        {
            return false;
        }

        var cache = _documents.GetOrCreateValue(document);
        return cache.TextLayouts.TryGetValue(key, out layout);
    }

    public void StoreTextLayout(
        CadDocument document,
        RenderTextLayoutCacheKey key,
        RenderTextLayout layout)
    {
        if (document is null)
        {
            return;
        }

        var cache = _documents.GetOrCreateValue(document);
        cache.TextLayouts[key] = layout;
    }

    public void Clear(CadDocument document)
    {
        if (document is null)
        {
            return;
        }

        if (_documents.TryGetValue(document, out var cache))
        {
            cache.Clear();
        }
    }

    private sealed class DocumentCache
    {
        public ConcurrentDictionary<RenderGeometryCacheKey, IReadOnlyList<XYZ>> Geometry { get; } = new();
        public ConcurrentDictionary<RenderTextLayoutCacheKey, RenderTextLayout> TextLayouts { get; } = new();

        public void Clear()
        {
            Geometry.Clear();
            TextLayouts.Clear();
        }
    }
}
