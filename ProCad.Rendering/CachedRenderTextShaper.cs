using ACadSharp;
using ACadSharp.Entities;

namespace ProCad.Rendering;

public sealed class CachedRenderTextShaper : IRenderTextShaper
{
    private readonly IRenderTextShaper _inner;
    private readonly IRenderCache _cache;

    public CachedRenderTextShaper(IRenderTextShaper inner, IRenderCache cache)
    {
        _inner = inner;
        _cache = cache;
    }

    public RenderTextLayout Shape(TextEntity text, CadRenderSceneSettings settings)
    {
        if (!TryGetLayout(text, settings, out var cached))
        {
            cached = _inner.Shape(text, settings);
            StoreLayout(text, settings, cached);
        }

        return cached;
    }

    public RenderTextLayout Shape(MText text, CadRenderSceneSettings settings)
    {
        if (!TryGetLayout(text, settings, out var cached))
        {
            cached = _inner.Shape(text, settings);
            StoreLayout(text, settings, cached);
        }

        return cached;
    }

    private bool TryGetLayout(TextEntity text, CadRenderSceneSettings settings, out RenderTextLayout layout)
    {
        layout = default;
        if (!CanCache(text, settings, out var key))
        {
            return false;
        }

        return _cache.TryGetTextLayout(text.Document!, key, out layout);
    }

    private bool TryGetLayout(MText text, CadRenderSceneSettings settings, out RenderTextLayout layout)
    {
        layout = default;
        if (!CanCache(text, settings, out var key))
        {
            return false;
        }

        return _cache.TryGetTextLayout(text.Document!, key, out layout);
    }

    private void StoreLayout(TextEntity text, CadRenderSceneSettings settings, RenderTextLayout layout)
    {
        if (!CanCache(text, settings, out var key))
        {
            return;
        }

        _cache.StoreTextLayout(text.Document!, key, layout);
    }

    private void StoreLayout(MText text, CadRenderSceneSettings settings, RenderTextLayout layout)
    {
        if (!CanCache(text, settings, out var key))
        {
            return;
        }

        _cache.StoreTextLayout(text.Document!, key, layout);
    }

    private static bool CanCache(TextEntity text, CadRenderSceneSettings settings, out RenderTextLayoutCacheKey key)
    {
        key = default;
        if (text.Document is null)
        {
            return false;
        }

        var value = text.Value ?? string.Empty;
        var height = RenderTextUtils.ResolveTextHeight(text);
        var styleHandle = text.Style?.Handle ?? 0;
        key = new RenderTextLayoutCacheKey(
            RenderTextLayoutKind.Text,
            value,
            height,
            lineSpacing: 0f,
            settingsWidthFactor: settings.TextWidthFactor,
            styleHandle: styleHandle,
            maxWidth: 0f);
        return true;
    }

    private static bool CanCache(MText text, CadRenderSceneSettings settings, out RenderTextLayoutCacheKey key)
    {
        key = default;
        if (text.Document is null)
        {
            return false;
        }

        var value = text.PlainText ?? string.Empty;
        var height = (float)text.Height;
        var styleHandle = text.Style?.Handle ?? 0;
        var lineSpacing = (float)text.LineSpacing;
        var maxWidth = text.RectangleWidth > 0 ? (float)text.RectangleWidth : 0f;
        key = new RenderTextLayoutCacheKey(
            RenderTextLayoutKind.MText,
            value,
            height,
            lineSpacing,
            settings.TextWidthFactor,
            styleHandle,
            maxWidth);
        return true;
    }
}
