using Avalonia.Input;

namespace ACadInspector.Services;

public static class CadInsertDragDropFormats
{
    public const string BlockName = "application/x-acadinspector-block-name";

    public static readonly DataFormat<string> BlockNameFormat =
        DataFormat.CreateStringApplicationFormat(BlockName);
}
