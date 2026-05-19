using System.IO;
using System.Text.Json;

namespace ProCad.Rendering;

public static class RenderStatsExporter
{
    public static string ToJson(RenderStats stats, bool indented = false)
    {
        var options = new JsonSerializerOptions
        {
            WriteIndented = indented
        };

        return JsonSerializer.Serialize(stats, options);
    }

    public static void WriteJson(RenderStats stats, string path, bool indented = false)
    {
        var json = ToJson(stats, indented);
        File.WriteAllText(path, json);
    }
}
