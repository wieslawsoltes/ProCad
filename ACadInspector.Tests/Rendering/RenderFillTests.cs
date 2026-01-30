using System.Linq;
using ACadInspector.Rendering;
using ACadSharp;
using ACadSharp.Entities;
using CSMath;
using Xunit;

namespace ACadInspector.Tests.Rendering;

public sealed class RenderFillTests
{
    [Fact]
    public void BuildScene_AddsFillForSolid()
    {
        var document = new ACadSharp.CadDocument();
        var solid = new Solid
        {
            FirstCorner = new XYZ(0, 0, 0),
            SecondCorner = new XYZ(4, 0, 0),
            ThirdCorner = new XYZ(4, 3, 0),
            FourthCorner = new XYZ(0, 3, 0)
        };
        document.Entities.Add(solid);

        var scene = CreateSceneBuilder().Build(document, new CadRenderSceneSettings());
        var fills = scene.Layers.SelectMany(layer => layer.Primitives).OfType<RenderFill>().ToArray();

        Assert.True(fills.Length > 0);
    }

    [Fact]
    public void BuildScene_AddsFillForClosedPolyline()
    {
        var document = new ACadSharp.CadDocument();
        var polyline = new LwPolyline(new[]
        {
            new XY(0, 0),
            new XY(4, 0),
            new XY(4, 3),
            new XY(0, 3)
        })
        {
            IsClosed = true
        };
        document.Entities.Add(polyline);

        var scene = CreateSceneBuilder().Build(document, new CadRenderSceneSettings());
        var fills = scene.Layers.SelectMany(layer => layer.Primitives).OfType<RenderFill>().ToArray();

        Assert.True(fills.Length > 0);
    }

    [Fact]
    public void BuildScene_AddsFillForSolidHatch()
    {
        var document = new ACadSharp.CadDocument();
        var hatch = new Hatch
        {
            IsSolid = true
        };

        var path = new Hatch.BoundaryPath();
        path.Flags = BoundaryPathFlags.External;
        path.Edges.Add(new Hatch.BoundaryPath.Line { Start = new XY(0, 0), End = new XY(5, 0) });
        path.Edges.Add(new Hatch.BoundaryPath.Line { Start = new XY(5, 0), End = new XY(5, 4) });
        path.Edges.Add(new Hatch.BoundaryPath.Line { Start = new XY(5, 4), End = new XY(0, 4) });
        path.Edges.Add(new Hatch.BoundaryPath.Line { Start = new XY(0, 4), End = new XY(0, 0) });
        hatch.Paths.Add(path);

        document.Entities.Add(hatch);

        var scene = CreateSceneBuilder().Build(document, new CadRenderSceneSettings());
        var fills = scene.Layers.SelectMany(layer => layer.Primitives).OfType<RenderHatchFill>().ToArray();

        Assert.True(fills.Length > 0);
    }

    [Fact]
    public void BuildScene_PreservesHatchLoopsForHoles()
    {
        var document = new ACadSharp.CadDocument();
        var hatch = new Hatch
        {
            IsSolid = true
        };

        hatch.Paths.Add(CreateRectanglePath(0, 0, 10, 8, BoundaryPathFlags.External));
        hatch.Paths.Add(CreateRectanglePath(2, 2, 4, 3, BoundaryPathFlags.Default));

        document.Entities.Add(hatch);

        var scene = CreateSceneBuilder().Build(document, new CadRenderSceneSettings());
        var fill = scene.Layers.SelectMany(layer => layer.Primitives).OfType<RenderHatchFill>().FirstOrDefault();

        Assert.NotNull(fill);
        Assert.Equal(2, fill.Loops.Count);
    }

    [Fact]
    public void BuildScene_AddsPatternForHatch()
    {
        var document = new ACadSharp.CadDocument();
        var hatch = new Hatch
        {
            IsSolid = false,
            Pattern = new HatchPattern("TEST")
        };
        hatch.Pattern.Lines.Add(new HatchPattern.Line
        {
            Angle = 0.0,
            BasePoint = new XY(0, 0),
            Offset = new XY(0, 1)
        });
        hatch.Paths.Add(CreateRectanglePath(0, 0, 6, 4, BoundaryPathFlags.External));

        document.Entities.Add(hatch);

        var scene = CreateSceneBuilder().Build(document, new CadRenderSceneSettings());
        var pattern = scene.Layers.SelectMany(layer => layer.Primitives).OfType<RenderHatchPattern>().FirstOrDefault();

        Assert.NotNull(pattern);
        Assert.True(pattern.Segments.Count > 0);
    }

    [Fact]
    public void BuildScene_UsesBoundaryWhenPatternDisabled()
    {
        var document = new ACadSharp.CadDocument();
        var hatch = new Hatch
        {
            IsSolid = false,
            Pattern = new HatchPattern("TEST")
        };
        hatch.Pattern.Lines.Add(new HatchPattern.Line
        {
            Angle = 0.0,
            BasePoint = new XY(0, 0),
            Offset = new XY(0, 1)
        });
        hatch.Paths.Add(CreateRectanglePath(0, 0, 6, 4, BoundaryPathFlags.External));

        document.Entities.Add(hatch);

        var settings = new CadRenderSceneSettings { EnableHatchPatterns = false };
        var scene = CreateSceneBuilder().Build(document, settings);
        var primitives = scene.Layers.SelectMany(layer => layer.Primitives).ToArray();

        Assert.DoesNotContain(primitives, primitive => primitive is RenderHatchPattern);
        Assert.Contains(primitives, primitive => primitive is RenderPolyline);
    }

    [Fact]
    public void BuildScene_AppliesHatchTransparency()
    {
        var document = new ACadSharp.CadDocument();
        var hatch = new Hatch
        {
            IsSolid = true,
            Transparency = new Transparency(50)
        };
        hatch.Paths.Add(CreateRectanglePath(0, 0, 4, 2, BoundaryPathFlags.External));
        document.Entities.Add(hatch);

        var scene = CreateSceneBuilder().Build(document, new CadRenderSceneSettings());
        var fill = scene.Layers.SelectMany(layer => layer.Primitives).OfType<RenderHatchFill>().FirstOrDefault();

        Assert.NotNull(fill);
        Assert.InRange(fill!.Color.A, 120, 135);
    }

    [Fact]
    public void BuildScene_AddsGradientForHatch()
    {
        var document = new ACadSharp.CadDocument();
        var hatch = new Hatch
        {
            IsSolid = true
        };
        hatch.GradientColor.Enabled = true;
        hatch.GradientColor.Colors.Add(new GradientColor { Value = 0.0, Color = new Color(255, 0, 0) });
        hatch.GradientColor.Colors.Add(new GradientColor { Value = 1.0, Color = new Color(0, 0, 255) });
        hatch.Paths.Add(CreateRectanglePath(0, 0, 6, 4, BoundaryPathFlags.External));

        document.Entities.Add(hatch);

        var scene = CreateSceneBuilder().Build(document, new CadRenderSceneSettings());
        var fill = scene.Layers.SelectMany(layer => layer.Primitives).OfType<RenderHatchFill>().FirstOrDefault();

        Assert.NotNull(fill);
        Assert.NotNull(fill.Gradient);
    }

    [Fact]
    public void BuildScene_UsesSolidFillWhenGradientDisabled()
    {
        var document = new ACadSharp.CadDocument();
        var hatch = new Hatch
        {
            IsSolid = true
        };
        hatch.GradientColor.Enabled = true;
        hatch.GradientColor.Colors.Add(new GradientColor { Value = 0.0, Color = new Color(255, 0, 0) });
        hatch.GradientColor.Colors.Add(new GradientColor { Value = 1.0, Color = new Color(0, 0, 255) });
        hatch.Paths.Add(CreateRectanglePath(0, 0, 6, 4, BoundaryPathFlags.External));

        document.Entities.Add(hatch);

        var settings = new CadRenderSceneSettings { EnableHatchGradients = false };
        var scene = CreateSceneBuilder().Build(document, settings);
        var fill = scene.Layers.SelectMany(layer => layer.Primitives).OfType<RenderHatchFill>().FirstOrDefault();

        Assert.NotNull(fill);
        Assert.Null(fill.Gradient);
    }

    private static Hatch.BoundaryPath CreateRectanglePath(
        double x,
        double y,
        double width,
        double height,
        BoundaryPathFlags flags)
    {
        var path = new Hatch.BoundaryPath();
        path.Flags = flags;
        path.Edges.Add(new Hatch.BoundaryPath.Line { Start = new XY(x, y), End = new XY(x + width, y) });
        path.Edges.Add(new Hatch.BoundaryPath.Line { Start = new XY(x + width, y), End = new XY(x + width, y + height) });
        path.Edges.Add(new Hatch.BoundaryPath.Line { Start = new XY(x + width, y + height), End = new XY(x, y + height) });
        path.Edges.Add(new Hatch.BoundaryPath.Line { Start = new XY(x, y + height), End = new XY(x, y) });
        return path;
    }

    private static CadRenderSceneBuilder CreateSceneBuilder()
    {
        var handlers = new IRenderEntityHandler[]
        {
            new SolidRenderHandler(),
            new PolylineRenderHandler(),
            new HatchRenderHandler(),
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
}
