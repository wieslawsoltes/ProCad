using ProCad.Editing.Operations;

namespace ProCad.Collaboration.Transforms;

public sealed record CadCollabTransformResult(
    IReadOnlyList<CadOperation> Operations,
    bool RequiresResync);
