using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Numerics;
using System.Reactive.Threading.Tasks;
using System.Threading.Tasks;
using ACadInspector.Core;
using ACadInspector.Editing.Clipboard;
using ACadInspector.Editing.Commands;
using ACadInspector.Editing.Constraints;
using ACadInspector.Editing.Interaction;
using ACadInspector.Editing.Prompt;
using ACadInspector.Editing.Selection;
using ACadInspector.Editing.Sessions;
using ACadInspector.Rendering;
using ACadInspector.Services;
using ACadInspector.ViewModels;
using ACadSharp;
using ACadSharp.Entities;
using CSMath;
using Xunit;

namespace ACadInspector.Tests.ViewModels;

public sealed class CadRenderInteractiveEditingTests
{
    [Fact]
    public async Task Interaction_LineCommand_CreatesEntityAndRefreshesScene()
    {
        var document = new CadDocument();
        var (viewModel, _, runtime) = CreateSystem(document);

        runtime.BeginCommand("LINE");
        await viewModel.InteractionCommand.Execute(CreatePointerDown(0f, 0f)).ToTask();
        await viewModel.InteractionCommand.Execute(CreatePointerDown(10f, 0f)).ToTask();

        await WaitUntilAsync(() =>
            document.Entities.OfType<Line>().Any() &&
            viewModel.Scene is not null &&
            viewModel.Scene.Layers.SelectMany(static layer => layer.Primitives).OfType<RenderLine>().Any());
        Assert.False(runtime.State.IsActive);
    }

    [Fact]
    public async Task Interaction_LineCommand_CanBeRestartedAfterCommitAndDrawSecondEntity()
    {
        var document = new CadDocument();
        var (viewModel, _, runtime) = CreateSystem(document);

        runtime.BeginCommand("LINE");
        await viewModel.InteractionCommand.Execute(CreatePointerDown(0f, 0f)).ToTask();
        await viewModel.InteractionCommand.Execute(CreatePointerDown(10f, 0f)).ToTask();
        await WaitUntilAsync(() => document.Entities.OfType<Line>().Count() == 1);
        Assert.False(runtime.State.IsActive);

        runtime.BeginCommand("LINE");
        await viewModel.InteractionCommand.Execute(CreatePointerDown(0f, 5f)).ToTask();
        await viewModel.InteractionCommand.Execute(CreatePointerDown(10f, 5f)).ToTask();
        await WaitUntilAsync(() => document.Entities.OfType<Line>().Count() == 2);
        Assert.False(runtime.State.IsActive);
    }

    [Fact]
    public async Task Interaction_StartCommandLineToolPath_AllowsSequentialLineCreation()
    {
        var document = new CadDocument();
        var (viewModel, _, runtime) = CreateSystem(document);

        await viewModel.StartCommand.Execute("LINE").ToTask();
        Assert.True(runtime.State.IsActive);
        await viewModel.InteractionCommand.Execute(CreatePointerDown(0f, 0f)).ToTask();
        await viewModel.InteractionCommand.Execute(CreatePointerDown(8f, 0f)).ToTask();
        await WaitUntilAsync(() => document.Entities.OfType<Line>().Count() == 1);
        Assert.False(runtime.State.IsActive);

        await viewModel.StartCommand.Execute("LINE").ToTask();
        Assert.True(runtime.State.IsActive);
        await viewModel.InteractionCommand.Execute(CreatePointerDown(0f, 4f)).ToTask();
        await viewModel.InteractionCommand.Execute(CreatePointerDown(8f, 4f)).ToTask();
        await WaitUntilAsync(() => document.Entities.OfType<Line>().Count() == 2);
        Assert.False(runtime.State.IsActive);
    }

    [Fact]
    public async Task Interaction_PlineCommand_ContinuesUntilExplicitEnterAndCreatesAllSegments()
    {
        var document = new CadDocument();
        var (viewModel, _, runtime) = CreateSystem(
            document,
            additionalHandlers: [new PlineCadCommand()],
            additionalAdapters: [new PlineInteractiveCommandAdapter()]);

        runtime.BeginCommand("PLINE");
        await viewModel.InteractionCommand.Execute(CreatePointerDown(0f, 0f)).ToTask();
        await viewModel.InteractionCommand.Execute(CreatePointerDown(6f, 0f)).ToTask();
        Assert.True(runtime.State.IsActive);
        await viewModel.InteractionCommand.Execute(CreatePointerDown(6f, 4f)).ToTask();
        Assert.True(runtime.State.IsActive);
        await viewModel.InteractionCommand.Execute(CreateKeyDown("Enter")).ToTask();

        await WaitUntilAsync(() =>
        {
            var polyline = document.Entities.OfType<LwPolyline>().FirstOrDefault();
            return polyline is not null &&
                   polyline.Vertices.Count == 3 &&
                   !runtime.State.IsActive;
        });
    }

    [Fact]
    public async Task Interaction_SplineCommand_ContinuesUntilExplicitEnterAndCreatesAllFitPoints()
    {
        var document = new CadDocument();
        var (viewModel, _, runtime) = CreateSystem(
            document,
            additionalHandlers: [new SplineCadCommand()],
            additionalAdapters: [new SplineInteractiveCommandAdapter()]);

        runtime.BeginCommand("SPLINE");
        await viewModel.InteractionCommand.Execute(CreatePointerDown(0f, 0f)).ToTask();
        await viewModel.InteractionCommand.Execute(CreatePointerDown(4f, 2f)).ToTask();
        Assert.True(runtime.State.IsActive);
        await viewModel.InteractionCommand.Execute(CreatePointerDown(8f, 0f)).ToTask();
        Assert.True(runtime.State.IsActive);
        await viewModel.InteractionCommand.Execute(CreateKeyDown("Enter")).ToTask();

        await WaitUntilAsync(() =>
        {
            var spline = document.Entities.OfType<Spline>().FirstOrDefault();
            return spline is not null &&
                   spline.FitPoints.Count >= 3 &&
                   !runtime.State.IsActive;
        });
    }

    [Fact]
    public async Task Interaction_TextCommand_CreatesEntityAndRefreshesScene()
    {
        var document = new CadDocument();
        var (viewModel, _, runtime) = CreateSystem(document);

        runtime.BeginCommand("TEXT");
        await viewModel.InteractionCommand.Execute(CreatePointerDown(4f, 2f)).ToTask();

        await WaitUntilAsync(() =>
            document.Entities.OfType<TextEntity>().Any() &&
            viewModel.Scene is not null &&
            viewModel.Scene.Layers.SelectMany(static layer => layer.Primitives).OfType<RenderText>().Any());
    }

    [Fact]
    public async Task Interaction_InsertCommand_CreatesInsertEntity()
    {
        var document = new CadDocument();
        var block = new ACadSharp.Tables.BlockRecord("INT_INSERT");
        block.Entities.Add(new Line
        {
            StartPoint = new XYZ(0, 0, 0),
            EndPoint = new XYZ(2, 0, 0)
        });
        document.BlockRecords.Add(block);

        var (viewModel, _, runtime) = CreateSystem(
            document,
            additionalHandlers: [new InsertCadCommand()],
            additionalAdapters: [new InsertInteractiveCommandAdapter()]);

        runtime.BeginCommand("INSERT");
        await viewModel.InteractionCommand.Execute(CreateTextInput("INT_INSERT")).ToTask();
        await viewModel.InteractionCommand.Execute(CreateKeyDown("Enter")).ToTask();
        await viewModel.InteractionCommand.Execute(CreatePointerDown(3f, 4f)).ToTask();

        await WaitUntilAsync(() =>
            document.Entities.OfType<Insert>().Any(insert =>
                System.Math.Abs(insert.InsertPoint.X - 3d) < 1e-6 &&
                System.Math.Abs(insert.InsertPoint.Y - 4d) < 1e-6));
    }

    [Fact]
    public async Task Interaction_InsertDropCommand_PlacesInsertAtDroppedPoint()
    {
        var document = new CadDocument();
        var block = new ACadSharp.Tables.BlockRecord("DROP_INSERT");
        block.Entities.Add(new Line
        {
            StartPoint = new XYZ(0, 0, 0),
            EndPoint = new XYZ(1, 0, 0)
        });
        document.BlockRecords.Add(block);

        var (viewModel, _, _) = CreateSystem(
            document,
            additionalHandlers: [new InsertCadCommand()],
            additionalAdapters: [new InsertInteractiveCommandAdapter()]);

        await viewModel.InsertDroppedBlockCommand
            .Execute(new CadInsertDropRequest("DROP_INSERT", new Vector2(9f, 11f)))
            .ToTask();

        await WaitUntilAsync(() =>
            document.Entities.OfType<Insert>().Any(insert =>
                System.Math.Abs(insert.InsertPoint.X - 9d) < 1e-6 &&
                System.Math.Abs(insert.InsertPoint.Y - 11d) < 1e-6));
    }

    [Fact]
    public async Task Interaction_MoveCommand_SelectThenPickPoints_MovesEntityAndRefreshesScene()
    {
        var document = new CadDocument();
        document.Entities.Add(new Line
        {
            StartPoint = new XYZ(0, 0, 0),
            EndPoint = new XYZ(10, 0, 0)
        });

        var (viewModel, _, runtime) = CreateSystem(
            document,
            additionalHandlers: [new MoveCadCommand()],
            additionalAdapters: [new MoveInteractiveCommandAdapter()]);

        runtime.BeginCommand("MOVE");
        await viewModel.InteractionCommand.Execute(CreatePointerDown(0f, 0f)).ToTask(); // select object
        await viewModel.InteractionCommand.Execute(CreatePointerDown(0f, 0f)).ToTask(); // base point
        await viewModel.InteractionCommand.Execute(CreatePointerDown(5f, 0f)).ToTask(); // target point

        await WaitUntilAsync(() =>
        {
            var moved = document.Entities.OfType<Line>().FirstOrDefault();
            return moved is not null &&
                   System.Math.Abs(moved.StartPoint.X - 5d) < 1e-6 &&
                   System.Math.Abs(moved.EndPoint.X - 15d) < 1e-6 &&
                   viewModel.Scene is not null &&
                   viewModel.Scene.Layers.SelectMany(static layer => layer.Primitives).OfType<RenderLine>().Any();
        });
    }

    [Fact]
    public async Task Interaction_GripDrag_LineEndpoint_StretchesEndpointOnly()
    {
        var document = new CadDocument();
        document.Entities.Add(new Line
        {
            StartPoint = new XYZ(0, 0, 0),
            EndPoint = new XYZ(10, 0, 0)
        });

        var (viewModel, _, _) = CreateSystem(document);

        // First click selects the entity and builds grip seeds.
        await viewModel.InteractionCommand.Execute(CreatePointerDown(0f, 0f)).ToTask();

        // Second click on the endpoint starts grip drag.
        await viewModel.InteractionCommand.Execute(CreatePointerDown(0f, 0f)).ToTask();
        await viewModel.InteractionCommand.Execute(CreatePointerUp(2f, 3f)).ToTask();

        await WaitUntilAsync(() =>
        {
            var line = document.Entities.OfType<Line>().FirstOrDefault();
            return line is not null &&
                   System.Math.Abs(line.StartPoint.X - 2d) < 1e-6 &&
                   System.Math.Abs(line.StartPoint.Y - 3d) < 1e-6 &&
                   System.Math.Abs(line.EndPoint.X - 10d) < 1e-6 &&
                   System.Math.Abs(line.EndPoint.Y - 0d) < 1e-6;
        });
    }

    [Fact]
    public async Task Interaction_SelectionClick_ShowsGripAdornersOnFirstPick()
    {
        var document = new CadDocument();
        document.Entities.Add(new Line
        {
            StartPoint = new XYZ(0, 0, 0),
            EndPoint = new XYZ(10, 0, 0)
        });

        var (viewModel, _, _) = CreateSystem(document);
        await viewModel.InteractionCommand.Execute(CreatePointerDown(0f, 0f)).ToTask();

        await WaitUntilAsync(() =>
            viewModel.OverlayScene.Primitives.Any(static primitive =>
                primitive.Kind == RenderOverlayPrimitiveKind.SquareMarker &&
                primitive.FillColor is not null));
    }

    [Fact]
    public async Task Interaction_SelectedEntityGeometryMutation_RefreshesGripAdornersWithoutPointerMove()
    {
        var document = new CadDocument();
        var line = new Line
        {
            StartPoint = new XYZ(0, 0, 0),
            EndPoint = new XYZ(10, 0, 0)
        };
        document.Entities.Add(line);

        var (viewModel, session, _, sessionHost) = CreateSystemWithHost(document);
        await viewModel.InteractionCommand.Execute(CreatePointerDown(0f, 0f)).ToTask();

        await WaitUntilAsync(() => TryGetGripMarkerMaxX(viewModel, out var maxXBefore) && maxXBefore >= 9.5f);

        line.StartPoint = new XYZ(5, 0, 0);
        line.EndPoint = new XYZ(15, 0, 0);
        sessionHost.NotifySessionChanged(session);

        await WaitUntilAsync(() => TryGetGripMarkerMaxX(viewModel, out var maxXAfter) && maxXAfter >= 14.5f);
    }

    [Fact]
    public async Task Interaction_TextSelection_ShowsDistinctTextGripAdorners()
    {
        var document = new CadDocument();
        var text = new TextEntity
        {
            InsertPoint = new XYZ(4, 3, 0),
            AlignmentPoint = new XYZ(4, 3, 0),
            Height = 1.5,
            Rotation = 0.0,
            Value = "TextGrip"
        };
        document.Entities.Add(text);

        var (viewModel, session, _, sessionHost) = CreateSystemWithHost(document);
        session.SetSelection([text], CadSelectionMode.Replace);
        sessionHost.NotifySessionChanged(session);

        await WaitUntilAsync(() => CountUniqueGripMarkerPositions(viewModel) >= 3);
    }

    [Fact]
    public async Task Interaction_MTextSelection_ShowsDistinctTextGripAdorners()
    {
        var document = new CadDocument();
        var mtext = new MText
        {
            InsertPoint = new XYZ(2, 2, 0),
            AlignmentPoint = XYZ.AxisX,
            Height = 1.0,
            RectangleWidth = 6.0,
            Value = "MText Grip"
        };
        document.Entities.Add(mtext);

        var (viewModel, session, _, sessionHost) = CreateSystemWithHost(document);
        session.SetSelection([mtext], CadSelectionMode.Replace);
        sessionHost.NotifySessionChanged(session);

        await WaitUntilAsync(() => CountUniqueGripMarkerPositions(viewModel) >= 3);
    }

    [Fact]
    public async Task Interaction_TextSelection_GripMarkersStayNearSelectionBounds()
    {
        var document = new CadDocument();
        var text = new TextEntity
        {
            InsertPoint = new XYZ(6, 3, 0),
            AlignmentPoint = new XYZ(6, 3, 0),
            Height = 2.0,
            Rotation = 0.25,
            Value = "TextGripBounds"
        };
        document.Entities.Add(text);

        var (viewModel, session, _, sessionHost) = CreateSystemWithHost(document);
        session.SetSelection([text], CadSelectionMode.Replace);
        sessionHost.NotifySessionChanged(session);

        await WaitUntilAsync(() =>
            CountUniqueGripMarkerPositions(viewModel) >= 3 &&
            TryGetSceneTextBounds(viewModel, out _));
        Assert.True(TryGetSceneTextBounds(viewModel, out var textBounds));
        var margin = MathF.Max(MathF.Max(textBounds.Size.X, textBounds.Size.Y) * 0.35f, 1.5f);
        var expanded = textBounds.Inflate(margin);
        var grips = GetGripMarkers(viewModel);
        Assert.NotEmpty(grips);
        Assert.All(grips, grip => Assert.True(
            expanded.Contains(grip),
            $"Grip marker {grip} should remain near rendered text bounds {textBounds.Min}..{textBounds.Max}."));
    }

    [Fact]
    public async Task Interaction_TextSelection_HasGripNearTopRightCorner()
    {
        var document = new CadDocument();
        var text = new TextEntity
        {
            InsertPoint = new XYZ(3, 2, 0),
            AlignmentPoint = new XYZ(3, 2, 0),
            Height = 1.8,
            Rotation = 0.0,
            Value = "TopRightGrip"
        };
        document.Entities.Add(text);

        var (viewModel, session, _, sessionHost) = CreateSystemWithHost(document);
        session.SetSelection([text], CadSelectionMode.Replace);
        sessionHost.NotifySessionChanged(session);

        await WaitUntilAsync(() =>
            CountUniqueGripMarkerPositions(viewModel) >= 3 &&
            TryGetSceneTextBounds(viewModel, out _));
        Assert.True(TryGetSceneTextBounds(viewModel, out var textBounds));
        var topRight = new Vector2(textBounds.MaxX, textBounds.MaxY);
        var tolerance = MathF.Max(MathF.Max(textBounds.Size.X, textBounds.Size.Y) * 0.3f, 1.25f);

        Assert.Contains(
            GetGripMarkers(viewModel),
            grip => Vector2.Distance(grip, topRight) <= tolerance);
    }

    [Fact]
    public async Task Interaction_LineCommand_PointerMoveShowsDashedRubberBandOverlay()
    {
        var document = new CadDocument();
        var (viewModel, _, runtime) = CreateSystem(document);

        runtime.BeginCommand("LINE");
        await viewModel.InteractionCommand.Execute(CreatePointerDown(0f, 0f)).ToTask();
        await viewModel.InteractionCommand.Execute(CreatePointerMove(6f, 4f)).ToTask();

        await WaitUntilAsync(() =>
            viewModel.OverlayScene.Primitives.Any(static primitive =>
                primitive.Kind == RenderOverlayPrimitiveKind.Line &&
                primitive.StrokeStyle == RenderOverlayStrokeStyle.Dashed));
    }

    [Fact]
    public async Task Interaction_OverlayRefreshBudget_RepeatedPointerMoveWithinThreshold()
    {
        const int moveCount = 160;
        const int budgetMilliseconds = 1400;
        var document = new CadDocument();
        var (viewModel, _, runtime) = CreateSystem(document);

        runtime.BeginCommand("LINE");
        await viewModel.InteractionCommand.Execute(CreatePointerDown(1f, 1f)).ToTask();

        var stopwatch = Stopwatch.StartNew();
        for (var index = 0; index < moveCount; index++)
        {
            await viewModel.InteractionCommand.Execute(CreatePointerMove(2f + index * 0.05f, 3f)).ToTask();
        }

        stopwatch.Stop();

        Assert.NotEmpty(viewModel.OverlayScene.Primitives);
        Assert.True(
            stopwatch.ElapsedMilliseconds <= budgetMilliseconds,
            $"Overlay refresh budget exceeded: {stopwatch.ElapsedMilliseconds} ms > {budgetMilliseconds} ms.");
    }

    [Fact]
    public async Task Interaction_LineCommand_FirstPick_ShowsPickPointAdorner()
    {
        var document = new CadDocument();
        var (viewModel, _, runtime) = CreateSystem(document);

        runtime.BeginCommand("LINE");
        await viewModel.InteractionCommand.Execute(CreatePointerDown(2f, 3f)).ToTask();

        await WaitUntilAsync(() =>
            viewModel.OverlayScene.Primitives.Any(static primitive =>
                primitive.Kind == RenderOverlayPrimitiveKind.SquareMarker));
    }

    [Fact]
    public async Task Interaction_CircleCommand_PointerMoveShowsCirclePreviewOverlay()
    {
        var document = new CadDocument();
        var (viewModel, _, runtime) = CreateSystem(
            document,
            additionalHandlers: [new CircleCadCommand()],
            additionalAdapters: [new CircleInteractiveCommandAdapter()]);

        runtime.BeginCommand("CIRCLE");
        await viewModel.InteractionCommand.Execute(CreatePointerDown(0f, 0f)).ToTask();
        await viewModel.InteractionCommand.Execute(CreatePointerMove(6f, 4f)).ToTask();

        await WaitUntilAsync(() =>
            viewModel.OverlayScene.Primitives.Any(static primitive =>
                primitive.Kind == RenderOverlayPrimitiveKind.Line &&
                primitive.StrokeStyle == RenderOverlayStrokeStyle.Dashed) &&
            viewModel.OverlayScene.Primitives.Any(static primitive =>
                primitive.Kind == RenderOverlayPrimitiveKind.SquareMarker));
    }

    [Fact]
    public async Task Interaction_ArcCommand_PointerMoveShowsArcPreviewOverlay()
    {
        var document = new CadDocument();
        var (viewModel, _, runtime) = CreateSystem(
            document,
            additionalHandlers: [new ArcCadCommand()],
            additionalAdapters: [new ArcInteractiveCommandAdapter()]);

        runtime.BeginCommand("ARC");
        await viewModel.InteractionCommand.Execute(CreatePointerDown(0f, 0f)).ToTask();
        await viewModel.InteractionCommand.Execute(CreatePointerDown(4f, 0f)).ToTask();
        await viewModel.InteractionCommand.Execute(CreatePointerMove(4f, 4f)).ToTask();

        await WaitUntilAsync(() =>
            viewModel.OverlayScene.Primitives.Any(static primitive =>
                primitive.Kind == RenderOverlayPrimitiveKind.Line &&
                primitive.StrokeStyle == RenderOverlayStrokeStyle.Dashed) &&
            viewModel.OverlayScene.Primitives.Any(static primitive =>
                primitive.Kind == RenderOverlayPrimitiveKind.Text &&
                primitive.Text is not null &&
                primitive.Text.Contains("Sweep", StringComparison.OrdinalIgnoreCase)));
    }

    [Fact]
    public async Task Interaction_Escape_ClearsTransientOverlaysAndDynamicInput()
    {
        var document = new CadDocument();
        var (viewModel, _, runtime) = CreateSystem(document);

        runtime.BeginCommand("LINE");
        await viewModel.InteractionCommand.Execute(CreatePointerDown(0f, 0f)).ToTask();
        await viewModel.InteractionCommand.Execute(CreatePointerMove(6f, 4f)).ToTask();
        await WaitUntilAsync(() => viewModel.OverlayScene.Primitives.Count > 0);

        await viewModel.InteractionCommand.Execute(CreateKeyDown("Escape")).ToTask();

        await WaitUntilAsync(() =>
            viewModel.OverlayScene.Primitives.Count == 0 &&
            viewModel.ToolVisualHints.Count == 0 &&
            viewModel.DynamicInput is null);
        Assert.False(runtime.State.IsActive);
    }

    [Fact]
    public async Task Interaction_IdleSelectionMode_DoesNotShowCommandHelpOnCanvas()
    {
        var document = new CadDocument();
        var (viewModel, _, runtime) = CreateSystem(document);

        Assert.False(runtime.State.IsActive);
        await viewModel.InteractionCommand.Execute(CreatePointerMove(1f, 1f)).ToTask();

        await WaitUntilAsync(() => viewModel.DynamicInput is null);
        Assert.True(string.IsNullOrWhiteSpace(viewModel.ActiveCommandHelp));
        Assert.DoesNotContain(
            viewModel.ToolVisualHints,
            static hint => string.Equals(hint.Kind, "Prompt", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(
            viewModel.OverlayScene.Primitives,
            static primitive =>
                primitive.Kind == RenderOverlayPrimitiveKind.Text &&
                primitive.Text is not null &&
                primitive.Text.Contains("Type a command", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Interaction_LayoutChange_ClearsTransientOverlays()
    {
        var document = new CadDocument();
        var (viewModel, _, runtime) = CreateSystem(document);

        runtime.BeginCommand("LINE");
        await viewModel.InteractionCommand.Execute(CreatePointerDown(1f, 1f)).ToTask();
        await viewModel.InteractionCommand.Execute(CreatePointerMove(6f, 3f)).ToTask();
        await WaitUntilAsync(() => viewModel.OverlayScene.Primitives.Count > 0);

        var paperLayout = Assert.IsType<CadRenderLayoutViewModel>(
            viewModel.Layouts.FirstOrDefault(static layout => layout.IsPaperSpace));
        viewModel.SelectedLayout = paperLayout;

        await WaitUntilAsync(() =>
            viewModel.OverlayScene.Primitives.Count == 0 &&
            viewModel.ToolVisualHints.Count == 0 &&
            viewModel.DynamicInput is null);
    }

    [Fact]
    public async Task Interaction_SnapHover_ShowsSnapMarkerAdorner()
    {
        var document = new CadDocument();
        document.Entities.Add(new Line
        {
            StartPoint = new XYZ(0, 0, 0),
            EndPoint = new XYZ(8, 0, 0)
        });

        var (viewModel, _, _) = CreateSystem(document);
        await viewModel.InteractionCommand.Execute(CreatePointerMove(0.2f, 0.1f)).ToTask();

        await WaitUntilAsync(() =>
            viewModel.OverlayScene.Primitives.Any(static primitive =>
                primitive.Kind is RenderOverlayPrimitiveKind.DiamondMarker or RenderOverlayPrimitiveKind.SquareMarker or RenderOverlayPrimitiveKind.PointMarker));
    }

    [Fact]
    public async Task Interaction_LineCommand_ShowsDynamicDimensionAndTokenCalloutHints()
    {
        var document = new CadDocument();
        var (viewModel, _, runtime) = CreateSystem(document);

        runtime.BeginCommand("LINE");
        await viewModel.InteractionCommand.Execute(CreatePointerDown(0f, 0f)).ToTask();
        await viewModel.InteractionCommand.Execute(CreatePointerMove(6f, 4f)).ToTask();

        await WaitUntilAsync(() =>
            viewModel.OverlayScene.Primitives.Any(static primitive =>
                primitive.Kind == RenderOverlayPrimitiveKind.Text &&
                primitive.Text is not null &&
                primitive.Text.Contains("<", StringComparison.Ordinal)) &&
            viewModel.OverlayScene.Primitives.Any(static primitive =>
                primitive.Kind == RenderOverlayPrimitiveKind.Text &&
                primitive.Text is not null &&
                primitive.Text.Contains("[", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task Interaction_LineCommand_SurfacesUiVisualHelperBadges()
    {
        var document = new CadDocument();
        var (viewModel, _, runtime) = CreateSystem(document);

        runtime.BeginCommand("LINE");
        await viewModel.InteractionCommand.Execute(CreatePointerMove(4f, 2f)).ToTask();

        await WaitUntilAsync(() =>
            viewModel.ActiveVisualHelpers.Count > 0 &&
            viewModel.ActiveVisualHelpers.Any(static badge => badge.Key == "tool") &&
            viewModel.ActiveVisualHelpers.Any(static badge => badge.Key == "preview"));
    }

    [Fact]
    public async Task Interaction_ActiveCommand_SurfacesCanvasCompletionChips()
    {
        var document = new CadDocument();
        var (viewModel, _, runtime) = CreateSystem(
            document,
            additionalHandlers: [new KeywordPromptRenderTestCommand()]);

        runtime.BeginCommand("KWTEST");

        await WaitUntilAsync(() =>
            viewModel.HasCanvasCompletions &&
            viewModel.CanvasCompletions.Any(static item =>
                item.Kind == "Keyword" &&
                string.Equals(item.Value, "MODE", StringComparison.OrdinalIgnoreCase)));
    }

    [Fact]
    public async Task Interaction_CanvasCompletionChip_InjectsKeywordThroughRuntimePath()
    {
        var document = new CadDocument();
        var (viewModel, _, runtime) = CreateSystem(
            document,
            additionalHandlers: [new KeywordPromptRenderTestCommand()]);

        runtime.BeginCommand("KWTEST");
        await WaitUntilAsync(() =>
            viewModel.CanvasCompletions.Any(static item =>
                string.Equals(item.Value, "MODE", StringComparison.OrdinalIgnoreCase)));
        var completion = viewModel.CanvasCompletions.First(static item =>
            string.Equals(item.Value, "MODE", StringComparison.OrdinalIgnoreCase));

        await viewModel.ApplyCanvasCompletionCommand.Execute(completion).ToTask();

        await WaitUntilAsync(() =>
            runtime.State.IsActive &&
            string.Equals(runtime.State.ActiveCommand, "KWTEST", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Interaction_AllToolCommands_SurfaceCanvasAndUiVisualHelpers()
    {
        var document = new CadDocument();
        document.Entities.Add(new Line
        {
            StartPoint = new XYZ(0, 0, 0),
            EndPoint = new XYZ(12, 0, 0)
        });

        var (viewModel, _, runtime) = CreateSystem(document);
        foreach (var command in ToolPanelCommands)
        {
            runtime.BeginCommand(command);
            await viewModel.InteractionCommand.Execute(CreatePointerMove(6f, 4f)).ToTask();

            await WaitUntilAsync(() =>
                    viewModel.ToolVisualHints.Count > 0 &&
                    viewModel.ActiveVisualHelpers.Count > 0 &&
                    viewModel.ActiveVisualHelpers.Any(static badge => badge.Key == "tool"),
                timeoutMs: 500);

            Assert.NotEmpty(viewModel.OverlayScene.Primitives);
            await viewModel.InteractionCommand.Execute(CreateKeyDown("Escape")).ToTask();
        }
    }

    [Fact]
    public async Task Interaction_BreakCommand_ShowsInteractivePreview()
    {
        var document = new CadDocument();
        document.Entities.Add(new Line
        {
            StartPoint = new XYZ(0, 0, 0),
            EndPoint = new XYZ(12, 0, 0)
        });

        var (viewModel, _, runtime) = CreateSystem(
            document,
            additionalHandlers: [new BreakCadCommand()],
            additionalAdapters: [new BreakInteractiveCommandAdapter()]);

        await viewModel.InteractionCommand.Execute(CreatePointerDown(0f, 0f)).ToTask();
        runtime.BeginCommand("BREAK");
        await viewModel.InteractionCommand.Execute(CreatePointerMove(5f, 1f)).ToTask();

        await WaitUntilAsync(() =>
            viewModel.OverlayScene.Primitives.Any(static primitive =>
                primitive.Kind == RenderOverlayPrimitiveKind.Text &&
                primitive.Text is not null &&
                primitive.Text.Contains("Break", StringComparison.OrdinalIgnoreCase)));
    }

    [Fact]
    public async Task Interaction_TrimCommand_ShowsInteractivePreview()
    {
        var document = new CadDocument();
        document.Entities.Add(new Line
        {
            StartPoint = new XYZ(0, 0, 0),
            EndPoint = new XYZ(12, 0, 0)
        });
        document.Entities.Add(new Line
        {
            StartPoint = new XYZ(6, -4, 0),
            EndPoint = new XYZ(6, 4, 0)
        });

        var (viewModel, _, runtime) = CreateSystem(
            document,
            additionalHandlers: [new TrimCadCommand()],
            additionalAdapters: [new TrimInteractiveCommandAdapter()]);

        await viewModel.InteractionCommand.Execute(CreatePointerDown(1f, 0f)).ToTask();
        await viewModel.InteractionCommand.Execute(CreatePointerDown(6f, 1f, CadInteractionModifiers.Shift)).ToTask();
        runtime.BeginCommand("TRIM");
        await viewModel.InteractionCommand.Execute(CreatePointerMove(10f, 0f)).ToTask();

        await WaitUntilAsync(() =>
            viewModel.OverlayScene.Primitives.Any(static primitive =>
                primitive.Kind == RenderOverlayPrimitiveKind.Text &&
                primitive.Text is not null &&
                primitive.Text.Contains("TRIM", StringComparison.OrdinalIgnoreCase)));
    }

    [Fact]
    public async Task Interaction_EraseCommand_ShowsSelectionCommitPreview()
    {
        var document = new CadDocument();
        document.Entities.Add(new Line
        {
            StartPoint = new XYZ(0, 0, 0),
            EndPoint = new XYZ(12, 0, 0)
        });

        var (viewModel, _, runtime) = CreateSystem(
            document,
            additionalHandlers: [new EraseCadCommand()],
            additionalAdapters: [new EraseInteractiveCommandAdapter()]);

        await viewModel.InteractionCommand.Execute(CreatePointerDown(0f, 0f)).ToTask();
        runtime.BeginCommand("ERASE");
        await viewModel.InteractionCommand.Execute(CreatePointerMove(4f, 1f)).ToTask();

        await WaitUntilAsync(() =>
            viewModel.OverlayScene.Primitives.Any(static primitive =>
                primitive.Kind == RenderOverlayPrimitiveKind.Text &&
                primitive.Text is not null &&
                primitive.Text.Contains("ERASE", StringComparison.OrdinalIgnoreCase)));
    }

    [Fact]
    public async Task Interaction_DragSelection_ShowsWindowAdornerAndSelectsEntities()
    {
        var document = new CadDocument();
        var inside = new Line
        {
            StartPoint = new XYZ(0, 0, 0),
            EndPoint = new XYZ(10, 0, 0)
        };
        var outside = new Line
        {
            StartPoint = new XYZ(30, 0, 0),
            EndPoint = new XYZ(40, 0, 0)
        };
        document.Entities.Add(inside);
        document.Entities.Add(outside);

        var (viewModel, session, _) = CreateSystem(document);
        await viewModel.InteractionCommand.Execute(CreatePointerDown(-5f, -5f)).ToTask();
        await viewModel.InteractionCommand.Execute(CreatePointerMove(12f, 2f, CadInteractionPointerButtons.Left)).ToTask();

        Assert.Contains(
            viewModel.OverlayScene.Primitives,
            static primitive => primitive.Kind == RenderOverlayPrimitiveKind.FilledRectangle);

        await viewModel.InteractionCommand.Execute(CreatePointerUp(12f, 2f)).ToTask();

        await WaitUntilAsync(() =>
        {
            var selected = session.SelectionSet.Items.OfType<Line>().ToArray();
            return selected.Length == 1 &&
                   System.Math.Abs(selected[0].StartPoint.X - inside.StartPoint.X) < 1e-6 &&
                   System.Math.Abs(selected[0].EndPoint.X - inside.EndPoint.X) < 1e-6;
        });
    }

    [Fact]
    public async Task Interaction_AltDragLasso_SelectsEntitiesInsideLassoRegion()
    {
        var document = new CadDocument();
        var inside = new Line
        {
            StartPoint = new XYZ(2, 2, 0),
            EndPoint = new XYZ(4, 2, 0)
        };
        var outside = new Line
        {
            StartPoint = new XYZ(30, 30, 0),
            EndPoint = new XYZ(35, 30, 0)
        };
        document.Entities.Add(inside);
        document.Entities.Add(outside);

        var (viewModel, session, _) = CreateSystem(document);
        await viewModel.InteractionCommand.Execute(CreatePointerDown(-1f, -1f, CadInteractionModifiers.Alt)).ToTask();
        await viewModel.InteractionCommand.Execute(CreatePointerMove(8f, -1f, CadInteractionPointerButtons.Left)).ToTask();
        await viewModel.InteractionCommand.Execute(CreatePointerMove(8f, 6f, CadInteractionPointerButtons.Left)).ToTask();
        await viewModel.InteractionCommand.Execute(CreatePointerMove(-1f, 6f, CadInteractionPointerButtons.Left)).ToTask();
        await viewModel.InteractionCommand.Execute(CreatePointerUp(-1f, -1f)).ToTask();

        await WaitUntilAsync(() =>
        {
            var selected = session.SelectionSet.Items.OfType<Line>().ToArray();
            return selected.Length == 1 &&
                   ReferenceEquals(selected[0], inside);
        });
    }

    [Fact]
    public async Task Interaction_AltShiftDragFence_SelectsEntitiesCrossingFence()
    {
        var document = new CadDocument();
        var crossing = new Line
        {
            StartPoint = new XYZ(5, -2, 0),
            EndPoint = new XYZ(5, 3, 0)
        };
        var outside = new Line
        {
            StartPoint = new XYZ(0, 10, 0),
            EndPoint = new XYZ(8, 10, 0)
        };
        document.Entities.Add(crossing);
        document.Entities.Add(outside);

        var (viewModel, session, _) = CreateSystem(document);
        await viewModel.InteractionCommand.Execute(CreatePointerDown(0f, 0f, CadInteractionModifiers.Alt | CadInteractionModifiers.Shift)).ToTask();
        await viewModel.InteractionCommand.Execute(CreatePointerMove(10f, 0f, CadInteractionPointerButtons.Left)).ToTask();
        await viewModel.InteractionCommand.Execute(CreatePointerUp(10f, 0f)).ToTask();

        await WaitUntilAsync(() =>
        {
            var selected = session.SelectionSet.Items.OfType<Line>().ToArray();
            return selected.Length == 1 &&
                   ReferenceEquals(selected[0], crossing);
        });
    }

    [Fact]
    public async Task Interaction_AltCtrlDragPolygon_SelectsOnlyEntitiesFullyInsidePolygon()
    {
        var document = new CadDocument();
        var inside = new Line
        {
            StartPoint = new XYZ(2, 2, 0),
            EndPoint = new XYZ(6, 2, 0)
        };
        var partial = new Line
        {
            StartPoint = new XYZ(8, 8, 0),
            EndPoint = new XYZ(12, 8, 0)
        };
        document.Entities.Add(inside);
        document.Entities.Add(partial);

        var (viewModel, session, _) = CreateSystem(document);
        await viewModel.InteractionCommand.Execute(CreatePointerDown(0f, 0f, CadInteractionModifiers.Alt | CadInteractionModifiers.Control)).ToTask();
        await viewModel.InteractionCommand.Execute(CreatePointerMove(10f, 0f, CadInteractionPointerButtons.Left)).ToTask();
        await viewModel.InteractionCommand.Execute(CreatePointerMove(10f, 10f, CadInteractionPointerButtons.Left)).ToTask();
        await viewModel.InteractionCommand.Execute(CreatePointerMove(0f, 10f, CadInteractionPointerButtons.Left)).ToTask();
        await viewModel.InteractionCommand.Execute(CreatePointerUp(0f, 0f)).ToTask();

        await WaitUntilAsync(() =>
        {
            var selected = session.SelectionSet.Items.OfType<Line>().ToArray();
            return selected.Length == 1 &&
                   ReferenceEquals(selected[0], inside);
        });
    }

    [Fact]
    public async Task Interaction_TypedAlias_EnterStartsInteractiveLineWorkflow()
    {
        var document = new CadDocument();
        var (viewModel, _, runtime) = CreateSystem(document);

        await viewModel.InteractionCommand.Execute(CreateTextInput("L")).ToTask();
        await viewModel.InteractionCommand.Execute(CreateKeyDown("Enter")).ToTask();
        Assert.True(
            runtime.State.IsActive &&
            string.Equals(runtime.State.ActiveCommand, "LINE", StringComparison.OrdinalIgnoreCase),
            $"Expected LINE command to be active after Enter; actual active='{runtime.State.ActiveCommand ?? "<null>"}', isActive={runtime.State.IsActive}.");
        await viewModel.InteractionCommand.Execute(CreatePointerDown(1f, 1f)).ToTask();
        Assert.True(
            runtime.State.IsActive,
            "Expected command to remain active after first point pick.");
        await viewModel.InteractionCommand.Execute(CreatePointerDown(6f, 1f)).ToTask();

        await WaitUntilAsync(() => document.Entities.OfType<Line>().Any());
    }

    [Fact]
    public async Task Interaction_CtrlZAndCtrlY_PerformUndoRedo()
    {
        var document = new CadDocument();
        var (viewModel, _, runtime) = CreateSystem(
            document,
            additionalHandlers:
            [
                new UndoCadCommand(),
                new RedoCadCommand()
            ]);

        runtime.BeginCommand("LINE");
        await viewModel.InteractionCommand.Execute(CreatePointerDown(0f, 0f)).ToTask();
        await viewModel.InteractionCommand.Execute(CreatePointerDown(8f, 0f)).ToTask();
        await WaitUntilAsync(() => document.Entities.OfType<Line>().Count() == 1);

        await viewModel.InteractionCommand.Execute(CreateKeyDown("Z", CadInteractionModifiers.Control)).ToTask();
        await WaitUntilAsync(() => !document.Entities.OfType<Line>().Any());

        await viewModel.InteractionCommand.Execute(CreateKeyDown("Y", CadInteractionModifiers.Control)).ToTask();
        await WaitUntilAsync(() => document.Entities.OfType<Line>().Count() == 1);
    }

    [Fact]
    public async Task Interaction_DeleteShortcut_ErasesSelectedEntity()
    {
        var document = new CadDocument();
        document.Entities.Add(new Line
        {
            StartPoint = new XYZ(0, 0, 0),
            EndPoint = new XYZ(8, 0, 0)
        });

        var (viewModel, _, _) = CreateSystem(
            document,
            additionalHandlers: [new EraseCadCommand()]);

        await viewModel.InteractionCommand.Execute(CreatePointerDown(0f, 0f)).ToTask();
        await viewModel.InteractionCommand.Execute(CreateKeyDown("Delete")).ToTask();

        await WaitUntilAsync(() => !document.Entities.OfType<Line>().Any());
    }

    [Fact]
    public async Task Interaction_CtrlA_SelectsAllVisibleEntities()
    {
        var document = new CadDocument();
        document.Entities.Add(new Line
        {
            StartPoint = new XYZ(0, 0, 0),
            EndPoint = new XYZ(6, 0, 0)
        });
        document.Entities.Add(new Line
        {
            StartPoint = new XYZ(10, 0, 0),
            EndPoint = new XYZ(16, 0, 0)
        });

        var (viewModel, session, _) = CreateSystem(document);
        await viewModel.InteractionCommand.Execute(CreateKeyDown("A", CadInteractionModifiers.Control)).ToTask();

        await WaitUntilAsync(() =>
            session.SelectionSet.Items.OfType<Line>().Count() == 2);
    }

    [Fact]
    public async Task Interaction_ShortcutMatrix_StartsDrawAndModifyCommands()
    {
        var document = new CadDocument();
        var (viewModel, _, runtime) = CreateSystem(
            document,
            additionalHandlers: [new MoveCadCommand()]);

        await viewModel.InteractionCommand.Execute(CreateKeyDown("F1", CadInteractionModifiers.Control | CadInteractionModifiers.Shift)).ToTask();
        await WaitUntilAsync(() =>
            runtime.State.IsActive &&
            string.Equals(runtime.State.ActiveCommand, "LINE", System.StringComparison.OrdinalIgnoreCase));

        await viewModel.InteractionCommand.Execute(CreateKeyDown("Escape")).ToTask();

        await viewModel.InteractionCommand.Execute(CreateKeyDown("F1", CadInteractionModifiers.Control | CadInteractionModifiers.Alt)).ToTask();
        await WaitUntilAsync(() =>
            runtime.State.IsActive &&
            string.Equals(runtime.State.ActiveCommand, "MOVE", System.StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Interaction_MinimalShortcutProfile_DisablesFunctionCommandMatrix()
    {
        var document = new CadDocument();
        var (viewModel, _, runtime) = CreateSystem(
            document,
            shortcutProfile: CadShortcutProfile.Minimal);

        await viewModel.InteractionCommand.Execute(CreateKeyDown("F1", CadInteractionModifiers.Control | CadInteractionModifiers.Shift)).ToTask();

        await WaitUntilAsync(() => !runtime.State.IsActive);
    }

    [Fact]
    public async Task Interaction_ShortcutConflictResolution_PrefersScopeSpecificBindings()
    {
        var document = new CadDocument();
        var customBindings = new[]
        {
            new CadShortcutBinding(
                Gesture: new CadShortcutGesture("Q", CadInteractionModifiers.Control),
                Action: CadShortcutActionKind.Command,
                CommandName: "CIRCLE",
                Scope: CadShortcutScope.Always,
                TransparentWhenCommandActive: false),
            new CadShortcutBinding(
                Gesture: new CadShortcutGesture("Q", CadInteractionModifiers.Control),
                Action: CadShortcutActionKind.Command,
                CommandName: "LINE",
                Scope: CadShortcutScope.CommandInactiveOnly,
                TransparentWhenCommandActive: false),
            new CadShortcutBinding(
                Gesture: new CadShortcutGesture("Q", CadInteractionModifiers.Control),
                Action: CadShortcutActionKind.Command,
                CommandName: "TEXT",
                Scope: CadShortcutScope.CommandActiveOnly,
                TransparentWhenCommandActive: false,
                Priority: 50)
        };

        var (viewModel, _, runtime) = CreateSystem(
            document,
            additionalHandlers: [new CircleCadCommand()],
            shortcutBindings: customBindings);

        await viewModel.InteractionCommand.Execute(CreateKeyDown("Q", CadInteractionModifiers.Control)).ToTask();
        Assert.True(
            runtime.State.IsActive &&
            string.Equals(runtime.State.ActiveCommand, "LINE", System.StringComparison.OrdinalIgnoreCase),
            $"Expected LINE when command was inactive, got '{runtime.State.ActiveCommand ?? "<null>"}'.");

        await viewModel.InteractionCommand.Execute(CreateKeyDown("Q", CadInteractionModifiers.Control)).ToTask();
        Assert.True(
            runtime.State.IsActive &&
            string.Equals(runtime.State.ActiveCommand, "TEXT", System.StringComparison.OrdinalIgnoreCase),
            $"Expected TEXT when command was active, got '{runtime.State.ActiveCommand ?? "<null>"}'.");
    }

    [Fact]
    public async Task Interaction_DeleteShortcut_DoesNotEraseWhileCommandIsActive()
    {
        var document = new CadDocument();
        document.Entities.Add(new Line
        {
            StartPoint = new XYZ(0, 0, 0),
            EndPoint = new XYZ(8, 0, 0)
        });

        var (viewModel, _, runtime) = CreateSystem(
            document,
            additionalHandlers: [new EraseCadCommand()]);

        runtime.BeginCommand("LINE");
        await viewModel.InteractionCommand.Execute(CreateKeyDown("Delete")).ToTask();

        await WaitUntilAsync(() =>
            runtime.State.IsActive &&
            string.Equals(runtime.State.ActiveCommand, "LINE", System.StringComparison.OrdinalIgnoreCase) &&
            document.Entities.OfType<Line>().Count() == 1);
    }

    [Fact]
    public async Task Interaction_CtrlShiftV_UsesPasteOrigShortcut()
    {
        var document = new CadDocument();
        document.Entities.Add(new Line
        {
            StartPoint = new XYZ(0, 0, 0),
            EndPoint = new XYZ(12, 0, 0)
        });

        var clipboard = new InMemoryCadClipboardService();
        var (viewModel, _, _) = CreateSystem(
            document,
            additionalHandlers:
            [
                new CopyClipCadCommand(clipboard),
                new PasteClipCadCommand(clipboard)
            ]);

        await viewModel.InteractionCommand.Execute(CreatePointerDown(0f, 0f)).ToTask();
        await viewModel.InteractionCommand.Execute(CreateKeyDown("C", CadInteractionModifiers.Control)).ToTask();
        await viewModel.InteractionCommand.Execute(CreateKeyDown("V", CadInteractionModifiers.Control | CadInteractionModifiers.Shift)).ToTask();

        await WaitUntilAsync(() => document.Entities.OfType<Line>().Count() == 2);
    }

    [Fact]
    public async Task Interaction_CustomShortcutBinding_ExecutesConfiguredCommand()
    {
        var document = new CadDocument();
        var customBindings = new[]
        {
            new CadShortcutBinding(
                Gesture: new CadShortcutGesture("Q", CadInteractionModifiers.Control),
                Action: CadShortcutActionKind.Command,
                CommandName: "LINE",
                Scope: CadShortcutScope.CommandInactiveOnly,
                TransparentWhenCommandActive: false)
        };

        var (viewModel, _, runtime) = CreateSystem(
            document,
            shortcutBindings: customBindings);

        await viewModel.InteractionCommand.Execute(CreateKeyDown("Q", CadInteractionModifiers.Control)).ToTask();

        await WaitUntilAsync(() =>
            runtime.State.IsActive &&
            string.Equals(runtime.State.ActiveCommand, "LINE", System.StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Interaction_CtrlCThenCtrlV_CopiesAndPastesSelection()
    {
        var document = new CadDocument();
        document.Entities.Add(new Line
        {
            StartPoint = new XYZ(0, 0, 0),
            EndPoint = new XYZ(12, 0, 0)
        });

        var clipboard = new InMemoryCadClipboardService();
        var (viewModel, _, _) = CreateSystem(
            document,
            additionalHandlers:
            [
                new CopyClipCadCommand(clipboard),
                new PasteClipCadCommand(clipboard)
            ]);

        await viewModel.InteractionCommand.Execute(CreatePointerDown(0f, 0f)).ToTask();
        await viewModel.InteractionCommand.Execute(CreateKeyDown("C", CadInteractionModifiers.Control)).ToTask();
        await viewModel.InteractionCommand.Execute(CreateKeyDown("V", CadInteractionModifiers.Control)).ToTask();

        await WaitUntilAsync(() => document.Entities.OfType<Line>().Count() == 2);
    }

    [Fact]
    public async Task Interaction_DragSelectedEntity_MovesSelectionWithGesture()
    {
        var document = new CadDocument();
        document.Entities.Add(new Line
        {
            StartPoint = new XYZ(0, 0, 0),
            EndPoint = new XYZ(10, 0, 0)
        });

        var (viewModel, _, _) = CreateSystem(
            document,
            additionalHandlers: [new MoveCadCommand()]);

        await viewModel.InteractionCommand.Execute(CreatePointerDown(2.5f, 0f)).ToTask(); // select
        await viewModel.InteractionCommand.Execute(CreatePointerDown(2.5f, 0f)).ToTask(); // drag start on selected
        await viewModel.InteractionCommand.Execute(CreatePointerMove(4f, 3f, CadInteractionPointerButtons.Left)).ToTask();
        await viewModel.InteractionCommand.Execute(CreatePointerUp(4f, 3f)).ToTask();

        await WaitUntilAsync(() =>
        {
            var moved = document.Entities.OfType<Line>().FirstOrDefault();
            return moved is not null &&
                   System.Math.Abs(moved.StartPoint.X - 1.5d) < 1e-6 &&
                   System.Math.Abs(moved.StartPoint.Y - 3d) < 1e-6 &&
                   System.Math.Abs(moved.EndPoint.X - 11.5d) < 1e-6 &&
                   System.Math.Abs(moved.EndPoint.Y - 3d) < 1e-6;
        });
    }

    [Fact]
    public async Task Interaction_DragSelectedEntityWithCtrl_CopiesSelection()
    {
        var document = new CadDocument();
        document.Entities.Add(new Line
        {
            StartPoint = new XYZ(0, 0, 0),
            EndPoint = new XYZ(10, 0, 0)
        });

        var (viewModel, _, _) = CreateSystem(
            document,
            additionalHandlers: [new CopyCadCommand()]);

        await viewModel.InteractionCommand.Execute(CreatePointerDown(2.5f, 0f)).ToTask(); // select
        await viewModel.InteractionCommand.Execute(CreatePointerDown(2.5f, 0f)).ToTask(); // drag start on selected
        await viewModel.InteractionCommand.Execute(CreatePointerMove(6f, 2f, CadInteractionPointerButtons.Left, CadInteractionModifiers.Control)).ToTask();
        await viewModel.InteractionCommand.Execute(CreatePointerUp(6f, 2f)).ToTask();

        await WaitUntilAsync(() => document.Entities.OfType<Line>().Count() == 2);
    }

    [Fact]
    public async Task Interaction_ShiftSpaceCyclesOverlappingSelectionCandidates()
    {
        var document = new CadDocument();
        var horizontal = new Line
        {
            StartPoint = new XYZ(-5, 0, 0),
            EndPoint = new XYZ(5, 0, 0)
        };
        var vertical = new Line
        {
            StartPoint = new XYZ(0, -5, 0),
            EndPoint = new XYZ(0, 5, 0)
        };
        document.Entities.Add(horizontal);
        document.Entities.Add(vertical);

        var (viewModel, session, _) = CreateSystem(document);
        await viewModel.InteractionCommand.Execute(CreatePointerDown(0f, 0f)).ToTask();
        var first = Assert.Single(session.SelectionSet.Items.OfType<Line>());
        var firstIsHorizontal = System.Math.Abs(first.StartPoint.Y - first.EndPoint.Y) < 1e-6;

        await viewModel.InteractionCommand.Execute(CreateKeyDown("Space", CadInteractionModifiers.Shift)).ToTask();

        await WaitUntilAsync(() =>
        {
            var selected = session.SelectionSet.Items.OfType<Line>().ToArray();
            if (selected.Length != 1)
            {
                return false;
            }

            var selectedIsHorizontal = System.Math.Abs(selected[0].StartPoint.Y - selected[0].EndPoint.Y) < 1e-6;
            return selectedIsHorizontal != firstIsHorizontal;
        });
    }

    [Fact]
    public async Task Interaction_AltCtrlClick_RemovesSubSelectionFromCurrentSet()
    {
        var document = new CadDocument();
        var first = new Line
        {
            StartPoint = new XYZ(0, 0, 0),
            EndPoint = new XYZ(10, 0, 0)
        };
        var second = new Line
        {
            StartPoint = new XYZ(0, 8, 0),
            EndPoint = new XYZ(10, 8, 0)
        };
        document.Entities.Add(first);
        document.Entities.Add(second);

        var (viewModel, session, _) = CreateSystem(document);
        await viewModel.InteractionCommand.Execute(CreatePointerDown(1f, 0f)).ToTask();
        await viewModel.InteractionCommand.Execute(CreatePointerDown(1f, 8f, CadInteractionModifiers.Shift)).ToTask();

        Assert.Equal(2, session.SelectionSet.Count);

        await viewModel.InteractionCommand
            .Execute(CreatePointerDown(1f, 8f, CadInteractionModifiers.Alt | CadInteractionModifiers.Control))
            .ToTask();

        var selected = session.SelectionSet.Items.OfType<Line>().ToArray();
        Assert.True(
            selected.Length == 1,
            string.Create(
                System.Globalization.CultureInfo.InvariantCulture,
                $"Expected one selected line after Alt+Ctrl remove; actual count={selected.Length}, status={viewModel.InteractionStatus}."));

        Assert.True(
            System.Math.Abs(selected[0].StartPoint.Y) < 1e-6 &&
            System.Math.Abs(selected[0].EndPoint.Y) < 1e-6,
            string.Create(
                System.Globalization.CultureInfo.InvariantCulture,
                $"Expected remaining line at Y=0, actual startY={selected[0].StartPoint.Y:0.###}, endY={selected[0].EndPoint.Y:0.###}."));
    }

    [Fact]
    public async Task Interaction_AltShiftClick_AddsEntityToSubSelectionSet()
    {
        var document = new CadDocument();
        var first = new Line
        {
            StartPoint = new XYZ(0, 0, 0),
            EndPoint = new XYZ(10, 0, 0)
        };
        var second = new Line
        {
            StartPoint = new XYZ(0, 8, 0),
            EndPoint = new XYZ(10, 8, 0)
        };
        document.Entities.Add(first);
        document.Entities.Add(second);

        var (viewModel, session, _) = CreateSystem(document);
        await viewModel.InteractionCommand.Execute(CreatePointerDown(1f, 0f)).ToTask();
        await viewModel.InteractionCommand.Execute(CreatePointerDown(1f, 8f, CadInteractionModifiers.Alt | CadInteractionModifiers.Shift)).ToTask();

        await WaitUntilAsync(() =>
        {
            var selected = session.SelectionSet.Items.OfType<Line>().ToArray();
            if (selected.Length != 2)
            {
                return false;
            }

            var hasLower = selected.Any(entity =>
                System.Math.Abs(entity.StartPoint.Y) < 1e-6 &&
                System.Math.Abs(entity.EndPoint.Y) < 1e-6);
            var hasUpper = selected.Any(entity =>
                System.Math.Abs(entity.StartPoint.Y - 8d) < 1e-6 &&
                System.Math.Abs(entity.EndPoint.Y - 8d) < 1e-6);
            return hasLower && hasUpper;
        });
    }

    [Fact]
    public async Task Interaction_DragMove_ResolvesParallelConstraint()
    {
        var document = new CadDocument();
        var source = new Line
        {
            StartPoint = new XYZ(0, 0, 0),
            EndPoint = new XYZ(10, 0, 0)
        };
        var target = new Line
        {
            StartPoint = new XYZ(0, 2, 0),
            EndPoint = new XYZ(4, 5, 0)
        };
        document.Entities.Add(source);
        document.Entities.Add(target);

        var (viewModel, session, _) = CreateSystem(
            document,
            additionalHandlers: [new MoveCadCommand()]);

        Assert.True(session.EntityIndex.TryGetId(source, out var sourceId));
        Assert.True(session.EntityIndex.TryGetId(target, out var targetId));
        session.Constraints.AddConstraint(
            CadConstraintKind.Parallel,
            [
                new CadConstraintReference(sourceId),
                new CadConstraintReference(targetId)
            ]);

        await viewModel.InteractionCommand.Execute(CreatePointerDown(1f, 2.5f)).ToTask(); // select target
        await viewModel.InteractionCommand.Execute(CreatePointerDown(1f, 2.5f)).ToTask(); // start drag
        await viewModel.InteractionCommand.Execute(CreatePointerMove(6f, 7f, CadInteractionPointerButtons.Left)).ToTask();
        await viewModel.InteractionCommand.Execute(CreatePointerUp(6f, 7f)).ToTask();

        await WaitUntilAsync(() => System.Math.Abs(target.StartPoint.Y - target.EndPoint.Y) < 1e-3);
    }

    private static (CadRenderViewModel ViewModel, ICadEditorSession Session, ICadCommandRuntime Runtime) CreateSystem(
        CadDocument document,
        IReadOnlyList<ICadCommandHandler>? additionalHandlers = null,
        IReadOnlyList<ICadInteractiveCommandAdapter>? additionalAdapters = null,
        IReadOnlyList<CadShortcutBinding>? shortcutBindings = null,
        CadShortcutProfile shortcutProfile = CadShortcutProfile.AutoCadLike)
    {
        var (viewModel, session, runtime, _) = CreateSystemWithHost(
            document,
            additionalHandlers,
            additionalAdapters,
            shortcutBindings,
            shortcutProfile);
        return (viewModel, session, runtime);
    }

    private static (
        CadRenderViewModel ViewModel,
        ICadEditorSession Session,
        ICadCommandRuntime Runtime,
        CadEditorSessionHostService SessionHost) CreateSystemWithHost(
        CadDocument document,
        IReadOnlyList<ICadCommandHandler>? additionalHandlers = null,
        IReadOnlyList<ICadInteractiveCommandAdapter>? additionalAdapters = null,
        IReadOnlyList<CadShortcutBinding>? shortcutBindings = null,
        CadShortcutProfile shortcutProfile = CadShortcutProfile.AutoCadLike)
    {
        var sceneBuilder = CreateSceneBuilder();
        var settings = new CadRenderSceneSettings();
        var initialScene = sceneBuilder.Build(document, settings);

        var selectionService = new CadSelectionService();
        var focusService = new CadSelectionFocusService();
        var documentContext = new CadDocumentContextService();
        var sessionFactory = new CadEditorSessionFactory();
        var sessionHost = new CadEditorSessionHostService(sessionFactory, documentContext, selectionService);
        var commandRegistry = CreateRegistry(additionalHandlers);
        var runtime = new CadCommandRuntime(commandRegistry, new CadCommandIntellisenseService(commandRegistry));

        var adapters = new List<ICadInteractiveCommandAdapter>
        {
            new LineInteractiveCommandAdapter(),
            new TextInteractiveCommandAdapter()
        };
        if (additionalAdapters is not null)
        {
            adapters.AddRange(additionalAdapters);
        }
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
            shortcutBindings ?? CadShortcutBindingCatalog.Create(shortcutProfile),
            collaborationWorkspace: null,
            statsExportService: null,
            statsFileName: null);

        var session = sessionHost.GetOrCreate(document);
        return (viewModel, session, runtime, sessionHost);
    }

    private static readonly string[] ToolPanelCommands =
    [
        "LINE",
        "PLINE",
        "XLINE",
        "RAY",
        "CIRCLE",
        "ARC",
        "ELLIPSE",
        "SPLINE",
        "POLYGON",
        "RECTANG",
        "POINT",
        "INSERT",
        "HATCH",
        "BOUNDARY",
        "MOVE",
        "COPY",
        "ROTATE",
        "SCALE",
        "MIRROR",
        "STRETCH",
        "ERASE",
        "OFFSET",
        "TRIM",
        "EXTEND",
        "BREAK",
        "JOIN",
        "FILLET",
        "CHAMFER",
        "ARRAY",
        "EXPLODE",
        "ALIGN",
        "MATCHPROP",
        "COPYCLIP",
        "CUT",
        "PASTECLIP",
        "TEXT",
        "MTEXT",
        "DIMLINEAR",
        "DIMALIGNED",
        "DIMRADIUS",
        "DIMDIAMETER",
        "DIMANGULAR",
        "LEADER",
        "MLEADER"
    ];

    private static ICadCommandRegistry CreateRegistry(IReadOnlyList<ICadCommandHandler>? additionalHandlers = null)
    {
        var registry = new CadCommandRegistry();
        var handlers = new List<ICadCommandHandler>
        {
            new LineCadCommand(),
            new TextCadCommand()
        };

        if (additionalHandlers is not null)
        {
            handlers.AddRange(additionalHandlers);
        }

        foreach (var handler in handlers)
        {
            registry.Register(handler);
        }

        return registry;
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

    private static CadInteractionEvent CreatePointerDown(
        float x,
        float y,
        CadInteractionModifiers modifiers = CadInteractionModifiers.None)
    {
        var point = new Vector2(x, y);
        return new CadInteractionEvent(
            Kind: CadInteractionEventKind.PointerDown,
            WorldPoint: point,
            ScreenPoint: point,
            Modifiers: modifiers,
            PointerButtons: CadInteractionPointerButtons.Left,
            Tolerance: 2f,
            WheelDelta: 0f,
            Key: null,
            Text: null);
    }

    private static CadInteractionEvent CreatePointerUp(float x, float y)
    {
        var point = new Vector2(x, y);
        return new CadInteractionEvent(
            Kind: CadInteractionEventKind.PointerUp,
            WorldPoint: point,
            ScreenPoint: point,
            Modifiers: CadInteractionModifiers.None,
            PointerButtons: CadInteractionPointerButtons.Left,
            Tolerance: 2f,
            WheelDelta: 0f,
            Key: null,
            Text: null);
    }

    private static CadInteractionEvent CreatePointerMove(
        float x,
        float y,
        CadInteractionPointerButtons pointerButtons = CadInteractionPointerButtons.None,
        CadInteractionModifiers modifiers = CadInteractionModifiers.None)
    {
        var point = new Vector2(x, y);
        return new CadInteractionEvent(
            Kind: CadInteractionEventKind.PointerMove,
            WorldPoint: point,
            ScreenPoint: point,
            Modifiers: modifiers,
            PointerButtons: pointerButtons,
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

    private static CadInteractionEvent CreateKeyDown(string key, CadInteractionModifiers modifiers = CadInteractionModifiers.None)
    {
        return new CadInteractionEvent(
            Kind: CadInteractionEventKind.KeyDown,
            WorldPoint: Vector2.Zero,
            ScreenPoint: Vector2.Zero,
            Modifiers: modifiers,
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

        Assert.True(condition(), "Timed out while waiting for interactive create/render pipeline.");
    }

    private static bool TryGetGripMarkerMaxX(CadRenderViewModel viewModel, out float maxX)
    {
        maxX = float.NegativeInfinity;
        var found = false;
        foreach (var primitive in viewModel.OverlayScene.Primitives)
        {
            if (primitive.Kind != RenderOverlayPrimitiveKind.SquareMarker ||
                primitive.FillColor is null)
            {
                continue;
            }

            maxX = found ? MathF.Max(maxX, primitive.Start.X) : primitive.Start.X;
            found = true;
        }

        return found;
    }

    private static int CountUniqueGripMarkerPositions(CadRenderViewModel viewModel)
    {
        var unique = new HashSet<(int X, int Y)>();
        foreach (var marker in GetGripMarkers(viewModel))
        {
            unique.Add((
                X: (int)MathF.Round(marker.X * 1_000f),
                Y: (int)MathF.Round(marker.Y * 1_000f)));
        }

        return unique.Count;
    }

    private static IReadOnlyList<Vector2> GetGripMarkers(CadRenderViewModel viewModel)
    {
        var markers = new List<Vector2>();
        foreach (var primitive in viewModel.OverlayScene.Primitives)
        {
            if (primitive.Kind != RenderOverlayPrimitiveKind.SquareMarker || primitive.FillColor is null)
            {
                continue;
            }

            markers.Add(primitive.Start);
        }

        return markers;
    }

    private static bool TryGetSceneTextBounds(CadRenderViewModel viewModel, out RenderBounds bounds)
    {
        bounds = RenderBounds.Empty;
        if (viewModel.Scene is null)
        {
            return false;
        }

        foreach (var primitive in viewModel.Scene.Layers.SelectMany(static layer => layer.Primitives))
        {
            if (primitive is not RenderText renderText)
            {
                continue;
            }

            bounds = bounds.IsEmpty ? renderText.Bounds : bounds.Expand(renderText.Bounds);
        }

        return !bounds.IsEmpty;
    }

    private sealed class KeywordPromptRenderTestCommand : ICadDescribedCommandHandler
    {
        public string Name => "KWTEST";
        public IReadOnlyList<string> Aliases => ["KWT"];
        public CadCommandDescriptor Descriptor => new(
            Name: "KWTEST",
            Aliases: ["KWT"],
            Description: "Keyword prompt command",
            Syntaxes:
            [
                new CadCommandSyntax(
                    Usage: "KWTEST [keyword]",
                    Description: "Keyword test command",
                    Parameters: Array.Empty<CadCommandParameterDescriptor>(),
                    Keywords:
                    [
                        new CadCommandKeywordDescriptor("MODE", "Switch mode"),
                        new CadCommandKeywordDescriptor("UNDO", "Undo token")
                    ],
                    BranchId: "default")
            ]);

        public bool CanExecute(CadCommandContext context)
        {
            return true;
        }

        public ValueTask<CadCommandResult> ExecuteAsync(CadCommandContext context)
        {
            return ValueTask.FromResult(CadCommandResult.Ok("KWTEST executed"));
        }
    }
}
