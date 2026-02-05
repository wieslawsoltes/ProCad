using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using ACadInspector.Rendering;
using ACadSharp.Entities;
using ACadSharp.Tables;
using HarfBuzzSharp;

namespace ACadInspector.Rendering;

/// <summary>
/// HarfBuzz-based text shaper for accurate Unicode metrics.
/// </summary>
public sealed class HarfBuzzRenderTextShaper : IRenderTextShaper, IDisposable
{
    private readonly IUnicodeTextService _unicode;
    private readonly SkiaRenderTextShaper _fallback;
    private readonly ConcurrentDictionary<string, FontHandle> _fonts = new(StringComparer.OrdinalIgnoreCase);

    public HarfBuzzRenderTextShaper(IUnicodeTextService unicode, SkiaRenderTextShaper fallback)
    {
        _unicode = unicode ?? throw new ArgumentNullException(nameof(unicode));
        _fallback = fallback ?? throw new ArgumentNullException(nameof(fallback));
    }

    public RenderTextLayout Shape(TextEntity text, CadRenderSceneSettings settings)
    {
        try
        {
            return ShapeText(text, settings);
        }
        catch
        {
            return _fallback.Shape(text, settings);
        }
    }

    public RenderTextLayout Shape(MText text, CadRenderSceneSettings settings)
    {
        try
        {
            return ShapeMText(text, settings);
        }
        catch
        {
            return _fallback.Shape(text, settings);
        }
    }

    private RenderTextLayout ShapeText(TextEntity text, CadRenderSceneSettings settings)
    {
        var value = text.Value ?? string.Empty;
        if (string.IsNullOrEmpty(value))
        {
            return new RenderTextLayout(string.Empty, 0f, 0f);
        }

        if (!TryResolveFont(text.Style, settings, out var font))
        {
            return _fallback.Shape(text, settings);
        }

        var height = RenderTextUtils.ResolveTextHeight(text);
        var width = MeasureLineWidth(value, font, height);
        var lineHeight = ResolveLineHeight(font, height);
        return new RenderTextLayout(value, width, lineHeight);
    }

    private RenderTextLayout ShapeMText(MText text, CadRenderSceneSettings settings)
    {
        var value = text.PlainText ?? string.Empty;
        if (string.IsNullOrEmpty(value))
        {
            return new RenderTextLayout(string.Empty, 0f, 0f);
        }

        if (!TryResolveFont(text.Style, settings, out var font))
        {
            return _fallback.Shape(text, settings);
        }

        var height = (float)text.Height;
        var lineHeight = height * MathF.Max(0.25f, (float)text.LineSpacing);
        var maxWidth = text.RectangleWidth > 0 ? (float)text.RectangleWidth : 0f;

        var lines = SplitLines(value);
        var measuredLines = maxWidth > 0 ? WrapLines(lines, font, height, maxWidth) : new List<string>(lines);

        var maxLineWidth = 0f;
        foreach (var line in measuredLines)
        {
            var lineWidth = MeasureLineWidth(line, font, height);
            if (lineWidth > maxLineWidth)
            {
                maxLineWidth = lineWidth;
            }
        }

        var totalHeight = lineHeight * Math.Max(measuredLines.Count, 1);
        return new RenderTextLayout(value, maxLineWidth, totalHeight);
    }

    private float MeasureLineWidth(string value, FontHandle font, float height)
    {
        if (string.IsNullOrEmpty(value))
        {
            return 0f;
        }

        using var buffer = new HarfBuzzSharp.Buffer();
        buffer.AddUtf16(value);
        buffer.GuessSegmentProperties();
        font.Font!.Shape(buffer, Array.Empty<Feature>());
        var positions = buffer.GlyphPositions;
        var scale = height / Math.Max(1, font.UnitsPerEm);
        var width = 0f;
        foreach (var position in positions)
        {
            width += position.XAdvance * scale;
        }

        return width;
    }

    private float ResolveLineHeight(FontHandle font, float fallbackHeight)
    {
        try
        {
            var extents = font.Font!.GetFontExtentsForDirection(Direction.LeftToRight);
            var scale = fallbackHeight / Math.Max(1, font.UnitsPerEm);
            var height = (extents.Ascender - extents.Descender + extents.LineGap) * scale;
            if (height > 0f && !float.IsNaN(height) && !float.IsInfinity(height))
            {
                return height;
            }
        }
        catch
        {
        }

        return fallbackHeight;
    }

    private List<string> WrapLines(string[] lines, FontHandle font, float height, float maxWidth)
    {
        var result = new List<string>();
        foreach (var line in lines)
        {
            if (string.IsNullOrEmpty(line))
            {
                result.Add(string.Empty);
                continue;
            }

            if (MeasureLineWidth(line, font, height) <= maxWidth)
            {
                result.Add(line);
                continue;
            }

            WrapLine(line, font, height, maxWidth, result);
        }

        return result;
    }

    private void WrapLine(string line, FontHandle font, float height, float maxWidth, List<string> output)
    {
        var breaks = _unicode.GetLineBreakOpportunities(line);
        var start = 0;

        while (start < line.Length)
        {
            var bestBreak = -1;
            foreach (var index in breaks)
            {
                if (index <= start)
                {
                    continue;
                }

                var segment = line.Substring(start, index - start);
                if (MeasureLineWidth(segment, font, height) <= maxWidth)
                {
                    bestBreak = index;
                    continue;
                }

                break;
            }

            if (bestBreak <= start)
            {
                bestBreak = Math.Min(start + 1, line.Length);
            }

            var slice = line.Substring(start, bestBreak - start).TrimEnd();
            output.Add(slice);

            start = bestBreak;
            while (start < line.Length && char.IsWhiteSpace(line[start]))
            {
                start++;
            }
        }
    }

    private bool TryResolveFont(TextStyle style, CadRenderSceneSettings settings, out FontHandle font)
    {
        font = FontHandle.Invalid;
        var fontFile = style?.Filename;
        if (string.IsNullOrWhiteSpace(fontFile))
        {
            return false;
        }

        if (HasShxExtension(fontFile))
        {
            return false;
        }

        var resolved = ResolveFontPath(fontFile, settings.SupportPaths);
        if (resolved is null)
        {
            return false;
        }

        font = _fonts.GetOrAdd(resolved, LoadFont);
        return font.IsValid;
    }

    private static bool HasShxExtension(string fontFile)
    {
        var ext = Path.GetExtension(fontFile);
        return ext.Equals(".shx", StringComparison.OrdinalIgnoreCase) ||
               ext.Equals(".shp", StringComparison.OrdinalIgnoreCase);
    }

    private static string? ResolveFontPath(string fontFile, IReadOnlyList<string>? supportPaths)
    {
        foreach (var candidate in EnumerateFontCandidates(fontFile, supportPaths))
        {
            if (File.Exists(candidate))
            {
                return Path.GetFullPath(candidate);
            }
        }

        return null;
    }

    private static IEnumerable<string> EnumerateFontCandidates(string fontFile, IReadOnlyList<string>? supportPaths)
    {
        var trimmed = fontFile.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            yield break;
        }

        var hasExtension = Path.HasExtension(trimmed);
        var names = hasExtension
            ? new[] { trimmed }
            : new[] { trimmed, $"{trimmed}.ttf", $"{trimmed}.otf", $"{trimmed}.ttc" };

        if (Path.IsPathRooted(trimmed))
        {
            foreach (var name in names)
            {
                yield return name;
            }

            yield break;
        }

        if (supportPaths is not null)
        {
            foreach (var path in supportPaths)
            {
                if (string.IsNullOrWhiteSpace(path))
                {
                    continue;
                }

                foreach (var name in names)
                {
                    yield return Path.Combine(path, name);
                }
            }
        }

        foreach (var name in names)
        {
            yield return Path.Combine(Directory.GetCurrentDirectory(), name);
        }
    }

    private static string[] SplitLines(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return Array.Empty<string>();
        }

        return value.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
    }

    private static FontHandle LoadFont(string path)
    {
        try
        {
            var blob = HarfBuzzSharp.Blob.FromFile(path);
            var face = new HarfBuzzSharp.Face(blob, 0);
            var font = new HarfBuzzSharp.Font(face);
            var unitsPerEm = Math.Max(1, (int)face.UnitsPerEm);
            font.SetScale(unitsPerEm, unitsPerEm);
            return new FontHandle(blob, face, font, unitsPerEm);
        }
        catch
        {
            return FontHandle.Invalid;
        }
    }

    public void Dispose()
    {
        foreach (var entry in _fonts.Values)
        {
            entry.Dispose();
        }

        _fonts.Clear();
    }

    private sealed class FontHandle : IDisposable
    {
        public static FontHandle Invalid { get; } = new(null, null, null, 1);

        public HarfBuzzSharp.Blob? Blob { get; }
        public HarfBuzzSharp.Face? Face { get; }
        public HarfBuzzSharp.Font? Font { get; }
        public int UnitsPerEm { get; }

        public bool IsValid => Font is not null && Face is not null;

        public FontHandle(HarfBuzzSharp.Blob? blob, HarfBuzzSharp.Face? face, HarfBuzzSharp.Font? font, int unitsPerEm)
        {
            Blob = blob;
            Face = face;
            Font = font;
            UnitsPerEm = unitsPerEm;
        }

        public void Dispose()
        {
            Font?.Dispose();
            Face?.Dispose();
            Blob?.Dispose();
        }
    }
}
