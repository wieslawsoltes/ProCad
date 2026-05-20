using System;
using System.IO;
using System.Linq;
using ProCad.Rendering;
using ACadSharp;
using ACadSharp.Entities;
using ACadSharp.Header;
using ACadSharp.Tables;
using CSMath;
using Xunit;

namespace ProCad.Tests.Rendering;

public sealed class RenderMissingEntityTests
{
    [Fact]
    public void BuildScene_RendersRayWithinExtents()
    {
        var document = new CadDocument();
        document.Header.ModelSpaceExtMin = new XYZ(0, 0, 0);
        document.Header.ModelSpaceExtMax = new XYZ(10, 10, 0);
        document.Entities.Add(new Ray { StartPoint = new XYZ(5, 5, 0), Direction = new XYZ(1, 0, 0) });

        var scene = CreateSceneBuilder().Build(document, new CadRenderSceneSettings());
        var line = scene.Layers.SelectMany(layer => layer.Primitives).OfType<RenderLine>().FirstOrDefault();

        Assert.NotNull(line);
        Assert.InRange(line!.Start.X, 0f, 10f);
        Assert.InRange(line.End.X, 0f, 10f);
    }

    [Fact]
    public void BuildScene_RendersXLineWithinExtents()
    {
        var document = new CadDocument();
        document.Header.ModelSpaceExtMin = new XYZ(-5, -5, 0);
        document.Header.ModelSpaceExtMax = new XYZ(5, 5, 0);
        document.Entities.Add(new XLine { FirstPoint = new XYZ(0, 0, 0), Direction = new XYZ(0, 1, 0) });

        var scene = CreateSceneBuilder().Build(document, new CadRenderSceneSettings());
        var line = scene.Layers.SelectMany(layer => layer.Primitives).OfType<RenderLine>().FirstOrDefault();

        Assert.NotNull(line);
        Assert.InRange(line!.Start.Y, -5f, 5f);
        Assert.InRange(line.End.Y, -5f, 5f);
    }

    [Fact]
    public void BuildScene_RendersPolygonMesh()
    {
        var document = new CadDocument();
        var mesh = new PolygonMesh
        {
            MVertexCount = 2,
            NVertexCount = 2,
            Flags = PolylineFlags.PolygonMesh
        };
        mesh.Vertices.Add(new PolygonMeshVertex(new XYZ(0, 0, 0)));
        mesh.Vertices.Add(new PolygonMeshVertex(new XYZ(1, 0, 0)));
        mesh.Vertices.Add(new PolygonMeshVertex(new XYZ(0, 1, 0)));
        mesh.Vertices.Add(new PolygonMeshVertex(new XYZ(1, 1, 0)));
        document.Entities.Add(mesh);

        var scene = CreateSceneBuilder().Build(document, new CadRenderSceneSettings());
        var primitives = scene.Layers.SelectMany(layer => layer.Primitives).ToList();
        Assert.Contains(primitives, primitive => primitive is RenderPolyline or RenderLine);
    }

    [Fact]
    public void BuildScene_RendersMLine()
    {
        var document = new CadDocument();
        var mline = new MLine();
        mline.Vertices.Add(new MLine.Vertex
        {
            Position = XYZ.Zero,
            Direction = XYZ.AxisX,
            Miter = XYZ.AxisY
        });
        mline.Vertices.Add(new MLine.Vertex
        {
            Position = new XYZ(5, 0, 0),
            Direction = XYZ.AxisX,
            Miter = XYZ.AxisY
        });
        document.Entities.Add(mline);

        var scene = CreateSceneBuilder().Build(document, new CadRenderSceneSettings());
        var primitives = scene.Layers.SelectMany(layer => layer.Primitives).ToList();
        Assert.Contains(primitives, primitive => primitive is RenderPolyline or RenderLine);
    }

    [Fact]
    public void BuildScene_RendersShapeFromShx()
    {
        var root = FindRepositoryRoot();
        var supportPath = Path.Combine(root, "external", "ACadSharp", "samples");
        var document = new CadDocument();
        var style = new TextStyle("Shx")
        {
            Filename = "test_shape.shx",
            Flags = StyleFlags.IsShape
        };
        document.TextStyles.Add(style);

        var shape = new Shape(style)
        {
            InsertionPoint = XYZ.Zero,
            Size = 2.0
        };
        CadShapeCompatibility.SetShapeNumberForTests(shape, 1);
        document.Entities.Add(shape);

        var scene = CreateSceneBuilder().Build(document, new CadRenderSceneSettings
        {
            SupportPaths = new[] { supportPath }
        });
        var polylines = scene.Layers.SelectMany(layer => layer.Primitives).OfType<RenderPolyline>().ToList();

        Assert.NotEmpty(polylines);
    }

    [Fact]
    public void BuildScene_RendersOle2Frame()
    {
        var document = new CadDocument();
        document.Entities.Add(new Ole2Frame
        {
            UpperLeftCorner = new XYZ(0, 2, 0),
            LowerRightCorner = new XYZ(4, 0, 0)
        });

        var scene = CreateSceneBuilder().Build(document, new CadRenderSceneSettings());
        var polyline = scene.Layers.SelectMany(layer => layer.Primitives).OfType<RenderPolyline>().FirstOrDefault();

        Assert.NotNull(polyline);
        Assert.True(polyline!.IsClosed);
    }

    [Fact]
    public void BuildScene_HidesOle2FrameWhenOleFrameVisibilityIsHidden()
    {
        var document = new CadDocument();
        document.Entities.Add(new Ole2Frame
        {
            UpperLeftCorner = new XYZ(0, 2, 0),
            LowerRightCorner = new XYZ(4, 0, 0),
            SourceApplication = "OLE"
        });

        var scene = CreateSceneBuilder().Build(document, new CadRenderSceneSettings
        {
            OleFrameVisibility = RenderFrameVisibility.Hidden
        });
        var primitives = scene.Layers.SelectMany(layer => layer.Primitives).ToArray();

        Assert.Empty(primitives);
    }

    [Fact]
    public void BuildScene_RendersToleranceWithText()
    {
        var document = new CadDocument();
        var tolerance = new Tolerance
        {
            Text = "A\\P0.05",
            InsertionPoint = new XYZ(0, 0, 0),
            Direction = XYZ.AxisX
        };
        document.Entities.Add(tolerance);

        var scene = CreateSceneBuilder().Build(document, new CadRenderSceneSettings());
        var texts = scene.Layers.SelectMany(layer => layer.Primitives).OfType<RenderText>().ToList();
        var frames = scene.Layers.SelectMany(layer => layer.Primitives).OfType<RenderPolyline>().ToList();

        Assert.NotEmpty(texts);
        Assert.NotEmpty(frames);
    }

    [Fact]
    public void BuildScene_RendersMTextTabs()
    {
        var document = new CadDocument();
        document.Entities.Add(new MText
        {
            Value = "A\\tB"
        });

        var scene = CreateSceneBuilder().Build(document, new CadRenderSceneSettings());
        var texts = scene.Layers.SelectMany(layer => layer.Primitives).OfType<RenderText>().ToList();

        Assert.True(texts.Count >= 2);
    }

    private static CadRenderSceneBuilder CreateSceneBuilder()
    {
        var handlers = new IRenderEntityHandler[]
        {
            new MLineRenderHandler(),
            new RayRenderHandler(),
            new XLineRenderHandler(),
            new PolygonMeshRenderHandler(),
            new ShapeRenderHandler(),
            new Ole2FrameRenderHandler(),
            new ToleranceRenderHandler(),
            new TextEntityRenderHandler(),
            new MTextRenderHandler(),
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

    private static string FindRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "ProCad.slnx")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Repository root was not found.");
    }
}
