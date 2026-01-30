namespace ACadInspector.Core;

public sealed record CadTypeDescriptor(
    Type Type,
    IReadOnlyList<CadPropertyDescriptor> Properties
);
