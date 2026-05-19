using System;
using ReactiveUI;

namespace ProCad.ViewModels;

public sealed class CadRenderEntityTypeRowViewModel : ReactiveObject
{
    private bool _isVisible;

    public string EntityType { get; }
    public int Count { get; }

    public bool IsVisible
    {
        get => _isVisible;
        set
        {
            if (!this.RaiseAndSetIfChanged(ref _isVisible, value))
            {
                return;
            }

            VisibilityChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public event EventHandler? VisibilityChanged;

    public CadRenderEntityTypeRowViewModel(string entityType, int count, bool isVisible)
    {
        EntityType = entityType ?? throw new ArgumentNullException(nameof(entityType));
        Count = count;
        _isVisible = isVisible;
    }
}
