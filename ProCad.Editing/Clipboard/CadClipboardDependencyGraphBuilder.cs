using ProCad.Editing.Commands;
using ProCad.Editing.Operations;
using ACadSharp;
using ACadSharp.Entities;
using ACadSharp.Tables;

namespace ProCad.Editing.Clipboard;

public static class CadClipboardDependencyGraphBuilder
{
    public static CadClipboardDependencies Build(CadDocument document, IReadOnlyList<Entity> entities)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(entities);

        if (entities.Count == 0)
        {
            return CadClipboardDependencies.Empty;
        }

        var layers = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        var lineTypes = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        var textStyles = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        var dimensionStyles = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        var blocks = new Dictionary<string, CadClipboardBlockDependency>(StringComparer.OrdinalIgnoreCase);
        var visitedBlocks = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        for (var index = 0; index < entities.Count; index++)
        {
            var entity = entities[index];
            CollectEntityDependencies(entity, layers, lineTypes, textStyles, dimensionStyles);
            if (entity is Insert insert && insert.Block is not null)
            {
                CollectBlockDependency(
                    insert.Block,
                    layers,
                    lineTypes,
                    textStyles,
                    dimensionStyles,
                    blocks,
                    visitedBlocks);
            }
        }

        return new CadClipboardDependencies(
            LayerNames: layers.ToArray(),
            LineTypeNames: lineTypes.ToArray(),
            TextStyleNames: textStyles.ToArray(),
            DimensionStyleNames: dimensionStyles.ToArray(),
            BlockDependencies: blocks.Values.OrderBy(static dependency => dependency.Name, StringComparer.OrdinalIgnoreCase).ToArray());
    }

    private static void CollectBlockDependency(
        BlockRecord block,
        SortedSet<string> layers,
        SortedSet<string> lineTypes,
        SortedSet<string> textStyles,
        SortedSet<string> dimensionStyles,
        Dictionary<string, CadClipboardBlockDependency> blocks,
        HashSet<string> visitedBlocks)
    {
        if (string.IsNullOrWhiteSpace(block.Name) || !visitedBlocks.Add(block.Name))
        {
            return;
        }

        var encodedEntities = new List<CadClipboardEntity>(block.Entities.Count);
        for (var index = 0; index < block.Entities.Count; index++)
        {
            if (block.Entities[index] is not Entity entity)
            {
                continue;
            }

            CollectEntityDependencies(entity, layers, lineTypes, textStyles, dimensionStyles);
            if (CadClipboardEntityCodec.TryEncode(entity, out var encodedEntity, out _))
            {
                encodedEntities.Add(encodedEntity);
            }

            if (entity is Insert nestedInsert && nestedInsert.Block is not null)
            {
                CollectBlockDependency(
                    nestedInsert.Block,
                    layers,
                    lineTypes,
                    textStyles,
                    dimensionStyles,
                    blocks,
                    visitedBlocks);
            }
        }

        blocks[block.Name] = new CadClipboardBlockDependency(block.Name, encodedEntities);
    }

    private static void CollectEntityDependencies(
        Entity entity,
        SortedSet<string> layers,
        SortedSet<string> lineTypes,
        SortedSet<string> textStyles,
        SortedSet<string> dimensionStyles)
    {
        var properties = CadEntityCreateProperties.FromEntity(entity);
        AddIfNotWhitespace(layers, properties.LayerName);
        AddIfNotWhitespace(lineTypes, properties.LineTypeName);
        AddIfNotWhitespace(textStyles, properties.TextStyleName);
        AddIfNotWhitespace(dimensionStyles, properties.DimensionStyleName);
    }

    private static void AddIfNotWhitespace(SortedSet<string> target, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            target.Add(value.Trim());
        }
    }
}
