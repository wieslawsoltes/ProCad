using System.Collections.Generic;
using ACadSharp;
using CSMath;

namespace ProCad.Rendering;

public interface IRenderCache
{
    bool TryGetGeometry(
        CadDocument document,
        RenderGeometryCacheKey key,
        out IReadOnlyList<XYZ> points);

    void StoreGeometry(
        CadDocument document,
        RenderGeometryCacheKey key,
        IReadOnlyList<XYZ> points);

    bool TryGetTextLayout(
        CadDocument document,
        RenderTextLayoutCacheKey key,
        out RenderTextLayout layout);

    void StoreTextLayout(
        CadDocument document,
        RenderTextLayoutCacheKey key,
        RenderTextLayout layout);

    void Clear(CadDocument document);
}
