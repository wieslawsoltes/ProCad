using System.Numerics;
using System.Reactive.Threading.Tasks;
using System.Threading;
using System.Threading.Tasks;
using ACadInspector.Core;
using ACadInspector.Editing.Interaction;
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

    private sealed class NullRenderStatsExportService : IRenderStatsExportService
    {
        public Task<RenderStatsExportResult?> SaveStatsAsync(string? suggestedFileName, CancellationToken cancellationToken)
        {
            return Task.FromResult<RenderStatsExportResult?>(null);
        }
    }
}
