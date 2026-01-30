namespace ACadInspector.Core;

public readonly record struct CadPropertyDescriptor(
    string Name,
    Type PropertyType,
    bool CanWrite,
    int[] DxfCodes,
    string? DxfReferenceType,
    Func<object, object?> Getter,
    Action<object, object?>? Setter
);
