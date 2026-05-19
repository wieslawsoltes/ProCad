namespace ProCad.ViewModels;

public sealed class CadVisualHelperBadgeViewModel
{
    public CadVisualHelperBadgeViewModel(string key, string label)
    {
        Key = key;
        Label = label;
    }

    public string Key { get; }
    public string Label { get; }
}
