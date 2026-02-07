using System.Linq;
using System.Numerics;
using System.Reactive.Threading.Tasks;
using System.Threading;
using System.Threading.Tasks;
using ACadInspector.Core;
using ACadInspector.Editing.Commands;
using ACadInspector.Editing.Controllers;
using ACadInspector.Editing.Interaction;
using ACadInspector.Editing.Sessions;
using ACadInspector.Rendering;
using ACadInspector.Services;
using ACadInspector.Tests.Rendering;
using ACadInspector.ViewModels;
using ACadSharp;
using ACadSharp.Entities;
using ACadSharp.Tables;
using Xunit;

namespace ACadInspector.Tests.ViewModels;

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
        var controllerHost = new CadEditorControllerHostService(
            sessionHost,
            new CadEditorControllerFactory(commandRegistry),
            documentContext);
        var adapterRegistry = new CadInteractiveCommandAdapterRegistry([new LineInteractiveCommandAdapter()]);
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
