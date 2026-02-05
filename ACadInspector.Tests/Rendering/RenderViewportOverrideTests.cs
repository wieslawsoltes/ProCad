using System;
using System.IO;
using ACadInspector.Rendering;
using ACadSharp;
using ACadSharp.Entities;
using ACadSharp.Objects;
using ACadSharp.Tables;
using CSMath;
using Xunit;

namespace ACadInspector.Tests.Rendering;

public sealed class RenderViewportOverrideTests
{
    [Fact]
    public void ResolveEntityColor_UsesViewportLayerOverride()
    {
        var document = new CadDocument();
        var layer = new Layer("TestLayer");
        document.Layers.Add(layer);

        var viewport = new Viewport
        {
            Center = new XYZ(0, 0, 0),
            Width = 10,
            Height = 10
        };

        var record = new XRecord();
        record.CreateEntry(0, viewport);
        record.CreateEntry(62, (short)1);

        var xdict = layer.CreateExtendedDictionary();
        xdict.Add("ACAD_LAYEROVERRIDE", record);

        var overrides = ViewportLayerOverrideResolver.Resolve(document, viewport);
        Assert.NotNull(overrides);

        var settings = new CadRenderSceneSettings();
        var context = CreateContext(document, settings, overrides);
        var line = new Line { Layer = layer };

        var color = context.ResolveEntityColor(line);

        Assert.Equal(255, color.R);
        Assert.Equal(0, color.G);
        Assert.Equal(0, color.B);
    }

    [Fact]
    public void ResolveEntityColor_StopsAtNextViewportOverride()
    {
        var document = new CadDocument();
        var layer = new Layer("TestLayer");
        document.Layers.Add(layer);

        var viewportA = new Viewport
        {
            Center = new XYZ(0, 0, 0),
            Width = 10,
            Height = 10
        };

        var viewportB = new Viewport
        {
            Center = new XYZ(0, 0, 0),
            Width = 10,
            Height = 10
        };

        var record = new XRecord();
        record.CreateEntry(0, viewportA);
        record.CreateEntry(62, (short)1);
        record.CreateEntry(0, viewportB);
        record.CreateEntry(62, (short)3);

        var xdict = layer.CreateExtendedDictionary();
        xdict.Add("ACAD_LAYEROVERRIDE", record);

        var overridesA = ViewportLayerOverrideResolver.Resolve(document, viewportA);
        var overridesB = ViewportLayerOverrideResolver.Resolve(document, viewportB);

        Assert.NotNull(overridesA);
        Assert.NotNull(overridesB);

        var settings = new CadRenderSceneSettings();
        var line = new Line { Layer = layer };

        var contextA = CreateContext(document, settings, overridesA);
        var colorA = contextA.ResolveEntityColor(line);
        Assert.Equal(255, colorA.R);
        Assert.Equal(0, colorA.G);
        Assert.Equal(0, colorA.B);

        var contextB = CreateContext(document, settings, overridesB);
        var colorB = contextB.ResolveEntityColor(line);
        Assert.Equal(0, colorB.R);
        Assert.Equal(255, colorB.G);
        Assert.Equal(0, colorB.B);
    }

    [Fact]
    public void ResolveEntityColor_UsesViewportTransparencyOverride()
    {
        var document = new CadDocument();
        var layer = new Layer("TestLayer");
        document.Layers.Add(layer);

        var viewport = new Viewport
        {
            Center = new XYZ(0, 0, 0),
            Width = 10,
            Height = 10
        };

        var transparency = new Transparency(50);
        var record = new XRecord();
        record.CreateEntry(0, viewport);
        record.CreateEntry(440, Transparency.ToAlphaValue(transparency));

        var xdict = layer.CreateExtendedDictionary();
        xdict.Add("ACAD_LAYEROVERRIDE", record);

        var overrides = ViewportLayerOverrideResolver.Resolve(document, viewport);
        Assert.NotNull(overrides);

        var settings = new CadRenderSceneSettings();
        var context = CreateContext(document, settings, overrides);
        var line = new Line { Layer = layer, Color = new ACadSharp.Color(1), Transparency = Transparency.ByLayer };

        var color = context.ResolveEntityColor(line);

        Assert.Equal(127, color.A);
    }

    [Fact]
    public void ResolveEntityColor_UsesViewportPlotStyleOverrideForCtb()
    {
        var document = new CadDocument();
        document.Header.PlotStyleMode = 0;

        var layer = new Layer("TestLayer") { Color = new ACadSharp.Color(1) };
        document.Layers.Add(layer);

        var viewport = new Viewport
        {
            Center = new XYZ(0, 0, 0),
            Width = 10,
            Height = 10
        };

        var record = new XRecord();
        record.CreateEntry(0, viewport);
        record.CreateEntry(62, (short)3);

        var xdict = layer.CreateExtendedDictionary();
        xdict.Add("ACAD_LAYEROVERRIDE", record);

        var overrides = ViewportLayerOverrideResolver.Resolve(document, viewport);
        Assert.NotNull(overrides);

        var line = new Line { Layer = layer, Color = ACadSharp.Color.ByLayer };

        string? path = null;
        try
        {
            path = WritePlotStyleFile(".ctb",
                "plot_style {\nindex=1\ncolor=255,0,0\n}\nplot_style {\nindex=3\ncolor=0,0,255\n}\n");
            var table = RenderPlotStyleTable.TryLoad(path);
            Assert.NotNull(table);

            var settings = new CadRenderSceneSettings { PlotStyleTable = table };
            var context = CreateContext(document, settings, overrides);

            var color = context.ResolveEntityColor(line);

            Assert.Equal(0, color.R);
            Assert.Equal(0, color.G);
            Assert.Equal(255, color.B);
        }
        finally
        {
            if (path is not null && File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    [Fact]
    public void ResolveEntityColor_UsesViewportPlotStyleOverrideForStb()
    {
        var document = new CadDocument();
        document.Header.PlotStyleMode = 1;

        var plotStyleDict = new CadDictionaryWithDefault(CadDictionary.AcadPlotStyleName, new PlotSettings("Normal"));
        document.RootDictionary.Add(CadDictionary.AcadPlotStyleName, plotStyleDict);

        var layer = new Layer("TestLayer");
        document.Layers.Add(layer);

        var viewport = new Viewport
        {
            Center = new XYZ(0, 0, 0),
            Width = 10,
            Height = 10
        };

        var record = new XRecord();
        record.CreateEntry(0, viewport);
        record.CreateEntry(1, "Override");

        var xdict = layer.CreateExtendedDictionary();
        xdict.Add("ACAD_LAYEROVERRIDE", record);

        var overrides = ViewportLayerOverrideResolver.Resolve(document, viewport);
        Assert.NotNull(overrides);

        var line = new Line { Layer = layer };

        string? path = null;
        try
        {
            path = WritePlotStyleFile(".stb",
                "plot_style {\nname=\"Normal\"\ncolor=255,0,0\n}\nplot_style {\nname=\"Override\"\ncolor=0,255,0\n}\n");
            var table = RenderPlotStyleTable.TryLoad(path);
            Assert.NotNull(table);

            var settings = new CadRenderSceneSettings { PlotStyleTable = table };
            var context = CreateContext(document, settings, overrides);

            var color = context.ResolveEntityColor(line);

            Assert.Equal(0, color.R);
            Assert.Equal(255, color.G);
            Assert.Equal(0, color.B);
        }
        finally
        {
            if (path is not null && File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    private static RenderBuildContext CreateContext(
        CadDocument document,
        CadRenderSceneSettings settings,
        RenderLayerOverrides? overrides)
    {
        return new RenderBuildContext(
            document,
            settings,
            new DefaultRenderStyleResolver(),
            new DefaultRenderLinePatternResolver(),
            new DefaultRenderShapeResolver(),
            new DefaultRenderTextShaper(),
            new DefaultRenderEntityVisibilityResolver(),
            new DefaultRenderGeometrySampler(),
            new DefaultRenderEntityOrderResolver(),
            new RenderEntityDispatcher(Array.Empty<IRenderEntityHandler>()),
            new RenderDiagnostics(),
            new RenderStatsAccumulator(),
            overrides);
    }

    private static string WritePlotStyleFile(string extension, string content)
    {
        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}{extension}");
        File.WriteAllText(path, content);
        return path;
    }
}
