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

namespace ACadInspector.Services;

public sealed class CadStylePreviewService
{
    private readonly IStylePreviewRenderer _renderer;
    private readonly ConcurrentDictionary<StylePreviewKey, Bitmap?> _cache;

    public CadStylePreviewService(IStylePreviewRenderer renderer)
    {
        _renderer = renderer;
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
            () => _renderer.RenderTextStyle(style, size),
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
            () => _renderer.RenderLineType(lineType, size),
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
            () => _renderer.RenderDimensionStyle(style, size),
            cancellationToken);
    }

    public void InvalidateTextStyle(TextStyle style)
    {
        ArgumentNullException.ThrowIfNull(style);
        Invalidate(style, PreviewKind.TextStyle);
    }

    public void InvalidateLineType(LineType lineType)
    {
        ArgumentNullException.ThrowIfNull(lineType);
        Invalidate(lineType, PreviewKind.LineType);
    }

    public void InvalidateDimensionStyle(DimensionStyle style)
    {
        ArgumentNullException.ThrowIfNull(style);
        Invalidate(style, PreviewKind.DimensionStyle);
    }

    public void InvalidateAll()
    {
        _cache.Clear();
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

    private void Invalidate(object style, PreviewKind kind)
    {
        foreach (var key in _cache.Keys)
        {
            if (!ReferenceEquals(key.Style, style) || key.Kind != kind)
            {
                continue;
            }

            if (_cache.TryRemove(key, out var removed))
            {
                removed?.Dispose();
            }
        }
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
