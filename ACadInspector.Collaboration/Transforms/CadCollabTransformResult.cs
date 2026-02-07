using ACadInspector.Editing.Operations;

namespace ACadInspector.Collaboration.Transforms;

public sealed record CadCollabTransformResult(
    IReadOnlyList<CadOperation> Operations,
    bool RequiresResync);
