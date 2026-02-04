using System;
using System.Collections.Generic;
using ACadSharp.Entities;
using ACadSharp.Tables;

namespace ACadInspector.Rendering;

public interface IDynamicBlockOverrideProvider
{
    DynamicBlockOverrideSet? GetBlockOverrides(BlockRecord block);
    DynamicBlockOverrideSet? GetInsertOverrides(Insert insert, BlockRecord block);
}

public sealed class DynamicBlockOverrideSet : IDynamicBlockPropertyProvider
{
    private readonly Dictionary<string, double> _numericByName =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<int, double> _numericById = new();
    private readonly HashSet<string> _strings =
        new(StringComparer.OrdinalIgnoreCase);

    public string? VisibilityStateName { get; set; }

    public IReadOnlyDictionary<string, double> NumericByName => _numericByName;
    public IReadOnlyDictionary<int, double> NumericById => _numericById;
    public IReadOnlyCollection<string> Strings => _strings;

    public bool ContainsString(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        return _strings.Contains(value);
    }

    public bool TryGetNumericValue(
        string? name,
        string? fallbackName,
        int? id,
        int? value1071,
        out double value)
    {
        value = 0;

        if (TryGetNumericByName(name, out value) ||
            TryGetNumericByName(fallbackName, out value))
        {
            return true;
        }

        if (TryGetNumericById(id, out value) ||
            TryGetNumericById(value1071, out value))
        {
            return true;
        }

        return false;
    }

    public void SetNumericOverride(string? name, string? fallbackName, int? id, int? value1071, double value)
    {
        if (!string.IsNullOrWhiteSpace(name))
        {
            _numericByName[name] = value;
        }

        if (!string.IsNullOrWhiteSpace(fallbackName))
        {
            _numericByName[fallbackName] = value;
        }

        if (id.HasValue)
        {
            _numericById[id.Value] = value;
        }

        if (value1071.HasValue)
        {
            _numericById[value1071.Value] = value;
        }
    }

    public void ClearNumericOverride(string? name, string? fallbackName, int? id, int? value1071)
    {
        if (!string.IsNullOrWhiteSpace(name))
        {
            _numericByName.Remove(name);
        }

        if (!string.IsNullOrWhiteSpace(fallbackName))
        {
            _numericByName.Remove(fallbackName);
        }

        if (id.HasValue)
        {
            _numericById.Remove(id.Value);
        }

        if (value1071.HasValue)
        {
            _numericById.Remove(value1071.Value);
        }
    }

    public bool TryGetNumericOverride(string? name, string? fallbackName, int? id, int? value1071, out double value)
    {
        value = 0;
        return TryGetNumericByName(name, out value) ||
               TryGetNumericByName(fallbackName, out value) ||
               TryGetNumericById(id, out value) ||
               TryGetNumericById(value1071, out value);
    }

    public void SetFlipOverride(string? flippedStateName, string? baseStateName, bool isFlipped)
    {
        if (!string.IsNullOrWhiteSpace(flippedStateName))
        {
            _strings.Remove(flippedStateName);
        }

        if (!string.IsNullOrWhiteSpace(baseStateName))
        {
            _strings.Remove(baseStateName);
        }

        if (isFlipped)
        {
            if (!string.IsNullOrWhiteSpace(flippedStateName))
            {
                _strings.Add(flippedStateName);
            }
        }
        else
        {
            if (!string.IsNullOrWhiteSpace(baseStateName))
            {
                _strings.Add(baseStateName);
            }
        }
    }

    public void ClearFlipOverride(string? flippedStateName, string? baseStateName)
    {
        if (!string.IsNullOrWhiteSpace(flippedStateName))
        {
            _strings.Remove(flippedStateName);
        }

        if (!string.IsNullOrWhiteSpace(baseStateName))
        {
            _strings.Remove(baseStateName);
        }
    }

    public bool TryGetFlipOverride(string? flippedStateName, string? baseStateName, out bool isFlipped)
    {
        isFlipped = false;
        if (!string.IsNullOrWhiteSpace(flippedStateName) && _strings.Contains(flippedStateName))
        {
            isFlipped = true;
            return true;
        }

        if (!string.IsNullOrWhiteSpace(baseStateName) && _strings.Contains(baseStateName))
        {
            isFlipped = false;
            return true;
        }

        return false;
    }

    private bool TryGetNumericByName(string? name, out double value)
    {
        value = 0;
        return !string.IsNullOrWhiteSpace(name) && _numericByName.TryGetValue(name, out value);
    }

    private bool TryGetNumericById(int? id, out double value)
    {
        value = 0;
        return id.HasValue && _numericById.TryGetValue(id.Value, out value);
    }
}

internal interface IDynamicBlockPropertyProvider
{
    bool ContainsString(string? value);
    bool TryGetNumericValue(string? name, string? fallbackName, int? id, int? value1071, out double value);
}

internal sealed class DynamicBlockPropertyProvider : IDynamicBlockPropertyProvider
{
    private readonly IDynamicBlockPropertyProvider? _baseProvider;
    private readonly IDynamicBlockPropertyProvider? _overrideProvider;

    private DynamicBlockPropertyProvider(
        IDynamicBlockPropertyProvider? baseProvider,
        IDynamicBlockPropertyProvider? overrideProvider)
    {
        _baseProvider = baseProvider;
        _overrideProvider = overrideProvider;
    }

    public static IDynamicBlockPropertyProvider? Create(
        IDynamicBlockPropertyProvider? baseProvider,
        IDynamicBlockPropertyProvider? overrideProvider)
    {
        if (overrideProvider is null)
        {
            return baseProvider;
        }

        if (baseProvider is null)
        {
            return overrideProvider;
        }

        return new DynamicBlockPropertyProvider(baseProvider, overrideProvider);
    }

    public bool ContainsString(string? value)
    {
        return (_overrideProvider?.ContainsString(value) ?? false) ||
               (_baseProvider?.ContainsString(value) ?? false);
    }

    public bool TryGetNumericValue(string? name, string? fallbackName, int? id, int? value1071, out double value)
    {
        value = 0;
        if (_overrideProvider is not null &&
            _overrideProvider.TryGetNumericValue(name, fallbackName, id, value1071, out value))
        {
            return true;
        }

        if (_baseProvider is not null &&
            _baseProvider.TryGetNumericValue(name, fallbackName, id, value1071, out value))
        {
            return true;
        }

        return false;
    }
}
