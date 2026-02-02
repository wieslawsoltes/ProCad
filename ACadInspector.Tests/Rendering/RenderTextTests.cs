using System;
using System.Linq;
using ACadInspector.Rendering;
using ACadSharp;
using ACadSharp.Entities;
using ACadSharp.Tables;
using ACadSharp.XData;
using CSMath;
using Xunit;

namespace ACadInspector.Tests.Rendering;

public sealed class RenderTextTests
{
    [Fact]
    public void BuildScene_IncludesRenderTextForTextEntity()
    {
        var document = new ACadSharp.CadDocument();
        var text = new TextEntity
        {
            Value = "Hello",
            Height = 2.0,
            InsertPoint = new XYZ(0, 0, 0)
        };
        document.Entities.Add(text);

        var scene = CreateSceneBuilder().Build(document, new CadRenderSceneSettings());
        var primitives = scene.Layers.SelectMany(layer => layer.Primitives);

        Assert.Contains(primitives, primitive => primitive is RenderText render && render.Text == "Hello");
    }

    [Fact]
    public void BuildScene_UsesFontFlagsFromStyle()
    {
        var document = new ACadSharp.CadDocument();
        var style = new TextStyle("Flags")
        {
            TrueType = FontFlags.Bold | FontFlags.Italic
        };
        document.TextStyles.Add(style);

        var text = new TextEntity
        {
            Value = "Flags",
            Height = 1.0,
            InsertPoint = new XYZ(0, 0, 0),
            Style = style
        };
        document.Entities.Add(text);

        var scene = CreateSceneBuilder().Build(document, new CadRenderSceneSettings());
        var renderText = scene.Layers.SelectMany(layer => layer.Primitives).OfType<RenderText>().FirstOrDefault();

        Assert.NotNull(renderText);
        Assert.True(renderText.IsBold);
        Assert.True(renderText.IsItalic);
    }

    [Fact]
    public void BuildScene_UsesStyleHeightWhenEntityHeightIsZero()
    {
        var document = new ACadSharp.CadDocument();
        var style = new TextStyle("Fixed")
        {
            Height = 2.5
        };
        document.TextStyles.Add(style);

        var text = new TextEntity
        {
            Value = "Fixed",
            Height = MathHelper.Epsilon * 0.5,
            InsertPoint = new XYZ(0, 0, 0),
            Style = style
        };
        document.Entities.Add(text);

        var scene = CreateSceneBuilder().Build(document, new CadRenderSceneSettings());
        var renderText = scene.Layers.SelectMany(layer => layer.Primitives).OfType<RenderText>().FirstOrDefault();

        Assert.NotNull(renderText);
        Assert.InRange(renderText.FontSize, 2.4f, 2.6f);
    }

    [Fact]
    public void BuildScene_AppliesMirrorFlags()
    {
        var document = new ACadSharp.CadDocument();
        var style = new TextStyle("Mirror")
        {
            MirrorFlag = TextMirrorFlag.UpsideDown
        };
        document.TextStyles.Add(style);

        var text = new TextEntity
        {
            Value = "Mirror",
            Height = 1.0,
            InsertPoint = new XYZ(0, 0, 0),
            Mirror = TextMirrorFlag.Backward,
            Style = style
        };
        document.Entities.Add(text);

        var scene = CreateSceneBuilder().Build(document, new CadRenderSceneSettings());
        var renderText = scene.Layers.SelectMany(layer => layer.Primitives).OfType<RenderText>().FirstOrDefault();

        Assert.NotNull(renderText);
        Assert.True(renderText.MirrorX);
        Assert.True(renderText.MirrorY);
    }

    [Fact]
    public void BuildScene_UsesFitAlignmentToScaleWidth()
    {
        var document = new ACadSharp.CadDocument();
        var text = new TextEntity
        {
            Value = "AB",
            Height = 1.0,
            InsertPoint = new XYZ(0, 0, 0),
            AlignmentPoint = new XYZ(6, 0, 0),
            HorizontalAlignment = TextHorizontalAlignment.Fit
        };
        document.Entities.Add(text);

        var scene = CreateSceneBuilder().Build(document, new CadRenderSceneSettings());
        var renderText = scene.Layers.SelectMany(layer => layer.Primitives).OfType<RenderText>().FirstOrDefault();

        Assert.NotNull(renderText);
        Assert.InRange(renderText.WidthFactor, 4.9f, 5.1f);
        Assert.InRange(renderText.FontSize, 0.9f, 1.1f);
    }

    [Fact]
    public void BuildScene_UsesAlignedAlignmentToScaleUniformly()
    {
        var document = new ACadSharp.CadDocument();
        var text = new TextEntity
        {
            Value = "AB",
            Height = 1.0,
            InsertPoint = new XYZ(0, 0, 0),
            AlignmentPoint = new XYZ(6, 0, 0),
            HorizontalAlignment = TextHorizontalAlignment.Aligned
        };
        document.Entities.Add(text);

        var scene = CreateSceneBuilder().Build(document, new CadRenderSceneSettings());
        var renderText = scene.Layers.SelectMany(layer => layer.Primitives).OfType<RenderText>().FirstOrDefault();

        Assert.NotNull(renderText);
        Assert.InRange(renderText.FontSize, 4.9f, 5.1f);
        Assert.InRange(renderText.WidthFactor, 0.9f, 1.1f);
    }

    [Fact]
    public void BuildScene_AppliesObliqueAngle()
    {
        var document = new ACadSharp.CadDocument();
        var text = new TextEntity
        {
            Value = "Lean",
            Height = 2.0,
            InsertPoint = new XYZ(0, 0, 0),
            ObliqueAngle = Math.PI / 12.0
        };
        document.Entities.Add(text);

        var scene = CreateSceneBuilder().Build(document, new CadRenderSceneSettings());
        var primitives = scene.Layers.SelectMany(layer => layer.Primitives);
        var renderText = primitives.OfType<RenderText>().FirstOrDefault();

        Assert.NotNull(renderText);
        Assert.InRange(renderText.ObliqueAngle, 0.25f, 0.27f);
    }

    [Fact]
    public void SkiaRenderTextShaper_UsesSkiaMetrics()
    {
        var text = new TextEntity
        {
            Value = "Hello",
            Height = 2.0,
            InsertPoint = new XYZ(0, 0, 0)
        };

        var layout = new SkiaRenderTextShaper().Shape(text, new CadRenderSceneSettings());

        Assert.False(string.IsNullOrWhiteSpace(layout.Text));
        Assert.True(layout.Width > 0f);
        Assert.True(layout.Height > 0f);
    }

    [Fact]
    public void DefaultRenderTextShaper_UsesWidthFactor()
    {
        var settings = new CadRenderSceneSettings { TextWidthFactor = 0.5f };
        var text = new TextEntity
        {
            Value = "AB",
            Height = 2.0,
            InsertPoint = new XYZ(0, 0, 0)
        };

        var layout = new DefaultRenderTextShaper().Shape(text, settings);

        Assert.InRange(layout.Width, 1.9f, 2.1f);
        Assert.InRange(layout.Height, 1.9f, 2.1f);
    }

    [Fact]
    public void DefaultRenderTextShaper_UsesStyleHeightWhenEntityHeightIsZero()
    {
        var settings = new CadRenderSceneSettings();
        var style = new TextStyle("Fixed")
        {
            Height = 3.0
        };
        var text = new TextEntity
        {
            Value = "A",
            Height = MathHelper.Epsilon * 0.5,
            InsertPoint = new XYZ(0, 0, 0),
            Style = style
        };

        var layout = new DefaultRenderTextShaper().Shape(text, settings);

        Assert.InRange(layout.Height, 2.9f, 3.1f);
    }

    [Fact]
    public void BuildScene_AppliesMTextColorOverride()
    {
        var document = new ACadSharp.CadDocument();
        var text = new MText
        {
            Value = "A\\C1;B",
            Height = 1.0,
            InsertPoint = new XYZ(0, 0, 0)
        };
        document.Entities.Add(text);

        var scene = CreateSceneBuilder().Build(document, new CadRenderSceneSettings());
        var runs = scene.Layers.SelectMany(layer => layer.Primitives).OfType<RenderText>().ToArray();

        var bRun = runs.FirstOrDefault(run => run.Text == "B");
        Assert.NotNull(bRun);
        Assert.True(bRun!.Color.R > bRun.Color.G);
    }

    [Fact]
    public void BuildScene_AppliesMTextFontAndHeightOverrides()
    {
        var document = new ACadSharp.CadDocument();
        var text = new MText
        {
            Value = "A\\FArial|b1|i1;\\H2x;B",
            Height = 1.0,
            InsertPoint = new XYZ(0, 0, 0)
        };
        document.Entities.Add(text);

        var scene = CreateSceneBuilder().Build(document, new CadRenderSceneSettings());
        var bRun = scene.Layers.SelectMany(layer => layer.Primitives)
            .OfType<RenderText>()
            .FirstOrDefault(run => run.Text == "B");

        Assert.NotNull(bRun);
        Assert.Equal("Arial", bRun!.FontFamily);
        Assert.True(bRun.IsBold);
        Assert.True(bRun.IsItalic);
        Assert.InRange(bRun.FontSize, 1.9f, 2.1f);
    }

    [Fact]
    public void BuildScene_ParsesMTextStacking()
    {
        var document = new ACadSharp.CadDocument();
        var text = new MText
        {
            Value = "\\S1/2;",
            Height = 1.0,
            InsertPoint = new XYZ(0, 0, 0)
        };
        document.Entities.Add(text);

        var scene = CreateSceneBuilder().Build(document, new CadRenderSceneSettings());
        var run = scene.Layers.SelectMany(layer => layer.Primitives)
            .OfType<RenderText>()
            .FirstOrDefault();

        Assert.NotNull(run);
        Assert.Equal("1/2", run!.Text);
    }

    [Fact]
    public void BuildScene_ScalesAnnotativeMTextInModelSpace()
    {
        var document = new ACadSharp.CadDocument();
        var text = new MText
        {
            Value = "Anno",
            Height = 1.0,
            InsertPoint = new XYZ(0, 0, 0),
            IsAnnotative = true
        };
        document.Entities.Add(text);

        var settings = new CadRenderSceneSettings
        {
            AnnotationScaleFactor = 10f
        };

        var scene = CreateSceneBuilder().Build(document, settings);
        var run = scene.Layers.SelectMany(layer => layer.Primitives)
            .OfType<RenderText>()
            .FirstOrDefault();

        Assert.NotNull(run);
        Assert.InRange(run!.FontSize, 9.9f, 10.1f);
    }

    [Fact]
    public void BuildScene_DoesNotScaleAnnotativeMTextInPaperSpace()
    {
        var document = new ACadSharp.CadDocument();
        var text = new MText
        {
            Value = "Anno",
            Height = 1.0,
            InsertPoint = new XYZ(0, 0, 0),
            IsAnnotative = true
        };
        document.PaperSpace.Entities.Add(text);

        var settings = new CadRenderSceneSettings
        {
            IsPaperSpace = true,
            AnnotationScaleFactor = 10f
        };

        var scene = CreateSceneBuilder().Build(document, settings);
        var run = scene.Layers.SelectMany(layer => layer.Primitives)
            .OfType<RenderText>()
            .FirstOrDefault();

        Assert.NotNull(run);
        Assert.InRange(run!.FontSize, 0.9f, 1.1f);
    }

    [Fact]
    public void BuildScene_AddsMTextBackgroundFillAndFrame()
    {
        var document = new ACadSharp.CadDocument();
        var text = new MText
        {
            Value = "Masked",
            Height = 1.0,
            InsertPoint = new XYZ(0, 0, 0),
            BackgroundFillFlags = BackgroundFillFlags.UseBackgroundFillColor | BackgroundFillFlags.TextFrame,
            BackgroundColor = new Color(10, 20, 30),
            BackgroundScale = 1.2
        };
        document.Entities.Add(text);

        var scene = CreateSceneBuilder().Build(document, new CadRenderSceneSettings());
        var primitives = scene.Layers.SelectMany(layer => layer.Primitives).ToArray();

        Assert.Contains(primitives, primitive =>
            primitive is RenderFill fill &&
            fill.Color.R == 10 &&
            fill.Color.G == 20 &&
            fill.Color.B == 30);
        Assert.Contains(primitives, primitive =>
            primitive is RenderPolyline polyline &&
            polyline.IsClosed);
    }

    [Fact]
    public void BuildScene_UsesDrawingWindowColorForMTextMask()
    {
        var document = new ACadSharp.CadDocument();
        var text = new MText
        {
            Value = "Masked",
            Height = 1.0,
            InsertPoint = new XYZ(0, 0, 0),
            BackgroundFillFlags = BackgroundFillFlags.UseDrawingWindowColor
        };
        document.Entities.Add(text);

        var background = new RenderColor(12, 34, 56, 255);
        var settings = new CadRenderSceneSettings { Background = background };
        var scene = CreateSceneBuilder().Build(document, settings);
        var fill = scene.Layers.SelectMany(layer => layer.Primitives).OfType<RenderFill>().FirstOrDefault();

        Assert.NotNull(fill);
        Assert.Equal(background.R, fill!.Color.R);
        Assert.Equal(background.G, fill.Color.G);
        Assert.Equal(background.B, fill.Color.B);
    }

    [Fact]
    public void BuildScene_AddsTextBackgroundFromExtendedData()
    {
        var document = new ACadSharp.CadDocument();
        var text = new TextEntity
        {
            Value = "Masked",
            Height = 1.0,
            InsertPoint = new XYZ(0, 0, 0)
        };
        text.ExtendedData.Add("ACAD_INSPECTOR_TEXT_BG", new ExtendedData(new ExtendedDataRecord[]
        {
            new ExtendedDataInteger16(3),
            new ExtendedDataInteger32(2),
            new ExtendedDataReal(1.1)
        }));
        document.Entities.Add(text);

        var scene = CreateSceneBuilder().Build(document, new CadRenderSceneSettings());
        var primitives = scene.Layers.SelectMany(layer => layer.Primitives).ToArray();

        var fill = primitives.OfType<RenderFill>().FirstOrDefault();
        Assert.NotNull(fill);
        Assert.True(fill!.Color.G >= 200);
        Assert.Contains(primitives, primitive =>
            primitive is RenderPolyline polyline &&
            polyline.IsClosed);
    }

    [Fact]
    public void DefaultRenderTextShaper_HandlesMTextLines()
    {
        var settings = new CadRenderSceneSettings { TextWidthFactor = 0.6f };
        var text = new MText
        {
            Value = "A\\PBB",
            Height = 1.0,
            InsertPoint = new XYZ(0, 0, 0),
            LineSpacing = 1.0
        };

        var layout = new DefaultRenderTextShaper().Shape(text, settings);

        Assert.InRange(layout.Width, 1.1f, 1.3f);
        Assert.InRange(layout.Height, 1.9f, 2.1f);
    }

    private static CadRenderSceneBuilder CreateSceneBuilder()
    {
        var handlers = new IRenderEntityHandler[]
        {
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
}
