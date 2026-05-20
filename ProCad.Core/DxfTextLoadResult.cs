namespace ProCad.Core;

public sealed class DxfTextLoadResult
{
    private DxfTextLoadResult(bool hasText, bool isBinary, string text, string? error)
    {
        HasText = hasText;
        IsBinary = isBinary;
        Text = text;
        Error = error;
    }

    public bool HasText { get; }

    public bool IsBinary { get; }

    public string Text { get; }

    public string? Error { get; }

    public static DxfTextLoadResult Success(string text)
    {
        return new DxfTextLoadResult(true, false, text, null);
    }

    public static DxfTextLoadResult Binary(string? error)
    {
        return new DxfTextLoadResult(false, true, string.Empty, error);
    }

    public static DxfTextLoadResult Unavailable(string? error)
    {
        return new DxfTextLoadResult(false, false, string.Empty, error);
    }
}
