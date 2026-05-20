using ACadSharp.Tables;

namespace ProCad.Services;

public interface IStylePreviewRenderer
{
    byte[]? RenderTextStyle(TextStyle style, int size);
    byte[]? RenderLineType(LineType lineType, int size);
    byte[]? RenderDimensionStyle(DimensionStyle style, int size);
}
