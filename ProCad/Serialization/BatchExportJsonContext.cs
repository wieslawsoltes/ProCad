using System.Text.Json.Serialization;
using ProCad.Core;

namespace ProCad.Serialization;

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    WriteIndented = true)]
[JsonSerializable(typeof(CadBatchSearchExport))]
[JsonSerializable(typeof(CadBatchSearchResult))]
public partial class BatchExportJsonContext : JsonSerializerContext
{
}
