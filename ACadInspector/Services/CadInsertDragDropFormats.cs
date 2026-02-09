using Avalonia.Input;

namespace ACadInspector.Services;

public static class CadInsertDragDropFormats
{
    // Avalonia application format identifiers allow only letters, digits, dot and hyphen.
    public const string BlockName = "acadinspector.block-name";
    public const string BlockNameMime = "application/x-acadinspector-block-name";

    public static readonly DataFormat<string> BlockNameFormat =
        DataFormat.CreateStringApplicationFormat(BlockName);

    public static readonly DataFormat<string> BlockNamePlatformFormat =
        DataFormat.CreateStringPlatformFormat(BlockNameMime);
}
