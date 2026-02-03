using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace ACadInspector.Rendering;

public readonly struct RenderPlotStyle
{
    public RenderColor? Color { get; }
    public float? LineWeightMm { get; }

    public RenderPlotStyle(RenderColor? color, float? lineWeightMm)
    {
        Color = color;
        LineWeightMm = lineWeightMm;
    }
}

public sealed class RenderPlotStyleTable
{
    private readonly Dictionary<int, RenderPlotStyle> _colorStyles;
    private readonly Dictionary<string, RenderPlotStyle> _namedStyles;

    public bool IsNamed { get; }

    private RenderPlotStyleTable(
        bool isNamed,
        Dictionary<int, RenderPlotStyle> colorStyles,
        Dictionary<string, RenderPlotStyle> namedStyles)
    {
        IsNamed = isNamed;
        _colorStyles = colorStyles;
        _namedStyles = namedStyles;
    }

    public bool TryGetByColorIndex(int index, out RenderPlotStyle style)
    {
        return _colorStyles.TryGetValue(index, out style);
    }

    public bool TryGetByName(string name, out RenderPlotStyle style)
    {
        return _namedStyles.TryGetValue(name, out style);
    }

    public static RenderPlotStyleTable? TryLoad(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        if (!File.Exists(path))
        {
            return null;
        }

        string content;
        try
        {
            content = File.ReadAllText(path);
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(content))
        {
            return null;
        }

        var isNamed = Path.GetExtension(path).Equals(".stb", StringComparison.OrdinalIgnoreCase);
        var colorStyles = new Dictionary<int, RenderPlotStyle>();
        var namedStyles = new Dictionary<string, RenderPlotStyle>(StringComparer.OrdinalIgnoreCase);

        RenderPlotStyleBuilder? current = null;
        foreach (var rawLine in content.Split('\n'))
        {
            var line = StripComments(rawLine).Trim();
            if (line.Length == 0)
            {
                continue;
            }

            if (line.StartsWith("plot_style", StringComparison.OrdinalIgnoreCase))
            {
                current = new RenderPlotStyleBuilder();
                continue;
            }

            if (line.StartsWith("}", StringComparison.Ordinal))
            {
                if (current is not null)
                {
                    current.Commit(isNamed, colorStyles, namedStyles);
                    current = null;
                }

                continue;
            }

            if (current is null)
            {
                continue;
            }

            if (!TryParseKeyValue(line, out var key, out var value))
            {
                continue;
            }

            switch (key)
            {
                case "index":
                case "aci":
                    if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var index))
                    {
                        current.Index = index;
                    }
                    break;
                case "name":
                    current.Name = TrimQuotes(value);
                    break;
                case "color":
                    if (TryParseColor(value, out var color))
                    {
                        current.Color = color;
                    }
                    break;
                case "lineweight":
                case "line_weight":
                    if (float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var weight))
                    {
                        current.LineWeightMm = weight;
                    }
                    break;
            }
        }

        if (current is not null)
        {
            current.Commit(isNamed, colorStyles, namedStyles);
        }

        if (colorStyles.Count == 0 && namedStyles.Count == 0)
        {
            return null;
        }

        return new RenderPlotStyleTable(isNamed, colorStyles, namedStyles);
    }

    private static string StripComments(string line)
    {
        var index = line.IndexOf(';');
        return index >= 0 ? line.Substring(0, index) : line;
    }

    private static bool TryParseKeyValue(string line, out string key, out string value)
    {
        key = string.Empty;
        value = string.Empty;
        var index = line.IndexOf('=');
        if (index < 0)
        {
            return false;
        }

        key = line.Substring(0, index).Trim().ToLowerInvariant();
        value = line.Substring(index + 1).Trim();
        value = value.TrimEnd(',');
        return true;
    }

    private static string TrimQuotes(string value)
    {
        if (value.Length >= 2 && value[0] == '"' && value[^1] == '"')
        {
            return value.Substring(1, value.Length - 2);
        }

        return value;
    }

    private static bool TryParseColor(string value, out RenderColor color)
    {
        color = default;
        var parts = value.Split(',', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 3)
        {
            return false;
        }

        if (!byte.TryParse(parts[0].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var r))
        {
            return false;
        }

        if (!byte.TryParse(parts[1].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var g))
        {
            return false;
        }

        if (!byte.TryParse(parts[2].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var b))
        {
            return false;
        }

        color = new RenderColor(r, g, b, 255);
        return true;
    }

    private sealed class RenderPlotStyleBuilder
    {
        public int? Index { get; set; }
        public string? Name { get; set; }
        public RenderColor? Color { get; set; }
        public float? LineWeightMm { get; set; }

        public void Commit(
            bool isNamed,
            Dictionary<int, RenderPlotStyle> colorStyles,
            Dictionary<string, RenderPlotStyle> namedStyles)
        {
            var style = new RenderPlotStyle(Color, LineWeightMm);
            if (isNamed)
            {
                if (!string.IsNullOrWhiteSpace(Name))
                {
                    namedStyles[Name] = style;
                }

                return;
            }

            if (Index.HasValue && Index.Value > 0)
            {
                colorStyles[Index.Value] = style;
            }
        }
    }
}
