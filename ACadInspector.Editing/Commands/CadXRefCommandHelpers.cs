using ACadInspector.Editing.Selection;
using ACadInspector.Editing.Sessions;
using ACadSharp;
using ACadSharp.Blocks;
using ACadSharp.Entities;
using ACadSharp.Tables;

namespace ACadInspector.Editing.Commands;

internal static class CadXRefCommandHelpers
{
    public static bool TryResolveXRefBlock(
        CadDocumentSession session,
        IReadOnlyList<string> arguments,
        string usage,
        out BlockRecord block,
        out string error)
    {
        block = null!;
        error = string.Empty;

        if (TryResolveBlockFromArguments(session.Document, arguments, out block))
        {
            return ValidateXRefBlock(block, out error);
        }

        if (TryResolveBlockFromSelection(session.SelectionSet, session.Document, out block))
        {
            return ValidateXRefBlock(block, out error);
        }

        error = usage;
        return false;
    }

    public static int RemoveInsertReferences(CadDocumentSession session, BlockRecord block)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(block);

        var removed = 0;
        removed += RemoveInsertReferencesFromCollection(session, session.Document.Entities, block);

        if (session.Document.BlockRecords is null)
        {
            return removed;
        }

        foreach (var owner in session.Document.BlockRecords)
        {
            removed += RemoveInsertReferencesFromCollection(session, owner.Entities, block);
        }

        return removed;
    }

    public static void BindToLocalBlock(BlockRecord block)
    {
        ArgumentNullException.ThrowIfNull(block);

        block.IsUnloaded = false;
        block.BlockEntity.XRefPath = string.Empty;

        var flags = block.Flags;
        flags &= ~BlockTypeFlags.XRef;
        flags &= ~BlockTypeFlags.XRefOverlay;
        flags &= ~BlockTypeFlags.XRefDependent;
        flags &= ~BlockTypeFlags.XRefResolved;
        block.Flags = flags;
    }

    public static bool IsXRef(BlockRecord block)
    {
        ArgumentNullException.ThrowIfNull(block);
        var flags = block.Flags;
        return flags.HasFlag(BlockTypeFlags.XRef) ||
            flags.HasFlag(BlockTypeFlags.XRefOverlay) ||
            flags.HasFlag(BlockTypeFlags.XRefDependent) ||
            flags.HasFlag(BlockTypeFlags.XRefResolved);
    }

    private static int RemoveInsertReferencesFromCollection(
        CadDocumentSession session,
        CadObjectCollection<Entity> entities,
        BlockRecord block)
    {
        var inserts = entities
            .OfType<Insert>()
            .Where(insert => ReferencesBlock(insert, block))
            .ToArray();
        if (inserts.Length == 0)
        {
            return 0;
        }

        for (var index = 0; index < inserts.Length; index++)
        {
            var insert = inserts[index];
            entities.Remove(insert);
            session.EntityIndex.Unregister(insert);
            session.SetSelection([insert], CadSelectionMode.Remove);
        }

        return inserts.Length;
    }

    private static bool TryResolveBlockFromArguments(
        CadDocument document,
        IReadOnlyList<string> arguments,
        out BlockRecord block)
    {
        block = null!;
        if (arguments.Count == 0 || document.BlockRecords is null)
        {
            return false;
        }

        var name = NormalizeBlockToken(arguments[0]);
        return !string.IsNullOrWhiteSpace(name) &&
            document.BlockRecords.TryGetValue(name, out block!);
    }

    private static bool TryResolveBlockFromSelection(
        CadSelectionSet selectionSet,
        CadDocument document,
        out BlockRecord block)
    {
        block = null!;
        if (document.BlockRecords is null)
        {
            return false;
        }

        foreach (var item in selectionSet.Items)
        {
            switch (item)
            {
                case BlockRecord selectedBlock:
                    block = selectedBlock;
                    return true;
                case Insert insert when insert.Block is not null:
                    if (document.BlockRecords.TryGetValue(insert.Block.Name, out var resolved))
                    {
                        block = resolved;
                        return true;
                    }

                    block = insert.Block;
                    return true;
            }
        }

        return false;
    }

    private static bool ValidateXRefBlock(
        BlockRecord block,
        out string error)
    {
        if (!IsXRef(block))
        {
            error = $"Block '{block.Name}' is not an external reference.";
            return false;
        }

        error = string.Empty;
        return true;
    }

    private static bool ReferencesBlock(Insert insert, BlockRecord block)
    {
        var candidate = insert.Block;
        return candidate is not null &&
            string.Equals(candidate.Name, block.Name, StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeBlockToken(string token)
    {
        return token.Trim().Trim('"');
    }
}
