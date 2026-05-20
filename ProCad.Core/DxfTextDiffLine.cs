namespace ProCad.Core;

public sealed class DxfTextDiffLine
{
    public DxfTextDiffLine(
        int? leftLineNumber,
        int? rightLineNumber,
        string leftText,
        string rightText,
        DxfTextDiffKind kind)
    {
        LeftLineNumber = leftLineNumber;
        RightLineNumber = rightLineNumber;
        LeftText = leftText;
        RightText = rightText;
        Kind = kind;
    }

    public int? LeftLineNumber { get; }

    public int? RightLineNumber { get; }

    public string LeftText { get; }

    public string RightText { get; }

    public DxfTextDiffKind Kind { get; }
}
