using ACadInspector.Editing.Identifiers;
using ACadInspector.Editing.Commands;
using ACadInspector.Editing.Operations;
using ACadInspector.Editing.Sessions;
using ACadSharp;
using ACadSharp.Entities;
using ACadSharp.Tables;
using CSMath;

namespace ACadInspector.Editing.Clipboard;

public static class CadClipboardDependencyResolver
{
    public static void EnsureDependencies(CadDocument document, CadClipboardDependencies? dependencies)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (dependencies is null)
        {
            return;
        }

        EnsureLayers(document, dependencies.LayerNames);
        EnsureLineTypes(document, dependencies.LineTypeNames);
        EnsureTextStyles(document, dependencies.TextStyleNames);
        EnsureDimensionStyles(document, dependencies.DimensionStyleNames);
        EnsureBlocks(document, dependencies.BlockDependencies);
    }

    private static void EnsureLayers(CadDocument document, IReadOnlyList<string> names)
    {
        for (var index = 0; index < names.Count; index++)
        {
            var name = names[index];
            if (!string.IsNullOrWhiteSpace(name))
            {
                CadEntityPropertyCodec.ResolveLayer(document, name.Trim());
            }
        }
    }

    private static void EnsureLineTypes(CadDocument document, IReadOnlyList<string> names)
    {
        for (var index = 0; index < names.Count; index++)
        {
            var name = names[index];
            if (!string.IsNullOrWhiteSpace(name))
            {
                CadEntityPropertyCodec.ResolveLineType(document, name.Trim());
            }
        }
    }

    private static void EnsureTextStyles(CadDocument document, IReadOnlyList<string> names)
    {
        for (var index = 0; index < names.Count; index++)
        {
            var name = names[index];
            if (!string.IsNullOrWhiteSpace(name))
            {
                CadEntityPropertyCodec.ResolveTextStyle(document, name.Trim());
            }
        }
    }

    private static void EnsureDimensionStyles(CadDocument document, IReadOnlyList<string> names)
    {
        for (var index = 0; index < names.Count; index++)
        {
            var name = names[index];
            if (!string.IsNullOrWhiteSpace(name))
            {
                CadEntityPropertyCodec.ResolveDimensionStyle(document, name.Trim());
            }
        }
    }

    private static void EnsureBlocks(CadDocument document, IReadOnlyList<CadClipboardBlockDependency> blocks)
    {
        if (document.BlockRecords is null || blocks.Count == 0)
        {
            return;
        }

        for (var index = 0; index < blocks.Count; index++)
        {
            var dependency = blocks[index];
            if (string.IsNullOrWhiteSpace(dependency.Name))
            {
                continue;
            }

            var blockName = dependency.Name.Trim();
            if (IsSystemBlockName(blockName))
            {
                continue;
            }

            if (!document.BlockRecords.TryGetValue(blockName, out var block))
            {
                block = new BlockRecord(blockName);
                document.BlockRecords.Add(block);
            }

            if (block.Entities.Count == 0 && dependency.Entities.Count > 0)
            {
                TryHydrateBlockEntities(document, block, dependency.Entities);
            }
        }
    }

    private static void TryHydrateBlockEntities(
        CadDocument document,
        BlockRecord block,
        IReadOnlyList<CadClipboardEntity> clipboardEntities)
    {
        if (clipboardEntities.Count == 0)
        {
            return;
        }

        var session = (CadDocumentSession)new CadEditorSessionFactory().Create(document);
        var operations = new List<CadOperation>(clipboardEntities.Count);
        var createdIds = new List<CadEntityId>(clipboardEntities.Count);

        for (var index = 0; index < clipboardEntities.Count; index++)
        {
            var id = CadEntityId.New();
            if (!CadClipboardEntityCodec.TryDecodeCreateOperation(
                    clipboardEntities[index],
                    id,
                    XYZ.Zero,
                    out var createOperation,
                    out _))
            {
                continue;
            }

            operations.Add(createOperation);
            createdIds.Add(id);
        }

        if (operations.Count == 0)
        {
            return;
        }

        var batch = CadOperationBatch.Create(
            actorId: session.SessionId.Value,
            baseVersion: session.Revision,
            sequence: 1,
            operations: operations);
        session.Apply(batch);

        for (var index = 0; index < createdIds.Count; index++)
        {
            if (!session.EntityIndex.TryGetEntity(createdIds[index], out var entity))
            {
                continue;
            }

            if (entity.Owner is BlockRecord owner)
            {
                owner.Entities.Remove(entity);
            }
            else
            {
                document.Entities.Remove(entity);
            }

            block.Entities.Add(entity);
        }
    }

    private static bool IsSystemBlockName(string blockName)
    {
        return blockName.StartsWith("*", StringComparison.Ordinal);
    }
}
