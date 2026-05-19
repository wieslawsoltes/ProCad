namespace ProCad.Core;

public readonly record struct CadPropertyEditResult(
    bool IsValid,
    string? Message,
    object? CoercedValue
)
{
    public static CadPropertyEditResult Success(object? value) => new(true, null, value);
    public static CadPropertyEditResult Failure(string message) => new(false, message, null);
}
