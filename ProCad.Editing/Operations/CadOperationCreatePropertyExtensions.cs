using ACadSharp;
using ACadSharp.Entities;

namespace ProCad.Editing.Operations;

public static class CadOperationCreatePropertyExtensions
{
    public static CadOperation WithCurrentProperties(this CadOperation operation, CadDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        return CadOperationPayloadCodec.WithCreateProperties(operation, CadEntityCreateProperties.FromHeader(document));
    }

    public static CadOperation WithSourceProperties(this CadOperation operation, Entity source)
    {
        ArgumentNullException.ThrowIfNull(source);
        return CadOperationPayloadCodec.WithCreateProperties(operation, CadEntityCreateProperties.FromEntity(source));
    }

    public static CadOperation WithCopiedCreateProperties(this CadOperation destination, CadOperation source)
    {
        return CadOperationPayloadCodec.CopyCreateProperties(source, destination);
    }
}
