using System;
using System.Buffers;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ACadInspector.Core;

namespace ACadInspector.IO;

public sealed class DxfTextService : IDxfTextService
{
    private static readonly byte[] BinaryMarker = Encoding.ASCII.GetBytes("AutoCAD Binary DXF");

    public async ValueTask<DxfTextLoadResult> TryLoadAsciiDxfAsync(
        CadFileFormat? format,
        string? path,
        Func<CancellationToken, ValueTask<Stream>>? openRead,
        CancellationToken cancellationToken)
    {
        if (format != CadFileFormat.Dxf)
        {
            return DxfTextLoadResult.Unavailable("Not a DXF document.");
        }

        if (string.IsNullOrWhiteSpace(path) && openRead is null)
        {
            return DxfTextLoadResult.Unavailable("DXF text source is not available.");
        }

        try
        {
            byte[] data;
            if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
            {
                await using var stream = File.OpenRead(path);
                data = await ReadAllBytesAsync(stream, cancellationToken).ConfigureAwait(false);
            }
            else if (openRead is not null)
            {
                await using var stream = await openRead(cancellationToken).ConfigureAwait(false);
                data = await ReadAllBytesAsync(stream, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                return DxfTextLoadResult.Unavailable("DXF text source is not available.");
            }

            if (IsBinaryDxf(data))
            {
                return DxfTextLoadResult.Binary("Binary DXF is not supported for text diff.");
            }

            var text = DecodeText(data);
            return DxfTextLoadResult.Success(text);
        }
        catch (OperationCanceledException)
        {
            return DxfTextLoadResult.Unavailable("DXF text diff cancelled.");
        }
        catch (Exception ex)
        {
            return DxfTextLoadResult.Unavailable(ex.Message);
        }
    }

    private static string DecodeText(byte[] data)
    {
        if (data.Length == 0)
        {
            return string.Empty;
        }

        return Encoding.UTF8.GetString(data);
    }

    private static bool IsBinaryDxf(ReadOnlySpan<byte> data)
    {
        if (data.Length >= BinaryMarker.Length)
        {
            var isMarker = true;
            for (var i = 0; i < BinaryMarker.Length; i++)
            {
                if (data[i] != BinaryMarker[i])
                {
                    isMarker = false;
                    break;
                }
            }

            if (isMarker)
            {
                return true;
            }
        }

        var scanLength = Math.Min(data.Length, 1024);
        for (var i = 0; i < scanLength; i++)
        {
            if (data[i] == 0)
            {
                return true;
            }
        }

        return false;
    }

    private static async Task<byte[]> ReadAllBytesAsync(Stream stream, CancellationToken cancellationToken)
    {
        using var memory = new MemoryStream();
        var buffer = ArrayPool<byte>.Shared.Rent(81920);
        try
        {
            while (true)
            {
                var read = await stream.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken).ConfigureAwait(false);
                if (read == 0)
                {
                    break;
                }

                await memory.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }

        return memory.ToArray();
    }
}
