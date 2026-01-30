using System.IO;
using ACadInspector.Core;
using ACadSharp;
using ACadSharp.IO;
using ACadSharp.IO.DWG;

namespace ACadInspector.IO;

public sealed class AcAdSharpDocumentService : ICadDocumentService
{
    public CadDocument Load(string path, CadReadOptions options, NotificationEventHandler? notification = null)
    {
        var format = ResolveFormat(path, options.Format);

        return format switch
        {
            CadFileFormat.Dxf => LoadDxf(path, options, notification),
            CadFileFormat.Dwg => LoadDwg(path, options, notification),
            _ => throw new NotSupportedException($"Unsupported CAD format: {format}.")
        };
    }

    public CadDocument Load(Stream stream, CadReadOptions options, NotificationEventHandler? notification = null)
    {
        var format = ResolveFormat(options.Format);

        return format switch
        {
            CadFileFormat.Dxf => LoadDxf(stream, options, notification),
            CadFileFormat.Dwg => LoadDwg(stream, options, notification),
            _ => throw new NotSupportedException($"Unsupported CAD format: {format}.")
        };
    }

    public void Save(string path, CadDocument document, CadWriteOptions options, NotificationEventHandler? notification = null)
    {
        switch (options.Format)
        {
            case CadFileFormat.Dxf:
                var dxfConfig = new DxfWriterConfiguration
                {
                    WriteAllHeaderVariables = options.WriteAllDxfHeaderVariables
                };
                DxfWriter.Write(path, document, options.WriteBinaryDxf, dxfConfig, notification);
                break;
            case CadFileFormat.Dwg:
                var dwgConfig = new DwgWriterConfiguration();
                DwgWriter.Write(path, document, dwgConfig, notification);
                break;
            default:
                throw new NotSupportedException($"Unsupported CAD format: {options.Format}.");
        }
    }

    public void Save(Stream stream, CadDocument document, CadWriteOptions options, NotificationEventHandler? notification = null)
    {
        switch (options.Format)
        {
            case CadFileFormat.Dxf:
                var dxfConfig = new DxfWriterConfiguration
                {
                    WriteAllHeaderVariables = options.WriteAllDxfHeaderVariables
                };
                DxfWriter.Write(stream, document, options.WriteBinaryDxf, dxfConfig, notification);
                break;
            case CadFileFormat.Dwg:
                var dwgConfig = new DwgWriterConfiguration();
                DwgWriter.Write(stream, document, dwgConfig, notification);
                break;
            default:
                throw new NotSupportedException($"Unsupported CAD format: {options.Format}.");
        }
    }

    private static CadDocument LoadDxf(string path, CadReadOptions options, NotificationEventHandler? notification)
    {
        var config = new DxfReaderConfiguration
        {
            ClearCache = options.ClearDxfCache,
            CreateDefaults = options.CreateDxfDefaults
        };

        return DxfReader.Read(path, config, notification);
    }

    private static CadDocument LoadDxf(Stream stream, CadReadOptions options, NotificationEventHandler? notification)
    {
        var config = new DxfReaderConfiguration
        {
            ClearCache = options.ClearDxfCache,
            CreateDefaults = options.CreateDxfDefaults
        };

        using var reader = new DxfReader(stream, notification)
        {
            Configuration = config
        };

        return reader.Read();
    }

    private static CadDocument LoadDwg(string path, CadReadOptions options, NotificationEventHandler? notification)
    {
        var config = new DwgReaderConfiguration
        {
            ReadSummaryInfo = options.ReadSummaryInfo,
            CrcCheck = options.DwgCrcCheck
        };

        return DwgReader.Read(path, config, notification);
    }

    private static CadDocument LoadDwg(Stream stream, CadReadOptions options, NotificationEventHandler? notification)
    {
        var config = new DwgReaderConfiguration
        {
            ReadSummaryInfo = options.ReadSummaryInfo,
            CrcCheck = options.DwgCrcCheck
        };

        return DwgReader.Read(stream, config, notification);
    }

    private static CadFileFormat ResolveFormat(string path, CadFileFormat? format)
    {
        if (format.HasValue)
        {
            return format.Value;
        }

        var ext = Path.GetExtension(path).ToLowerInvariant();
        return ext switch
        {
            ".dxf" => CadFileFormat.Dxf,
            ".dwg" => CadFileFormat.Dwg,
            _ => throw new ArgumentException($"Unable to infer CAD file format from extension '{ext}'.", nameof(path))
        };
    }

    private static CadFileFormat ResolveFormat(CadFileFormat? format)
    {
        if (format.HasValue)
        {
            return format.Value;
        }

        throw new ArgumentException("CAD file format must be provided when loading from a stream.", nameof(format));
    }
}
