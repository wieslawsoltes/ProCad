using Avalonia.Input;

namespace ProCad.Services;

public static class CadInsertDragDropFormats
{
    // Avalonia application format identifiers allow only letters, digits, dot and hyphen.
    public const string BlockName = "procad.block-name";
    public const string BlockNameMime = "application/x-procad-block-name";

    public static readonly DataFormat<string> BlockNameFormat =
        DataFormat.CreateStringApplicationFormat(BlockName);

    public static readonly DataFormat<string> BlockNamePlatformFormat =
        DataFormat.CreateStringPlatformFormat(BlockNameMime);
}
