using System.Diagnostics;
using ProCad.Core;
using ProCad.Diagnostics;
using ProCad.Editing.Identifiers;
using ProCad.Editing.Operations;
using ProCad.Editing.Sessions;
using ProCad.Rendering;
using ProCad.Services;
using ProCad.ViewModels;
using ACadSharp;
using ACadSharp.Entities;
using CSMath;
using Xunit;

namespace ProCad.Tests.ViewModels;

public sealed class ProCadSelectionSyncMatrixTests
{
    [Fact]
    public async Task SelectionAndSessionRevision_KeepTreePropertiesDxfPanelsInSync()
    {
        var document = new CadDocument();
        var baseLine = new Line(new XYZ(0d, 0d, 0d), new XYZ(5d, 0d, 0d));
        document.Entities.Add(baseLine);

        var selection = new CadSelectionService();
        var documentContext = new CadDocumentContextService();
        var sessionHost = new CadEditorSessionHostService(new CadEditorSessionFactory(), documentContext, selection);
        var focusService = new CadSelectionFocusService();
        var blockEditorService = new CadBlockEditorService(documentContext, new CadDocumentDockService(), null!);

        var tree = new CadDocumentTreeViewModel(
            selection,
            documentContext,
            focusService,
            blockEditorService,
            sessionHost,
            new FastPathDiagnosticsService());
        var propertyGrid = new PropertyGridViewModel(
            new CadPropertyEditPipeline(Array.Empty<ICadPropertyValidator>()),
            new RenderCacheStampProvider(),
            selection,
            blockEditorService,
            new CadDynamicBlockOverrideService(),
            new FastPathDiagnosticsService())
        {
            IsActive = true
        };
        var dxfSemantics = new CadDxfSemanticsViewModel(selection, new FastPathDiagnosticsService())
        {
            IsActive = true
        };
        var dxfRaw = new CadDxfRawViewModel(selection, documentContext)
        {
            IsActive = true
        };
        var dwgSemantics = new CadDwgSemanticsViewModel(selection, documentContext, new FastPathDiagnosticsService())
        {
            IsActive = true
        };

        var documentViewModel = new CadDocumentViewModel(
            document,
            CadFileFormat.Dxf,
            path: null,
            displayName: "SyncDoc",
            render: null!);
        documentContext.Register(documentViewModel);
        tree.LoadDocument(documentViewModel);

        var session = (CadDocumentSession)sessionHost.GetOrCreate(document);
        selection.SelectedObject = baseLine;
        await WaitUntilAsync(() =>
            ReferenceEquals(propertyGrid.SelectedObject, baseLine) &&
            dxfSemantics.PropertyRowsView.Count > 0 &&
            dwgSemantics.HeaderRowsView.Count > 0 &&
            !string.IsNullOrWhiteSpace(dxfRaw.RawDxfDocument.Text));

        var createId = CadEntityId.New();
        var createLine = CadOperationPayloadCodec
            .CreateLine(createId, new XYZ(10d, 1d, 0d), new XYZ(16d, 1d, 0d))
            .WithCurrentProperties(document);
        var createBatch = CadOperationBatch.Create(
            actorId: session.SessionId.Value,
            baseVersion: session.Revision,
            sequence: 1,
            operations: [createLine]);
        session.Apply(createBatch);
        sessionHost.NotifySessionChanged(session);

        Assert.True(session.EntityIndex.TryGetEntity(createId, out var createdEntity));
        selection.SelectedObject = createdEntity;
        await WaitUntilAsync(() =>
            ReferenceEquals(propertyGrid.SelectedObject, createdEntity) &&
            ReferenceEquals(ResolveTreeSource(tree.SelectedItem), createdEntity));

        var movedLine = Assert.IsType<Line>(createdEntity);
        var startBeforeMove = movedLine.StartPoint;

        var commandRegistry = new ProCad.Editing.Commands.CadCommandRegistry();
        commandRegistry.Register(new ProCad.Editing.Commands.MoveCadCommand());
        commandRegistry.Register(new ProCad.Editing.Commands.UndoCadCommand());
        commandRegistry.Register(new ProCad.Editing.Commands.RedoCadCommand());
        var move = await commandRegistry.ExecuteAsync($"MOVE 4,0 {createdEntity.Handle:X}", session);
        Assert.True(move.Success);
        sessionHost.NotifySessionChanged(session);
        await WaitUntilAsync(() => Math.Abs(movedLine.StartPoint.X - (startBeforeMove.X + 4d)) < 1e-6);
        Assert.Same(createdEntity, propertyGrid.SelectedObject);
        Assert.True(dxfSemantics.PropertyRowsView.Count > 0);
        Assert.False(string.IsNullOrWhiteSpace(dxfRaw.RawDxfDocument.Text));

        var startAfterMove = movedLine.StartPoint;
        var undo = await commandRegistry.ExecuteAsync("UNDO", session);
        Assert.True(undo.Success);
        sessionHost.NotifySessionChanged(session);
        await WaitUntilAsync(() =>
            Math.Abs(movedLine.StartPoint.X - startBeforeMove.X) < 1e-6 &&
            Math.Abs(movedLine.StartPoint.Y - startBeforeMove.Y) < 1e-6);

        var redo = await commandRegistry.ExecuteAsync("REDO", session);
        Assert.True(redo.Success);
        sessionHost.NotifySessionChanged(session);
        await WaitUntilAsync(() =>
            Math.Abs(movedLine.StartPoint.X - startAfterMove.X) < 1e-6 &&
            Math.Abs(movedLine.StartPoint.Y - startAfterMove.Y) < 1e-6);

        Assert.True(session.EntityIndex.TryGetId((Entity)createdEntity, out var createdId));
        var remoteBatch = CadOperationBatch.Create(
            actorId: Guid.NewGuid(),
            baseVersion: session.Revision,
            sequence: 7,
            operations:
            [
                CadOperationPayloadCodec.TransformLine(
                    createdId,
                    movedLine.StartPoint,
                    movedLine.EndPoint,
                    new XYZ(movedLine.StartPoint.X, movedLine.StartPoint.Y + 3d, movedLine.StartPoint.Z),
                    new XYZ(movedLine.EndPoint.X, movedLine.EndPoint.Y + 3d, movedLine.EndPoint.Z))
            ]);
        session.Apply(remoteBatch);
        sessionHost.NotifySessionChanged(session);
        await WaitUntilAsync(() =>
            Math.Abs(movedLine.StartPoint.Y - (startAfterMove.Y + 3d)) < 1e-6 &&
            dxfSemantics.SelectedTitle.Contains("LINE", StringComparison.OrdinalIgnoreCase));

        var deleteBatch = CadOperationBatch.Create(
            actorId: session.SessionId.Value,
            baseVersion: session.Revision,
            sequence: 8,
            operations:
            [
                CadOperationPayloadCodec.DeleteLine(
                    createdId,
                    movedLine.StartPoint,
                    movedLine.EndPoint)
            ]);
        session.Apply(deleteBatch);
        sessionHost.NotifySessionChanged(session);
        await WaitUntilAsync(() =>
            selection.SelectedObject is null &&
            propertyGrid.SelectedObject is null &&
            dxfSemantics.SelectedTitle == "No selection" &&
            dxfRaw.SelectedTitle == "No selection");
    }

    private static object? ResolveTreeSource(object? selected)
    {
        return selected switch
        {
            CadDocumentTreeNode node => node.Source,
            Avalonia.Controls.DataGridHierarchical.HierarchicalNode hierarchical when hierarchical.Item is CadDocumentTreeNode node => node.Source,
            _ => null
        };
    }

    private static async Task WaitUntilAsync(Func<bool> condition, int timeoutMs = 3000)
    {
        var stopwatch = Stopwatch.StartNew();
        while (stopwatch.ElapsedMilliseconds < timeoutMs)
        {
            if (condition())
            {
                return;
            }

            await Task.Delay(25);
        }

        Assert.True(condition(), "Timed out waiting for panel synchronization.");
    }
}
