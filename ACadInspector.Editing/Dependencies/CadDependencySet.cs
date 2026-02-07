using ACadSharp.Tables;

namespace ACadInspector.Editing.Dependencies;

public sealed record CadDependencySet(
    IReadOnlyCollection<Layer> Layers,
    IReadOnlyCollection<LineType> LineTypes,
    IReadOnlyCollection<TextStyle> TextStyles,
    IReadOnlyCollection<BlockRecord> Blocks);
