using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;

namespace ProCad.Rendering;

/// <summary>
/// Resolves SHX/SHP fonts and exposes cached glyph geometry.
/// </summary>
public interface IShxFontResolver
{
    /// <summary>
    /// Attempts to load a SHX/SHP font for the provided filename and settings.
    /// </summary>
    bool TryGetFont(string? fontFile, CadRenderSceneSettings settings, [NotNullWhen(true)] out IShxFont? font);
}

/// <summary>
/// Provides access to glyph geometry from a SHX/SHP font.
/// </summary>
public interface IShxFont
{
    /// <summary>
    /// Attempts to retrieve cached glyph geometry for the provided code point.
    /// </summary>
    bool TryGetGlyph(int codePoint, [NotNullWhen(true)] out ShxGlyph glyph);
}

/// <summary>
/// Represents cached SHX glyph geometry and nominal bounds.
/// </summary>
public readonly struct ShxGlyph
{
    /// <summary>
    /// Gets the glyph vector geometry in font units.
    /// </summary>
    public RenderShapeGeometry Geometry { get; }

    /// <summary>
    /// Gets the nominal glyph width in font units.
    /// </summary>
    public float NominalWidth { get; }

    /// <summary>
    /// Gets the nominal glyph height in font units.
    /// </summary>
    public float NominalHeight { get; }

    public ShxGlyph(RenderShapeGeometry geometry)
    {
        Geometry = geometry;
        var size = geometry.Bounds.Size;
        NominalWidth = size.X;
        NominalHeight = size.Y;
    }
}

/// <summary>
/// Default SHX font resolver that caches loaded fonts and glyphs.
/// </summary>
public sealed class DefaultShxFontResolver : IShxFontResolver
{
    private readonly Dictionary<string, ShxFont> _fonts =
        new(StringComparer.OrdinalIgnoreCase);

    public bool TryGetFont(string? fontFile, CadRenderSceneSettings settings, [NotNullWhen(true)] out IShxFont? font)
    {
        font = null;
        if (string.IsNullOrWhiteSpace(fontFile))
        {
            return false;
        }

        var resolvedPath = ResolveFontPath(fontFile, settings.SupportPaths);
        if (resolvedPath is null)
        {
            return false;
        }

        if (_fonts.TryGetValue(resolvedPath, out var cached))
        {
            font = cached;
            return true;
        }

        try
        {
            var shapeFile = ShxShapeFile.Load(resolvedPath);
            var loaded = new ShxFont(shapeFile);
            _fonts[resolvedPath] = loaded;
            font = loaded;
            return true;
        }
        catch (IOException)
        {
            return false;
        }
        catch (InvalidDataException)
        {
            return false;
        }
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

    private static IEnumerable<string> EnumerateFontCandidates(
        string fontFile,
        IReadOnlyList<string>? supportPaths)
    {
        var trimmed = fontFile.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            yield break;
        }

        var hasExtension = Path.HasExtension(trimmed);
        var names = hasExtension
            ? new[] { trimmed }
            : new[] { trimmed, $"{trimmed}.shx", $"{trimmed}.shp" };

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
}

internal sealed class ShxFont : IShxFont
{
    private readonly ShxShapeRenderer _renderer;
    private readonly Dictionary<int, ShxGlyph> _glyphs = new();

    public ShxFont(ShxShapeFile shapeFile)
    {
        _renderer = new ShxShapeRenderer(shapeFile);
    }

    public bool TryGetGlyph(int codePoint, out ShxGlyph glyph)
    {
        if (_glyphs.TryGetValue(codePoint, out glyph))
        {
            return true;
        }

        if (!_renderer.TryRenderShape(codePoint, out var geometry))
        {
            glyph = default;
            return false;
        }

        glyph = new ShxGlyph(geometry);
        _glyphs[codePoint] = glyph;
        return true;
    }
}
