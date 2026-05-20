using ProCad.Editing.Identifiers;

namespace ProCad.Editing.Operations;

public sealed record CadOperation(
    CadOperationKind Kind,
    CadEntityId? EntityId,
    IReadOnlyDictionary<string, string>? Payload = null,
    IReadOnlyList<CadOperation>? Children = null);

public sealed record CadOperationInverse(CadOperation Operation);
