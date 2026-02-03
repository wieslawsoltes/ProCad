using ACadSharp.Entities;

namespace ACadInspector.Rendering;

/// <summary>
/// Describes the source and logical owner of a render primitive.
/// </summary>
public readonly struct RenderPrimitiveMetadata
{
    public Entity? SourceEntity { get; }
    public Entity? OwnerEntity { get; }

    public RenderPrimitiveMetadata(Entity? sourceEntity, Entity? ownerEntity)
    {
        SourceEntity = sourceEntity;
        OwnerEntity = ownerEntity;
    }
}
