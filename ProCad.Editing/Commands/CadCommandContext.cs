using ProCad.Editing.Sessions;

namespace ProCad.Editing.Commands;

public sealed class CadCommandContext
{
    public ICadEditorSession? Session { get; }
    public string RawInput { get; }
    public string CommandName { get; }
    public IReadOnlyList<string> Arguments { get; }
    public CancellationToken CancellationToken { get; }

    public CadCommandContext(
        ICadEditorSession? session,
        string rawInput,
        string commandName,
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken)
    {
        Session = session;
        RawInput = rawInput;
        CommandName = commandName;
        Arguments = arguments;
        CancellationToken = cancellationToken;
    }
}
