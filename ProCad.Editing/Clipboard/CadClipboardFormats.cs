using System;

namespace ProCad.Editing.Clipboard;

public static class CadClipboardFormats
{
    public const string CadJsonMime = "application/x-procad-cadjson";
    public const string CadDxfMime = "application/x-procad-dxf";
    public const string CadTextPrefix = "ProCadClipboard:";

    public static readonly string LegacyCadJsonMime =
        string.Concat("application/x-", "acad", "inspector-cadjson");

    public static readonly string LegacyCadDxfMime =
        string.Concat("application/x-", "acad", "inspector-dxf");

    public static readonly string LegacyCadTextPrefix =
        string.Concat("A", "Cad", "InspectorClipboard:");

    public static bool TryRemoveCadTextPrefix(string? text, out string serializedPayload)
    {
        serializedPayload = string.Empty;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        if (text.StartsWith(CadTextPrefix, StringComparison.Ordinal))
        {
            serializedPayload = text[CadTextPrefix.Length..];
            return true;
        }

        if (text.StartsWith(LegacyCadTextPrefix, StringComparison.Ordinal))
        {
            serializedPayload = text[LegacyCadTextPrefix.Length..];
            return true;
        }

        return false;
    }
}
