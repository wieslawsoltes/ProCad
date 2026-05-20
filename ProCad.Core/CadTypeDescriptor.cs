namespace ProCad.Core;

public sealed record CadTypeDescriptor(
    Type Type,
    IReadOnlyList<CadPropertyDescriptor> Properties
);
