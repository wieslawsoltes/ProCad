using System;
using System.Linq;
using ProCad.Rendering;
using ACadSharp.Entities;
using CSMath;
using Xunit;

namespace ProCad.Tests.Rendering;

public sealed class RenderPaperSpaceTests
{
    [Fact]
    public void BuildScene_RendersViewportContentsInPaperSpace()
    {
        var document = new ACadSharp.CadDocument();
        var line = new Line
        {
            StartPoint = new XYZ(0, 0, 0),
            EndPoint = new XYZ(10, 0, 0)
        };
        document.Entities.Add(line);

        var paperViewport = new Viewport
        {
            Center = new XYZ(0, 0, 0),
            Width = 1.0,
            Height = 1.0,
            ViewCenter = new XY(0, 0),
            ViewHeight = 1.0
        };
        var viewport = new Viewport
        {
            Center = new XYZ(0, 0, 0),
            Width = 10.0,
            Height = 10.0,
            ViewCenter = new XY(5, 0),
            ViewHeight = 10.0
        };
        document.PaperSpace.Entities.Add(paperViewport);
        document.PaperSpace.Entities.Add(viewport);

        var settings = new CadRenderSceneSettings { IsPaperSpace = true };
        var scene = CreateSceneBuilder().Build(document, settings);
        var primitives = scene.Layers.SelectMany(layer => layer.Primitives).ToArray();
        var clip = primitives.OfType<RenderClipGroup>().FirstOrDefault();

        Assert.NotNull(clip);
        Assert.Contains(clip!.Primitives, primitive =>
            primitive is RenderLine || primitive is RenderPolyline);
        Assert.Contains(primitives, primitive =>
            primitive is RenderPolyline polyline &&
            polyline.IsClosed &&
            polyline.Points.Count == 4);
    }

    [Fact]
    public void BuildScene_UsesPaperBoundsForSceneExtents()
    {
        var document = new ACadSharp.CadDocument();
        document.PaperSpace.Entities.Clear();

        var layout = document.PaperSpace.Layout;
        layout.MinExtents = new XYZ(0, 0, 0);
        layout.MaxExtents = new XYZ(100, 50, 0);

        var settings = new CadRenderSceneSettings
        {
            IsPaperSpace = true,
            LayoutName = layout.Name
        };

        var scene = CreateSceneBuilder().Build(document, settings);

        Assert.True(scene.IsPaperSpace);
        Assert.NotNull(scene.PaperBounds);

        var paper = scene.PaperBounds!.Value;
        Assert.Equal(0f, paper.Min.X);
        Assert.Equal(0f, paper.Min.Y);
        Assert.Equal(100f, paper.Max.X);
        Assert.Equal(50f, paper.Max.Y);

        Assert.False(scene.Bounds.IsEmpty);
        Assert.True(scene.Bounds.Contains(paper.Min));
        Assert.True(scene.Bounds.Contains(paper.Max));
    }

    [Fact]
    public void BuildScene_PrefersLayoutPaperSizeOverHeaderExtents()
    {
        var document = new ACadSharp.CadDocument();
        document.PaperSpace.Entities.Clear();

        var layout = document.PaperSpace.Layout;
        layout.MinExtents = new XYZ(0, 0, 0);
        layout.MaxExtents = new XYZ(0, 0, 0);
        layout.PaperWidth = 420.0;
        layout.PaperHeight = 297.0;

        document.Header.PaperSpaceExtMin = new XYZ(0, 0, 0);
        document.Header.PaperSpaceExtMax = new XYZ(210, 297, 0);

        var settings = new CadRenderSceneSettings
        {
            IsPaperSpace = true,
            LayoutName = layout.Name
        };

        var scene = CreateSceneBuilder().Build(document, settings);

        Assert.NotNull(scene.PaperBounds);
        var bounds = scene.PaperBounds!.Value;
        Assert.Equal(0f, bounds.Min.X);
        Assert.Equal(0f, bounds.Min.Y);
        Assert.Equal(420f, bounds.Max.X);
        Assert.Equal(297f, bounds.Max.Y);
    }

    [Fact]
    public void BuildScene_UsesAnnotationScaleForViewportContent()
    {
        var document = new ACadSharp.CadDocument();
        var text = new MText
        {
            InsertPoint = new XYZ(0, 0, 0),
            Height = 1.0,
            Value = "Test",
            IsAnnotative = true
        };
        document.Entities.Add(text);

        var paperViewport = new Viewport
        {
            Center = new XYZ(0, 0, 0),
            Width = 1.0,
            Height = 1.0,
            ViewCenter = new XY(0, 0),
            ViewHeight = 1.0
        };
        var viewport = new Viewport
        {
            Center = new XYZ(0, 0, 0),
            Width = 10.0,
            Height = 10.0,
            ViewCenter = new XY(0, 0),
            ViewHeight = 10.0
        };
        document.PaperSpace.Entities.Add(paperViewport);
        document.PaperSpace.Entities.Add(viewport);

        var builder = CreateSceneBuilderForText();

        var scene1 = builder.Build(document, new CadRenderSceneSettings
        {
            IsPaperSpace = true,
            AnnotationScaleFactor = 1f
        });
        var scene2 = builder.Build(document, new CadRenderSceneSettings
        {
            IsPaperSpace = true,
            AnnotationScaleFactor = 2f
        });

        var height1 = ResolveFirstTextHeight(scene1);
        var height2 = ResolveFirstTextHeight(scene2);

        Assert.True(height2 > height1 * 1.9f);
        Assert.True(height2 < height1 * 2.1f);
    }

    [Fact]
    public void BuildScene_RendersEntitiesInsideTwistedViewport()
    {
        var document = new ACadSharp.CadDocument();
        var line = new Line
        {
            StartPoint = new XYZ(7, 0, 0),
            EndPoint = new XYZ(7, 1, 0)
        };
        document.Entities.Add(line);

        var paperViewport = new Viewport
        {
            Center = new XYZ(0, 0, 0),
            Width = 1.0,
            Height = 1.0,
            ViewCenter = new XY(0, 0),
            ViewHeight = 1.0
        };
        var viewport = new Viewport
        {
            Center = new XYZ(0, 0, 0),
            Width = 10.0,
            Height = 10.0,
            ViewCenter = new XY(0, 0),
            ViewHeight = 10.0,
            TwistAngle = Math.PI / 4.0
        };
        document.PaperSpace.Entities.Add(paperViewport);
        document.PaperSpace.Entities.Add(viewport);

        var settings = new CadRenderSceneSettings { IsPaperSpace = true };
        var scene = CreateSceneBuilder().Build(document, settings);

        Assert.Contains(scene.PrimitiveMetadata.Values, metadata =>
            ReferenceEquals(metadata.SourceEntity, line) || ReferenceEquals(metadata.OwnerEntity, line));
    }

    private static CadRenderSceneBuilder CreateSceneBuilder()
    {
        var handlers = new IRenderEntityHandler[]
        {
            new LineRenderHandler(),
            new ViewportRenderHandler(),
            new FallbackRenderHandler()
        };

        return new CadRenderSceneBuilder(
            new RenderEntityDispatcher(handlers),
            new DefaultRenderStyleResolver(),
            new DefaultRenderLinePatternResolver(),
            new DefaultRenderShapeResolver(),
            new DefaultRenderTextShaper(),
            new DefaultRenderEntityVisibilityResolver(),
            new DefaultRenderGeometrySampler(),
            new DefaultRenderEntityOrderResolver(),
            new RenderCacheStampProvider());
    }

    private static CadRenderSceneBuilder CreateSceneBuilderForText()
    {
        var handlers = new IRenderEntityHandler[]
        {
            new MTextRenderHandler(),
            new ViewportRenderHandler(),
            new FallbackRenderHandler()
        };

        return new CadRenderSceneBuilder(
            new RenderEntityDispatcher(handlers),
            new DefaultRenderStyleResolver(),
            new DefaultRenderLinePatternResolver(),
            new DefaultRenderShapeResolver(),
            new DefaultRenderTextShaper(),
            new DefaultRenderEntityVisibilityResolver(),
            new DefaultRenderGeometrySampler(),
            new DefaultRenderEntityOrderResolver(),
            new RenderCacheStampProvider());
    }

    private static float ResolveFirstTextHeight(RenderScene scene)
    {
        var text = scene.Layers
            .SelectMany(layer => layer.Primitives)
            .OfType<RenderClipGroup>()
            .SelectMany(group => group.Primitives)
            .OfType<RenderText>()
            .FirstOrDefault();

        Assert.NotNull(text);
        return text!.LayoutHeight;
    }
}
