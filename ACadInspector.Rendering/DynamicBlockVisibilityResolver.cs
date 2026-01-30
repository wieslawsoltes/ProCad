using System.Collections.Generic;
using ACadSharp.Objects;
using ACadSharp.Objects.Evaluations;

namespace ACadInspector.Rendering;

internal static class DynamicBlockVisibilityResolver
{
    public static string? ResolveStateName(
        DynamicBlockPropertySet? properties,
        IReadOnlyDictionary<string, BlockVisibilityParameter.State> states)
    {
        if (properties is null || states.Count == 0)
        {
            return null;
        }

        foreach (var value in properties.Strings)
        {
            if (states.ContainsKey(value))
            {
                return value;
            }
        }

        return null;
    }

    public static string? ResolveStateName(
        CadDictionary? dictionary,
        IReadOnlyDictionary<string, BlockVisibilityParameter.State> states)
    {
        if (dictionary is null || states.Count == 0)
        {
            return null;
        }

        if (!dictionary.TryGetEntry<CadDictionary>("AcDbBlockRepresentation", out var representation))
        {
            return null;
        }

        if (!representation.TryGetEntry<CadDictionary>("AppDataCache", out var appDataCache))
        {
            return null;
        }

        if (!appDataCache.TryGetEntry<CadDictionary>("ACAD_ENHANCEDBLOCKDATA", out var enhancedBlockData))
        {
            return null;
        }

        foreach (var entry in enhancedBlockData)
        {
            if (entry is not XRecord record)
            {
                continue;
            }

            var name = TryGetVisibilityStateName(record, states);
            if (!string.IsNullOrWhiteSpace(name))
            {
                return name;
            }
        }

        return null;
    }

    private static string? TryGetVisibilityStateName(
        XRecord record,
        IReadOnlyDictionary<string, BlockVisibilityParameter.State> states)
    {
        foreach (var entry in record.Entries)
        {
            if (entry.Code != 1 || entry.Value is not string name)
            {
                continue;
            }

            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            if (states.ContainsKey(name))
            {
                return name;
            }
        }

        return null;
    }
}
