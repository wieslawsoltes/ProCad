namespace ProCad.Services;

public interface ICadTool
{
    string Id { get; }
    string DisplayName { get; }

    void Activate(in CadToolContext context);
    void Deactivate(in CadToolContext context);
    void HandleInput(in CadToolInput input, in CadToolContext context);
}
