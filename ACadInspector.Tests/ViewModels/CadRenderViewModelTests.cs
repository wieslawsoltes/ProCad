using System.Numerics;
using System.Reactive.Threading.Tasks;
using System.Threading;
using System.Threading.Tasks;
using ACadInspector.Core;
using ACadInspector.Editing.Commands;
using ACadInspector.Editing.Interaction;
using ACadInspector.Editing.Prompt;
using ACadInspector.Editing.Sessions;
using ACadInspector.Editing.Undo;
using ACadInspector.Rendering;
using ACadInspector.Services;
using ACadInspector.Tests.Rendering;
using ACadInspector.ViewModels;
using ACadSharp;
using Xunit;

namespace ACadInspector.Tests.ViewModels;

public sealed class CadRenderViewModelTests
{
    [Fact]
    public void LayoutChange_TogglesGridAndAxesForPaperSpace()
    {
        var document = new CadDocument();
        var scene = RenderSceneSamples.CreateBaselineScene();
        var viewModel = new CadRenderViewModel(
            document,
            scene,
            new NullSceneBuilder(scene),
            new CadRenderSceneSettings(),
            CadRenderLayoutSelection.ModelSpace,
            documentPath: null,
            dynamicBlockOverrides: null,
            dynamicBlockOverrideChanges: null,
            new CadSelectionService(),
            new CadSelectionFocusService(),
            new NullRenderStatsExportService(),
            statsFileName: null,
            allowLayoutUpdates: false);

        var modelLayout = Assert.Single(viewModel.Layouts, layout => !layout.IsPaperSpace);
        var paperLayout = Assert.Single(viewModel.Layouts, layout => layout.IsPaperSpace);

        viewModel.ShowGrid = true;
        viewModel.ShowAxes = true;
        viewModel.SelectedLayout = paperLayout;

        Assert.False(viewModel.ShowGrid);
        Assert.False(viewModel.ShowAxes);

        viewModel.SelectedLayout = modelLayout;

        Assert.True(viewModel.ShowGrid);
        Assert.True(viewModel.ShowAxes);
    }

    [Fact]
    public void InitialScene_DisablesFitOnLoadAfterFirstScene()
    {
        var document = new CadDocument();
        var scene = RenderSceneSamples.CreateBaselineScene();
        var viewModel = new CadRenderViewModel(
            document,
            scene,
            new NullSceneBuilder(scene),
            new CadRenderSceneSettings(),
            CadRenderLayoutSelection.ModelSpace,
            documentPath: null,
            dynamicBlockOverrides: null,
            dynamicBlockOverrideChanges: null,
            new CadSelectionService(),
            new CadSelectionFocusService(),
            new NullRenderStatsExportService(),
            statsFileName: null,
            allowLayoutUpdates: false);

        Assert.False(viewModel.FitOnLoad);
    }

    [Fact]
    public async Task FunctionHotkeys_ToggleDraftingFlags()
    {
        var document = new CadDocument();
        var scene = RenderSceneSamples.CreateBaselineScene();
        var viewModel = new CadRenderViewModel(
            document,
            scene,
            new NullSceneBuilder(scene),
            new CadRenderSceneSettings(),
            CadRenderLayoutSelection.ModelSpace,
            documentPath: null,
            dynamicBlockOverrides: null,
            dynamicBlockOverrideChanges: null,
            new CadSelectionService(),
            new CadSelectionFocusService(),
            new NullRenderStatsExportService(),
            statsFileName: null,
            allowLayoutUpdates: false);

        await viewModel.InteractionCommand.Execute(CreateKeyDown("F3")).ToTask();
        await viewModel.InteractionCommand.Execute(CreateKeyDown("F8")).ToTask();
        await viewModel.InteractionCommand.Execute(CreateKeyDown("F10")).ToTask();
        await viewModel.InteractionCommand.Execute(CreateKeyDown("F11")).ToTask();
        await viewModel.InteractionCommand.Execute(CreateKeyDown("F12")).ToTask();
        await viewModel.InteractionCommand.Execute(CreateKeyDown("F7")).ToTask();

        Assert.False(viewModel.OsnapEnabled);
        Assert.True(viewModel.OrthoEnabled);
        Assert.True(viewModel.PolarEnabled);
        Assert.False(viewModel.OtrackEnabled);
        Assert.False(viewModel.DynamicInputEnabled);
        Assert.False(viewModel.ShowGrid);
    }

    [Fact]
    public async Task Dispose_UnsubscribesFromSelectionAndSessionEvents()
    {
        var document = new CadDocument();
        var context = new CadDocumentContextService();
        var selection = new CadSelectionService();
        var sessionHost = new CadEditorSessionHostService(
            new CadEditorSessionFactory(),
            context,
            selection);
        var scene = RenderSceneSamples.CreateBaselineScene();
        var builder = new CountingSceneBuilder(scene);
        var runtime = new TestCommandRuntime();
        var viewModel = new CadRenderViewModel(
            document,
            scene,
            builder,
            new CadRenderSceneSettings(),
            CadRenderLayoutSelection.ModelSpace,
            documentPath: null,
            dynamicBlockOverrides: null,
            dynamicBlockOverrideChanges: null,
            selection,
            new CadSelectionFocusService(),
            sessionHost,
            runtime,
            interactiveAdapterRegistry: null,
            shortcutBindings: null,
            collaborationWorkspace: null,
            statsExportService: new NullRenderStatsExportService(),
            statsFileName: null,
            allowLayoutUpdates: true);

        selection.SelectedObject = document;
        Assert.Same(document, viewModel.SelectedObject);

        var session = sessionHost.GetOrCreate(document);
        sessionHost.NotifySessionChanged(session);
        await WaitUntilAsync(() => builder.BuildCount > 0);

        viewModel.Dispose();
        var buildCountBefore = builder.BuildCount;
        selection.SelectedObject = new CadDocument();
        sessionHost.NotifySessionChanged(session);
        await Task.Delay(120);

        Assert.Same(document, viewModel.SelectedObject);
        Assert.Equal(buildCountBefore, builder.BuildCount);
    }

    [Fact]
    public void Dispose_ClearsTransientVisualState()
    {
        var document = new CadDocument();
        var scene = RenderSceneSamples.CreateBaselineScene();
        var viewModel = new CadRenderViewModel(
            document,
            scene,
            new NullSceneBuilder(scene),
            new CadRenderSceneSettings(),
            CadRenderLayoutSelection.ModelSpace,
            documentPath: null,
            dynamicBlockOverrides: null,
            dynamicBlockOverrideChanges: null,
            new CadSelectionService(),
            new CadSelectionFocusService(),
            new NullRenderStatsExportService(),
            statsFileName: null,
            allowLayoutUpdates: false);

        viewModel.ToolVisualHints =
        [
            new CadToolVisualHint(
                Kind: "Prompt",
                Anchor: new Vector2(1f, 2f),
                SecondaryAnchor: null,
                Text: "hint")
        ];
        viewModel.OverlayScene = new RenderOverlayScene(
        [
            new RenderOverlayPrimitive(
                Kind: RenderOverlayPrimitiveKind.Text,
                Start: new Vector2(1f, 2f),
                End: new Vector2(1f, 2f),
                Color: RenderColor.FromRgb(0, 120, 255),
                StrokeWidth: 1f,
                MarkerRadius: 0f,
                Text: "hint")
        ]);
        viewModel.DynamicInput = new CadDynamicInputPayload("LINE", "Specify next point", new Vector2(1f, 2f));

        viewModel.Dispose();

        Assert.Empty(viewModel.ToolVisualHints);
        Assert.Empty(viewModel.OverlayScene.Primitives);
        Assert.Null(viewModel.DynamicInput);
    }

    private static CadInteractionEvent CreateKeyDown(string key)
    {
        return new CadInteractionEvent(
            Kind: CadInteractionEventKind.KeyDown,
            WorldPoint: Vector2.Zero,
            ScreenPoint: Vector2.Zero,
            Modifiers: CadInteractionModifiers.None,
            PointerButtons: CadInteractionPointerButtons.None,
            Tolerance: 0f,
            WheelDelta: 0f,
            Key: key,
            Text: null);
    }

    private sealed class NullSceneBuilder : ICadRenderSceneBuilder
    {
        private readonly RenderScene _scene;

        public NullSceneBuilder(RenderScene scene)
        {
            _scene = scene;
        }

        public RenderScene Build(CadDocument document, CadRenderSceneSettings settings)
        {
            return _scene;
        }

        public RenderScene BuildBlock(CadDocument document, ACadSharp.Tables.BlockRecord block, CadRenderSceneSettings settings)
        {
            return _scene;
        }
    }

    private sealed class CountingSceneBuilder : ICadRenderSceneBuilder
    {
        private readonly RenderScene _scene;

        public CountingSceneBuilder(RenderScene scene)
        {
            _scene = scene;
        }

        public int BuildCount { get; private set; }

        public RenderScene Build(CadDocument document, CadRenderSceneSettings settings)
        {
            BuildCount++;
            return _scene;
        }

        public RenderScene BuildBlock(CadDocument document, ACadSharp.Tables.BlockRecord block, CadRenderSceneSettings settings)
        {
            BuildCount++;
            return _scene;
        }
    }

    private sealed class TestCommandRuntime : ICadCommandRuntime
    {
        public CadPromptState State => CadPromptState.Idle;
        public string? LastCommandInput => null;
        public event EventHandler<CadPromptState>? StateChanged;
        public event EventHandler<CadCommandExecutedEventArgs>? CommandExecuted
        {
            add { }
            remove { }
        }

        public void BeginCommand(string commandName)
        {
        }

        public void Cancel()
        {
        }

        public CadPromptState Preview(string input, int cursorIndex)
        {
            return CadPromptState.Idle;
        }

        public ValueTask<CadPromptResolution> SubmitAsync(string input, ICadEditorSession? session, CancellationToken cancellationToken = default)
        {
            return ValueTask.FromResult(new CadPromptResolution(false, null, CadPromptState.Idle));
        }

        public ValueTask<CadPromptResolution> SubmitTokenAsync(
            CadPromptToken token,
            ICadEditorSession? session,
            bool commit = false,
            CancellationToken cancellationToken = default)
        {
            return ValueTask.FromResult(new CadPromptResolution(false, null, CadPromptState.Idle));
        }
    }

    private static async Task WaitUntilAsync(Func<bool> condition, int timeoutMs = 2000)
    {
        var elapsed = 0;
        while (elapsed < timeoutMs)
        {
            if (condition())
            {
                return;
            }

            await Task.Delay(25);
            elapsed += 25;
        }

        Assert.True(condition(), "Timed out waiting for render lifecycle condition.");
    }

    private sealed class NullRenderStatsExportService : IRenderStatsExportService
    {
        public Task<RenderStatsExportResult?> SaveStatsAsync(string? suggestedFileName, CancellationToken cancellationToken)
        {
            return Task.FromResult<RenderStatsExportResult?>(null);
        }
    }
}
