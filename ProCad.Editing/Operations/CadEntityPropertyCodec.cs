using System.Globalization;
using ACadSharp;
using ACadSharp.Tables;

namespace ProCad.Editing.Operations;

internal static class CadEntityPropertyCodec
{
    public const string Layer = "layer";
    public const string LineType = "lineType";
    public const string Color = "color";
    public const string LineWeight = "lineWeight";
    public const string LineTypeScale = "lineTypeScale";
    public const string IsInvisible = "isInvisible";
    public const string Transparency = "transparency";

    public static string SerializeColor(ACadSharp.Color color)
    {
        if (color.IsTrueColor)
        {
            return $"TC:{color.TrueColor.ToString(CultureInfo.InvariantCulture)}";
        }

        return $"I:{color.Index.ToString(CultureInfo.InvariantCulture)}";
    }

    public static bool TryDeserializeColor(string token, out ACadSharp.Color color)
    {
        color = ACadSharp.Color.ByLayer;
        if (string.IsNullOrWhiteSpace(token))
        {
            return false;
        }

        if (token.StartsWith("I:", StringComparison.OrdinalIgnoreCase) &&
            short.TryParse(token.AsSpan(2), NumberStyles.Integer, CultureInfo.InvariantCulture, out var index))
        {
            if (index < 0 || index > 257)
            {
                return false;
            }

            color = new ACadSharp.Color(index);
            return true;
        }

        if (token.StartsWith("TC:", StringComparison.OrdinalIgnoreCase) &&
            uint.TryParse(token.AsSpan(3), NumberStyles.Integer, CultureInfo.InvariantCulture, out var trueColor))
        {
            color = ACadSharp.Color.FromTrueColor(trueColor);
            return true;
        }

        return false;
    }

    public static string SerializeLineWeight(LineWeightType lineWeight)
    {
        return ((short)lineWeight).ToString(CultureInfo.InvariantCulture);
    }

    public static bool TryDeserializeLineWeight(string token, out LineWeightType lineWeight)
    {
        lineWeight = LineWeightType.ByLayer;
        if (!short.TryParse(token, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
        {
            return false;
        }

        lineWeight = (LineWeightType)value;
        return true;
    }

    public static string SerializeLineTypeScale(double value)
    {
        return value.ToString("R", CultureInfo.InvariantCulture);
    }

    public static bool TryDeserializeLineTypeScale(string token, out double value)
    {
        return double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
    }

    public static string SerializeBoolean(bool value)
    {
        return value ? "1" : "0";
    }

    public static bool TryDeserializeBoolean(string token, out bool value)
    {
        value = false;
        if (token == "1")
        {
            value = true;
            return true;
        }

        if (token == "0")
        {
            value = false;
            return true;
        }

        return bool.TryParse(token, out value);
    }

    public static string SerializeTransparency(ACadSharp.Transparency transparency)
    {
        return transparency.Value.ToString(CultureInfo.InvariantCulture);
    }

    public static bool TryDeserializeTransparency(string token, out ACadSharp.Transparency transparency)
    {
        transparency = ACadSharp.Transparency.ByLayer;
        if (!short.TryParse(token, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
        {
            return false;
        }

        try
        {
            transparency = new ACadSharp.Transparency(value);
            return true;
        }
        catch (ArgumentOutOfRangeException)
        {
            return false;
        }
    }

    public static ACadSharp.Tables.Layer ResolveLayer(CadDocument document, string layerName)
    {
        if (document.Layers.TryGetValue(layerName, out var layer))
        {
            return layer;
        }

        var created = new ACadSharp.Tables.Layer(layerName);
        document.Layers.Add(created);
        return created;
    }

    public static ACadSharp.Tables.LineType ResolveLineType(CadDocument document, string lineTypeName)
    {
        if (document.LineTypes.TryGetValue(lineTypeName, out var lineType))
        {
            return lineType;
        }

        var created = new ACadSharp.Tables.LineType(lineTypeName);
        document.LineTypes.Add(created);
        return created;
    }

    public static TextStyle ResolveTextStyle(CadDocument document, string textStyleName)
    {
        if (document.TextStyles.TryGetValue(textStyleName, out var textStyle))
        {
            return textStyle;
        }

        var created = new TextStyle(textStyleName);
        document.TextStyles.Add(created);
        return created;
    }

    public static DimensionStyle ResolveDimensionStyle(CadDocument document, string dimensionStyleName)
    {
        if (document.DimensionStyles.TryGetValue(dimensionStyleName, out var dimensionStyle))
        {
            return dimensionStyle;
        }

        var created = new DimensionStyle(dimensionStyleName);
        document.DimensionStyles.Add(created);
        return created;
    }
}
