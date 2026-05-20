using System;
using System.Globalization;

namespace ProCad.Core;

public static class CadValueConverter
{
    public static bool CanEdit(Type type)
    {
        var target = Nullable.GetUnderlyingType(type) ?? type;
        return target.IsEnum ||
               target == typeof(string) ||
               target == typeof(bool) ||
               target == typeof(byte) ||
               target == typeof(sbyte) ||
               target == typeof(short) ||
               target == typeof(ushort) ||
               target == typeof(int) ||
               target == typeof(uint) ||
               target == typeof(long) ||
               target == typeof(ulong) ||
               target == typeof(float) ||
               target == typeof(double) ||
               target == typeof(decimal) ||
               target == typeof(Guid) ||
               target == typeof(DateTime);
    }

    public static string FormatValue(object? value, Type type)
    {
        if (value is null)
        {
            return string.Empty;
        }

        if (value is IFormattable formattable)
        {
            return formattable.ToString(null, CultureInfo.InvariantCulture);
        }

        return value.ToString() ?? string.Empty;
    }

    public static bool TryConvert(object? value, Type targetType, out object? converted)
    {
        converted = null;
        var underlying = Nullable.GetUnderlyingType(targetType) ?? targetType;

        if (value is null)
        {
            if (!underlying.IsValueType || Nullable.GetUnderlyingType(targetType) is not null)
            {
                converted = null;
                return true;
            }

            return false;
        }

        if (underlying.IsInstanceOfType(value))
        {
            converted = value;
            return true;
        }

        if (value is string text)
        {
            if (text.Length == 0 && Nullable.GetUnderlyingType(targetType) is not null)
            {
                converted = null;
                return true;
            }

            if (underlying == typeof(string))
            {
                converted = text;
                return true;
            }

            if (underlying.IsEnum)
            {
                if (Enum.TryParse(underlying, text, true, out var parsed))
                {
                    converted = parsed;
                    return true;
                }

                return false;
            }

            if (underlying == typeof(Guid))
            {
                if (Guid.TryParse(text, out var guid))
                {
                    converted = guid;
                    return true;
                }

                return false;
            }

            if (underlying == typeof(DateTime))
            {
                if (DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var dt))
                {
                    converted = dt;
                    return true;
                }

                return false;
            }

            if (double.TryParse(text, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out var asDouble))
            {
                return TryConvert(asDouble, underlying, out converted);
            }

            if (long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var asLong))
            {
                return TryConvert(asLong, underlying, out converted);
            }

            if (bool.TryParse(text, out var asBool))
            {
                return TryConvert(asBool, underlying, out converted);
            }

            return false;
        }

        if (value is IConvertible)
        {
            try
            {
                converted = Convert.ChangeType(value, underlying, CultureInfo.InvariantCulture);
                return true;
            }
            catch (Exception)
            {
                converted = null;
                return false;
            }
        }

        return false;
    }
}
