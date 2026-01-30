using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;

namespace ACadInspector.Rendering;

/// <summary>
/// Resolves linetype shapes from SHX/SHP files with in-memory caching.
/// </summary>
public sealed class DefaultRenderShapeResolver : IRenderShapeResolver
{
    private readonly Dictionary<string, ShxShapeFile> _shapeFiles =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly Dictionary<string, ShxShapeRenderer> _renderers =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly Dictionary<ShapeCacheKey, RenderShapeGeometry> _geometryCache = new();

    /// <inheritdoc />
    public bool TryResolveShape(
        string? shapeFile,
        short shapeNumber,
        CadRenderSceneSettings settings,
        [NotNullWhen(true)] out RenderShapeGeometry? geometry)
    {
        geometry = null;
        if (shapeNumber <= 0 || string.IsNullOrWhiteSpace(shapeFile))
        {
            return false;
        }

        var resolvedPath = ResolveShapePath(shapeFile, settings);
        if (resolvedPath is null)
        {
            return false;
        }

        var key = new ShapeCacheKey(resolvedPath, shapeNumber);
        if (_geometryCache.TryGetValue(key, out geometry))
        {
            return true;
        }

        try
        {
            var renderer = GetRenderer(resolvedPath);
            if (!renderer.TryRenderShape(shapeNumber, out geometry))
            {
                return false;
            }

            _geometryCache[key] = geometry;
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

    private ShxShapeRenderer GetRenderer(string shapePath)
    {
        if (_renderers.TryGetValue(shapePath, out var renderer))
        {
            return renderer;
        }

        var shapeFile = LoadShapeFile(shapePath);
        renderer = new ShxShapeRenderer(shapeFile);
        _renderers[shapePath] = renderer;
        return renderer;
    }

    private ShxShapeFile LoadShapeFile(string path)
    {
        if (_shapeFiles.TryGetValue(path, out var file))
        {
            return file;
        }

        file = ShxShapeFile.Load(path);
        _shapeFiles[path] = file;
        return file;
    }

    private static string? ResolveShapePath(string shapeFile, CadRenderSceneSettings settings)
    {
        foreach (var candidate in EnumerateShapeCandidates(shapeFile, settings.SupportPaths))
        {
            if (File.Exists(candidate))
            {
                return Path.GetFullPath(candidate);
            }
        }

        return null;
    }

    private static IEnumerable<string> EnumerateShapeCandidates(
        string shapeFile,
        IReadOnlyList<string>? supportPaths)
    {
        var trimmed = shapeFile.Trim();
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

    private readonly struct ShapeCacheKey : IEquatable<ShapeCacheKey>
    {
        private static readonly StringComparer PathComparer = StringComparer.OrdinalIgnoreCase;

        public string Path { get; }
        public int ShapeNumber { get; }

        public ShapeCacheKey(string path, int shapeNumber)
        {
            Path = path;
            ShapeNumber = shapeNumber;
        }

        public bool Equals(ShapeCacheKey other)
        {
            return ShapeNumber == other.ShapeNumber && PathComparer.Equals(Path, other.Path);
        }

        public override bool Equals(object? obj)
        {
            return obj is ShapeCacheKey other && Equals(other);
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(PathComparer.GetHashCode(Path), ShapeNumber);
        }
    }
}
