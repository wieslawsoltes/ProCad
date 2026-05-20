using System;
using ProCad.ViewModels;
using ACadSharp.Blocks;
using ACadSharp.Entities;
using ACadSharp.Tables;

namespace ProCad.Services;

public sealed class CadBlockEditorService
{
    private readonly CadDocumentContextService _documentContext;
    private readonly CadDocumentDockService _dockService;
    private readonly CadBlockEditorViewModelFactory _factory;

    public CadBlockEditorService(
        CadDocumentContextService documentContext,
        CadDocumentDockService dockService,
        CadBlockEditorViewModelFactory factory)
    {
        _documentContext = documentContext;
        _dockService = dockService;
        _factory = factory;
    }

    public bool CanOpen(object? selection)
    {
        return TryResolveBlock(selection, out _, out _);
    }

    public bool TryOpenBlockEditor(object? selection)
    {
        if (!TryResolveBlock(selection, out var block, out var documentViewModel))
        {
            return false;
        }

        if (block is null)
        {
            return false;
        }

        return TryOpenBlockEditor(block, documentViewModel);
    }

    public bool TryOpenBlockEditor(BlockRecord block, CadDocumentViewModel? documentViewModel)
    {
        if (block is null)
        {
            throw new ArgumentNullException(nameof(block));
        }

        documentViewModel ??= _documentContext.ResolveViewModel(block);
        if (documentViewModel is null)
        {
            return false;
        }

        if (_dockService.TryActivateDocument(dockable =>
            dockable is CadBlockEditorViewModel editor &&
            ReferenceEquals(editor.Block, block)))
        {
            return true;
        }

        var editorViewModel = _factory.Create(documentViewModel, block);
        return _dockService.TryAddDocument(editorViewModel);
    }

    private bool TryResolveBlock(
        object? selection,
        out BlockRecord? block,
        out CadDocumentViewModel? documentViewModel)
    {
        documentViewModel = _documentContext.ResolveViewModel(selection);
        block = ResolveBlock(selection, documentViewModel?.Document);
        return block is not null;
    }

    private static BlockRecord? ResolveBlock(object? selection, ACadSharp.CadDocument? document)
    {
        switch (selection)
        {
            case CadBlockRowViewModel row:
                return row.Block;
            case CadBlockEditorViewModel editor:
                return editor.Block;
            case CadDocumentTreeNode node:
                return ResolveBlock(node.Source, document);
            case BlockRecord record:
                return record;
            case Block blockEntity:
                return blockEntity.BlockOwner;
            case AttributeDefinition attributeDefinition:
                return attributeDefinition.Owner as BlockRecord;
            case AttributeEntity attributeEntity:
                return attributeEntity.Owner as BlockRecord;
            case Insert insert:
                return ResolveInsertBlock(insert, document);
            case Entity entity when entity.Owner is BlockRecord owner:
                return owner;
        }

        return null;
    }

    private static BlockRecord? ResolveInsertBlock(Insert insert, ACadSharp.CadDocument? document)
    {
        var block = insert.Block;
        if (block is null)
        {
            return null;
        }

        if (document?.BlockRecords is not null &&
            document.BlockRecords.TryGetValue(block.Name, out var resolved))
        {
            return resolved;
        }

        return block;
    }
}
