using System.Collections.Generic;
using ReactiveUI.SourceGenerators;

namespace ACadInspector.ViewModels;

public enum CadLineTypeSegmentKind
{
    Dash,
    Space,
    Dot
}

public static class CadLineTypeSegmentKindValues
{
    public static IReadOnlyList<CadLineTypeSegmentKind> All { get; } =
    [
        CadLineTypeSegmentKind.Dash,
        CadLineTypeSegmentKind.Space,
        CadLineTypeSegmentKind.Dot
    ];
}

public sealed partial class CadLineTypeSegmentEditorRowViewModel : ViewModelBase
{
    [Reactive]
    public partial CadLineTypeSegmentKind Kind { get; set; } = CadLineTypeSegmentKind.Dash;

    [Reactive]
    public partial string Length { get; set; } = "0.5";

    [Reactive]
    public partial bool IsText { get; set; }

    [Reactive]
    public partial bool IsShape { get; set; }

    [Reactive]
    public partial string TextValue { get; set; } = string.Empty;

    [Reactive]
    public partial string ShapeNumber { get; set; } = "0";

    [Reactive]
    public partial string StyleName { get; set; } = string.Empty;

    [Reactive]
    public partial string Scale { get; set; } = "1";

    [Reactive]
    public partial string RotationDegrees { get; set; } = "0";

    [Reactive]
    public partial string OffsetX { get; set; } = "0";

    [Reactive]
    public partial string OffsetY { get; set; } = "0";

    [Reactive]
    public partial bool RotationIsAbsolute { get; set; }
}
