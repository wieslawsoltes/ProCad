namespace ProCad.Editing.Prompt;

public interface ICadPromptSession
{
    IReadOnlyList<string> History { get; }
    string? CurrentPrompt { get; }

    void SetPrompt(string? prompt);
    void PushHistory(string value);
    void Clear();
}

public sealed class CadPromptSession : ICadPromptSession
{
    private readonly List<string> _history = new();

    public IReadOnlyList<string> History => _history;
    public string? CurrentPrompt { get; private set; }

    public void SetPrompt(string? prompt)
    {
        CurrentPrompt = prompt;
    }

    public void PushHistory(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        _history.Add(value.Trim());
    }

    public void Clear()
    {
        _history.Clear();
        CurrentPrompt = null;
    }
}
