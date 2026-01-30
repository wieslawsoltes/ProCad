using System.Collections.Generic;
using ACadInspector.Rendering;
using ACadSharp;
using ACadSharp.Entities;
using CSMath;
using Xunit;

namespace ACadInspector.Tests.Rendering;

public sealed class RenderCacheTests
{
    [Fact]
    public void CachedGeometrySampler_ReusesSamplesPerEntity()
    {
        var document = new CadDocument();
        var circle = new Circle
        {
            Center = new XYZ(0, 0, 0),
            Radius = 5
        };
        document.Entities.Add(circle);

        var cache = new RenderCache();
        var inner = new CountingGeometrySampler();
        var stampProvider = new RenderCacheStampProvider();
        stampProvider.AdvanceStamp(document);
        var sampler = new CachedRenderGeometrySampler(inner, cache, stampProvider);

        var first = sampler.SampleCircle(circle, 16);
        var second = sampler.SampleCircle(circle, 16);

        Assert.Same(first, second);
        Assert.Equal(1, inner.CircleCalls);
    }

    [Fact]
    public void CachedGeometrySampler_ResetsWhenStampChanges()
    {
        var document = new CadDocument();
        var circle = new Circle
        {
            Center = new XYZ(0, 0, 0),
            Radius = 5
        };
        document.Entities.Add(circle);

        var cache = new RenderCache();
        var inner = new CountingGeometrySampler();
        var stampProvider = new RenderCacheStampProvider();
        stampProvider.AdvanceStamp(document);
        var sampler = new CachedRenderGeometrySampler(inner, cache, stampProvider);

        sampler.SampleCircle(circle, 16);
        stampProvider.AdvanceStamp(document);
        sampler.SampleCircle(circle, 16);

        Assert.Equal(2, inner.CircleCalls);
    }

    [Fact]
    public void CacheStampProvider_TracksEntityEdits()
    {
        var document = new CadDocument();
        var stampProvider = new RenderCacheStampProvider();
        var initial = stampProvider.GetStamp(document);

        document.Entities.Add(new Line
        {
            StartPoint = new XYZ(0, 0, 0),
            EndPoint = new XYZ(1, 0, 0)
        });

        var updated = stampProvider.GetStamp(document);

        Assert.True(updated > initial);
    }

    [Fact]
    public void CachedTextShaper_ReusesLayoutPerEntity()
    {
        var document = new CadDocument();
        var text = new TextEntity
        {
            Value = "CACHE",
            Height = 2.0,
            InsertPoint = new XYZ(0, 0, 0)
        };
        document.Entities.Add(text);

        var cache = new RenderCache();
        var inner = new CountingTextShaper();
        var shaper = new CachedRenderTextShaper(inner, cache);
        var settings = new CadRenderSceneSettings();

        var first = shaper.Shape(text, settings);
        var second = shaper.Shape(text, settings);

        Assert.Equal(first, second);
        Assert.Equal(1, inner.TextCalls);
    }

    [Fact]
    public void CachedTextShaper_ReusesLayoutAcrossEntities()
    {
        var document = new CadDocument();
        var textA = new TextEntity
        {
            Value = "CACHE",
            Height = 2.0,
            InsertPoint = new XYZ(0, 0, 0)
        };
        var textB = new TextEntity
        {
            Value = "CACHE",
            Height = 2.0,
            InsertPoint = new XYZ(1, 0, 0)
        };
        document.Entities.Add(textA);
        document.Entities.Add(textB);

        var cache = new RenderCache();
        var inner = new CountingTextShaper();
        var shaper = new CachedRenderTextShaper(inner, cache);
        var settings = new CadRenderSceneSettings();

        shaper.Shape(textA, settings);
        shaper.Shape(textB, settings);

        Assert.Equal(1, inner.TextCalls);
    }

    [Fact]
    public void CachedTextShaper_SeparatesMTextByRectangleWidth()
    {
        var document = new CadDocument();
        var textA = new MText
        {
            Value = "MTEXT",
            Height = 2.0,
            RectangleWidth = 10.0,
            InsertPoint = new XYZ(0, 0, 0)
        };
        var textB = new MText
        {
            Value = "MTEXT",
            Height = 2.0,
            RectangleWidth = 25.0,
            InsertPoint = new XYZ(0, 1, 0)
        };
        document.Entities.Add(textA);
        document.Entities.Add(textB);

        var cache = new RenderCache();
        var inner = new CountingTextShaper();
        var shaper = new CachedRenderTextShaper(inner, cache);
        var settings = new CadRenderSceneSettings();

        shaper.Shape(textA, settings);
        shaper.Shape(textB, settings);

        Assert.Equal(2, inner.MTextCalls);
    }

    private sealed class CountingGeometrySampler : IRenderGeometrySampler
    {
        public int CircleCalls { get; private set; }

        public IReadOnlyList<XYZ> SampleCircle(Circle circle, int precision)
        {
            CircleCalls++;
            return new List<XYZ>
            {
                new(0, 0, 0),
                new(1, 0, 0)
            };
        }

        public IReadOnlyList<XYZ> SampleArc(Arc arc, int precision)
        {
            return new List<XYZ> { new(0, 0, 0) };
        }

        public IReadOnlyList<XYZ> SampleEllipse(Ellipse ellipse, int precision)
        {
            return new List<XYZ> { new(0, 0, 0) };
        }

        public IReadOnlyList<XYZ> SampleSpline(Spline spline, int precision)
        {
            return new List<XYZ> { new(0, 0, 0) };
        }

        public IReadOnlyList<XYZ> SamplePolyline(IPolyline polyline, int precision)
        {
            return new List<XYZ> { new(0, 0, 0) };
        }
    }

    private sealed class CountingTextShaper : IRenderTextShaper
    {
        public int TextCalls { get; private set; }
        public int MTextCalls { get; private set; }

        public RenderTextLayout Shape(TextEntity text, CadRenderSceneSettings settings)
        {
            TextCalls++;
            return new RenderTextLayout(text.Value ?? string.Empty, 10f, 2f);
        }

        public RenderTextLayout Shape(MText text, CadRenderSceneSettings settings)
        {
            MTextCalls++;
            return new RenderTextLayout(text.PlainText ?? string.Empty, 10f, 2f);
        }
    }
}
