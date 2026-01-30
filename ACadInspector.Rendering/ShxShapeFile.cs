using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace ACadInspector.Rendering;

internal sealed class ShxShapeFile
{
    private const int ShapesStartIndex = 0x17;
    private static readonly byte[] ShapesHeader10 = Encoding.ASCII.GetBytes("AutoCAD-86 shapes 1.0");
    private static readonly byte[] ShapesHeader11 = Encoding.ASCII.GetBytes("AutoCAD-86 shapes 1.1");

    private readonly Dictionary<int, int[]> _shapes;

    private ShxShapeFile(Dictionary<int, int[]> shapes)
    {
        _shapes = shapes;
    }

    public static ShxShapeFile Load(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("Shape file path is required.", nameof(path));
        }

        var data = File.ReadAllBytes(path);
        return Load(data);
    }

    public static ShxShapeFile Load(byte[] data)
    {
        if (data is null || data.Length == 0)
        {
            throw new InvalidDataException("Shape file is empty.");
        }

        if (!StartsWith(data, ShapesHeader10) && !StartsWith(data, ShapesHeader11))
        {
            throw new InvalidDataException("Unsupported SHX shape file header.");
        }

        var shapes = ParseShapes(data);
        return new ShxShapeFile(shapes);
    }

    public bool TryGetCodes(int shapeNumber, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out int[]? codes)
    {
        return _shapes.TryGetValue(shapeNumber, out codes);
    }

    private static Dictionary<int, int[]> ParseShapes(byte[] data)
    {
        if (data.Length <= ShapesStartIndex || data[ShapesStartIndex] != 0x1A)
        {
            throw new InvalidDataException("Invalid SHX shape file signature.");
        }

        var reader = new ShxShapeReader(data, ShapesStartIndex + 1);
        var firstNumber = reader.ReadUInt16();
        var lastNumber = reader.ReadUInt16();
        var shapeCount = reader.ReadUInt16();
        var indexTable = new List<(int Number, int Length)>(shapeCount);

        for (var i = 0; i < shapeCount; i++)
        {
            var shapeNumber = reader.ReadUInt16();
            var length = reader.ReadUInt16();
            indexTable.Add((shapeNumber, length));
        }

        if (indexTable.Count > 0)
        {
            if (indexTable[0].Number != firstNumber || indexTable[^1].Number != lastNumber)
            {
                throw new InvalidDataException("Invalid SHX index table.");
            }
        }

        var shapes = new Dictionary<int, int[]>(shapeCount);
        foreach (var entry in indexTable)
        {
            var record = reader.ReadBytes(entry.Length);
            var codes = ParseShapeRecord(record);
            shapes[entry.Number] = codes;
        }

        var eof = reader.ReadBytes(3);
        if (eof.Length != 3 || eof[0] != (byte)'E' || eof[1] != (byte)'O' || eof[2] != (byte)'F')
        {
            throw new InvalidDataException("Missing SHX EOF marker.");
        }

        return shapes;
    }

    private static int[] ParseShapeRecord(byte[] record)
    {
        var nameEnd = Array.IndexOf(record, (byte)0);
        if (nameEnd < 0 || nameEnd + 1 > record.Length)
        {
            throw new InvalidDataException("Invalid SHX shape record.");
        }

        var codesSpan = record.AsSpan(nameEnd + 1);
        return ParseShapeCodes(codesSpan);
    }

    private static int[] ParseShapeCodes(ReadOnlySpan<byte> data)
    {
        var reader = new ShxShapeReader(data);
        var codes = new List<int>();

        while (reader.HasData)
        {
            var code = reader.ReadByte();
            codes.Add(code);

            if (code == 0)
            {
                break;
            }

            if (code > 14 || code is 1 or 2 or 5 or 6 or 14)
            {
                continue;
            }

            if (code is 3 or 4)
            {
                codes.Add(reader.ReadByte());
                continue;
            }

            if (code == 7)
            {
                codes.Add(reader.ReadByte());
                continue;
            }

            if (code == 8)
            {
                codes.Add(reader.ReadInt8());
                codes.Add(reader.ReadInt8());
                continue;
            }

            if (code == 9)
            {
                while (true)
                {
                    var x = reader.ReadInt8();
                    var y = reader.ReadInt8();
                    codes.Add(x);
                    codes.Add(y);
                    if (x == 0 && y == 0)
                    {
                        break;
                    }
                }

                continue;
            }

            if (code == 10)
            {
                codes.Add(reader.ReadByte());
                codes.Add(reader.ReadOctant());
                continue;
            }

            if (code == 11)
            {
                codes.Add(reader.ReadByte());
                codes.Add(reader.ReadByte());
                codes.Add(reader.ReadByte());
                codes.Add(reader.ReadByte());
                codes.Add(reader.ReadOctant());
                continue;
            }

            if (code == 12)
            {
                codes.Add(reader.ReadInt8());
                codes.Add(reader.ReadInt8());
                codes.Add(reader.ReadInt8());
                continue;
            }

            if (code == 13)
            {
                while (true)
                {
                    var x = reader.ReadInt8();
                    var y = reader.ReadInt8();
                    codes.Add(x);
                    codes.Add(y);
                    if (x == 0 && y == 0)
                    {
                        break;
                    }

                    codes.Add(reader.ReadInt8());
                }
            }
        }

        return codes.ToArray();
    }

    private static bool StartsWith(byte[] data, byte[] header)
    {
        if (data.Length < header.Length)
        {
            return false;
        }

        for (var i = 0; i < header.Length; i++)
        {
            if (data[i] != header[i])
            {
                return false;
            }
        }

        return true;
    }

    private ref struct ShxShapeReader
    {
        private readonly ReadOnlySpan<byte> _data;
        private int _index;

        public bool HasData => _index < _data.Length;

        public ShxShapeReader(ReadOnlySpan<byte> data, int index = 0)
        {
            _data = data;
            _index = index;
        }

        public byte ReadByte()
        {
            return _data[_index++];
        }

        public int ReadInt8()
        {
            var value = _data[_index++];
            if (value > 127)
            {
                return (value & 127) - 128;
            }

            return value;
        }

        public int ReadOctant()
        {
            var value = _data[_index++];
            if ((value & 128) == 128)
            {
                return -(value & 127);
            }

            return value;
        }

        public int ReadUInt16()
        {
            var value = _data[_index] | (_data[_index + 1] << 8);
            _index += 2;
            return value;
        }

        public byte[] ReadBytes(int count)
        {
            var slice = _data.Slice(_index, count);
            _index += count;
            return slice.ToArray();
        }
    }
}
