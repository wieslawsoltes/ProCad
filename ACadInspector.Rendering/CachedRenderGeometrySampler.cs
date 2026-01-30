using System.Collections.Generic;
using ACadSharp;
using ACadSharp.Entities;
using CSMath;

namespace ACadInspector.Rendering;

public sealed class CachedRenderGeometrySampler : IRenderGeometrySampler
{
    private readonly IRenderGeometrySampler _inner;
    private readonly IRenderCache _cache;
    private readonly IRenderCacheStampProvider _stampProvider;

    public CachedRenderGeometrySampler(
        IRenderGeometrySampler inner,
        IRenderCache cache,
        IRenderCacheStampProvider stampProvider)
    {
        _inner = inner;
        _cache = cache;
        _stampProvider = stampProvider;
    }

    public IReadOnlyList<XYZ> SampleCircle(Circle circle, int precision)
    {
        if (!TryGetGeometry(circle, RenderGeometryKind.Circle, precision, out var cached))
        {
            var sampled = _inner.SampleCircle(circle, precision);
            cached = CopyPoints(sampled);
            StoreGeometry(circle, RenderGeometryKind.Circle, precision, cached);
        }

        return cached;
    }

    public IReadOnlyList<XYZ> SampleArc(Arc arc, int precision)
    {
        if (!TryGetGeometry(arc, RenderGeometryKind.Arc, precision, out var cached))
        {
            var sampled = _inner.SampleArc(arc, precision);
            cached = CopyPoints(sampled);
            StoreGeometry(arc, RenderGeometryKind.Arc, precision, cached);
        }

        return cached;
    }

    public IReadOnlyList<XYZ> SampleEllipse(Ellipse ellipse, int precision)
    {
        if (!TryGetGeometry(ellipse, RenderGeometryKind.Ellipse, precision, out var cached))
        {
            var sampled = _inner.SampleEllipse(ellipse, precision);
            cached = CopyPoints(sampled);
            StoreGeometry(ellipse, RenderGeometryKind.Ellipse, precision, cached);
        }

        return cached;
    }

    public IReadOnlyList<XYZ> SampleSpline(Spline spline, int precision)
    {
        if (!TryGetGeometry(spline, RenderGeometryKind.Spline, precision, out var cached))
        {
            var sampled = _inner.SampleSpline(spline, precision);
            cached = CopyPoints(sampled);
            StoreGeometry(spline, RenderGeometryKind.Spline, precision, cached);
        }

        return cached;
    }

    public IReadOnlyList<XYZ> SamplePolyline(IPolyline polyline, int precision)
    {
        if (!TryGetGeometry(polyline as CadObject, RenderGeometryKind.Polyline, precision, out var cached))
        {
            var sampled = _inner.SamplePolyline(polyline, precision);
            cached = CopyPoints(sampled);
            StoreGeometry(polyline as CadObject, RenderGeometryKind.Polyline, precision, cached);
        }

        return cached;
    }

    private bool TryGetGeometry(
        CadObject? entity,
        RenderGeometryKind kind,
        int precision,
        out IReadOnlyList<XYZ> points)
    {
        points = null!;
        if (entity is null || entity.Document is null || entity.Handle == 0 || precision <= 0)
        {
            return false;
        }

        var stamp = _stampProvider.GetStamp(entity.Document);
        var key = new RenderGeometryCacheKey(entity.Handle, kind, precision, stamp);
        return _cache.TryGetGeometry(entity.Document, key, out points);
    }

    private void StoreGeometry(
        CadObject? entity,
        RenderGeometryKind kind,
        int precision,
        IReadOnlyList<XYZ> points)
    {
        if (entity is null || entity.Document is null || entity.Handle == 0 || precision <= 0)
        {
            return;
        }

        var stamp = _stampProvider.GetStamp(entity.Document);
        var key = new RenderGeometryCacheKey(entity.Handle, kind, precision, stamp);
        _cache.StoreGeometry(entity.Document, key, points);
    }

    private static IReadOnlyList<XYZ> CopyPoints(IReadOnlyList<XYZ> points)
    {
        if (points.Count == 0)
        {
            return System.Array.Empty<XYZ>();
        }

        var result = new XYZ[points.Count];
        for (var i = 0; i < points.Count; i++)
        {
            result[i] = points[i];
        }

        return result;
    }
}
