using System.Numerics;
using ProCad.Controls;
using ProCad.Rendering;
using SkiaSharp;

namespace ProCad.Controls.Skia;

/// <summary>
/// Renders ProCad render scenes into a Skia canvas.
/// </summary>
public sealed class CadSkiaSceneRenderer
{
    private const float GridTargetPixels = 96f;
    private const float AxisStrokePixels = 1.25f;

    /// <summary>
    /// Renders a scene into the provided canvas.
    /// </summary>
    public void Render(SKCanvas canvas, RenderScene? scene, CadSceneViewport viewport, CadRenderOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(canvas);

        var renderOptions = options ?? CadRenderOptions.Default;
        var background = scene is not null && renderOptions.UseSceneBackground
            ? scene.Background
            : renderOptions.Background;
        canvas.Clear(ToSkColor(background));

        if (scene is null || !viewport.IsValid)
        {
            return;
        }

        using var restore = new SKAutoCanvasRestore(canvas, true);
        canvas.ClipRect(new SKRect(0f, 0f, (float)viewport.Size.Width, (float)viewport.Size.Height));

        if (renderOptions.ShowGrid)
        {
            DrawGrid(canvas, viewport);
        }

        if (renderOptions.ShowAxes)
        {
            DrawAxes(canvas, viewport);
        }

        canvas.Save();
        ApplyWorldTransform(canvas, viewport);
        DrawScenePrimitives(canvas, scene, viewport, renderOptions);
        canvas.Restore();

        DrawSceneText(canvas, scene, viewport, renderOptions);
        DrawSelection(canvas, viewport, renderOptions);
    }

    private static void DrawScenePrimitives(
        SKCanvas canvas,
        RenderScene scene,
        CadSceneViewport viewport,
        CadRenderOptions options)
    {
        var layers = scene.Layers;
        for (var layerIndex = 0; layerIndex < layers.Count; layerIndex++)
        {
            var layer = layers[layerIndex];
            if (!layer.IsVisible && !options.IncludeHiddenLayers)
            {
                continue;
            }

            var primitives = layer.Primitives;
            for (var i = 0; i < primitives.Count; i++)
            {
                if (primitives[i] is RenderText)
                {
                    continue;
                }

                DrawPrimitive(canvas, primitives[i], viewport, options);
            }
        }
    }

    private static void DrawSceneText(
        SKCanvas canvas,
        RenderScene scene,
        CadSceneViewport viewport,
        CadRenderOptions options)
    {
        var layers = scene.Layers;
        for (var layerIndex = 0; layerIndex < layers.Count; layerIndex++)
        {
            var layer = layers[layerIndex];
            if (!layer.IsVisible && !options.IncludeHiddenLayers)
            {
                continue;
            }

            var primitives = layer.Primitives;
            for (var i = 0; i < primitives.Count; i++)
            {
                if (primitives[i] is RenderText text)
                {
                    DrawText(canvas, text, viewport);
                }
            }
        }
    }

    private static void DrawPrimitive(
        SKCanvas canvas,
        IRenderPrimitive primitive,
        CadSceneViewport viewport,
        CadRenderOptions options)
    {
        switch (primitive)
        {
            case RenderLine line:
                DrawLine(canvas, line, viewport, options);
                break;
            case RenderPolyline polyline:
                DrawPolyline(canvas, polyline, viewport, options);
                break;
            case RenderFill fill:
                DrawFill(canvas, fill);
                break;
            case RenderTriangle triangle:
                DrawTriangle(canvas, triangle);
                break;
            case RenderHatchFill hatchFill:
                DrawHatchFill(canvas, hatchFill);
                break;
            case RenderHatchPattern hatchPattern:
                DrawHatchPattern(canvas, hatchPattern, viewport, options);
                break;
            case RenderCircle circle:
                DrawCircle(canvas, circle, viewport, options);
                break;
            case RenderArc arc:
                DrawArc(canvas, arc, viewport, options);
                break;
            case RenderPoint point:
                DrawPoint(canvas, point, viewport, options);
                break;
            case RenderImage image:
                DrawImagePlaceholder(canvas, image, viewport, options);
                break;
            case RenderClipGroup clipGroup:
                DrawClipGroup(canvas, clipGroup, viewport, options);
                break;
        }
    }

    private static void DrawLine(
        SKCanvas canvas,
        RenderLine line,
        CadSceneViewport viewport,
        CadRenderOptions options)
    {
        using var paint = CreateStrokePaint(line.Color, line.Thickness, line.LineCap, line.LineJoin, viewport, options, line.DashPattern, line.DashPhase);
        canvas.DrawLine(line.Start.X, line.Start.Y, line.End.X, line.End.Y, paint);
    }

    private static void DrawPolyline(
        SKCanvas canvas,
        RenderPolyline polyline,
        CadSceneViewport viewport,
        CadRenderOptions options)
    {
        if (polyline.Points.Count == 0)
        {
            return;
        }

        using var path = CreatePath(polyline.Points, polyline.IsClosed);
        using var paint = CreateStrokePaint(
            polyline.Color,
            polyline.Thickness,
            polyline.LineCap,
            polyline.LineJoin,
            viewport,
            options,
            polyline.DashPattern,
            polyline.DashPhase);
        canvas.DrawPath(path, paint);
    }

    private static void DrawFill(SKCanvas canvas, RenderFill fill)
    {
        if (fill.Points.Count == 0)
        {
            return;
        }

        using var path = CreatePath(fill.Points, isClosed: true);
        using var paint = CreateFillPaint(fill.Color);
        canvas.DrawPath(path, paint);
    }

    private static void DrawTriangle(SKCanvas canvas, RenderTriangle triangle)
    {
        using var path = new SKPath();
        path.MoveTo(triangle.A.X, triangle.A.Y);
        path.LineTo(triangle.B.X, triangle.B.Y);
        path.LineTo(triangle.C.X, triangle.C.Y);
        path.Close();
        using var paint = CreateFillPaint(Shade(triangle.Color, triangle.Shade));
        canvas.DrawPath(path, paint);
    }

    private static void DrawHatchFill(SKCanvas canvas, RenderHatchFill fill)
    {
        using var path = CreateLoopPath(fill.Loops, fill.FillMode);
        using var paint = CreateFillPaint(fill.Color);
        canvas.DrawPath(path, paint);
    }

    private static void DrawHatchPattern(
        SKCanvas canvas,
        RenderHatchPattern pattern,
        CadSceneViewport viewport,
        CadRenderOptions options)
    {
        using var paint = CreateStrokePaint(
            pattern.Color,
            pattern.Thickness,
            pattern.LineCap,
            pattern.LineJoin,
            viewport,
            options,
            dashPattern: null,
            dashPhase: 0f);

        using var clip = CreateLoopPath(pattern.Loops, pattern.FillMode);
        canvas.Save();
        canvas.ClipPath(clip, SKClipOperation.Intersect, antialias: true);

        var segments = pattern.Segments;
        for (var i = 0; i < segments.Count; i++)
        {
            var segment = segments[i];
            canvas.DrawLine(segment.Start.X, segment.Start.Y, segment.End.X, segment.End.Y, paint);
        }

        canvas.Restore();
    }

    private static void DrawCircle(
        SKCanvas canvas,
        RenderCircle circle,
        CadSceneViewport viewport,
        CadRenderOptions options)
    {
        using var paint = CreateStrokePaint(circle.Color, circle.Thickness, circle.LineCap, circle.LineJoin, viewport, options);
        canvas.DrawCircle(circle.Center.X, circle.Center.Y, circle.Radius, paint);
    }

    private static void DrawArc(
        SKCanvas canvas,
        RenderArc arc,
        CadSceneViewport viewport,
        CadRenderOptions options)
    {
        var sweep = NormalizeSweep(arc.StartAngle, arc.EndAngle);
        var segmentCount = Math.Max(12, (int)MathF.Ceiling(MathF.Abs(sweep) * arc.Radius / Math.Max(1f, viewport.ScreenToWorldDistance(8d))));
        using var path = new SKPath();

        for (var i = 0; i <= segmentCount; i++)
        {
            var t = i / (float)segmentCount;
            var angle = arc.StartAngle + (sweep * t);
            var point = arc.Center + new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * arc.Radius;
            if (i == 0)
            {
                path.MoveTo(point.X, point.Y);
            }
            else
            {
                path.LineTo(point.X, point.Y);
            }
        }

        using var paint = CreateStrokePaint(arc.Color, arc.Thickness, arc.LineCap, arc.LineJoin, viewport, options);
        canvas.DrawPath(path, paint);
    }

    private static void DrawPoint(
        SKCanvas canvas,
        RenderPoint point,
        CadSceneViewport viewport,
        CadRenderOptions options)
    {
        var radius = point.DisplaySize > 0d
            ? Math.Max((float)point.DisplaySize * 0.5f, viewport.ScreenToWorldDistance(3d))
            : viewport.ScreenToWorldDistance(4d);

        using var paint = CreateStrokePaint(point.Color, point.Thickness, point.LineCap, point.LineJoin, viewport, options);
        canvas.DrawLine(point.Point.X - radius, point.Point.Y, point.Point.X + radius, point.Point.Y, paint);
        canvas.DrawLine(point.Point.X, point.Point.Y - radius, point.Point.X, point.Point.Y + radius, paint);
    }

    private static void DrawImagePlaceholder(
        SKCanvas canvas,
        RenderImage image,
        CadSceneViewport viewport,
        CadRenderOptions options)
    {
        var p0 = image.Origin;
        var p1 = image.Origin + image.UVector * image.Size.X;
        var p2 = image.Origin + image.UVector * image.Size.X + image.VVector * image.Size.Y;
        var p3 = image.Origin + image.VVector * image.Size.Y;

        using var path = new SKPath();
        path.MoveTo(p0.X, p0.Y);
        path.LineTo(p1.X, p1.Y);
        path.LineTo(p2.X, p2.Y);
        path.LineTo(p3.X, p3.Y);
        path.Close();

        var fill = image.Color.A == 0 ? new RenderColor(128, 128, 128, 32) : new RenderColor(image.Color.R, image.Color.G, image.Color.B, (byte)Math.Clamp(image.Opacity * 96f, 24f, 160f));
        using var fillPaint = CreateFillPaint(fill);
        using var strokePaint = CreateStrokePaint(image.Color.A == 0 ? RenderColor.DefaultForeground : image.Color, 0f, RenderLineCap.Flat, RenderLineJoin.Miter, viewport, options);
        canvas.DrawPath(path, fillPaint);
        canvas.DrawPath(path, strokePaint);
    }

    private static void DrawClipGroup(
        SKCanvas canvas,
        RenderClipGroup clipGroup,
        CadSceneViewport viewport,
        CadRenderOptions options)
    {
        using var clip = CreateLoopPath(clipGroup.Loops, clipGroup.FillMode);
        canvas.Save();
        canvas.ClipPath(clip, SKClipOperation.Intersect, antialias: true);

        var primitives = clipGroup.Primitives;
        for (var i = 0; i < primitives.Count; i++)
        {
            DrawPrimitive(canvas, primitives[i], viewport, options);
        }

        canvas.Restore();
    }

    private static void DrawText(SKCanvas canvas, RenderText text, CadSceneViewport viewport)
    {
        if (string.IsNullOrEmpty(text.Text))
        {
            return;
        }

        var anchor = viewport.WorldToScreen(text.Anchor);
        var fontSize = Math.Max(1f, text.FontSize * (float)viewport.Scale);
        var offsetX = text.Offset.X * (float)viewport.Scale;
        var offsetY = -text.Offset.Y * (float)viewport.Scale;

        using var paint = new SKPaint
        {
            IsAntialias = true,
            Color = ToSkColor(text.Color),
            Style = SKPaintStyle.Fill,
            TextSize = fontSize,
            Typeface = ResolveTypeface(text),
            FakeBoldText = text.IsBold,
            TextSkewX = text.IsItalic ? -0.25f : MathF.Tan(text.ObliqueAngle)
        };

        canvas.Save();
        canvas.Translate((float)anchor.X, (float)anchor.Y);
        canvas.RotateDegrees(-RadiansToDegrees(text.Rotation));
        canvas.Scale(Math.Max(0.01f, text.WidthFactor), 1f);

        var scaleX = text.MirrorX ? -1f : 1f;
        var scaleY = text.MirrorY ? -1f : 1f;
        if (scaleX < 0f || scaleY < 0f)
        {
            canvas.Scale(scaleX, scaleY);
        }

        canvas.DrawText(text.Text, offsetX, offsetY, paint);
        canvas.Restore();
    }

    private static void DrawSelection(
        SKCanvas canvas,
        CadSceneViewport viewport,
        CadRenderOptions options)
    {
        var primitive = options.SelectedPrimitive;
        if (primitive is null || primitive.Bounds.IsEmpty)
        {
            return;
        }

        var min = viewport.WorldToScreen(primitive.Bounds.Min);
        var max = viewport.WorldToScreen(primitive.Bounds.Max);
        var left = (float)Math.Min(min.X, max.X);
        var top = (float)Math.Min(min.Y, max.Y);
        var right = (float)Math.Max(min.X, max.X);
        var bottom = (float)Math.Max(min.Y, max.Y);

        using var paint = new SKPaint
        {
            IsAntialias = true,
            Color = ToSkColor(options.SelectionColor),
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 1.5f
        };
        canvas.DrawRect(new SKRect(left, top, right, bottom), paint);
    }

    private static void DrawGrid(SKCanvas canvas, CadSceneViewport viewport)
    {
        var step = CalculateGridStep(viewport);
        if (step <= 0f)
        {
            return;
        }

        var worldMin = viewport.ScreenToWorld(new CadPoint(0d, viewport.Size.Height));
        var worldMax = viewport.ScreenToWorld(new CadPoint(viewport.Size.Width, 0d));
        var startX = MathF.Floor(worldMin.X / step) * step;
        var endX = MathF.Ceiling(worldMax.X / step) * step;
        var startY = MathF.Floor(worldMin.Y / step) * step;
        var endY = MathF.Ceiling(worldMax.Y / step) * step;

        using var paint = new SKPaint
        {
            IsAntialias = false,
            Color = new SKColor(255, 255, 255, 28),
            StrokeWidth = 1f,
            Style = SKPaintStyle.Stroke
        };

        for (var x = startX; x <= endX; x += step)
        {
            var p0 = viewport.WorldToScreen(new Vector2(x, worldMin.Y));
            var p1 = viewport.WorldToScreen(new Vector2(x, worldMax.Y));
            canvas.DrawLine((float)p0.X, (float)p0.Y, (float)p1.X, (float)p1.Y, paint);
        }

        for (var y = startY; y <= endY; y += step)
        {
            var p0 = viewport.WorldToScreen(new Vector2(worldMin.X, y));
            var p1 = viewport.WorldToScreen(new Vector2(worldMax.X, y));
            canvas.DrawLine((float)p0.X, (float)p0.Y, (float)p1.X, (float)p1.Y, paint);
        }
    }

    private static void DrawAxes(SKCanvas canvas, CadSceneViewport viewport)
    {
        var worldMin = viewport.ScreenToWorld(new CadPoint(0d, viewport.Size.Height));
        var worldMax = viewport.ScreenToWorld(new CadPoint(viewport.Size.Width, 0d));

        using var xPaint = new SKPaint
        {
            IsAntialias = true,
            Color = new SKColor(224, 80, 80, 160),
            StrokeWidth = AxisStrokePixels,
            Style = SKPaintStyle.Stroke
        };
        using var yPaint = new SKPaint
        {
            IsAntialias = true,
            Color = new SKColor(80, 180, 96, 160),
            StrokeWidth = AxisStrokePixels,
            Style = SKPaintStyle.Stroke
        };

        if (worldMin.Y <= 0f && worldMax.Y >= 0f)
        {
            var p0 = viewport.WorldToScreen(new Vector2(worldMin.X, 0f));
            var p1 = viewport.WorldToScreen(new Vector2(worldMax.X, 0f));
            canvas.DrawLine((float)p0.X, (float)p0.Y, (float)p1.X, (float)p1.Y, xPaint);
        }

        if (worldMin.X <= 0f && worldMax.X >= 0f)
        {
            var p0 = viewport.WorldToScreen(new Vector2(0f, worldMin.Y));
            var p1 = viewport.WorldToScreen(new Vector2(0f, worldMax.Y));
            canvas.DrawLine((float)p0.X, (float)p0.Y, (float)p1.X, (float)p1.Y, yPaint);
        }
    }

    private static SKPath CreatePath(IReadOnlyList<Vector2> points, bool isClosed)
    {
        var path = new SKPath();
        path.MoveTo(points[0].X, points[0].Y);
        for (var i = 1; i < points.Count; i++)
        {
            path.LineTo(points[i].X, points[i].Y);
        }

        if (isClosed)
        {
            path.Close();
        }

        return path;
    }

    private static SKPath CreateLoopPath(IReadOnlyList<IReadOnlyList<Vector2>> loops, RenderLoopFillMode fillMode)
    {
        var path = new SKPath
        {
            FillType = fillMode == RenderLoopFillMode.NonZero ? SKPathFillType.Winding : SKPathFillType.EvenOdd
        };

        for (var loopIndex = 0; loopIndex < loops.Count; loopIndex++)
        {
            var loop = loops[loopIndex];
            if (loop.Count == 0)
            {
                continue;
            }

            path.MoveTo(loop[0].X, loop[0].Y);
            for (var i = 1; i < loop.Count; i++)
            {
                path.LineTo(loop[i].X, loop[i].Y);
            }

            path.Close();
        }

        return path;
    }

    private static SKPaint CreateStrokePaint(
        RenderColor color,
        float thickness,
        RenderLineCap lineCap,
        RenderLineJoin lineJoin,
        CadSceneViewport viewport,
        CadRenderOptions options,
        float[]? dashPattern = null,
        float dashPhase = 0f)
    {
        var minimumWorldStroke = viewport.ScreenToWorldDistance(options.MinimumStrokeThickness);
        var strokeWidth = Math.Max(thickness, minimumWorldStroke);
        var paint = new SKPaint
        {
            IsAntialias = true,
            Color = ToSkColor(color),
            Style = SKPaintStyle.Stroke,
            StrokeWidth = strokeWidth,
            StrokeCap = ToStrokeCap(lineCap),
            StrokeJoin = ToStrokeJoin(lineJoin)
        };

        if (dashPattern is { Length: > 0 })
        {
            paint.PathEffect = SKPathEffect.CreateDash(dashPattern, dashPhase);
        }

        return paint;
    }

    private static SKPaint CreateFillPaint(RenderColor color)
    {
        return new SKPaint
        {
            IsAntialias = true,
            Color = ToSkColor(color),
            Style = SKPaintStyle.Fill
        };
    }

    private static void ApplyWorldTransform(SKCanvas canvas, CadSceneViewport viewport)
    {
        canvas.Translate(
            (float)((viewport.Size.Width * 0.5d) + viewport.State.PanX),
            (float)((viewport.Size.Height * 0.5d) + viewport.State.PanY));
        canvas.Scale((float)viewport.Scale, (float)-viewport.Scale);
        var center = viewport.WorldCenter;
        canvas.Translate(-center.X, -center.Y);
    }

    private static float CalculateGridStep(CadSceneViewport viewport)
    {
        var targetWorld = viewport.ScreenToWorldDistance(GridTargetPixels);
        if (targetWorld <= 0f || float.IsNaN(targetWorld) || float.IsInfinity(targetWorld))
        {
            return 0f;
        }

        var exponent = MathF.Floor(MathF.Log10(targetWorld));
        var magnitude = MathF.Pow(10f, exponent);
        var normalized = targetWorld / magnitude;
        var factor = normalized <= 2f ? 2f : normalized <= 5f ? 5f : 10f;
        return factor * magnitude;
    }

    private static float NormalizeSweep(float startAngle, float endAngle)
    {
        var sweep = endAngle - startAngle;
        if (MathF.Abs(sweep) < 0.0001f)
        {
            return MathF.PI * 2f;
        }

        while (sweep < 0f)
        {
            sweep += MathF.PI * 2f;
        }

        return sweep;
    }

    private static SKColor ToSkColor(RenderColor color)
    {
        return new SKColor(color.R, color.G, color.B, color.A);
    }

    private static RenderColor Shade(RenderColor color, float shade)
    {
        var factor = Math.Clamp(shade, 0f, 1f);
        return new RenderColor(
            (byte)Math.Clamp(color.R * factor, 0f, 255f),
            (byte)Math.Clamp(color.G * factor, 0f, 255f),
            (byte)Math.Clamp(color.B * factor, 0f, 255f),
            color.A);
    }

    private static SKStrokeCap ToStrokeCap(RenderLineCap lineCap)
    {
        return lineCap switch
        {
            RenderLineCap.Round => SKStrokeCap.Round,
            RenderLineCap.Square => SKStrokeCap.Square,
            _ => SKStrokeCap.Butt
        };
    }

    private static SKStrokeJoin ToStrokeJoin(RenderLineJoin lineJoin)
    {
        return lineJoin switch
        {
            RenderLineJoin.Round => SKStrokeJoin.Round,
            RenderLineJoin.Bevel => SKStrokeJoin.Bevel,
            _ => SKStrokeJoin.Miter
        };
    }

    private static SKTypeface ResolveTypeface(RenderText text)
    {
        if (string.IsNullOrWhiteSpace(text.FontFamily))
        {
            return SKTypeface.Default;
        }

        var weight = text.IsBold ? SKFontStyleWeight.Bold : SKFontStyleWeight.Normal;
        var slant = text.IsItalic ? SKFontStyleSlant.Italic : SKFontStyleSlant.Upright;
        return SKTypeface.FromFamilyName(text.FontFamily, weight, SKFontStyleWidth.Normal, slant) ?? SKTypeface.Default;
    }

    private static float RadiansToDegrees(float radians)
    {
        return radians * 180f / MathF.PI;
    }
}
