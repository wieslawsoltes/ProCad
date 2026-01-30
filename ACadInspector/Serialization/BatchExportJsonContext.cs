using System.Text.Json.Serialization;
using ACadInspector.Core;

namespace ACadInspector.Serialization;

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    WriteIndented = true)]
[JsonSerializable(typeof(CadBatchSearchExport))]
[JsonSerializable(typeof(CadBatchSearchResult))]
public partial class BatchExportJsonContext : JsonSerializerContext
{
}
