using System.Collections.Generic;

namespace ACadInspector.Core;

public static partial class CadHeaderMetadataRegistry
{
    private static readonly List<CadHeaderVariableDescriptor> VariablesInternal = new();

    static CadHeaderMetadataRegistry()
    {
        Build();
    }

    public static IReadOnlyList<CadHeaderVariableDescriptor> Variables => VariablesInternal;

    static partial void Build();
}
