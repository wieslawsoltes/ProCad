using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace ProCad.Rendering;

internal static class RenderAcisSabDecoder
{
    private static readonly string[] SabHeaders =
    {
        "ACIS BinaryFile",
        "ASM BinaryFile"
    };

    public static bool TryDecode(string? text, out string satText)
    {
        satText = string.Empty;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        if (!TryFindHeader(text, out var headerIndex, out var header))
        {
            return false;
        }

        if (TryDecodeHexPayload(text, headerIndex, header, out var hexBytes))
        {
            return TryDecode(hexBytes, out satText);
        }

        var bytes = Encoding.Latin1.GetBytes(text.Substring(headerIndex));
        return TryDecode(bytes, out satText);
    }

    public static bool TryDecode(ReadOnlySpan<byte> data, out string satText)
    {
        satText = string.Empty;
        if (!TryGetHeader(data, out var headerLength))
        {
            return false;
        }

        var reader = new SabReader(data.Slice(headerLength));
        if (!reader.TryReadInt32(out var version) ||
            !reader.TryReadInt32(out var numRecords) ||
            !reader.TryReadInt32(out var numEntities) ||
            !reader.TryReadInt32(out var hasHistory))
        {
            return false;
        }

        var builder = new StringBuilder();
        AppendInt(builder, version);
        AppendInt(builder, numRecords);
        AppendInt(builder, numEntities);
        AppendInt(builder, hasHistory);
        builder.Append('\n');

        if (!TryReadTaggedString(ref reader, builder) ||
            !TryReadTaggedString(ref reader, builder) ||
            !TryReadTaggedString(ref reader, builder))
        {
            return false;
        }

        builder.Append('\n');

        if (!TryReadTaggedDouble(ref reader, builder) ||
            !TryReadTaggedDouble(ref reader, builder) ||
            !TryReadTaggedDouble(ref reader, builder))
        {
            return false;
        }

        builder.Append('\n');

        string? currentRecord = null;
        var booleanIndex = 0;

        while (reader.TryReadByte(out var tag))
        {
            switch (tag)
            {
                case 17:
                    builder.Append("#\n");
                    break;
                case 13:
                case 7:
                case 14:
                    {
                        if (!reader.TryReadByte(out var length))
                        {
                            return false;
                        }

                        if (!reader.TryReadString(length, out var value))
                        {
                            return false;
                        }

                        if (tag == 13)
                        {
                            currentRecord = value;
                        }

                        builder.Append(value);
                        builder.Append(tag == 14 ? "-" : " ");
                        break;
                    }
                case 8:
                    {
                        if (!reader.TryReadInt16(out var length))
                        {
                            return false;
                        }

                        if (length < 0)
                        {
                            return false;
                        }

                        if (!reader.TryReadString((ushort)length, out var value))
                        {
                            return false;
                        }

                        builder.Append(value);
                        builder.Append(' ');
                        break;
                    }
                case 9:
                    {
                        if (!reader.TryReadInt32(out var length))
                        {
                            return false;
                        }

                        if (length < 0)
                        {
                            return false;
                        }

                        if (!reader.TryReadString(length, out var value))
                        {
                            return false;
                        }

                        builder.Append(value);
                        builder.Append(' ');
                        break;
                    }
                case 10:
                case 11:
                    {
                        var token = ResolveBooleanToken(currentRecord, tag == 10, ref booleanIndex);
                        builder.Append(token);
                        builder.Append(' ');
                        break;
                    }
                case 15:
                    builder.Append("{ ");
                    break;
                case 16:
                    builder.Append("} ");
                    break;
                case 2:
                    {
                        if (!reader.TryReadSByte(out var value))
                        {
                            return false;
                        }

                        AppendInt(builder, value);
                        break;
                    }
                case 3:
                    {
                        if (!reader.TryReadInt16(out var value))
                        {
                            return false;
                        }

                        AppendInt(builder, value);
                        break;
                    }
                case 4:
                case 12:
                case 21:
                    {
                        if (!reader.TryReadInt32(out var value))
                        {
                            return false;
                        }

                        AppendPointer(builder, value);
                        break;
                    }
                case 5:
                    {
                        if (!reader.TryReadSingle(out var value))
                        {
                            return false;
                        }

                        AppendDouble(builder, value);
                        break;
                    }
                case 6:
                    {
                        if (!reader.TryReadDouble(out var value))
                        {
                            return false;
                        }

                        AppendDouble(builder, value);
                        break;
                    }
                case 19:
                case 20:
                    {
                        if (!reader.TryReadDouble(out var x) ||
                            !reader.TryReadDouble(out var y) ||
                            !reader.TryReadDouble(out var z))
                        {
                            return false;
                        }

                        AppendDouble(builder, x);
                        AppendDouble(builder, y);
                        AppendDouble(builder, z);
                        break;
                    }
                case 23:
                    {
                        if (!reader.TryReadInt64(out var value))
                        {
                            return false;
                        }

                        AppendPointer(builder, value);
                        break;
                    }
                default:
                    return false;
            }
        }

        satText = builder.ToString();
        return !string.IsNullOrWhiteSpace(satText);
    }

    private static bool TryDecodeHexPayload(string text, int headerIndex, string header, out byte[] bytes)
    {
        bytes = Array.Empty<byte>();
        if (headerIndex < 0)
        {
            return false;
        }

        var headerSpan = text.AsSpan(headerIndex, Math.Min(header.Length, text.Length - headerIndex));
        if (!headerSpan.Equals(header.AsSpan(), StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var payload = text.AsSpan(headerIndex + header.Length);
        var hexChars = new List<char>(payload.Length);
        foreach (var ch in payload)
        {
            if (char.IsWhiteSpace(ch))
            {
                continue;
            }

            if (!IsHexDigit(ch))
            {
                return false;
            }

            hexChars.Add(ch);
        }

        if (hexChars.Count == 0 || (hexChars.Count % 2) != 0)
        {
            return false;
        }

        bytes = new byte[header.Length + hexChars.Count / 2];
        var headerBytes = Encoding.ASCII.GetBytes(header);
        headerBytes.CopyTo(bytes, 0);
        var offset = header.Length;
        for (var i = 0; i < hexChars.Count; i += 2)
        {
            bytes[offset++] = (byte)((ParseHex(hexChars[i]) << 4) | ParseHex(hexChars[i + 1]));
        }

        return true;
    }

    private static bool IsHexDigit(char value)
    {
        return (value >= '0' && value <= '9') ||
               (value >= 'a' && value <= 'f') ||
               (value >= 'A' && value <= 'F');
    }

    private static int ParseHex(char value)
    {
        if (value >= '0' && value <= '9')
        {
            return value - '0';
        }

        if (value >= 'a' && value <= 'f')
        {
            return value - 'a' + 10;
        }

        return value - 'A' + 10;
    }

    private static bool TryGetHeader(ReadOnlySpan<byte> data, out int length)
    {
        length = 0;
        for (var h = 0; h < SabHeaders.Length; h++)
        {
            var header = SabHeaders[h];
            if (data.Length < header.Length)
            {
                continue;
            }

            var match = true;
            for (var i = 0; i < header.Length; i++)
            {
                var value = data[i];
                if (value >= (byte)'a' && value <= (byte)'z')
                {
                    value = (byte)(value - 32);
                }

                var headerChar = header[i];
                if (headerChar >= 'a' && headerChar <= 'z')
                {
                    headerChar = (char)(headerChar - 32);
                }

                if (value != headerChar)
                {
                    match = false;
                    break;
                }
            }

            if (match)
            {
                length = header.Length;
                return true;
            }
        }

        return false;
    }

    private static bool TryFindHeader(string text, out int index, out string header)
    {
        index = -1;
        header = string.Empty;

        for (var i = 0; i < SabHeaders.Length; i++)
        {
            var candidate = SabHeaders[i];
            var candidateIndex = text.IndexOf(candidate, StringComparison.OrdinalIgnoreCase);
            if (candidateIndex < 0)
            {
                continue;
            }

            if (index < 0 || candidateIndex < index)
            {
                index = candidateIndex;
                header = candidate;
            }
        }

        return index >= 0;
    }

    private static bool TryReadTaggedString(ref SabReader reader, StringBuilder builder)
    {
        if (!reader.TryReadByte(out _))
        {
            return false;
        }

        if (!reader.TryReadByte(out var length))
        {
            return false;
        }

        if (!reader.TryReadString(length, out var value))
        {
            return false;
        }

        AppendInt(builder, length);
        builder.Append(value);
        builder.Append(' ');
        return true;
    }

    private static bool TryReadTaggedDouble(ref SabReader reader, StringBuilder builder)
    {
        if (!reader.TryReadByte(out _))
        {
            return false;
        }

        if (!reader.TryReadDouble(out var value))
        {
            return false;
        }

        AppendDouble(builder, value);
        return true;
    }

    private static void AppendInt(StringBuilder builder, long value)
    {
        builder.Append(value.ToString(CultureInfo.InvariantCulture));
        builder.Append(' ');
    }

    private static void AppendDouble(StringBuilder builder, double value)
    {
        builder.Append(value.ToString("G", CultureInfo.InvariantCulture));
        builder.Append(' ');
    }

    private static void AppendPointer(StringBuilder builder, long value)
    {
        builder.Append('$');
        builder.Append(value.ToString(CultureInfo.InvariantCulture));
        builder.Append(' ');
    }

    private static string ResolveBooleanToken(string? record, bool value, ref int argIndex)
    {
        record ??= string.Empty;
        if (!string.Equals(record, "varblendsplsur", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(record, "face", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(record, "bdy_geom", StringComparison.OrdinalIgnoreCase))
        {
            argIndex = 0;
        }

        if (IsAny(record, "sphere", "plane", "stripc", "torus"))
        {
            return value ? "reverse_v" : "forward_v";
        }

        if (IsAny(record, "spline", "edge", "meshsurf", "pcurve", "intcurve"))
        {
            return value ? "reversed" : "forward";
        }

        if (IsAny(record, "surfcur", "bldcur", "parcur", "projcur", "perspsil"))
        {
            return value ? "surf1" : "surf2";
        }

        if (string.Equals(record, "sweepsur", StringComparison.OrdinalIgnoreCase))
        {
            return value ? "angled" : "normal";
        }

        if (string.Equals(record, "var_cross_section", StringComparison.OrdinalIgnoreCase))
        {
            return value ? "radius" : "no_radius";
        }

        if (string.Equals(record, "var_radius", StringComparison.OrdinalIgnoreCase))
        {
            return value ? "uncalibrated" : "calibrated";
        }

        if (string.Equals(record, "wire", StringComparison.OrdinalIgnoreCase))
        {
            return value ? "in" : "out";
        }

        if (string.Equals(record, "adv_var_blend", StringComparison.OrdinalIgnoreCase))
        {
            return value ? "smooth" : "sharp";
        }

        if (string.Equals(record, "attrib_fhlhead", StringComparison.OrdinalIgnoreCase))
        {
            return value ? "valid" : "invalid";
        }

        if (IsAny(record, "attrib_fhlplist", "attrib_fhl_slist"))
        {
            return value ? "visible" : "invisible";
        }

        if (IsAny(record, "bl_ent_ent", "bl_inst"))
        {
            return value ? "set" : "unset";
        }

        if (string.Equals(record, "face", StringComparison.OrdinalIgnoreCase))
        {
            if (argIndex == 0)
            {
                argIndex++;
                return value ? "reversed" : "forward";
            }

            if (argIndex == 1)
            {
                argIndex++;
                return value ? "double" : "single";
            }

            argIndex = 0;
            return value ? "in" : "out";
        }

        if (string.Equals(record, "varblendsplsur", StringComparison.OrdinalIgnoreCase))
        {
            if (argIndex == 0)
            {
                argIndex++;
                return value ? "convex" : "concave";
            }

            argIndex = 0;
            return value ? "rb_envelope" : "rb_snapshot";
        }

        if (string.Equals(record, "attrib_var_blend", StringComparison.OrdinalIgnoreCase))
        {
            if (argIndex == 0)
            {
                argIndex++;
                return value ? "uncalibrated" : "calibrated";
            }

            if (argIndex == 1)
            {
                argIndex++;
                return value ? "two_radii" : "one_radius";
            }

            argIndex = 0;
            return value ? "reversed" : "forward";
        }

        if (string.Equals(record, "bdy_geom", StringComparison.OrdinalIgnoreCase))
        {
            if (argIndex == 0)
            {
                argIndex++;
                return value ? "non_cross" : "cross";
            }

            argIndex++;
            return value ? "smooth" : "non_smooth";
        }

        return value ? "I" : "F";
    }

    private static bool IsAny(string record, params string[] values)
    {
        for (var i = 0; i < values.Length; i++)
        {
            if (string.Equals(record, values[i], StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private ref struct SabReader
    {
        private ReadOnlySpan<byte> _data;
        private int _offset;

        public SabReader(ReadOnlySpan<byte> data)
        {
            _data = data;
            _offset = 0;
        }

        public bool TryReadByte(out byte value)
        {
            if (_offset + 1 > _data.Length)
            {
                value = 0;
                return false;
            }

            value = _data[_offset++];
            return true;
        }

        public bool TryReadSByte(out sbyte value)
        {
            if (!TryReadByte(out var raw))
            {
                value = 0;
                return false;
            }

            value = unchecked((sbyte)raw);
            return true;
        }

        public bool TryReadInt16(out short value)
        {
            if (_offset + 2 > _data.Length)
            {
                value = 0;
                return false;
            }

            value = BinaryPrimitives.ReadInt16LittleEndian(_data.Slice(_offset, 2));
            _offset += 2;
            return true;
        }

        public bool TryReadInt32(out int value)
        {
            if (_offset + 4 > _data.Length)
            {
                value = 0;
                return false;
            }

            value = BinaryPrimitives.ReadInt32LittleEndian(_data.Slice(_offset, 4));
            _offset += 4;
            return true;
        }

        public bool TryReadInt64(out long value)
        {
            if (_offset + 8 > _data.Length)
            {
                value = 0;
                return false;
            }

            value = BinaryPrimitives.ReadInt64LittleEndian(_data.Slice(_offset, 8));
            _offset += 8;
            return true;
        }

        public bool TryReadSingle(out float value)
        {
            if (!TryReadInt32(out var bits))
            {
                value = 0;
                return false;
            }

            value = BitConverter.Int32BitsToSingle(bits);
            return true;
        }

        public bool TryReadDouble(out double value)
        {
            if (_offset + 8 > _data.Length)
            {
                value = 0;
                return false;
            }

            value = BitConverter.ToDouble(_data.Slice(_offset, 8));
            _offset += 8;
            return true;
        }

        public bool TryReadString(int length, out string value)
        {
            if (length <= 0 || _offset + length > _data.Length)
            {
                value = string.Empty;
                return false;
            }

            value = Encoding.ASCII.GetString(_data.Slice(_offset, length));
            _offset += length;
            return true;
        }
    }
}
