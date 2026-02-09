using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Numerics;
using System.Reactive.Threading.Tasks;
using System.Threading.Tasks;
using ACadInspector.Core;
using ACadInspector.Editing.Commands;
using ACadInspector.Editing.Interaction;
using ACadInspector.Editing.Prompt;
using ACadInspector.Editing.Sessions;
using ACadInspector.Rendering;
using ACadInspector.Services;
using ACadInspector.Tests.Rendering;
using ACadInspector.ViewModels;
using ACadSharp;
using ACadSharp.Entities;
using ACadSharp.Tables;
using CSMath;
using Xunit;

namespace ACadInspector.Tests.ViewModels;

public sealed class CadPreviewParityMatrixTests
{
    [Theory]
    [MemberData(nameof(PreviewMatrixCases))]
    public async Task PreviewMatrix_CommandFamilies_RenderExpectedOverlayGeometry(PreviewMatrixCase matrixCase)
    {
        var document = new CadDocument();
        matrixCase.SeedDocument?.Invoke(document);
        var viewModel = CreateSystem(document, matrixCase.Handlers, matrixCase.Adapters);

        await viewModel.StartCommand.Execute(matrixCase.CommandName).ToTask();
        foreach (var interaction in matrixCase.Interactions)
        {
            await viewModel.InteractionCommand.Execute(interaction).ToTask();
        }

        await WaitUntilAsync(() =>
            ContainsPrimitiveKinds(viewModel.OverlayScene, matrixCase.RequiredKinds) &&
            ContainsText(viewModel.OverlayScene, matrixCase.RequiredTextContains));

        Assert.True(ContainsPrimitiveKinds(viewModel.OverlayScene, matrixCase.RequiredKinds));
        Assert.True(ContainsText(viewModel.OverlayScene, matrixCase.RequiredTextContains));
    }

    public static IEnumerable<object[]> PreviewMatrixCases()
    {
        yield return
        [
            new PreviewMatrixCase(
                "LINE",
                [new LineCadCommand()],
                [new LineInteractiveCommandAdapter()],
                null,
                [CreatePointerDown(0f, 0f), CreatePointerMove(6f, 4f)],
                [RenderOverlayPrimitiveKind.Line, RenderOverlayPrimitiveKind.SquareMarker],
                RequiredTextContains: null)
        ];

        yield return
        [
            new PreviewMatrixCase(
                "CIRCLE",
                [new CircleCadCommand()],
                [new CircleInteractiveCommandAdapter()],
                null,
                [CreatePointerDown(0f, 0f), CreatePointerMove(4f, 3f)],
                [RenderOverlayPrimitiveKind.Line, RenderOverlayPrimitiveKind.SquareMarker, RenderOverlayPrimitiveKind.Text],
                RequiredTextContains: "R=")
        ];

        yield return
        [
            new PreviewMatrixCase(
                "ARC",
                [new ArcCadCommand()],
                [new ArcInteractiveCommandAdapter()],
                null,
                [CreatePointerDown(0f, 0f), CreatePointerDown(4f, 0f), CreatePointerMove(4f, 4f)],
                [RenderOverlayPrimitiveKind.Line, RenderOverlayPrimitiveKind.Text],
                RequiredTextContains: "Sweep")
        ];

        yield return
        [
            new PreviewMatrixCase(
                "ELLIPSE",
                [new EllipseCadCommand()],
                [new EllipseInteractiveCommandAdapter()],
                null,
                [CreatePointerDown(0f, 0f), CreatePointerDown(8f, 0f), CreatePointerMove(8f, 3f)],
                [RenderOverlayPrimitiveKind.Line, RenderOverlayPrimitiveKind.Text],
                RequiredTextContains: "ratio")
        ];

        yield return
        [
            new PreviewMatrixCase(
                "SPLINE",
                [new SplineCadCommand()],
                [new SplineInteractiveCommandAdapter()],
                null,
                [CreatePointerDown(0f, 0f), CreatePointerMove(5f, 2f)],
                [RenderOverlayPrimitiveKind.Line, RenderOverlayPrimitiveKind.SquareMarker],
                RequiredTextContains: null)
        ];

        yield return
        [
            new PreviewMatrixCase(
                "DIMLINEAR",
                [new DimLinearCadCommand()],
                [new DimLinearInteractiveCommandAdapter()],
                null,
                [CreatePointerDown(0f, 0f), CreatePointerDown(8f, 0f), CreatePointerMove(4f, 3f)],
                [RenderOverlayPrimitiveKind.Line, RenderOverlayPrimitiveKind.SquareMarker, RenderOverlayPrimitiveKind.Text],
                RequiredTextContains: "Specify")
        ];

        yield return
        [
            new PreviewMatrixCase(
                "DIMRADIUS",
                [new DimRadiusCadCommand()],
                [new DimRadiusInteractiveCommandAdapter()],
                null,
                [CreatePointerDown(0f, 0f), CreatePointerMove(4f, 2f)],
                [RenderOverlayPrimitiveKind.Line, RenderOverlayPrimitiveKind.SquareMarker],
                RequiredTextContains: null)
        ];

        yield return
        [
            new PreviewMatrixCase(
                "LEADER",
                [new LeaderCadCommand()],
                [new LeaderInteractiveCommandAdapter()],
                null,
                [CreatePointerDown(0f, 0f), CreatePointerMove(6f, 2f)],
                [RenderOverlayPrimitiveKind.Line, RenderOverlayPrimitiveKind.SquareMarker],
                RequiredTextContains: null)
        ];

        yield return
        [
            new PreviewMatrixCase(
                "MLEADER",
                [new MLeaderCadCommand()],
                [new MLeaderInteractiveCommandAdapter()],
                null,
                [CreatePointerDown(0f, 0f), CreatePointerMove(6f, 2f)],
                [RenderOverlayPrimitiveKind.Line, RenderOverlayPrimitiveKind.SquareMarker],
                RequiredTextContains: null)
        ];

        yield return
        [
            new PreviewMatrixCase(
                "INSERT",
                [new InsertCadCommand()],
                [new InsertInteractiveCommandAdapter()],
                static document =>
                {
                    var block = new BlockRecord("PREVIEW_BLOCK");
                    block.Entities.Add(new Line
                    {
                        StartPoint = new XYZ(0d, 0d, 0d),
                        EndPoint = new XYZ(1d, 0d, 0d)
                    });
                    document.BlockRecords.Add(block);
                },
                [CreateTextInput("PREVIEW_BLOCK"), CreateKeyDown("Enter"), CreatePointerMove(4f, 2f)],
                [RenderOverlayPrimitiveKind.Text],
                RequiredTextContains: "insertion point")
        ];
    }

    private static bool ContainsPrimitiveKinds(RenderOverlayScene scene, IReadOnlyList<RenderOverlayPrimitiveKind> kinds)
    {
        for (var index = 0; index < kinds.Count; index++)
        {
            if (!scene.Primitives.Any(primitive => primitive.Kind == kinds[index]))
            {
                return false;
            }
        }

        return true;
    }

    private static bool ContainsText(RenderOverlayScene scene, string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return true;
        }

        return scene.Primitives.Any(primitive =>
            primitive.Kind == RenderOverlayPrimitiveKind.Text &&
            primitive.Text is not null &&
            primitive.Text.Contains(text, StringComparison.OrdinalIgnoreCase));
    }

    private static CadRenderViewModel CreateSystem(
        CadDocument document,
        IReadOnlyList<ICadCommandHandler> handlers,
        IReadOnlyList<ICadInteractiveCommandAdapter> adapters)
    {
        var sceneBuilder = CreateSceneBuilder();
        var settings = new CadRenderSceneSettings();
        var initialScene = sceneBuilder.Build(document, settings);
        var selectionService = new CadSelectionService();
        var focusService = new CadSelectionFocusService();
        var documentContext = new CadDocumentContextService();
        var sessionHost = new CadEditorSessionHostService(new CadEditorSessionFactory(), documentContext, selectionService);
        var commandRegistry = new CadCommandRegistry();
        for (var index = 0; index < handlers.Count; index++)
        {
            commandRegistry.Register(handlers[index]);
        }

        var runtime = new CadCommandRuntime(commandRegistry, new CadCommandIntellisenseService(commandRegistry));
        var adapterRegistry = new CadInteractiveCommandAdapterRegistry(adapters);
        var viewModel = new CadRenderViewModel(
            document,
            initialScene,
            sceneBuilder,
            settings,
            CadRenderLayoutSelection.ModelSpace,
            documentPath: null,
            dynamicBlockOverrides: null,
            dynamicBlockOverrideChanges: null,
            selectionService,
            focusService,
            sessionHost,
            runtime,
            adapterRegistry,
            shortcutBindings: null,
            collaborationWorkspace: null,
            statsExportService: null,
            statsFileName: null);
        sessionHost.GetOrCreate(document);
        return viewModel;
    }

    private static CadRenderSceneBuilder CreateSceneBuilder()
    {
        var handlers = new IRenderEntityHandler[]
        {
            new LineRenderHandler(),
            new TextEntityRenderHandler(),
            new FallbackRenderHandler()
        };

        return new CadRenderSceneBuilder(
            new RenderEntityDispatcher(handlers),
            new DefaultRenderStyleResolver(),
            new DefaultRenderLinePatternResolver(),
            new DefaultRenderShapeResolver(),
            new DefaultRenderTextShaper(),
            new DefaultRenderEntityVisibilityResolver(),
            new DefaultRenderGeometrySampler(),
            new DefaultRenderEntityOrderResolver(),
            new RenderCacheStampProvider());
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

    private static CadInteractionEvent CreatePointerMove(float x, float y)
    {
        var point = new Vector2(x, y);
        return new CadInteractionEvent(
            Kind: CadInteractionEventKind.PointerMove,
            WorldPoint: point,
            ScreenPoint: point,
            Modifiers: CadInteractionModifiers.None,
            PointerButtons: CadInteractionPointerButtons.None,
            Tolerance: 2f,
            WheelDelta: 0f,
            Key: null,
            Text: null);
    }

    private static CadInteractionEvent CreateTextInput(string text)
    {
        return new CadInteractionEvent(
            Kind: CadInteractionEventKind.TextInput,
            WorldPoint: Vector2.Zero,
            ScreenPoint: Vector2.Zero,
            Modifiers: CadInteractionModifiers.None,
            PointerButtons: CadInteractionPointerButtons.None,
            Tolerance: 0f,
            WheelDelta: 0f,
            Key: null,
            Text: text);
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

        Assert.True(condition(), "Timed out while waiting for preview parity matrix overlay.");
    }

    public sealed record PreviewMatrixCase(
        string CommandName,
        IReadOnlyList<ICadCommandHandler> Handlers,
        IReadOnlyList<ICadInteractiveCommandAdapter> Adapters,
        Action<CadDocument>? SeedDocument,
        IReadOnlyList<CadInteractionEvent> Interactions,
        IReadOnlyList<RenderOverlayPrimitiveKind> RequiredKinds,
        string? RequiredTextContains);
}
