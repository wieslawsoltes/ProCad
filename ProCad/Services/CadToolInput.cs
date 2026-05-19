using ProCad.ViewModels;

namespace ProCad.Services;

public enum CadToolInputKind
{
    Hover,
    Select,
    ClearHover
}

public readonly struct CadToolInput
{
    public CadToolInputKind Kind { get; }
    public CadRenderHitTestRequest? HitTest { get; }

    public CadToolInput(CadToolInputKind kind, CadRenderHitTestRequest? hitTest)
    {
        Kind = kind;
        HitTest = hitTest;
    }

    public static CadToolInput Hover(CadRenderHitTestRequest request) => new(CadToolInputKind.Hover, request);

    public static CadToolInput Select(CadRenderHitTestRequest request) => new(CadToolInputKind.Select, request);

    public static CadToolInput ClearHover() => new(CadToolInputKind.ClearHover, null);
}
