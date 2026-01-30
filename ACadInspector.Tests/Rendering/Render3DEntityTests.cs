using System.Linq;
using ACadInspector.Rendering;
using ACadSharp.Entities;
using CSMath;
using Xunit;

namespace ACadInspector.Tests.Rendering;

public sealed class Render3DEntityTests
{
    [Fact]
    public void BuildScene_RendersFace3DEdges()
    {
        var document = new ACadSharp.CadDocument();
        var face = new Face3D
        {
            FirstCorner = new XYZ(0, 0, 0),
            SecondCorner = new XYZ(4, 0, 0),
            ThirdCorner = new XYZ(4, 3, 0),
            FourthCorner = new XYZ(0, 3, 0)
        };
        document.Entities.Add(face);

        var scene = CreateSceneBuilder().Build(document, new CadRenderSceneSettings());
        var primitives = scene.Layers.SelectMany(layer => layer.Primitives).ToArray();

        Assert.Contains(primitives, primitive => primitive is RenderLine || primitive is RenderPolyline);
    }

    [Fact]
    public void BuildScene_RendersMeshFaces()
    {
        var document = new ACadSharp.CadDocument();
        var mesh = new Mesh();
        mesh.Vertices.Add(new XYZ(0, 0, 0));
        mesh.Vertices.Add(new XYZ(4, 0, 0));
        mesh.Vertices.Add(new XYZ(4, 3, 0));
        mesh.Vertices.Add(new XYZ(0, 3, 0));
        mesh.Faces.Add(new[] { 0, 1, 2, 3 });
        document.Entities.Add(mesh);

        var scene = CreateSceneBuilder().Build(document, new CadRenderSceneSettings());
        var primitives = scene.Layers.SelectMany(layer => layer.Primitives).ToArray();

        Assert.Contains(primitives, primitive => primitive is RenderLine || primitive is RenderPolyline);
    }

    [Fact]
    public void BuildScene_RendersPolyfaceMeshFaces()
    {
        var document = new ACadSharp.CadDocument();
        var mesh = new PolyfaceMesh();
        mesh.Vertices.Add(new VertexFaceMesh { Location = new XYZ(0, 0, 0) });
        mesh.Vertices.Add(new VertexFaceMesh { Location = new XYZ(4, 0, 0) });
        mesh.Vertices.Add(new VertexFaceMesh { Location = new XYZ(4, 3, 0) });
        mesh.Vertices.Add(new VertexFaceMesh { Location = new XYZ(0, 3, 0) });
        mesh.Faces.Add(new VertexFaceRecord
        {
            Index1 = 1,
            Index2 = 2,
            Index3 = 3,
            Index4 = 4
        });
        document.Entities.Add(mesh);

        var scene = CreateSceneBuilder().Build(document, new CadRenderSceneSettings());
        var primitives = scene.Layers.SelectMany(layer => layer.Primitives).ToArray();

        Assert.Contains(primitives, primitive => primitive is RenderLine || primitive is RenderPolyline);
    }

    [Fact]
    public void BuildScene_TessellatesSubdMeshFaces()
    {
        var baseDocument = new ACadSharp.CadDocument();
        var baseMesh = new Mesh();
        baseMesh.Vertices.Add(new XYZ(0, 0, 0));
        baseMesh.Vertices.Add(new XYZ(4, 0, 0));
        baseMesh.Vertices.Add(new XYZ(4, 3, 0));
        baseMesh.Vertices.Add(new XYZ(0, 3, 0));
        baseMesh.Faces.Add(new[] { 0, 1, 2, 3 });
        baseDocument.Entities.Add(baseMesh);

        var subdDocument = new ACadSharp.CadDocument();
        var subdMesh = new Mesh { SubdivisionLevel = 1 };
        subdMesh.Vertices.Add(new XYZ(0, 0, 0));
        subdMesh.Vertices.Add(new XYZ(4, 0, 0));
        subdMesh.Vertices.Add(new XYZ(4, 3, 0));
        subdMesh.Vertices.Add(new XYZ(0, 3, 0));
        subdMesh.Faces.Add(new[] { 0, 1, 2, 3 });
        subdDocument.Entities.Add(subdMesh);

        var builder = CreateSceneBuilder();
        var settings = new CadRenderSceneSettings { Quality = RenderQuality.High };
        var baseScene = builder.Build(baseDocument, settings);
        var subdScene = builder.Build(subdDocument, settings);

        var baseCount = baseScene.Layers.SelectMany(layer => layer.Primitives).Count();
        var subdCount = subdScene.Layers.SelectMany(layer => layer.Primitives).Count();

        Assert.True(subdCount > baseCount);
    }

    [Fact]
    public void BuildScene_ShadedMeshAddsTriangles()
    {
        var document = new ACadSharp.CadDocument();
        var mesh = new Mesh();
        mesh.Vertices.Add(new XYZ(0, 0, 0));
        mesh.Vertices.Add(new XYZ(4, 0, 0));
        mesh.Vertices.Add(new XYZ(4, 3, 0));
        mesh.Vertices.Add(new XYZ(0, 3, 0));
        mesh.Faces.Add(new[] { 0, 1, 2, 3 });
        document.Entities.Add(mesh);

        var settings = new CadRenderSceneSettings
        {
            Quality = RenderQuality.High,
            VisualStyle = RenderVisualStyle.Shaded
        };

        var scene = CreateSceneBuilder().Build(document, settings);
        var triangles = scene.Layers.SelectMany(layer => layer.Primitives).OfType<RenderTriangle>().ToArray();

        Assert.NotEmpty(triangles);
    }

    private static CadRenderSceneBuilder CreateSceneBuilder()
    {
        var handlers = new IRenderEntityHandler[]
        {
            new Face3DRenderHandler(),
            new MeshRenderHandler(),
            new PolyfaceMeshRenderHandler(),
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
