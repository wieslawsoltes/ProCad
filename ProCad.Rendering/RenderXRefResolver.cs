using System;
using System.Collections.Generic;
using System.IO;
using ProCad.Core;
using ACadSharp;
using ACadSharp.Blocks;
using ACadSharp.Tables;

namespace ProCad.Rendering;

public interface IRenderXRefResolver
{
    bool TryResolve(BlockRecord block, CadRenderSceneSettings settings, out RenderXRefInfo info);
}

public readonly struct RenderXRefInfo
{
    public CadDocument Document { get; }
    public string Path { get; }

    public RenderXRefInfo(CadDocument document, string path)
    {
        Document = document;
        Path = path;
    }
}

public sealed class NullRenderXRefResolver : IRenderXRefResolver
{
    public static NullRenderXRefResolver Instance { get; } = new();

    private NullRenderXRefResolver()
    {
    }

    public bool TryResolve(BlockRecord block, CadRenderSceneSettings settings, out RenderXRefInfo info)
    {
        info = default;
        return false;
    }
}

public sealed class DefaultRenderXRefResolver : IRenderXRefResolver
{
    private readonly ICadDocumentService _documentService;
    private readonly Dictionary<string, CadDocument> _cache = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _resolving = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _sync = new();

    public DefaultRenderXRefResolver(ICadDocumentService documentService)
    {
        _documentService = documentService ?? throw new ArgumentNullException(nameof(documentService));
    }

    public bool TryResolve(BlockRecord block, CadRenderSceneSettings settings, out RenderXRefInfo info)
    {
        info = default;
        if (block is null)
        {
            return false;
        }

        if (!IsXRef(block) || block.IsUnloaded)
        {
            return false;
        }

        var path = block.BlockEntity?.XRefPath;
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        var resolvedPath = ResolvePath(path, settings.SupportPaths);
        if (resolvedPath is null)
        {
            return false;
        }

        lock (_sync)
        {
            if (_cache.TryGetValue(resolvedPath, out var cached))
            {
                info = new RenderXRefInfo(cached, resolvedPath);
                return true;
            }

            if (_resolving.Contains(resolvedPath))
            {
                return false;
            }

            _resolving.Add(resolvedPath);
        }

        CadDocument? loaded = null;
        try
        {
            loaded = _documentService.Load(resolvedPath, new CadReadOptions(ReadSummaryInfo: false));
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
        catch (InvalidDataException)
        {
        }
        catch (Exception)
        {
        }
        finally
        {
            lock (_sync)
            {
                _resolving.Remove(resolvedPath);
            }
        }

        if (loaded is null)
        {
            return false;
        }

        lock (_sync)
        {
            _cache[resolvedPath] = loaded;
        }

        info = new RenderXRefInfo(loaded, resolvedPath);
        return true;
    }

    private static bool IsXRef(BlockRecord block)
    {
        var flags = block.Flags;
        return flags.HasFlag(BlockTypeFlags.XRef) || flags.HasFlag(BlockTypeFlags.XRefOverlay);
    }

    private static string? ResolvePath(string xrefPath, IReadOnlyList<string>? supportPaths)
    {
        foreach (var candidate in EnumerateCandidates(xrefPath, supportPaths))
        {
            if (File.Exists(candidate))
            {
                return Path.GetFullPath(candidate);
            }
        }

        return null;
    }

    private static IEnumerable<string> EnumerateCandidates(string xrefPath, IReadOnlyList<string>? supportPaths)
    {
        var trimmed = xrefPath.Trim().Trim('"');
        if (string.IsNullOrEmpty(trimmed))
        {
            yield break;
        }

        var hasExtension = Path.HasExtension(trimmed);
        var names = hasExtension
            ? new[] { trimmed }
            : new[] { trimmed, $"{trimmed}.dwg", $"{trimmed}.dxf" };

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
