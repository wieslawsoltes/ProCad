using System.Linq;
using ProCad.Rendering;
using ACadSharp;
using ACadSharp.Entities;
using ACadSharp.Header;
using ACadSharp.Tables;
using CSMath;
using Xunit;

namespace ProCad.Tests.Rendering;

public sealed class RenderLinePatternTests
{
    [Fact]
    public void BuildScene_SplitsDashedLineIntoSegments()
    {
        var document = new CadDocument();
        var dashLineType = new LineType("DASH");
        dashLineType.AddSegment(new LineType.Segment { Length = 1.0 });
        dashLineType.AddSegment(new LineType.Segment { Length = -0.5 });
        document.LineTypes.Add(dashLineType);

        var line = new Line
        {
            StartPoint = new XYZ(0, 0, 0),
            EndPoint = new XYZ(10, 0, 0),
            LineType = dashLineType
        };
        document.Entities.Add(line);

        var sceneBuilder = CreateSceneBuilder();
        var scene = sceneBuilder.Build(document, new CadRenderSceneSettings());
        var primitives = scene.Layers.SelectMany(layer => layer.Primitives).ToArray();
        var hasDashPattern = primitives.OfType<RenderLine>().Any(static line => line.HasDashPattern) ||
                             primitives.OfType<RenderPolyline>().Any(static polyline => polyline.HasDashPattern);

        Assert.True(primitives.Length > 1 || hasDashPattern);
    }

    [Fact]
    public void BuildScene_DisablesDashPatternRendering_WhenOptionOff()
    {
        var document = new CadDocument();
        var dashLineType = new LineType("DASH");
        dashLineType.AddSegment(new LineType.Segment { Length = 1.0 });
        dashLineType.AddSegment(new LineType.Segment { Length = -0.5 });
        document.LineTypes.Add(dashLineType);

        var line = new Line
        {
            StartPoint = new XYZ(0, 0, 0),
            EndPoint = new XYZ(10, 0, 0),
            LineType = dashLineType
        };
        document.Entities.Add(line);

        var sceneBuilder = CreateSceneBuilder();
        var scene = sceneBuilder.Build(document, new CadRenderSceneSettings
        {
            EnableDashPatternRendering = false
        });
        var lines = scene.Layers.SelectMany(layer => layer.Primitives).OfType<RenderLine>().ToArray();

        var renderLine = Assert.Single(lines);
        Assert.False(renderLine.HasDashPattern);
    }

    [Fact]
    public void BuildScene_AddsLineTypeTextDecoration()
    {
        var document = new CadDocument();
        var lineType = new LineType("TEXTLINE");
        lineType.AddSegment(new LineType.Segment
        {
            Length = 1.0,
            Flags = LineTypeShapeFlags.Text,
            Text = "X",
            Scale = 1.0
        });
        lineType.AddSegment(new LineType.Segment { Length = -0.5 });
        document.LineTypes.Add(lineType);

        var line = new Line
        {
            StartPoint = new XYZ(0, 0, 0),
            EndPoint = new XYZ(10, 0, 0),
            LineType = lineType
        };
        document.Entities.Add(line);

        var scene = CreateSceneBuilder().Build(document, new CadRenderSceneSettings());
        var primitives = scene.Layers.SelectMany(layer => layer.Primitives).ToArray();

        Assert.Contains(primitives, primitive => primitive is RenderText);
    }

    [Fact]
    public void BuildScene_AddsLineTypeShapeDecoration()
    {
        var document = new CadDocument();
        var lineType = new LineType("SHAPELINE");
        lineType.AddSegment(new LineType.Segment
        {
            Length = 1.0,
            Flags = LineTypeShapeFlags.Shape,
            ShapeNumber = 1,
            Scale = 1.0
        });
        lineType.AddSegment(new LineType.Segment { Length = -0.5 });
        document.LineTypes.Add(lineType);

        var line = new Line
        {
            StartPoint = new XYZ(0, 0, 0),
            EndPoint = new XYZ(10, 0, 0),
            LineType = lineType
        };
        document.Entities.Add(line);

        var scene = CreateSceneBuilder().Build(document, new CadRenderSceneSettings());
        var primitives = scene.Layers.SelectMany(layer => layer.Primitives).ToArray();

        Assert.Contains(primitives, primitive => primitive is RenderLine);
    }

    [Fact]
    public void BuildScene_ShapeLineTypeScaledBelowMinimum_CompletesAsContinuousLine()
    {
        // Mirrors the BATTING insulation linetype in Autodesk's "Architectural - Annotation
        // Scaling and Multileaders" sample: once the model-space viewport scale and a small entity
        // linetype scale are applied, every segment collapses to the resolver's minimum length,
        // which previously made the stroker loop forever.
        var document = new CadDocument();
        var lineType = new LineType("BATTING");
        lineType.AddSegment(new LineType.Segment { Length = 0.0001 });
        lineType.AddSegment(new LineType.Segment { Length = -0.1, Flags = LineTypeShapeFlags.Shape, ShapeNumber = 134, Scale = 0.1 });
        lineType.AddSegment(new LineType.Segment { Length = -0.2, Flags = LineTypeShapeFlags.Shape, ShapeNumber = 134, Scale = 0.1 });
        lineType.AddSegment(new LineType.Segment { Length = -0.1 });
        document.LineTypes.Add(lineType);

        var line = new Line
        {
            StartPoint = new XYZ(0, 0, 0),
            EndPoint = new XYZ(500, 0, 0),
            LineType = lineType,
            LineTypeScale = 0.1
        };
        document.Entities.Add(line);

        var settings = new CadRenderSceneSettings
        {
            ModelSpaceLineTypeScaling = true,
            ViewportScale = 0.0012090210302132557f
        };
        var build = System.Threading.Tasks.Task.Run(() => CreateSceneBuilder().Build(document, settings));

        Assert.True(build.Wait(System.TimeSpan.FromSeconds(10)), "Scene build did not finish.");
        var renderLine = Assert.Single(build.Result.Layers.SelectMany(layer => layer.Primitives).OfType<RenderLine>());
        Assert.Equal(500f, renderLine.End.X, 3);
    }

    [Fact]
    public void BuildScene_PatternTooDenseForLine_DrawsContinuousLine()
    {
        var document = new CadDocument();
        var lineType = new LineType("FINE");
        lineType.AddSegment(new LineType.Segment { Length = 0.01, Flags = LineTypeShapeFlags.Text, Text = "x", Scale = 0.01 });
        lineType.AddSegment(new LineType.Segment { Length = -0.01 });
        document.LineTypes.Add(lineType);

        var line = new Line
        {
            StartPoint = new XYZ(0, 0, 0),
            EndPoint = new XYZ(10000, 0, 0),
            LineType = lineType
        };
        document.Entities.Add(line);

        var scene = CreateSceneBuilder().Build(document, new CadRenderSceneSettings());
        var primitives = scene.Layers.SelectMany(layer => layer.Primitives).ToArray();

        var renderLine = Assert.Single(primitives);
        Assert.IsType<RenderLine>(renderLine);
    }

    [Fact]
    public void ResolveLinePattern_UsesTextShaperLayout()
    {
        var document = new CadDocument();
        var lineType = new LineType("TEXTLINE");
        lineType.AddSegment(new LineType.Segment
        {
            Length = 1.0,
            Flags = LineTypeShapeFlags.Text,
            Text = "AB",
            Scale = 1.0
        });
        document.LineTypes.Add(lineType);

        var line = new Line
        {
            StartPoint = new XYZ(0, 0, 0),
            EndPoint = new XYZ(10, 0, 0),
            LineType = lineType
        };

        var resolver = new DefaultRenderLinePatternResolver(new StubTextShaper());
        var pattern = resolver.ResolveLinePattern(line, document, new CadRenderSceneSettings());

        var textSegment = pattern.Segments.First(segment => segment.IsText);
        Assert.Equal(42f, textSegment.LayoutWidth);
        Assert.Equal(10f, textSegment.LayoutHeight);
    }

    [Fact]
    public void ResolveLinePattern_AppliesPaperSpaceScale()
    {
        var document = new CadDocument();
        document.Header.PaperSpaceLineTypeScaling = SpaceLineTypeScaling.Viewport;

        var lineType = new LineType("DASH");
        lineType.AddSegment(new LineType.Segment { Length = 1.0 });
        document.LineTypes.Add(lineType);

        var line = new Line
        {
            StartPoint = new XYZ(0, 0, 0),
            EndPoint = new XYZ(10, 0, 0),
            LineType = lineType
        };

        var resolver = new DefaultRenderLinePatternResolver();
        var settings = new CadRenderSceneSettings
        {
            IsPaperSpace = true,
            ViewportScale = 2f
        };

        var pattern = resolver.ResolveLinePattern(line, document, settings);

        Assert.InRange(pattern.Segments[0].Length, 1.9f, 2.1f);
    }

    [Fact]
    public void ResolveLinePattern_UsesLayoutOverrideInPaperSpace()
    {
        var document = new CadDocument();
        document.Header.PaperSpaceLineTypeScaling = SpaceLineTypeScaling.Normal;

        var lineType = new LineType("DASH");
        lineType.AddSegment(new LineType.Segment { Length = 1.0 });
        document.LineTypes.Add(lineType);

        var line = new Line
        {
            StartPoint = new XYZ(0, 0, 0),
            EndPoint = new XYZ(10, 0, 0),
            LineType = lineType
        };

        var resolver = new DefaultRenderLinePatternResolver();
        var settings = new CadRenderSceneSettings
        {
            IsPaperSpace = true,
            ViewportScale = 2f,
            PaperSpaceLineTypeScalingOverride = SpaceLineTypeScaling.Viewport
        };

        var pattern = resolver.ResolveLinePattern(line, document, settings);

        Assert.InRange(pattern.Segments[0].Length, 1.9f, 2.1f);
    }

    [Fact]
    public void ResolveLinePattern_AppliesModelSpaceScale()
    {
        var document = new CadDocument();
        var lineType = new LineType("DASH");
        lineType.AddSegment(new LineType.Segment { Length = 1.0 });
        document.LineTypes.Add(lineType);

        var line = new Line
        {
            StartPoint = new XYZ(0, 0, 0),
            EndPoint = new XYZ(10, 0, 0),
            LineType = lineType
        };

        var resolver = new DefaultRenderLinePatternResolver();
        var settings = new CadRenderSceneSettings
        {
            ModelSpaceLineTypeScaling = true,
            ViewportScale = 3f
        };

        var pattern = resolver.ResolveLinePattern(line, document, settings);

        Assert.InRange(pattern.Segments[0].Length, 2.9f, 3.1f);
    }

    [Fact]
    public void ResolveLinePattern_SkipsModelSpaceScaleWhenDisabled()
    {
        var document = new CadDocument();
        var lineType = new LineType("DASH");
        lineType.AddSegment(new LineType.Segment { Length = 1.0 });
        document.LineTypes.Add(lineType);

        var line = new Line
        {
            StartPoint = new XYZ(0, 0, 0),
            EndPoint = new XYZ(10, 0, 0),
            LineType = lineType
        };

        var resolver = new DefaultRenderLinePatternResolver();
        var settings = new CadRenderSceneSettings
        {
            ModelSpaceLineTypeScaling = false,
            ViewportScale = 3f
        };

        var pattern = resolver.ResolveLinePattern(line, document, settings);

        Assert.InRange(pattern.Segments[0].Length, 0.9f, 1.1f);
    }

    [Fact]
    public void ResolveLinePattern_ReturnsContinuous_WhenDashPatternRenderingDisabled()
    {
        var document = new CadDocument();
        var lineType = new LineType("DASH");
        lineType.AddSegment(new LineType.Segment { Length = 1.0 });
        lineType.AddSegment(new LineType.Segment { Length = -0.5 });
        document.LineTypes.Add(lineType);

        var line = new Line
        {
            StartPoint = new XYZ(0, 0, 0),
            EndPoint = new XYZ(10, 0, 0),
            LineType = lineType
        };

        var resolver = new DefaultRenderLinePatternResolver();
        var pattern = resolver.ResolveLinePattern(line, document, new CadRenderSceneSettings
        {
            EnableDashPatternRendering = false
        });

        Assert.True(pattern.IsContinuous);
    }

    private static CadRenderSceneBuilder CreateSceneBuilder()
    {
        var handlers = new IRenderEntityHandler[]
        {
            new LineRenderHandler(),
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

    private sealed class StubTextShaper : IRenderTextShaper
    {
        public RenderTextLayout Shape(TextEntity text, CadRenderSceneSettings settings)
        {
            return new RenderTextLayout(text.Value ?? string.Empty, 42f, 10f);
        }

        public RenderTextLayout Shape(MText text, CadRenderSceneSettings settings)
        {
            return new RenderTextLayout(text.PlainText ?? string.Empty, 0f, 0f);
        }
    }
}
