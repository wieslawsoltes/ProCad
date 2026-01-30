using System;
using System.Collections.Generic;
using ACadInspector.ViewModels;
using ACadSharp;
using ACadSharp.Classes;
using ACadSharp.Header;
using ReactiveUI;
using ReactiveUI.SourceGenerators;

namespace ACadInspector.Services;

public sealed partial class CadDocumentContextService : ReactiveObject
{
    private readonly Dictionary<CadDocument, CadDocumentViewModel> _documents =
        new(ReferenceEqualityComparer.Instance);

    [Reactive]
    public partial CadDocumentViewModel? ActiveDocument { get; set; }

    public void Register(CadDocumentViewModel viewModel)
    {
        _documents[viewModel.Document] = viewModel;
        ActiveDocument = viewModel;
    }

    public bool TryGetViewModel(CadDocument document, out CadDocumentViewModel viewModel)
    {
        return _documents.TryGetValue(document, out viewModel!);
    }

    public CadDocumentViewModel? ResolveViewModel(object? selection)
    {
        var document = ResolveDocument(selection);
        if (document is not null && _documents.TryGetValue(document, out var viewModel))
        {
            return viewModel;
        }

        return ActiveDocument;
    }

    public CadDocument? ResolveDocument(object? selection)
    {
        switch (selection)
        {
            case CadDocument document:
                return document;
            case CadObject cadObject when cadObject.Document is not null:
                return cadObject.Document;
            case CadHeader header when header.Document is not null:
                return header.Document;
        }

        var active = ActiveDocument?.Document;
        if (active is null)
        {
            return null;
        }

        if (selection is CadSummaryInfo summary && ReferenceEquals(summary, active.SummaryInfo))
        {
            return active;
        }

        if (selection is DxfClass dxfClass && ContainsClass(active.Classes, dxfClass))
        {
            return active;
        }

        return null;
    }

    public bool TrySetActiveFromSelection(object? selection)
    {
        var document = ResolveDocument(selection);
        if (document is null)
        {
            return false;
        }

        if (_documents.TryGetValue(document, out var viewModel))
        {
            ActiveDocument = viewModel;
            return true;
        }

        return false;
    }

    public IReadOnlyList<CadDocument> GetDocuments()
    {
        if (_documents.Count == 0)
        {
            return Array.Empty<CadDocument>();
        }

        var list = new List<CadDocument>(_documents.Count);
        foreach (var entry in _documents.Keys)
        {
            list.Add(entry);
        }

        return list;
    }

    private static bool ContainsClass(DxfClassCollection? classes, DxfClass target)
    {
        if (classes is null)
        {
            return false;
        }

        foreach (var entry in classes)
        {
            if (ReferenceEquals(entry, target))
            {
                return true;
            }
        }

        return false;
    }
}
