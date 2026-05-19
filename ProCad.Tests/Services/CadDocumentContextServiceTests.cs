using System.Diagnostics;
using System.Collections.Generic;
using ProCad.Core;
using ProCad.Services;
using ProCad.ViewModels;
using ACadSharp;
using ACadSharp.Entities;
using CSMath;
using Xunit;

namespace ProCad.Tests.Services;

public sealed class CadDocumentContextServiceTests
{
    [Fact]
    public void RegisterUnregister_MultiDocumentRecoveryLoop_CompletesWithinBudget()
    {
        const int documentCount = 250;
        const int budgetMilliseconds = 600;
        var context = new CadDocumentContextService();
        var viewModels = new List<CadDocumentViewModel>(documentCount);
        for (var index = 0; index < documentCount; index++)
        {
            viewModels.Add(new CadDocumentViewModel(
                new CadDocument(),
                CadFileFormat.Dxf,
                path: null,
                displayName: $"Doc-{index}",
                render: null!));
        }

        var stopwatch = Stopwatch.StartNew();
        for (var index = 0; index < viewModels.Count; index++)
        {
            context.Register(viewModels[index]);
        }

        for (var index = viewModels.Count - 1; index >= 0; index--)
        {
            context.Unregister(viewModels[index].Document);
        }

        stopwatch.Stop();
        Assert.Empty(context.GetDocuments());
        Assert.Null(context.ActiveDocument);
        Assert.True(
            stopwatch.ElapsedMilliseconds <= budgetMilliseconds,
            $"Document context recovery budget exceeded: {stopwatch.ElapsedMilliseconds} ms > {budgetMilliseconds} ms.");
    }

    [Fact]
    public void ResolveDocument_UnregisteredEntitySelection_FallsBackToActiveDocument()
    {
        var activeDocument = new CadDocument();
        activeDocument.Entities.Add(new Line(new XYZ(0d, 0d, 0d), new XYZ(1d, 0d, 0d)));

        var foreignDocument = new CadDocument();
        var foreignLine = new Line(new XYZ(10d, 0d, 0d), new XYZ(11d, 0d, 0d));
        foreignDocument.Entities.Add(foreignLine);

        var context = new CadDocumentContextService();
        var activeViewModel = new CadDocumentViewModel(activeDocument, CadFileFormat.Dxf, path: null, displayName: "Active", render: null!);
        context.Register(activeViewModel);

        var resolved = context.ResolveDocument(foreignLine);

        Assert.Same(activeDocument, resolved);
    }

    [Fact]
    public void Unregister_RaisesDocumentUnregisteredEvent_AndUpdatesActiveDocument()
    {
        var first = new CadDocument();
        var second = new CadDocument();
        var context = new CadDocumentContextService();
        var firstViewModel = new CadDocumentViewModel(first, CadFileFormat.Dxf, path: null, displayName: "First", render: null!);
        var secondViewModel = new CadDocumentViewModel(second, CadFileFormat.Dxf, path: null, displayName: "Second", render: null!);
        context.Register(firstViewModel);
        context.Register(secondViewModel);

        CadDocument? unregistered = null;
        context.DocumentUnregistered += (_, args) => unregistered = args.Document;

        var removed = context.Unregister(second);

        Assert.True(removed);
        Assert.Same(second, unregistered);
        Assert.Same(firstViewModel, context.ActiveDocument);
        Assert.Single(context.GetDocuments());
    }
}
