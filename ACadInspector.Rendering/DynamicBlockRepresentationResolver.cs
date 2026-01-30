using ACadSharp.Entities;
using ACadSharp.Objects;

namespace ACadInspector.Rendering;

internal sealed class DynamicBlockRepresentationInfo
{
    public CadDictionary RepresentationDictionary { get; }
    public BlockRepresentationData? RepresentationData { get; }
    public CadDictionary? EnhancedBlockData { get; }
    public DynamicBlockPropertySet? Properties { get; }

    public DynamicBlockRepresentationInfo(
        CadDictionary representationDictionary,
        BlockRepresentationData? representationData,
        CadDictionary? enhancedBlockData,
        DynamicBlockPropertySet? properties)
    {
        RepresentationDictionary = representationDictionary;
        RepresentationData = representationData;
        EnhancedBlockData = enhancedBlockData;
        Properties = properties;
    }
}

internal static class DynamicBlockRepresentationResolver
{
    public static DynamicBlockRepresentationInfo? Resolve(Insert insert)
    {
        if (insert?.XDictionary is null)
        {
            return null;
        }

        if (!insert.XDictionary.TryGetEntry<CadDictionary>("AcDbBlockRepresentation", out var representation))
        {
            return null;
        }

        representation.TryGetEntry<BlockRepresentationData>("AcDbRepData", out var repData);

        CadDictionary? enhancedBlockData = null;
        if (representation.TryGetEntry<CadDictionary>("AppDataCache", out var appDataCache))
        {
            appDataCache.TryGetEntry("ACAD_ENHANCEDBLOCKDATA", out enhancedBlockData);
        }

        var properties = DynamicBlockPropertySet.Create(enhancedBlockData);
        return new DynamicBlockRepresentationInfo(representation, repData, enhancedBlockData, properties);
    }
}
