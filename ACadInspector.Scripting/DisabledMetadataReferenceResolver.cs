using System.Collections.Immutable;
using Microsoft.CodeAnalysis;

namespace ACadInspector.Scripting;

public sealed class DisabledMetadataReferenceResolver : MetadataReferenceResolver
{
    public override bool ResolveMissingAssemblies => false;

    public override PortableExecutableReference? ResolveMissingAssembly(
        MetadataReference definition,
        AssemblyIdentity referenceIdentity)
    {
        return null;
    }

    public override ImmutableArray<PortableExecutableReference> ResolveReference(
        string reference,
        string? baseFilePath,
        MetadataReferenceProperties properties)
    {
        return ImmutableArray<PortableExecutableReference>.Empty;
    }

    public override bool Equals(object? other)
    {
        return other is DisabledMetadataReferenceResolver;
    }

    public override int GetHashCode()
    {
        return typeof(DisabledMetadataReferenceResolver).GetHashCode();
    }
}
