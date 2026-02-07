using ACadInspector.Editing.Operations;

namespace ACadInspector.Editing.Commands;

public sealed record CadCommandResult(
    bool Success,
    string Message,
    IReadOnlyList<CadOperation>? Operations = null)
{
    public static CadCommandResult Ok(string message = "OK", IReadOnlyList<CadOperation>? operations = null)
        => new(true, message, operations);

    public static CadCommandResult Fail(string message)
        => new(false, message);
}
