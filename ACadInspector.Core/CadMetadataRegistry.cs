using System.Collections.Generic;

namespace ACadInspector.Core;

public static partial class CadMetadataRegistry
{
    private static readonly Dictionary<Type, CadTypeDescriptor> TypesInternal = new();

    static CadMetadataRegistry()
    {
        Build();
    }

    public static IReadOnlyDictionary<Type, CadTypeDescriptor> Types => TypesInternal;

    static partial void Build();
}
