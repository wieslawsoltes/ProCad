using System;
using System.Collections.Generic;
using ACadSharp.Objects;

namespace ProCad.Rendering;

public sealed class DynamicBlockPropertySet : IDynamicBlockPropertyProvider
{
    private readonly List<DynamicBlockPropertyRecord> _records;
    private readonly HashSet<string> _strings;

    internal IReadOnlyList<DynamicBlockPropertyRecord> Records => _records;
    public IReadOnlyCollection<string> Strings => _strings;

    private DynamicBlockPropertySet(List<DynamicBlockPropertyRecord> records, HashSet<string> strings)
    {
        _records = records;
        _strings = strings;
    }

    public static DynamicBlockPropertySet? Create(CadDictionary? enhancedBlockData)
    {
        if (enhancedBlockData is null)
        {
            return null;
        }

        var records = new List<DynamicBlockPropertyRecord>();
        var strings = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var entry in enhancedBlockData)
        {
            if (entry is not XRecord record)
            {
                continue;
            }

            var parsed = DynamicBlockPropertyRecord.Create(record);
            records.Add(parsed);
            foreach (var value in parsed.Strings)
            {
                strings.Add(value);
            }
        }

        return records.Count == 0 ? null : new DynamicBlockPropertySet(records, strings);
    }

    public bool ContainsString(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        return _strings.Contains(value);
    }

    public bool TryGetNumericValue(string? name, string? fallbackName, int? id, int? value1071, out double value)
    {
        value = 0;
        foreach (var record in _records)
        {
            if (record.MatchesName(name) || record.MatchesName(fallbackName) ||
                record.MatchesId(id) || record.MatchesId(value1071))
            {
                if (record.TryGetFirstNumber(out value))
                {
                    return true;
                }
            }
        }

        return false;
    }
}

internal sealed class DynamicBlockPropertyRecord
{
    private readonly List<string> _strings;
    private readonly List<double> _numbers;
    private readonly List<int> _integers;

    public string Name { get; }
    public IReadOnlyList<string> Strings => _strings;

    private DynamicBlockPropertyRecord(
        string name,
        List<string> strings,
        List<double> numbers,
        List<int> integers)
    {
        Name = name;
        _strings = strings;
        _numbers = numbers;
        _integers = integers;
    }

    public static DynamicBlockPropertyRecord Create(XRecord record)
    {
        var name = record.Name ?? string.Empty;
        var strings = new List<string>();
        var numbers = new List<double>();
        var integers = new List<int>();

        foreach (var entry in record.Entries)
        {
            switch (entry.Value)
            {
                case string text:
                    if (!string.IsNullOrWhiteSpace(text))
                    {
                        strings.Add(text);
                    }
                    break;
                case double value when IsNumericCode(entry.Code):
                    numbers.Add(value);
                    break;
                case float value when IsNumericCode(entry.Code):
                    numbers.Add(value);
                    break;
                case short value when IsIntegerCode(entry.Code):
                    integers.Add(value);
                    break;
                case int value when IsIntegerCode(entry.Code):
                    integers.Add(value);
                    break;
            }
        }

        return new DynamicBlockPropertyRecord(name, strings, numbers, integers);
    }

    public bool MatchesName(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        for (var i = 0; i < _strings.Count; i++)
        {
            if (string.Equals(_strings[i], value, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        if (!string.IsNullOrWhiteSpace(Name) &&
            string.Equals(Name, value, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return false;
    }

    public bool MatchesId(int? value)
    {
        if (!value.HasValue)
        {
            return false;
        }

        for (var i = 0; i < _integers.Count; i++)
        {
            if (_integers[i] == value.Value)
            {
                return true;
            }
        }

        return false;
    }

    public bool TryGetFirstNumber(out double value)
    {
        if (_numbers.Count > 0)
        {
            value = _numbers[0];
            return true;
        }

        value = 0;
        return false;
    }

    private static bool IsNumericCode(int code)
    {
        if (code is >= 40 and <= 59)
        {
            return true;
        }

        if (code is >= 140 and <= 149)
        {
            return true;
        }

        return code is >= 1040 and <= 1049;
    }

    private static bool IsIntegerCode(int code)
    {
        if (code is >= 70 and <= 79)
        {
            return true;
        }

        if (code is >= 90 and <= 99)
        {
            return true;
        }

        return code is >= 1070 and <= 1071;
    }
}
