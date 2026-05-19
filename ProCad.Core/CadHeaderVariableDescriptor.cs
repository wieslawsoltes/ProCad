using System;
using ACadSharp.Header;

namespace ProCad.Core;

public sealed class CadHeaderVariableDescriptor
{
    public CadHeaderVariableDescriptor(
        string variableName,
        string propertyName,
        Type propertyType,
        bool canWrite,
        int[] dxfCodes,
        string? referenceType,
        bool isName,
        Func<CadHeader, object?> getter,
        Action<CadHeader, object?>? setter)
    {
        VariableName = variableName;
        PropertyName = propertyName;
        PropertyType = propertyType;
        CanWrite = canWrite;
        DxfCodes = dxfCodes;
        ReferenceType = referenceType;
        IsName = isName;
        Getter = getter;
        Setter = setter;
    }

    public string VariableName { get; }

    public string PropertyName { get; }

    public Type PropertyType { get; }

    public bool CanWrite { get; }

    public int[] DxfCodes { get; }

    public string? ReferenceType { get; }

    public bool IsName { get; }

    public Func<CadHeader, object?> Getter { get; }

    public Action<CadHeader, object?>? Setter { get; }
}
