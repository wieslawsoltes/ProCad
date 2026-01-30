using System;
using System.Collections.Generic;
using System.Buffers.Binary;
using System.Text;
using ACadInspector.Rendering;
using Xunit;

namespace ACadInspector.Tests.Rendering;

public sealed class RenderAcisSabDecoderTests
{
    [Fact]
    public void TryDecode_DecodesMinimalSab()
    {
        var sab = BuildMinimalSab();

        Assert.True(RenderAcisSabDecoder.TryDecode(sab, out var satText));
        Assert.Contains("point", satText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("#", satText, StringComparison.Ordinal);

        Assert.True(RenderAcisSatParser.TryParse(satText, out var document));
        Assert.Contains(document.Records, record => record.Type.Contains("point", StringComparison.OrdinalIgnoreCase));
    }

    private static byte[] BuildMinimalSab()
    {
        var bytes = new List<byte>();
        AppendAscii(bytes, "ACIS BinaryFile");
        AppendInt32(bytes, 400);
        AppendInt32(bytes, 1);
        AppendInt32(bytes, 1);
        AppendInt32(bytes, 0);

        AppendTaggedString(bytes, "Test");
        AppendTaggedString(bytes, "ACIS 7.0");
        AppendTaggedString(bytes, "Mon Jan 01 00:00:00 2000");

        AppendTaggedDouble(bytes, 1.0);
        AppendTaggedDouble(bytes, 1e-6);
        AppendTaggedDouble(bytes, 1e-10);

        AppendTag(bytes, 2);
        AppendSByte(bytes, -1);
        AppendIdent(bytes, "point");
        AppendPointer(bytes, -1);
        AppendTaggedDouble(bytes, 0);
        AppendTaggedDouble(bytes, 0);
        AppendTaggedDouble(bytes, 0);
        AppendTag(bytes, 17);

        return bytes.ToArray();
    }

    private static void AppendTaggedString(List<byte> bytes, string value)
    {
        AppendTag(bytes, 7);
        AppendByte(bytes, (byte)value.Length);
        AppendAscii(bytes, value);
    }

    private static void AppendTaggedDouble(List<byte> bytes, double value)
    {
        AppendTag(bytes, 6);
        AppendDouble(bytes, value);
    }

    private static void AppendIdent(List<byte> bytes, string value)
    {
        AppendTag(bytes, 13);
        AppendByte(bytes, (byte)value.Length);
        AppendAscii(bytes, value);
    }

    private static void AppendPointer(List<byte> bytes, int value)
    {
        AppendTag(bytes, 12);
        AppendInt32(bytes, value);
    }

    private static void AppendTag(List<byte> bytes, byte tag)
    {
        bytes.Add(tag);
    }

    private static void AppendByte(List<byte> bytes, byte value)
    {
        bytes.Add(value);
    }

    private static void AppendSByte(List<byte> bytes, sbyte value)
    {
        bytes.Add(unchecked((byte)value));
    }

    private static void AppendInt32(List<byte> bytes, int value)
    {
        Span<byte> buffer = stackalloc byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(buffer, value);
        AppendSpan(bytes, buffer);
    }

    private static void AppendDouble(List<byte> bytes, double value)
    {
        Span<byte> buffer = stackalloc byte[8];
        BinaryPrimitives.WriteInt64LittleEndian(buffer, BitConverter.DoubleToInt64Bits(value));
        AppendSpan(bytes, buffer);
    }

    private static void AppendAscii(List<byte> bytes, string value)
    {
        var buffer = Encoding.ASCII.GetBytes(value);
        bytes.AddRange(buffer);
    }

    private static void AppendSpan(List<byte> bytes, ReadOnlySpan<byte> buffer)
    {
        for (var i = 0; i < buffer.Length; i++)
        {
            bytes.Add(buffer[i]);
        }
    }
}
