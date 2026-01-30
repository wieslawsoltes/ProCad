using System;
using System.IO;
using System.Linq;
using ACadInspector.Core;
using ACadInspector.IO;
using ACadInspector.Rendering;
using ACadSharp;
using ACadSharp.Entities;
using ACadSharp.Objects;
using ACadSharp.Tables;
using CSMath;
using Xunit;

namespace ACadInspector.Tests.Rendering;

public sealed class RenderInsertTests
{
    [Fact]
    public void XRefResolver_LoadsDocumentFromSupportPaths()
    {
        var tempDir = CreateTempDirectory();
        try
        {
            var xrefPath = Path.Combine(tempDir, "xref.dxf");
            var xrefDocument = new CadDocument();
            xrefDocument.Entities.Add(new Line
            {
                StartPoint = new XYZ(0, 0, 0),
                EndPoint = new XYZ(5, 0, 0)
            });

            var service = new AcAdSharpDocumentService();
            service.Save(xrefPath, xrefDocument, new CadWriteOptions(CadFileFormat.Dxf));

            var block = new BlockRecord("XREF", "xref.dxf");
            var resolver = new DefaultRenderXRefResolver(service);
            var settings = new CadRenderSceneSettings { SupportPaths = new[] { tempDir } };

            Assert.True(resolver.TryResolve(block, settings, out var info));
            Assert.NotNull(info.Document);
            Assert.True(info.Document.Entities.OfType<Line>().Any());
        }
        finally
        {
            DeleteTempDirectory(tempDir);
        }
    }

    [Fact]
    public void BuildScene_RendersXRefEntities()
    {
        var tempDir = CreateTempDirectory();
        try
        {
            var xrefPath = Path.Combine(tempDir, "xref.dxf");
            var xrefDocument = new CadDocument();
            xrefDocument.Entities.Add(new Line
            {
                StartPoint = new XYZ(0, 0, 0),
                EndPoint = new XYZ(5, 0, 0)
            });

            var service = new AcAdSharpDocumentService();
            service.Save(xrefPath, xrefDocument, new CadWriteOptions(CadFileFormat.Dxf));

            var host = new CadDocument();
            var xrefBlock = new BlockRecord("XREF", "xref.dxf");
            host.BlockRecords.Add(xrefBlock);

            var insert = new Insert(xrefBlock)
            {
                InsertPoint = new XYZ(0, 0, 0)
            };
            host.Entities.Add(insert);

            var settings = new CadRenderSceneSettings { SupportPaths = new[] { tempDir } };
            var scene = CreateSceneBuilder(new DefaultRenderXRefResolver(service)).Build(host, settings);
            var primitives = scene.Layers.SelectMany(layer => layer.Primitives).ToArray();

            Assert.Contains(primitives, primitive => primitive is RenderLine);
        }
        finally
        {
            DeleteTempDirectory(tempDir);
        }
    }

    [Fact]
    public void BuildScene_ClipsInsertWithSpatialFilter()
    {
        var document = new CadDocument();
        var block = new BlockRecord("CLIP");
        block.Entities.Add(new Line
        {
            StartPoint = new XYZ(-5, 0, 0),
            EndPoint = new XYZ(5, 0, 0)
        });
        document.BlockRecords.Add(block);

        var insert = new Insert(block)
        {
            InsertPoint = new XYZ(0, 0, 0)
        };

        var filter = new SpatialFilter
        {
            Origin = XYZ.Zero,
            InsertTransform = Matrix4.Identity
        };
        filter.BoundaryPoints.Add(new XY(0, 0));
        filter.BoundaryPoints.Add(new XY(2, 2));
        insert.SpatialFilter = filter;
        document.Entities.Add(insert);

        var scene = CreateSceneBuilder(NullRenderXRefResolver.Instance)
            .Build(document, new CadRenderSceneSettings());
        var primitives = scene.Layers.SelectMany(layer => layer.Primitives).ToArray();

        Assert.Contains(primitives, primitive => primitive is RenderClipGroup);
    }

    private static CadRenderSceneBuilder CreateSceneBuilder(IRenderXRefResolver xrefResolver)
    {
        var handlers = new IRenderEntityHandler[]
        {
            new InsertRenderHandler(xrefResolver),
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

    private static string CreateTempDirectory()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"acad-xref-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        return tempDir;
    }

    private static void DeleteTempDirectory(string tempDir)
    {
        if (string.IsNullOrWhiteSpace(tempDir))
        {
            return;
        }

        if (Directory.Exists(tempDir))
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }
}
