using System.Linq;
using System.Numerics;
using System.Reactive.Threading.Tasks;
using System.Threading;
using System.Threading.Tasks;
using ProCad.Core;
using ProCad.Editing.Commands;
using ProCad.Editing.Controllers;
using ProCad.Editing.Interaction;
using ProCad.Editing.Sessions;
using ProCad.Rendering;
using ProCad.Services;
using ProCad.Tests.Rendering;
using ProCad.ViewModels;
using ACadSharp;
using ACadSharp.Entities;
using ACadSharp.Tables;
using Xunit;

namespace ProCad.Tests.ViewModels;

public sealed class CadBlockEditorParityTests
{
    [Fact]
    public async Task BlockEditor_StartCommand_UsesActiveDocumentControllerRuntime()
    {
        var (document, documentViewModel, block, factory, controllerHost) = CreateSystem();
        var editor = factory.Create(documentViewModel, block);
        var controller = controllerHost.GetOrCreate(document);

        await editor.Render.StartCommand.Execute("LINE").ToTask();

        Assert.Equal("LINE", controller.CommandRuntime.State.ActiveCommand);
        Assert.True(controller.CommandRuntime.State.IsActive);
    }

    [Fact]
    public async Task BlockEditor_InteractiveLineFlow_CommitsThroughDocumentSession()
    {
        var (document, documentViewModel, block, factory, controllerHost) = CreateSystem();
        var editor = factory.Create(documentViewModel, block);
        var controller = controllerHost.GetOrCreate(document);

        await editor.Render.StartCommand.Execute("LINE").ToTask();
        await editor.Render.InteractionCommand.Execute(CreatePointerDown(0f, 0f)).ToTask();
        await editor.Render.InteractionCommand.Execute(CreatePointerDown(10f, 5f)).ToTask();

        Assert.True(document.Entities.OfType<Line>().Any());
        Assert.False(controller.CommandRuntime.State.IsActive);
    }

    [Fact]
    public async Task BlockEditor_InsertDrop_CommitsThroughDocumentControllerSession()
    {
        var (document, documentViewModel, block, factory, controllerHost) = CreateSystem();
        var insertBlock = new BlockRecord("PARITY_INSERT");
        insertBlock.Entities.Add(new Line(new CSMath.XYZ(0, 0, 0), new CSMath.XYZ(1, 0, 0)));
        document.BlockRecords.Add(insertBlock);
        var editor = factory.Create(documentViewModel, block);
        var controller = controllerHost.GetOrCreate(document);
        var revisionBefore = controller.Session.Revision;

        await editor.Render.InsertDroppedBlockCommand
            .Execute(new CadInsertDropRequest("PARITY_INSERT", new Vector2(7f, 3f)))
            .ToTask();

        var inserted = Assert.Single(document.Entities.OfType<Insert>());
        Assert.Equal("PARITY_INSERT", inserted.Block.Name);
        Assert.Equal(7d, inserted.InsertPoint.X, 6);
        Assert.Equal(3d, inserted.InsertPoint.Y, 6);
        Assert.True(controller.Session.Revision > revisionBefore);
        Assert.False(controller.CommandRuntime.State.IsActive);
    }

    [Fact]
    public async Task ModelSpaceAndBlockEditor_InsertDrop_UseEquivalentRuntimePath()
    {
        var (document, documentViewModel, block, factory, controllerHost) = CreateSystem();
        var insertBlock = new BlockRecord("PARITY_SHARED");
        insertBlock.Entities.Add(new Circle { Center = new CSMath.XYZ(0, 0, 0), Radius = 1d });
        document.BlockRecords.Add(insertBlock);
        var editor = factory.Create(documentViewModel, block);
        var controller = controllerHost.GetOrCreate(document);
        var revisionBefore = controller.Session.Revision;

        await documentViewModel.Render.InsertDroppedBlockCommand
            .Execute(new CadInsertDropRequest("PARITY_SHARED", new Vector2(1f, 2f)))
            .ToTask();
        await editor.Render.InsertDroppedBlockCommand
            .Execute(new CadInsertDropRequest("PARITY_SHARED", new Vector2(5f, 6f)))
            .ToTask();

        var inserted = document.Entities.OfType<Insert>()
            .Where(static entity => string.Equals(entity.Block?.Name, "PARITY_SHARED", System.StringComparison.Ordinal))
            .ToArray();
        Assert.Equal(2, inserted.Length);
        Assert.Contains(inserted, static entity =>
            System.Math.Abs(entity.InsertPoint.X - 1d) < 1e-6 &&
            System.Math.Abs(entity.InsertPoint.Y - 2d) < 1e-6);
        Assert.Contains(inserted, static entity =>
            System.Math.Abs(entity.InsertPoint.X - 5d) < 1e-6 &&
            System.Math.Abs(entity.InsertPoint.Y - 6d) < 1e-6);
        Assert.True(controller.Session.Revision > revisionBefore);
        Assert.False(controller.CommandRuntime.State.IsActive);
    }

    private static (
        CadDocument Document,
        CadDocumentViewModel DocumentViewModel,
        BlockRecord Block,
        CadBlockEditorViewModelFactory Factory,
        CadEditorControllerHostService ControllerHost) CreateSystem()
    {
        var document = new CadDocument();
        var block = new BlockRecord("TEST_BLOCK");
        document.BlockRecords.Add(block);
        var scene = RenderSceneSamples.CreateBaselineScene();
        var sceneBuilder = new StubSceneBuilder(scene);
        var renderSettings = new CadRenderSceneSettings();
        var selectionService = new CadSelectionService();
        var focusService = new CadSelectionFocusService();
        var documentContext = new CadDocumentContextService();
        var sessionHost = new CadEditorSessionHostService(new CadEditorSessionFactory(), documentContext, selectionService);
        var commandRegistry = new CadCommandRegistry();
        commandRegistry.Register(new LineCadCommand());
        commandRegistry.Register(new InsertCadCommand());
        var controllerHost = new CadEditorControllerHostService(
            sessionHost,
            new CadEditorControllerFactory(commandRegistry),
            documentContext);
        var adapterRegistry = new CadInteractiveCommandAdapterRegistry(
        [
            new LineInteractiveCommandAdapter(),
            new InsertInteractiveCommandAdapter()
        ]);
        var controller = controllerHost.GetOrCreate(document);
        var statsExport = new NullRenderStatsExportService();
        var documentRender = new CadRenderViewModel(
            document,
            scene,
            sceneBuilder,
            renderSettings,
            CadRenderLayoutSelection.ModelSpace,
            documentPath: null,
            dynamicBlockOverrides: null,
            dynamicBlockOverrideChanges: null,
            selectionService,
            focusService,
            sessionHost,
            controller.CommandRuntime,
            interactiveAdapterRegistry: adapterRegistry,
            collaborationWorkspace: null,
            statsExportService: statsExport);
        var documentViewModel = new CadDocumentViewModel(
            document,
            CadFileFormat.Dxf,
            path: null,
            displayName: "Drawing1.dxf",
            documentRender);
        documentContext.Register(documentViewModel);
        documentContext.ActiveDocument = documentViewModel;

        var factory = new CadBlockEditorViewModelFactory(
            sceneBuilder,
            renderSettings,
            selectionService,
            focusService,
            statsExport,
            sessionHost,
            controllerHost,
            adapterRegistry,
            collaborationWorkspace: null);
        return (document, documentViewModel, block, factory, controllerHost);
    }

    private static CadInteractionEvent CreatePointerDown(float x, float y)
    {
        var point = new Vector2(x, y);
        return new CadInteractionEvent(
            Kind: CadInteractionEventKind.PointerDown,
            WorldPoint: point,
            ScreenPoint: point,
            Modifiers: CadInteractionModifiers.None,
            PointerButtons: CadInteractionPointerButtons.Left,
            Tolerance: 2f,
            WheelDelta: 0f,
            Key: null,
            Text: null);
    }

    private sealed class StubSceneBuilder : ICadRenderSceneBuilder
    {
        private readonly RenderScene _scene;

        public StubSceneBuilder(RenderScene scene)
        {
            _scene = scene;
        }

        public RenderScene Build(CadDocument document, CadRenderSceneSettings settings)
        {
            return _scene;
        }

        public RenderScene BuildBlock(CadDocument document, BlockRecord block, CadRenderSceneSettings settings)
        {
            return _scene;
        }
    }

    private sealed class NullRenderStatsExportService : IRenderStatsExportService
    {
        public Task<RenderStatsExportResult?> SaveStatsAsync(string? suggestedFileName, CancellationToken cancellationToken)
        {
            return Task.FromResult<RenderStatsExportResult?>(null);
        }
    }
}
