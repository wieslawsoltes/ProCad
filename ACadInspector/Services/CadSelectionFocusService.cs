using ReactiveUI;
using ReactiveUI.SourceGenerators;

namespace ACadInspector.Services;

public sealed partial class CadSelectionFocusService : ReactiveObject
{
    [Reactive]
    public partial CadSelectionFocusRequest? FocusRequest { get; private set; }

    public void RequestFocus(object? target)
    {
        if (target is null)
        {
            FocusRequest = null;
            return;
        }

        FocusRequest = new CadSelectionFocusRequest(target);
    }
}

public sealed class CadSelectionFocusRequest
{
    public object Target { get; }

    public CadSelectionFocusRequest(object target)
    {
        Target = target;
    }
}
