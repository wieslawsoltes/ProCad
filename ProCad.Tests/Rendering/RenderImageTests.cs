using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Numerics;
using ProCad.Rendering;
using ACadSharp;
using ACadSharp.Entities;
using ACadSharp.Objects;
using ACadSharp.Tables;
using CSMath;
using Xunit;

namespace ProCad.Tests.Rendering;

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
            Flags = UnderlayDisplayFlags.ShowUnderlay | UnderlayDisplayFlags.ClippingOn | UnderlayDisplayFlags.ClipInsideMode,
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
        Assert.Equal(RenderLoopFillMode.NonZero, clipGroup!.FillMode);
        Assert.Single(clipGroup.Loops);
        Assert.Contains(clipGroup!.Primitives, primitive => primitive is RenderImage);
    }

    [Fact]
    public void BuildScene_PdfUnderlayInsideClip_PreservesFullImageExtent()
    {
        var document = new CadDocument();
        var definition = new PdfUnderlayDefinition
        {
            Name = "PDF1A",
            File = "inside.pdf",
            Page = "1"
        };
        var underlay = new PdfUnderlay(definition)
        {
            Flags = UnderlayDisplayFlags.ShowUnderlay | UnderlayDisplayFlags.ClippingOn | UnderlayDisplayFlags.ClipInsideMode,
            InsertPoint = new XYZ(2, 3, 0),
            XScale = 6,
            YScale = 4
        };
        underlay.ClipBoundaryVertices.Add(new XY(0.25, 0.25));
        underlay.ClipBoundaryVertices.Add(new XY(0.75, 0.75));
        document.Entities.Add(underlay);

        var scene = CreateSceneBuilder().Build(document, new CadRenderSceneSettings());
        var clipGroup = scene.Layers.SelectMany(layer => layer.Primitives)
            .OfType<RenderClipGroup>()
            .FirstOrDefault();

        Assert.NotNull(clipGroup);
        Assert.Equal(RenderLoopFillMode.NonZero, clipGroup!.FillMode);
        Assert.Single(clipGroup.Loops);
        var image = Assert.Single(clipGroup.Primitives.OfType<RenderImage>());
        Assert.Equal(2f, image.Origin.X);
        Assert.Equal(3f, image.Origin.Y);
        Assert.Equal(6f, image.UVector.X);
        Assert.Equal(0f, image.UVector.Y);
        Assert.Equal(0f, image.VVector.X);
        Assert.Equal(4f, image.VVector.Y);
        Assert.Equal(1f, image.Size.X);
        Assert.Equal(1f, image.Size.Y);
    }

    [Fact]
    public void BuildScene_RendersPdfUnderlayUsingNormalOrientation()
    {
        var document = new CadDocument();
        var definition = new PdfUnderlayDefinition
        {
            Name = "PDF2",
            File = "normal.pdf",
            Page = "1"
        };
        var underlay = new PdfUnderlay(definition)
        {
            Flags = UnderlayDisplayFlags.ShowUnderlay,
            InsertPoint = new XYZ(10, 20, 0),
            Normal = -XYZ.AxisZ,
            Rotation = 0,
            XScale = 2,
            YScale = 1
        };
        document.Entities.Add(underlay);

        var scene = CreateSceneBuilder().Build(document, new CadRenderSceneSettings());
        var imagePrimitive = scene.Layers.SelectMany(layer => layer.Primitives)
            .OfType<RenderImage>()
            .FirstOrDefault();

        Assert.NotNull(imagePrimitive);
        Assert.Equal(10f, imagePrimitive!.Origin.X);
        Assert.Equal(20f, imagePrimitive.Origin.Y);
        Assert.Equal(-2f, imagePrimitive.UVector.X);
        Assert.Equal(0f, imagePrimitive.UVector.Y);
        Assert.Equal(0f, imagePrimitive.VVector.X);
        Assert.Equal(1f, imagePrimitive.VVector.Y);
    }

    [Fact]
    public void BuildScene_PdfUnderlayWithStoredClip_ClippingDisabledRendersFullExtent()
    {
        var document = new CadDocument();
        var definition = new PdfUnderlayDefinition
        {
            Name = "PDF3",
            File = "noclip.pdf",
            Page = "1"
        };
        var underlay = new PdfUnderlay(definition)
        {
            Flags = UnderlayDisplayFlags.ShowUnderlay,
            InsertPoint = new XYZ(5, 7, 0),
            XScale = 2,
            YScale = 3
        };
        underlay.ClipBoundaryVertices.Add(new XY(0.25, 0.25));
        underlay.ClipBoundaryVertices.Add(new XY(0.75, 0.75));
        document.Entities.Add(underlay);

        var scene = CreateSceneBuilder().Build(document, new CadRenderSceneSettings());
        Assert.DoesNotContain(
            scene.Layers.SelectMany(layer => layer.Primitives),
            primitive => primitive is RenderClipGroup);

        var imagePrimitive = scene.Layers.SelectMany(layer => layer.Primitives)
            .OfType<RenderImage>()
            .FirstOrDefault();
        Assert.NotNull(imagePrimitive);
        Assert.Equal(5f, imagePrimitive!.Origin.X);
        Assert.Equal(7f, imagePrimitive.Origin.Y);
        Assert.Equal(2f, imagePrimitive.UVector.X);
        Assert.Equal(0f, imagePrimitive.UVector.Y);
        Assert.Equal(0f, imagePrimitive.VVector.X);
        Assert.Equal(3f, imagePrimitive.VVector.Y);
        Assert.Equal(1f, imagePrimitive.Size.X);
        Assert.Equal(1f, imagePrimitive.Size.Y);
    }

    [Fact]
    public void BuildScene_RendersPdfUnderlayOutsideClipAsEvenOddMask()
    {
        var document = new CadDocument();
        var definition = new PdfUnderlayDefinition
        {
            Name = "PDF4",
            File = "outside.pdf",
            Page = "1"
        };
        var underlay = new PdfUnderlay(definition)
        {
            Flags = UnderlayDisplayFlags.ShowUnderlay | UnderlayDisplayFlags.ClippingOn,
            InsertPoint = XYZ.Zero,
            XScale = 6,
            YScale = 6
        };
        underlay.ClipBoundaryVertices.Add(new XY(0.25, 0.25));
        underlay.ClipBoundaryVertices.Add(new XY(0.75, 0.75));
        document.Entities.Add(underlay);

        var scene = CreateSceneBuilder().Build(document, new CadRenderSceneSettings());
        var clipGroup = scene.Layers.SelectMany(layer => layer.Primitives)
            .OfType<RenderClipGroup>()
            .FirstOrDefault();

        Assert.NotNull(clipGroup);
        Assert.Equal(RenderLoopFillMode.EvenOdd, clipGroup!.FillMode);
        Assert.Equal(2, clipGroup.Loops.Count);
        var image = Assert.Single(clipGroup.Primitives.OfType<RenderImage>());
        Assert.Equal(0f, image.Origin.X);
        Assert.Equal(0f, image.Origin.Y);
        Assert.Equal(6f, image.UVector.X);
        Assert.Equal(0f, image.UVector.Y);
        Assert.Equal(0f, image.VVector.X);
        Assert.Equal(6f, image.VVector.Y);
        Assert.Equal(1f, image.Size.X);
        Assert.Equal(1f, image.Size.Y);
    }

    [Fact]
    public void BuildScene_PdfUnderlayOutsideClip_RejectsHitInsideClipBoundary()
    {
        var document = new CadDocument();
        var definition = new PdfUnderlayDefinition
        {
            Name = "PDF5",
            File = "outside-hit.pdf",
            Page = "1"
        };
        var underlay = new PdfUnderlay(definition)
        {
            Flags = UnderlayDisplayFlags.ShowUnderlay | UnderlayDisplayFlags.ClippingOn,
            InsertPoint = XYZ.Zero,
            XScale = 6,
            YScale = 6
        };
        underlay.ClipBoundaryVertices.Add(new XY(0.25, 0.25));
        underlay.ClipBoundaryVertices.Add(new XY(0.75, 0.75));
        document.Entities.Add(underlay);

        var scene = CreateSceneBuilder().Build(document, new CadRenderSceneSettings());
        var hitTest = new RenderHitTestEngine();
        var results = new List<RenderHitTestResult>();

        hitTest.HitTestPoint(scene, new Vector2(3f, 3f), tolerance: 0f, results);
        Assert.Empty(results);

        hitTest.HitTestPoint(scene, new Vector2(1f, 1f), tolerance: 0f, results);
        Assert.Contains(results, result => result.Primitive is RenderImage);
    }

    [Fact]
    public void BuildScene_HiddenPdfUnderlay_RendersFrameWithoutImage()
    {
        var document = new CadDocument();
        var definition = new PdfUnderlayDefinition
        {
            Name = "PDF6",
            File = "hidden.pdf",
            Page = "1"
        };
        var underlay = new PdfUnderlay(definition)
        {
            Flags = UnderlayDisplayFlags.ClippingOn,
            InsertPoint = new XYZ(1, 2, 0),
            XScale = 4,
            YScale = 3
        };
        underlay.ClipBoundaryVertices.Add(new XY(0.25, 0.25));
        underlay.ClipBoundaryVertices.Add(new XY(0.75, 0.75));
        document.Entities.Add(underlay);

        var scene = CreateSceneBuilder().Build(document, new CadRenderSceneSettings
        {
            UnderlayFrameVisibility = RenderFrameVisibility.Hidden
        });
        var primitives = scene.Layers.SelectMany(layer => layer.Primitives).ToArray();

        Assert.DoesNotContain(primitives, primitive => primitive is RenderImage);
        var frame = Assert.Single(primitives.OfType<RenderPolyline>());
        Assert.True(frame.IsClosed);
        Assert.Equal(4, frame.Points.Count);
        Assert.Contains(frame.Points, point => point.X == 2f && point.Y == 2.75f);
        Assert.Contains(frame.Points, point => point.X == 4f && point.Y == 2.75f);
        Assert.Contains(frame.Points, point => point.X == 4f && point.Y == 4.25f);
        Assert.Contains(frame.Points, point => point.X == 2f && point.Y == 4.25f);
    }

    [Fact]
    public void BuildScene_PdfUnderlayFrameHidden_DoesNotRenderFrameWhenUnderlayVisible()
    {
        var document = new CadDocument();
        var definition = new PdfUnderlayDefinition
        {
            Name = "PDF7",
            File = "visible.pdf",
            Page = "1"
        };
        var underlay = new PdfUnderlay(definition)
        {
            Flags = UnderlayDisplayFlags.ShowUnderlay,
            InsertPoint = XYZ.Zero,
            XScale = 2,
            YScale = 2
        };
        document.Entities.Add(underlay);

        var scene = CreateSceneBuilder().Build(document, new CadRenderSceneSettings
        {
            UnderlayFrameVisibility = RenderFrameVisibility.Hidden
        });
        var primitives = scene.Layers.SelectMany(layer => layer.Primitives).ToArray();

        Assert.Contains(primitives, primitive => primitive is RenderImage);
        Assert.DoesNotContain(primitives, primitive => primitive is RenderPolyline);
    }

    [Fact]
    public void BuildScene_RendersRasterImageOutsideClipAsEvenOddMask()
    {
        using var support = new TemporarySupportFile("inverse.png");
        var document = new CadDocument();
        var definition = new ImageDefinition
        {
            Name = "IMG2",
            FileName = "inverse.png",
            Size = new XY(6, 6)
        };
        var image = new RasterImage(definition)
        {
            Flags = ImageDisplayFlags.ShowImage | ImageDisplayFlags.UseClippingBoundary,
            ClippingState = true,
            ClipType = ClipType.Rectangular,
            ClipMode = ClipMode.Outside,
            Size = new XY(6, 6),
            InsertPoint = XYZ.Zero,
            UVector = XYZ.AxisX,
            VVector = XYZ.AxisY
        };
        image.ClipBoundaryVertices.Add(new XY(2, 2));
        image.ClipBoundaryVertices.Add(new XY(4, 4));
        document.Entities.Add(image);

        var scene = CreateSceneBuilder().Build(document, new CadRenderSceneSettings
        {
            SupportPaths = new[] { support.Directory }
        });
        var clipGroup = scene.Layers.SelectMany(layer => layer.Primitives)
            .OfType<RenderClipGroup>()
            .FirstOrDefault();

        Assert.NotNull(clipGroup);
        Assert.Equal(RenderLoopFillMode.EvenOdd, clipGroup!.FillMode);
        Assert.Equal(2, clipGroup.Loops.Count);
    }

    [Fact]
    public void BuildScene_RasterImageOutsideClip_RejectsHitInsideClipBoundary()
    {
        using var support = new TemporarySupportFile("inverse-hit.png");
        var document = new CadDocument();
        var definition = new ImageDefinition
        {
            Name = "IMG3",
            FileName = "inverse-hit.png",
            Size = new XY(6, 6)
        };
        var image = new RasterImage(definition)
        {
            Flags = ImageDisplayFlags.ShowImage | ImageDisplayFlags.UseClippingBoundary,
            ClippingState = true,
            ClipType = ClipType.Rectangular,
            ClipMode = ClipMode.Outside,
            Size = new XY(6, 6),
            InsertPoint = XYZ.Zero,
            UVector = XYZ.AxisX,
            VVector = XYZ.AxisY
        };
        image.ClipBoundaryVertices.Add(new XY(2, 2));
        image.ClipBoundaryVertices.Add(new XY(4, 4));
        document.Entities.Add(image);

        var scene = CreateSceneBuilder().Build(document, new CadRenderSceneSettings
        {
            SupportPaths = new[] { support.Directory }
        });

        var hitTest = new RenderHitTestEngine();
        var results = new List<RenderHitTestResult>();

        hitTest.HitTestPoint(scene, new Vector2(3f, 3f), tolerance: 0f, results);
        Assert.Empty(results);

        hitTest.HitTestPoint(scene, new Vector2(1f, 1f), tolerance: 0f, results);
        Assert.Contains(results, result => result.Primitive is RenderImage);
    }

    [Fact]
    public void BuildScene_RasterImageOutsideClipWithoutBoundary_DoesNotInvertEntireImage()
    {
        using var support = new TemporarySupportFile("inverse-default.png");
        var document = new CadDocument();
        var definition = new ImageDefinition
        {
            Name = "IMG3A",
            FileName = "inverse-default.png",
            Size = new XY(6, 6)
        };
        var image = new RasterImage(definition)
        {
            Flags = ImageDisplayFlags.ShowImage | ImageDisplayFlags.UseClippingBoundary,
            ClippingState = true,
            ClipType = ClipType.Rectangular,
            ClipMode = ClipMode.Outside,
            Size = new XY(6, 6),
            InsertPoint = XYZ.Zero,
            UVector = XYZ.AxisX,
            VVector = XYZ.AxisY
        };
        document.Entities.Add(image);

        var scene = CreateSceneBuilder().Build(document, new CadRenderSceneSettings
        {
            SupportPaths = new[] { support.Directory }
        });

        var clipGroup = scene.Layers.SelectMany(layer => layer.Primitives)
            .OfType<RenderClipGroup>()
            .FirstOrDefault();
        Assert.NotNull(clipGroup);
        Assert.Equal(RenderLoopFillMode.NonZero, clipGroup!.FillMode);
        Assert.Single(clipGroup.Loops);

        var hitTest = new RenderHitTestEngine();
        var results = new List<RenderHitTestResult>();
        hitTest.HitTestPoint(scene, new Vector2(3f, 3f), tolerance: 0f, results);
        Assert.Contains(results, result => result.Primitive is RenderImage);
    }

    [Fact]
    public void BuildScene_HiddenRasterImage_RendersBoundaryWithoutImage()
    {
        var document = new CadDocument();
        var definition = new ImageDefinition
        {
            Name = "IMG3B",
            FileName = "hidden.png",
            Size = new XY(4, 3)
        };
        var image = new RasterImage(definition)
        {
            Size = new XY(4, 3),
            InsertPoint = XYZ.Zero,
            UVector = XYZ.AxisX,
            VVector = XYZ.AxisY
        };
        image.ShowImage = false;
        document.Entities.Add(image);

        var scene = CreateSceneBuilder().Build(document, new CadRenderSceneSettings
        {
            ImageFrameVisibility = RenderFrameVisibility.Hidden
        });
        var primitives = scene.Layers.SelectMany(layer => layer.Primitives).ToArray();

        Assert.DoesNotContain(primitives, primitive => primitive is RenderImage);
        var frame = Assert.Single(primitives.OfType<RenderPolyline>());
        Assert.True(frame.IsClosed);
        Assert.Equal(4, frame.Points.Count);
    }

    [Fact]
    public void BuildScene_RasterImageFrameHidden_DoesNotRenderFramePolyline()
    {
        using var support = new TemporarySupportFile("frame-hidden.png");
        var document = new CadDocument();
        var definition = new ImageDefinition
        {
            Name = "IMG4",
            FileName = "frame-hidden.png",
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
            SupportPaths = new[] { support.Directory },
            ImageFrameVisibility = RenderFrameVisibility.Hidden
        });

        Assert.DoesNotContain(
            scene.Layers.SelectMany(layer => layer.Primitives),
            primitive => primitive is RenderPolyline);
    }

    [Fact]
    public void BuildScene_RasterImageFrameVisible_UsesImageExtentWhenNotClipped()
    {
        using var support = new TemporarySupportFile("frame-visible.png");
        var document = new CadDocument();
        var definition = new ImageDefinition
        {
            Name = "IMG5",
            FileName = "frame-visible.png",
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
            SupportPaths = new[] { support.Directory },
            ImageFrameVisibility = RenderFrameVisibility.DisplayAndPlot
        });

        var frame = scene.Layers.SelectMany(layer => layer.Primitives)
            .OfType<RenderPolyline>()
            .FirstOrDefault();
        Assert.NotNull(frame);
        Assert.True(frame!.IsClosed);
        Assert.Equal(4, frame.Points.Count);
        Assert.Contains(frame.Points, point => point.X == -0.5f && point.Y == -0.5f);
        Assert.Contains(frame.Points, point => point.X == 3.5f && point.Y == -0.5f);
        Assert.Contains(frame.Points, point => point.X == 3.5f && point.Y == 2.5f);
        Assert.Contains(frame.Points, point => point.X == -0.5f && point.Y == 2.5f);
    }

    [Fact]
    public void BuildScene_RasterImageFrameVisible_UsesClipBoundaryWhenClipped()
    {
        using var support = new TemporarySupportFile("frame-clipped.png");
        var document = new CadDocument();
        var definition = new ImageDefinition
        {
            Name = "IMG6",
            FileName = "frame-clipped.png",
            Size = new XY(6, 6)
        };
        var image = new RasterImage(definition)
        {
            Flags = ImageDisplayFlags.ShowImage | ImageDisplayFlags.UseClippingBoundary,
            ClippingState = true,
            ClipType = ClipType.Rectangular,
            Size = new XY(6, 6),
            InsertPoint = XYZ.Zero,
            UVector = XYZ.AxisX,
            VVector = XYZ.AxisY
        };
        image.ClipBoundaryVertices.Add(new XY(2, 2));
        image.ClipBoundaryVertices.Add(new XY(4, 4));
        document.Entities.Add(image);

        var scene = CreateSceneBuilder().Build(document, new CadRenderSceneSettings
        {
            SupportPaths = new[] { support.Directory },
            ImageFrameVisibility = RenderFrameVisibility.DisplayAndPlot
        });

        var frame = scene.Layers.SelectMany(layer => layer.Primitives)
            .OfType<RenderPolyline>()
            .FirstOrDefault();
        Assert.NotNull(frame);
        Assert.True(frame!.IsClosed);
        Assert.Equal(4, frame.Points.Count);
        Assert.Contains(frame.Points, point => point.X == 2f && point.Y == 2f);
        Assert.Contains(frame.Points, point => point.X == 4f && point.Y == 2f);
        Assert.Contains(frame.Points, point => point.X == 4f && point.Y == 4f);
        Assert.Contains(frame.Points, point => point.X == 2f && point.Y == 4f);
    }

    [Fact]
    public void BuildScene_RasterImage_AppliesBrightnessToneToImageTint()
    {
        using var support = new TemporarySupportFile("tone.png");
        var document = new CadDocument();
        var definition = new ImageDefinition
        {
            Name = "IMG7",
            FileName = "tone.png",
            Size = new XY(4, 3)
        };
        var image = new RasterImage(definition)
        {
            Flags = ImageDisplayFlags.ShowImage,
            Size = new XY(4, 3),
            InsertPoint = XYZ.Zero,
            UVector = XYZ.AxisX,
            VVector = XYZ.AxisY,
            Brightness = 75,
            Contrast = 50,
            Color = new ACadSharp.Color(20, 40, 60)
        };
        document.Entities.Add(image);

        var scene = CreateSceneBuilder().Build(document, new CadRenderSceneSettings
        {
            SupportPaths = new[] { support.Directory }
        });

        var renderImage = scene.Layers.SelectMany(layer => layer.Primitives)
            .OfType<RenderImage>()
            .FirstOrDefault();
        Assert.NotNull(renderImage);
        Assert.True(renderImage!.Color.R > 20);
        Assert.True(renderImage.Color.G > 40);
        Assert.True(renderImage.Color.B > 60);
    }

    [Fact]
    public void BuildScene_UnderlayContrastZero_FlattensTintToNeutralGray()
    {
        var document = new CadDocument();
        var definition = new PdfUnderlayDefinition
        {
            Name = "PDF8",
            File = "contrast.pdf",
            Page = "1"
        };
        var underlay = new PdfUnderlay(definition)
        {
            Flags = UnderlayDisplayFlags.ShowUnderlay,
            InsertPoint = XYZ.Zero,
            XScale = 2,
            YScale = 2,
            Contrast = 0,
            Color = new ACadSharp.Color(255, 0, 0)
        };
        document.Entities.Add(underlay);

        var scene = CreateSceneBuilder().Build(document, new CadRenderSceneSettings());
        var renderImage = scene.Layers.SelectMany(layer => layer.Primitives)
            .OfType<RenderImage>()
            .FirstOrDefault();

        Assert.NotNull(renderImage);
        Assert.Equal(128, renderImage!.Color.R);
        Assert.Equal(128, renderImage.Color.G);
        Assert.Equal(128, renderImage.Color.B);
    }

    [Fact]
    public void BuildScene_UnderlayAdjustForBackground_IncreasesContrastOnDarkBackground()
    {
        var document = new CadDocument();
        var definition = new PdfUnderlayDefinition
        {
            Name = "PDF9",
            File = "adjust-bg.pdf",
            Page = "1"
        };
        var underlay = new PdfUnderlay(definition)
        {
            Flags = UnderlayDisplayFlags.ShowUnderlay | UnderlayDisplayFlags.AdjustForBackground,
            InsertPoint = XYZ.Zero,
            XScale = 2,
            YScale = 2,
            Contrast = 100,
            Color = new ACadSharp.Color(20, 20, 20)
        };
        document.Entities.Add(underlay);

        var scene = CreateSceneBuilder().Build(document, new CadRenderSceneSettings
        {
            Background = new RenderColor(24, 26, 31, 255)
        });
        var renderImage = scene.Layers.SelectMany(layer => layer.Primitives)
            .OfType<RenderImage>()
            .FirstOrDefault();

        Assert.NotNull(renderImage);
        Assert.True(renderImage!.Color.R > 150);
        Assert.True(renderImage.Color.G > 150);
        Assert.True(renderImage.Color.B > 150);
    }

    [Fact]
    public void BuildScene_UnderlayAdjustForBackground_DarkensNearWhiteTintOnLightBackground()
    {
        var document = new CadDocument();
        var definition = new PdfUnderlayDefinition
        {
            Name = "PDF10",
            File = "adjust-bg-light.pdf",
            Page = "1"
        };
        var underlay = new PdfUnderlay(definition)
        {
            Flags = UnderlayDisplayFlags.ShowUnderlay | UnderlayDisplayFlags.AdjustForBackground,
            InsertPoint = XYZ.Zero,
            XScale = 2,
            YScale = 2,
            Contrast = 100,
            Color = new ACadSharp.Color(240, 240, 240)
        };
        document.Entities.Add(underlay);

        var scene = CreateSceneBuilder().Build(document, new CadRenderSceneSettings
        {
            Background = new RenderColor(245, 245, 245, 255)
        });
        var renderImage = scene.Layers.SelectMany(layer => layer.Primitives)
            .OfType<RenderImage>()
            .FirstOrDefault();

        Assert.NotNull(renderImage);
        Assert.True(renderImage!.Color.R < 100);
        Assert.True(renderImage.Color.G < 100);
        Assert.True(renderImage.Color.B < 100);
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
            Directory = Path.Combine(Path.GetTempPath(), "procad-tests");
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
