namespace ACadInspector.Editing.Identifiers;

public readonly record struct CadEntityId(Guid Value)
{
    public static CadEntityId New() => new(Guid.NewGuid());
    public bool IsEmpty => Value == Guid.Empty;
    public override string ToString() => Value.ToString("D");
}

public readonly record struct CadConstraintId(Guid Value)
{
    public static CadConstraintId New() => new(Guid.NewGuid());
    public bool IsEmpty => Value == Guid.Empty;
    public override string ToString() => Value.ToString("D");
}

public readonly record struct CadDocumentSessionId(Guid Value)
{
    public static CadDocumentSessionId New() => new(Guid.NewGuid());
    public bool IsEmpty => Value == Guid.Empty;
    public override string ToString() => Value.ToString("D");
}
