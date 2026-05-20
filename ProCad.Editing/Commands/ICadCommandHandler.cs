namespace ProCad.Editing.Commands;

public interface ICadCommandHandler
{
    string Name { get; }
    IReadOnlyList<string> Aliases { get; }
    bool CanExecute(CadCommandContext context);
    ValueTask<CadCommandResult> ExecuteAsync(CadCommandContext context);
}

public interface ICadDescribedCommandHandler : ICadCommandHandler
{
    CadCommandDescriptor Descriptor { get; }
}
