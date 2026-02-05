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

    [Fact]
    public void BuildScene_UsesOcsInsertPointForNonDefaultNormal()
    {
        var document = new CadDocument();
        var block = new BlockRecord("WCS");
        block.Entities.Add(new Line
        {
            StartPoint = XYZ.Zero,
            EndPoint = new XYZ(1, 0, 0)
        });
        document.BlockRecords.Add(block);

        var insert = new Insert(block)
        {
            InsertPoint = new XYZ(-10, 0, 0),
            Normal = -XYZ.AxisZ,
            XScale = 1,
            YScale = 1,
            ZScale = 1,
            Rotation = 0
        };
        document.Entities.Add(insert);

        var scene = CreateSceneBuilder(NullRenderXRefResolver.Instance)
            .Build(document, new CadRenderSceneSettings());
        var line = Assert.Single(scene.Layers.SelectMany(layer => layer.Primitives).OfType<RenderLine>());

        var expectedTransform = insert.GetTransform();
        var expectedStart = expectedTransform.ApplyTransform(XYZ.Zero);
        var expectedEnd = expectedTransform.ApplyTransform(new XYZ(1, 0, 0));

        Assert.True(Math.Abs(line.Start.X - (float)expectedStart.X) < 0.001f);
        Assert.True(Math.Abs(line.Start.Y - (float)expectedStart.Y) < 0.001f);
        Assert.True(Math.Abs(line.End.X - (float)expectedEnd.X) < 0.001f);
        Assert.True(Math.Abs(line.End.Y - (float)expectedEnd.Y) < 0.001f);
    }

    [Fact]
    public void BuildScene_RendersAttributeEntitiesInWorldSpace()
    {
        var document = new CadDocument();
        var block = new BlockRecord("ATTR");
        var definition = new AttributeDefinition
        {
            Tag = "TAG",
            Value = "Label",
            InsertPoint = new XYZ(1, 0, 0),
            Height = 1
        };
        block.Entities.Add(definition);
        document.BlockRecords.Add(block);

        var insert = new Insert(block)
        {
            InsertPoint = new XYZ(10, 0, 0)
        };

        var attr = insert.Attributes.First(attr => string.Equals(attr.Tag, "TAG", StringComparison.OrdinalIgnoreCase));
        var worldPoint = insert.GetTransform().ApplyTransform(definition.InsertPoint);
        attr.InsertPoint = worldPoint;
        document.Entities.Add(insert);

        var handlers = new IRenderEntityHandler[]
        {
            new InsertRenderHandler(NullRenderXRefResolver.Instance),
            new TextEntityRenderHandler(),
            new FallbackRenderHandler()
        };

        var sceneBuilder = new CadRenderSceneBuilder(
            new RenderEntityDispatcher(handlers),
            new DefaultRenderStyleResolver(),
            new DefaultRenderLinePatternResolver(),
            new DefaultRenderShapeResolver(),
            new DefaultRenderTextShaper(),
            new DefaultRenderEntityVisibilityResolver(),
            new DefaultRenderGeometrySampler(),
            new DefaultRenderEntityOrderResolver(),
            new RenderCacheStampProvider());

        var scene = sceneBuilder
            .Build(document, new CadRenderSceneSettings());
        var text = Assert.Single(scene.Layers.SelectMany(layer => layer.Primitives).OfType<RenderText>());

        Assert.True(Math.Abs(text.Anchor.X - (float)worldPoint.X) < 0.001f);
        Assert.True(Math.Abs(text.Anchor.Y - (float)worldPoint.Y) < 0.001f);
    }

    [Fact]
    public void BuildScene_ResolvesByBlockColorsFromInsert()
    {
        var document = new CadDocument();
        var block = new BlockRecord("BYBLOCK");
        block.Entities.Add(new Line
        {
            StartPoint = XYZ.Zero,
            EndPoint = new XYZ(1, 0, 0),
            Color = Color.ByBlock
        });
        document.BlockRecords.Add(block);

        var insert = new Insert(block)
        {
            InsertPoint = XYZ.Zero,
            Color = new Color(1)
        };
        document.Entities.Add(insert);

        var scene = CreateSceneBuilder(NullRenderXRefResolver.Instance)
            .Build(document, new CadRenderSceneSettings());
        var line = Assert.Single(scene.Layers.SelectMany(layer => layer.Primitives).OfType<RenderLine>());

        Assert.Equal(255, line.Color.R);
        Assert.Equal(0, line.Color.G);
        Assert.Equal(0, line.Color.B);
    }

    [Fact]
    public void BuildScene_ResolvesNestedByBlockColorsFromOuterInsert()
    {
        var document = new CadDocument();
        var inner = new BlockRecord("INNER");
        inner.Entities.Add(new Line
        {
            StartPoint = XYZ.Zero,
            EndPoint = new XYZ(1, 0, 0),
            Color = Color.ByBlock
        });
        document.BlockRecords.Add(inner);

        var outer = new BlockRecord("OUTER");
        outer.Entities.Add(new Insert(inner)
        {
            InsertPoint = XYZ.Zero,
            Color = Color.ByBlock
        });
        document.BlockRecords.Add(outer);

        var insert = new Insert(outer)
        {
            InsertPoint = XYZ.Zero,
            Color = new Color(3)
        };
        document.Entities.Add(insert);

        var scene = CreateSceneBuilder(NullRenderXRefResolver.Instance)
            .Build(document, new CadRenderSceneSettings());
        var line = Assert.Single(scene.Layers.SelectMany(layer => layer.Primitives).OfType<RenderLine>());

        Assert.Equal(0, line.Color.R);
        Assert.Equal(255, line.Color.G);
        Assert.Equal(0, line.Color.B);
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
