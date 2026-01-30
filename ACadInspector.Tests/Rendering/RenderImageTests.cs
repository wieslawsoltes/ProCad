using System.IO;
using System.Linq;
using ACadInspector.Rendering;
using ACadSharp;
using ACadSharp.Entities;
using ACadSharp.Objects;
using ACadSharp.Tables;
using CSMath;
using Xunit;

namespace ACadInspector.Tests.Rendering;

public sealed class RenderImageTests
{
    [Fact]
    public void BuildScene_RendersRasterImagePrimitive()
    {
        using var support = new TemporarySupportFile("image.png");
        var document = new CadDocument();
        var definition = new ImageDefinition
        {
            Name = "IMG1",
            FileName = "image.png",
            Size = new XY(4, 3)
        };
        var image = new RasterImage(definition)
        {
            Flags = ImageDisplayFlags.ShowImage,
            Size = new XY(4, 3),
            InsertPoint = XYZ.Zero,
            UVector = XYZ.AxisX,
            VVector = XYZ.AxisY
        };
        document.Entities.Add(image);

        var scene = CreateSceneBuilder().Build(document, new CadRenderSceneSettings
        {
            SupportPaths = new[] { support.Directory }
        });

        var imagePrimitive = scene.Layers.SelectMany(layer => layer.Primitives)
            .OfType<RenderImage>()
            .FirstOrDefault();

        Assert.NotNull(imagePrimitive);
        Assert.Equal(4f, imagePrimitive!.Size.X);
        Assert.Equal(3f, imagePrimitive.Size.Y);
        Assert.EndsWith("image.png", imagePrimitive.SourcePath);
    }

    [Fact]
    public void BuildScene_RendersPdfUnderlayWithClip()
    {
        var document = new CadDocument();
        var definition = new PdfUnderlayDefinition
        {
            Name = "PDF1",
            File = "plan.pdf",
            Page = "1"
        };
        var underlay = new PdfUnderlay(definition)
        {
            Flags = UnderlayDisplayFlags.ShowUnderlay | UnderlayDisplayFlags.ClippingOn,
            InsertPoint = XYZ.Zero,
            XScale = 1,
            YScale = 1
        };
        underlay.ClipBoundaryVertices.Add(new XY(0, 0));
        underlay.ClipBoundaryVertices.Add(new XY(4, 0));
        underlay.ClipBoundaryVertices.Add(new XY(4, 2));
        underlay.ClipBoundaryVertices.Add(new XY(0, 2));
        document.Entities.Add(underlay);

        var scene = CreateSceneBuilder().Build(document, new CadRenderSceneSettings());
        var clipGroup = scene.Layers.SelectMany(layer => layer.Primitives)
            .OfType<RenderClipGroup>()
            .FirstOrDefault();

        Assert.NotNull(clipGroup);
        Assert.Contains(clipGroup!.Primitives, primitive => primitive is RenderImage);
    }

    private static CadRenderSceneBuilder CreateSceneBuilder()
    {
        var handlers = new IRenderEntityHandler[]
        {
            new RasterImageRenderHandler(),
            new PdfUnderlayRenderHandler(),
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

    private sealed class TemporarySupportFile : IDisposable
    {
        public string Directory { get; }
        private readonly string _path;

        public TemporarySupportFile(string fileName)
        {
            Directory = Path.Combine(Path.GetTempPath(), "acadinspector-tests");
            System.IO.Directory.CreateDirectory(Directory);
            _path = Path.Combine(Directory, fileName);
            File.WriteAllBytes(_path, new byte[] { 0x00 });
        }

        public void Dispose()
        {
            if (File.Exists(_path))
            {
                File.Delete(_path);
            }
        }
    }
}
