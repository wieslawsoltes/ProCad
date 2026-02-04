using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using ACadSharp.Tables;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using SkiaSharp;

namespace ACadInspector.Services;

public sealed class CadStylePreviewService
{
    private const string TextSample = "AaBb";
    private readonly ConcurrentDictionary<StylePreviewKey, Bitmap?> _cache;

    public CadStylePreviewService()
    {
        _cache = new ConcurrentDictionary<StylePreviewKey, Bitmap?>(new StylePreviewKeyComparer());
    }

    public Task<Bitmap?> GetTextStylePreviewAsync(TextStyle style, int size, CancellationToken cancellationToken)
    {
        if (style is null)
        {
            throw new ArgumentNullException(nameof(style));
        }

        return GetPreviewAsync(
            new StylePreviewKey(style, PreviewKind.TextStyle, size),
            () => BuildTextStylePreviewData(style, size),
            cancellationToken);
    }

    public Task<Bitmap?> GetLineTypePreviewAsync(LineType lineType, int size, CancellationToken cancellationToken)
    {
        if (lineType is null)
        {
            throw new ArgumentNullException(nameof(lineType));
        }

        return GetPreviewAsync(
            new StylePreviewKey(lineType, PreviewKind.LineType, size),
            () => BuildLineTypePreviewData(lineType, size),
            cancellationToken);
    }

    public Task<Bitmap?> GetDimensionStylePreviewAsync(DimensionStyle style, int size, CancellationToken cancellationToken)
    {
        if (style is null)
        {
            throw new ArgumentNullException(nameof(style));
        }

        return GetPreviewAsync(
            new StylePreviewKey(style, PreviewKind.DimensionStyle, size),
            () => BuildDimensionStylePreviewData(style, size),
            cancellationToken);
    }

    private async Task<Bitmap?> GetPreviewAsync(
        StylePreviewKey key,
        Func<byte[]?> previewFactory,
        CancellationToken cancellationToken)
    {
        if (_cache.TryGetValue(key, out var cached))
        {
            return cached;
        }

        byte[]? data;
        try
        {
            data = await Task.Run(previewFactory, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return null;
        }
        catch
        {
            return null;
        }

        if (data is null || cancellationToken.IsCancellationRequested)
        {
            return null;
        }

        Bitmap? preview;
        try
        {
            preview = await Dispatcher.UIThread.InvokeAsync(
                () => new Bitmap(new MemoryStream(data)),
                DispatcherPriority.Background,
                cancellationToken);
        }
        catch (OperationCanceledException)
        {
            return null;
        }

        if (preview is not null && !cancellationToken.IsCancellationRequested)
        {
            _cache.TryAdd(key, preview);
        }

        return preview;
    }

    private static byte[]? BuildTextStylePreviewData(TextStyle style, int size)
    {
        var surface = CreateSurface(size);
        if (surface is null)
        {
            return null;
        }

        using (surface)
        {
            var canvas = surface.Canvas;
            canvas.Clear(SKColors.Transparent);

            using var paint = new SKPaint
            {
                IsAntialias = true,
                Color = SKColors.Black,
                TextAlign = SKTextAlign.Center
            };

            paint.Typeface = ResolveTypeface(style);
            paint.TextScaleX = (float)Math.Clamp(style.Width <= 0 ? 1.0 : style.Width, 0.1, 5.0);

            var targetWidth = size * 0.9f;
            var targetHeight = size * 0.55f;
            paint.TextSize = FitTextSize(TextSample, paint, targetWidth, targetHeight);

            var bounds = new SKRect();
            paint.MeasureText(TextSample, ref bounds);
            var x = size * 0.5f;
            var y = size * 0.5f - bounds.MidY;
            canvas.DrawText(TextSample, x, y, paint);

            return EncodeSurface(surface);
        }
    }

    private static byte[]? BuildLineTypePreviewData(LineType lineType, int size)
    {
        var surface = CreateSurface(size);
        if (surface is null)
        {
            return null;
        }

        using (surface)
        {
            var canvas = surface.Canvas;
            canvas.Clear(SKColors.Transparent);

            var padding = 6f;
            var y = size * 0.5f;
            var length = MathF.Max(1f, size - padding * 2f);
            var strokeWidth = MathF.Max(1.5f, size * 0.05f);

            using var stroke = new SKPaint
            {
                IsAntialias = true,
                Color = SKColors.Black,
                StrokeWidth = strokeWidth,
                Style = SKPaintStyle.Stroke,
                StrokeCap = SKStrokeCap.Butt
            };

            using var dotPaint = new SKPaint
            {
                IsAntialias = true,
                Color = SKColors.Black,
                Style = SKPaintStyle.Fill
            };

            var patternLength = (float)lineType.PatternLength;
            if (!lineType.IsComplex || patternLength <= 0.0001f)
            {
                canvas.DrawLine(padding, y, padding + length, y, stroke);
                return EncodeSurface(surface);
            }

            var scale = length / patternLength;
            var x = padding;

            foreach (var segment in lineType.Segments)
            {
                if (x > padding + length)
                {
                    break;
                }

                if (segment.Length > 0)
                {
                    var segLength = (float)segment.Length * scale;
                    var x2 = MathF.Min(padding + length, x + segLength);
                    canvas.DrawLine(x, y, x2, y, stroke);
                    x = x2;
                }
                else if (segment.Length < 0)
                {
                    x += (float)(-segment.Length) * scale;
                }
                else
                {
                    canvas.DrawCircle(x, y, strokeWidth * 0.75f, dotPaint);
                }
            }

            return EncodeSurface(surface);
        }
    }

    private static byte[]? BuildDimensionStylePreviewData(DimensionStyle style, int size)
    {
        var surface = CreateSurface(size);
        if (surface is null)
        {
            return null;
        }

        using (surface)
        {
            var canvas = surface.Canvas;
            canvas.Clear(SKColors.Transparent);

            var padding = 6f;
            var width = MathF.Max(1f, size - padding * 2f);
            var lineY = size * 0.6f;
            var arrowScale = size / 20f;
            var arrowSize = Math.Clamp((float)(style.ArrowSize <= 0 ? 2.5 : style.ArrowSize) * arrowScale, 2f, size * 0.25f);
            var x0 = padding + arrowSize;
            var x1 = padding + width - arrowSize;

            using var stroke = new SKPaint
            {
                IsAntialias = true,
                Color = SKColors.Black,
                StrokeWidth = MathF.Max(1.5f, size * 0.04f),
                Style = SKPaintStyle.Stroke,
                StrokeCap = SKStrokeCap.Butt
            };

            using var fill = new SKPaint
            {
                IsAntialias = true,
                Color = SKColors.Black,
                Style = SKPaintStyle.Fill
            };

            canvas.DrawLine(x0, lineY, x1, lineY, stroke);
            DrawArrow(canvas, fill, stroke, new SKPoint(x0, lineY), new SKPoint(-1, 0), arrowSize);
            DrawArrow(canvas, fill, stroke, new SKPoint(x1, lineY), new SKPoint(1, 0), arrowSize);

            var textHeight = Math.Clamp((float)(style.TextHeight <= 0 ? 2.5 : style.TextHeight) * arrowScale, 6f, size * 0.4f);
            using var textPaint = new SKPaint
            {
                IsAntialias = true,
                Color = SKColors.Black,
                TextAlign = SKTextAlign.Center,
                TextSize = textHeight
            };

            var text = "100";
            var bounds = new SKRect();
            textPaint.MeasureText(text, ref bounds);
            var textY = lineY - arrowSize - 2f;
            canvas.DrawText(text, size * 0.5f, textY, textPaint);

            return EncodeSurface(surface);
        }
    }

    private static SKSurface? CreateSurface(int size)
    {
        var pixelSize = Math.Max(1, size);
        var info = new SKImageInfo(pixelSize, pixelSize, SKColorType.Bgra8888, SKAlphaType.Premul);
        return SKSurface.Create(info);
    }

    private static SKTypeface ResolveTypeface(TextStyle style)
    {
        if (!string.IsNullOrWhiteSpace(style.Filename))
        {
            try
            {
                if (File.Exists(style.Filename))
                {
                    var typeface = SKTypeface.FromFile(style.Filename);
                    if (typeface is not null)
                    {
                        return typeface;
                    }
                }
            }
            catch
            {
            }
        }

        return SKTypeface.Default;
    }

    private static float FitTextSize(string text, SKPaint paint, float targetWidth, float targetHeight)
    {
        var size = MathF.Max(6f, targetHeight);
        paint.TextSize = size;

        var bounds = new SKRect();
        paint.MeasureText(text, ref bounds);
        if (bounds.Width <= 0 || bounds.Height <= 0)
        {
            return size;
        }

        var scale = MathF.Min(targetWidth / bounds.Width, targetHeight / bounds.Height);
        size = MathF.Max(6f, size * scale);
        return size;
    }

    private static void DrawArrow(SKCanvas canvas, SKPaint fill, SKPaint stroke, SKPoint tip, SKPoint direction, float size)
    {
        var ortho = new SKPoint(-direction.Y, direction.X);
        var basePoint = new SKPoint(tip.X + direction.X * size, tip.Y + direction.Y * size);
        var left = new SKPoint(basePoint.X + ortho.X * size * 0.45f, basePoint.Y + ortho.Y * size * 0.45f);
        var right = new SKPoint(basePoint.X - ortho.X * size * 0.45f, basePoint.Y - ortho.Y * size * 0.45f);

        using var path = new SKPath();
        path.MoveTo(tip);
        path.LineTo(left);
        path.LineTo(right);
        path.Close();
        canvas.DrawPath(path, fill);
        canvas.DrawPath(path, stroke);
    }

    private static byte[]? EncodeSurface(SKSurface surface)
    {
        using var image = surface.Snapshot();
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data?.ToArray();
    }

    private enum PreviewKind
    {
        TextStyle,
        LineType,
        DimensionStyle
    }

    private readonly record struct StylePreviewKey(object Style, PreviewKind Kind, int Size);

    private sealed class StylePreviewKeyComparer : IEqualityComparer<StylePreviewKey>
    {
        public bool Equals(StylePreviewKey x, StylePreviewKey y)
        {
            return ReferenceEquals(x.Style, y.Style) &&
                   x.Kind == y.Kind &&
                   x.Size == y.Size;
        }

        public int GetHashCode(StylePreviewKey obj)
        {
            return HashCode.Combine(RuntimeHelpers.GetHashCode(obj.Style), (int)obj.Kind, obj.Size);
        }
    }
}
