using System.Collections.Generic;
using System.IO;
using System.Linq;
using ProCad.Rendering;
using ACadSharp.IO;
using ACadSharp.IO.DWG;
using Xunit;

namespace ProCad.Tests.Rendering;

public sealed class RenderCorpusTests
{
    public static IEnumerable<object[]> CorpusFiles()
    {
        yield return new object[] { "external/ACadSharp/samples/sample_AC1009_ascii.dxf", 1 };
        yield return new object[] { "external/ACadSharp/samples/sample_AC1015_ascii.dxf", 1 };
        yield return new object[] { "external/ACadSharp/samples/sample_AC1015.dwg", 1 };
        yield return new object[] { "external/ACadSharp/samples/dynamic-blocks/BLOCKVISIBILITYPARAMETER.dwg", 1 };
        yield return new object[] { "external/ACadSharp/samples/sample_base/empty.dwg", 0 };
    }

    [Theory]
    [MemberData(nameof(CorpusFiles))]
    public void BuildScene_RendersCorpusFiles(string relativePath, int minPrimitives)
    {
        var root = GetRepositoryRoot();
        var path = Path.Combine(root, relativePath);
        Assert.True(File.Exists(path));

        var document = LoadDocument(path);
        var settings = new CadRenderSceneSettings
        {
            SupportPaths = new[]
            {
                Path.Combine(root, "external", "ACadSharp", "samples")
            }
        };

        var scene = CreateSceneBuilder().Build(document, settings);
        var primitiveCount = scene.Layers.Sum(layer => layer.Primitives.Count);

        Assert.True(primitiveCount >= minPrimitives);
    }

    private static ACadSharp.CadDocument LoadDocument(string path)
    {
        var extension = Path.GetExtension(path).ToLowerInvariant();
        return extension switch
        {
            ".dxf" => DxfReader.Read(path, new DxfReaderConfiguration
            {
                ClearCache = true,
                CreateDefaults = false
            }),
            ".dwg" => DwgReader.Read(path, new DwgReaderConfiguration
            {
                ReadSummaryInfo = false,
                CrcCheck = false
            }),
            _ => throw new InvalidDataException($"Unsupported extension: {extension}")
        };
    }

    private static string GetRepositoryRoot()
    {
        return Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
    }

    private static CadRenderSceneBuilder CreateSceneBuilder()
    {
        var handlers = new IRenderEntityHandler[]
        {
            new TableRenderHandler(),
            new InsertRenderHandler(NullRenderXRefResolver.Instance),
            new DimensionRenderHandler(),
            new LeaderRenderHandler(),
            new MultiLeaderRenderHandler(),
            new LineRenderHandler(),
            new PointRenderHandler(),
            new ArcRenderHandler(),
            new CircleRenderHandler(),
            new EllipseRenderHandler(),
            new SplineRenderHandler(),
            new PolylineRenderHandler(),
            new Face3DRenderHandler(),
            new MeshRenderHandler(),
            new PolyfaceMeshRenderHandler(),
            new SolidRenderHandler(),
            new HatchRenderHandler(),
            new WipeoutRenderHandler(),
            new RasterImageRenderHandler(),
            new PdfUnderlayRenderHandler(),
            new ViewportRenderHandler(),
            new TextEntityRenderHandler(),
            new MTextRenderHandler(),
            new ProxyEntityRenderHandler(),
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
