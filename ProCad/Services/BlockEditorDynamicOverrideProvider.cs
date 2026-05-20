using ProCad.Rendering;
using ACadSharp.Entities;
using ACadSharp.Tables;

namespace ProCad.Services;

public sealed class BlockEditorDynamicOverrideProvider : IDynamicBlockOverrideProvider
{
    public DynamicBlockOverrideSet? Overrides { get; set; }

    public DynamicBlockOverrideSet? GetBlockOverrides(BlockRecord block)
    {
        return Overrides;
    }

    public DynamicBlockOverrideSet? GetInsertOverrides(Insert insert, BlockRecord block)
    {
        return null;
    }
}
