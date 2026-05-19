using System.IO;
using Microsoft.CodeAnalysis;

namespace ProCad.Scripting;

public sealed class DisabledSourceReferenceResolver : SourceReferenceResolver
{
    public override string NormalizePath(string path, string? baseFilePath)
    {
        return path;
    }

    public override Stream OpenRead(string resolvedPath)
    {
        throw new FileNotFoundException("Script source loading is disabled.", resolvedPath);
    }

    public override string? ResolveReference(string path, string? baseFilePath)
    {
        return null;
    }

    public override bool Equals(object? other)
    {
        return other is DisabledSourceReferenceResolver;
    }

    public override int GetHashCode()
    {
        return typeof(DisabledSourceReferenceResolver).GetHashCode();
    }
}
