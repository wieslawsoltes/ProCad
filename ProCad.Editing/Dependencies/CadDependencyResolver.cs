using ACadSharp.Entities;
using ACadSharp.Tables;

namespace ProCad.Editing.Dependencies;

public sealed class CadDependencyResolver : ICadDependencyResolver
{
    public CadDependencySet CollectForCopy(IEnumerable<Entity> entities)
    {
        ArgumentNullException.ThrowIfNull(entities);

        var layers = new HashSet<Layer>(System.Collections.Generic.ReferenceEqualityComparer.Instance);
        var lineTypes = new HashSet<LineType>(System.Collections.Generic.ReferenceEqualityComparer.Instance);
        var textStyles = new HashSet<TextStyle>(System.Collections.Generic.ReferenceEqualityComparer.Instance);
        var blocks = new HashSet<BlockRecord>(System.Collections.Generic.ReferenceEqualityComparer.Instance);

        foreach (var entity in entities)
        {
            if (entity is null)
            {
                continue;
            }

            layers.Add(entity.Layer);
            lineTypes.Add(entity.LineType);

            switch (entity)
            {
                case Insert insert when insert.Block is not null:
                    blocks.Add(insert.Block);
                    break;
                case TextEntity text when text.Style is not null:
                    textStyles.Add(text.Style);
                    break;
                case MText mtext when mtext.Style is not null:
                    textStyles.Add(mtext.Style);
                    break;
            }
        }

        return new CadDependencySet(
            layers.ToArray(),
            lineTypes.ToArray(),
            textStyles.ToArray(),
            blocks.ToArray());
    }
}
