using System;
using System.Diagnostics;
using System.Threading.Tasks;
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
using Avalonia.Controls.DataGridHierarchical;
using CSMath;
using Xunit;

namespace ProCad.Tests.ViewModels;

public sealed class CadDocumentTreeViewModelTests
{
    [Fact]
    public async Task NewEntities_AreResolvableInTree_AndPropertyGridAfterSessionRefresh()
    {
        var document = new CadDocument();
        var selectionService = new CadSelectionService();
        var documentContext = new CadDocumentContextService();
        var focusService = new CadSelectionFocusService();
        var sessionHost = new CadEditorSessionHostService(new CadEditorSessionFactory(), documentContext, selectionService);
        var blockEditorService = new CadBlockEditorService(documentContext, new CadDocumentDockService(), null!);
        var tree = new CadDocumentTreeViewModel(
            selectionService,
            documentContext,
            focusService,
            blockEditorService,
            sessionHost,
            new FastPathDiagnosticsService());
        var propertyGrid = new PropertyGridViewModel(
            new CadPropertyEditPipeline(Array.Empty<ICadPropertyValidator>()),
            new RenderCacheStampProvider(),
            selectionService,
            blockEditorService,
            new CadDynamicBlockOverrideService(),
            new FastPathDiagnosticsService())
        {
            IsActive = true
        };

        var documentViewModel = new CadDocumentViewModel(
            document,
            CadFileFormat.Dxf,
            path: null,
            displayName: "Test",
            render: null!);
        tree.LoadDocument(documentViewModel);

        var session = sessionHost.GetOrCreate(document);
        var entityId = CadEntityId.New();
        var create = CadOperationPayloadCodec
            .CreateLine(entityId, new XYZ(1d, 2d, 0d), new XYZ(5d, 2d, 0d))
            .WithCurrentProperties(document);
        var batch = CadOperationBatch.Create(
            actorId: session.SessionId.Value,
            baseVersion: session.Revision,
            sequence: 1,
            operations: [create]);
        session.Apply(batch);
        sessionHost.NotifySessionChanged(session);

        Assert.True(session.EntityIndex.TryGetEntity(entityId, out var createdEntity));
        selectionService.SelectedObject = createdEntity;

        await WaitUntilAsync(() => ReferenceEquals(ResolveSelectedSource(tree.SelectedItem), createdEntity));

        Assert.Same(createdEntity, propertyGrid.SelectedObject);
        Assert.NotEmpty(propertyGrid.Rows);
    }

    private static object? ResolveSelectedSource(object? selected)
    {
        return selected switch
        {
            CadDocumentTreeNode node => node.Source,
            HierarchicalNode hierarchical when hierarchical.Item is CadDocumentTreeNode node => node.Source,
            _ => null
        };
    }

    private static async Task WaitUntilAsync(Func<bool> condition, int timeoutMs = 2000)
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

        Assert.True(condition(), "Timed out while waiting for tree/property synchronization.");
    }
}
