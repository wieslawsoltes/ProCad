namespace ProCad.Core;

public readonly record struct CadPropertyEditContext(
    object Target,
    CadPropertyDescriptor Descriptor,
    object? Value
);
