namespace ProCad.ViewModels;

public sealed class CadRenderLayoutViewModel : ViewModelBase
{
    public string Name { get; }
    public string DisplayName { get; }
    public bool IsPaperSpace { get; }

    public CadRenderLayoutViewModel(string name, bool isPaperSpace, string? displayName = null)
    {
        Name = name;
        IsPaperSpace = isPaperSpace;
        DisplayName = string.IsNullOrWhiteSpace(displayName) ? name : displayName;
    }
}
