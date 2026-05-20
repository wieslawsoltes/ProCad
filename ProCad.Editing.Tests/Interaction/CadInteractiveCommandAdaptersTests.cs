using System.Linq;
using ProCad.Editing.Commands;
using ProCad.Editing.Interaction;
using ProCad.Editing.Prompt;
using ProCad.Editing.Sessions;
using ACadSharp;
using ACadSharp.Entities;
using Xunit;

namespace ProCad.Editing.Tests.Interaction;

public sealed class CadInteractiveCommandAdaptersTests
{
    [Theory]
    [InlineData("F", "FILLET")]
    [InlineData("CHA", "CHAMFER")]
    [InlineData("AR", "ARRAY")]
    [InlineData("AL", "ALIGN")]
    [InlineData("MA", "MATCHPROP")]
    [InlineData("I", "INSERT")]
    public void AdapterRegistry_ResolvesCommonAliases(string alias, string expectedCommand)
    {
        var registry = new CadInteractiveCommandAdapterRegistry(
        [
            new FilletInteractiveCommandAdapter(),
            new ChamferInteractiveCommandAdapter(),
            new ArrayInteractiveCommandAdapter(),
            new AlignInteractiveCommandAdapter(),
            new MatchPropInteractiveCommandAdapter(),
            new InsertInteractiveCommandAdapter()
        ]);

        var resolved = registry.TryGet(alias, out var adapter);

        Assert.True(resolved);
        Assert.NotNull(adapter);
        Assert.Equal(expectedCommand, adapter.CommandName);
    }

    [Fact]
    public async Task LineAdapter_CommitsAfterSecondPickedPoint()
    {
        var commandRegistry = new CadCommandRegistry();
        commandRegistry.Register(new LineCadCommand());
        var intellisense = new CadCommandIntellisenseService(commandRegistry);
        var runtime = new CadCommandRuntime(commandRegistry, intellisense);
        var adapter = new LineInteractiveCommandAdapter();
        var session = new CadEditorSessionFactory().Create(new CadDocument());

        runtime.BeginCommand("LINE");

        var first = await adapter.SubmitAsync(
            runtime,
            new CadPromptToken(CadPromptTokenType.Coordinate, "0,0"),
            session,
            commit: false);
        var second = await adapter.SubmitAsync(
            runtime,
            new CadPromptToken(CadPromptTokenType.Coordinate, "10,0"),
            session,
            commit: false);

        Assert.True(first.Handled);
        Assert.Null(first.Result);
        Assert.True(second.Result?.Success);
        Assert.Contains("Created LINE", second.Result?.Message, StringComparison.OrdinalIgnoreCase);
        Assert.False(runtime.State.IsActive);
    }

    [Fact]
    public async Task CircleAdapter_ConvertsSecondPickToRadiusAndCommits()
    {
        var commandRegistry = new CadCommandRegistry();
        commandRegistry.Register(new CircleCadCommand());
        var intellisense = new CadCommandIntellisenseService(commandRegistry);
        var runtime = new CadCommandRuntime(commandRegistry, intellisense);
        var adapter = new CircleInteractiveCommandAdapter();
        var session = new CadEditorSessionFactory().Create(new CadDocument());

        runtime.BeginCommand("CIRCLE");

        var first = await adapter.SubmitAsync(
            runtime,
            new CadPromptToken(CadPromptTokenType.Coordinate, "0,0"),
            session,
            commit: false);
        var second = await adapter.SubmitAsync(
            runtime,
            new CadPromptToken(CadPromptTokenType.Coordinate, "0,5"),
            session,
            commit: false);

        Assert.True(first.Handled);
        Assert.Null(first.Result);
        Assert.True(second.Result?.Success);
        Assert.Contains("Created CIRCLE", second.Result?.Message, StringComparison.OrdinalIgnoreCase);
        Assert.False(runtime.State.IsActive);
    }

    [Fact]
    public async Task XLineAdapter_CommitsAfterSecondPickedPoint()
    {
        var commandRegistry = new CadCommandRegistry();
        commandRegistry.Register(new XLineCadCommand());
        var intellisense = new CadCommandIntellisenseService(commandRegistry);
        var runtime = new CadCommandRuntime(commandRegistry, intellisense);
        var adapter = new XLineInteractiveCommandAdapter();
        var session = new CadEditorSessionFactory().Create(new CadDocument());

        runtime.BeginCommand("XLINE");

        var first = await adapter.SubmitAsync(
            runtime,
            new CadPromptToken(CadPromptTokenType.Coordinate, "0,0"),
            session,
            commit: false);
        var second = await adapter.SubmitAsync(
            runtime,
            new CadPromptToken(CadPromptTokenType.Coordinate, "10,0"),
            session,
            commit: false);

        Assert.True(first.Handled);
        Assert.Null(first.Result);
        Assert.True(second.Result?.Success);
        Assert.Contains("Created XLINE", second.Result?.Message, StringComparison.OrdinalIgnoreCase);
        Assert.False(runtime.State.IsActive);
    }

    [Fact]
    public async Task RayAdapter_CommitsAfterSecondPickedPoint()
    {
        var commandRegistry = new CadCommandRegistry();
        commandRegistry.Register(new RayCadCommand());
        var intellisense = new CadCommandIntellisenseService(commandRegistry);
        var runtime = new CadCommandRuntime(commandRegistry, intellisense);
        var adapter = new RayInteractiveCommandAdapter();
        var session = new CadEditorSessionFactory().Create(new CadDocument());

        runtime.BeginCommand("RAY");

        var first = await adapter.SubmitAsync(
            runtime,
            new CadPromptToken(CadPromptTokenType.Coordinate, "0,0"),
            session,
            commit: false);
        var second = await adapter.SubmitAsync(
            runtime,
            new CadPromptToken(CadPromptTokenType.Coordinate, "3,4"),
            session,
            commit: false);

        Assert.True(first.Handled);
        Assert.Null(first.Result);
        Assert.True(second.Result?.Success);
        Assert.Contains("Created RAY", second.Result?.Message, StringComparison.OrdinalIgnoreCase);
        Assert.False(runtime.State.IsActive);
    }

    [Fact]
    public async Task EllipseAdapter_ComputesRatioFromThirdPickAndCommits()
    {
        var commandRegistry = new CadCommandRegistry();
        commandRegistry.Register(new EllipseCadCommand());
        var intellisense = new CadCommandIntellisenseService(commandRegistry);
        var runtime = new CadCommandRuntime(commandRegistry, intellisense);
        var adapter = new EllipseInteractiveCommandAdapter();
        var session = new CadEditorSessionFactory().Create(new CadDocument());

        runtime.BeginCommand("ELLIPSE");

        var first = await adapter.SubmitAsync(
            runtime,
            new CadPromptToken(CadPromptTokenType.Coordinate, "0,0"),
            session,
            commit: false);
        var second = await adapter.SubmitAsync(
            runtime,
            new CadPromptToken(CadPromptTokenType.Coordinate, "10,0"),
            session,
            commit: false);
        var third = await adapter.SubmitAsync(
            runtime,
            new CadPromptToken(CadPromptTokenType.Coordinate, "0,5"),
            session,
            commit: false);

        Assert.True(first.Handled);
        Assert.Null(first.Result);
        Assert.True(second.Handled);
        Assert.Null(second.Result);
        Assert.True(third.Result?.Success);
        Assert.Contains("Created ELLIPSE", third.Result?.Message, StringComparison.OrdinalIgnoreCase);
        Assert.False(runtime.State.IsActive);
    }

    [Fact]
    public async Task PolygonAdapter_UsesDefaultSidesAndCommitsAfterSecondPick()
    {
        var commandRegistry = new CadCommandRegistry();
        commandRegistry.Register(new PolygonCadCommand());
        var intellisense = new CadCommandIntellisenseService(commandRegistry);
        var runtime = new CadCommandRuntime(commandRegistry, intellisense);
        var adapter = new PolygonInteractiveCommandAdapter();
        var session = new CadEditorSessionFactory().Create(new CadDocument());

        runtime.BeginCommand("POLYGON");

        var first = await adapter.SubmitAsync(
            runtime,
            new CadPromptToken(CadPromptTokenType.Coordinate, "0,0"),
            session,
            commit: false);
        var second = await adapter.SubmitAsync(
            runtime,
            new CadPromptToken(CadPromptTokenType.Coordinate, "0,5"),
            session,
            commit: false);

        Assert.True(first.Handled);
        Assert.Null(first.Result);
        Assert.True(second.Result?.Success);
        Assert.Contains("Created POLYGON", second.Result?.Message, StringComparison.OrdinalIgnoreCase);
        Assert.False(runtime.State.IsActive);
    }

    [Fact]
    public async Task SplineAdapter_ContinuesCollectingPointsUntilExplicitCommit()
    {
        var commandRegistry = new CadCommandRegistry();
        commandRegistry.Register(new SplineCadCommand());
        var intellisense = new CadCommandIntellisenseService(commandRegistry);
        var runtime = new CadCommandRuntime(commandRegistry, intellisense);
        var adapter = new SplineInteractiveCommandAdapter();
        var session = new CadEditorSessionFactory().Create(new CadDocument());

        runtime.BeginCommand("SPLINE");

        var first = await adapter.SubmitAsync(
            runtime,
            new CadPromptToken(CadPromptTokenType.Coordinate, "0,0"),
            session,
            commit: false);
        var second = await adapter.SubmitAsync(
            runtime,
            new CadPromptToken(CadPromptTokenType.Coordinate, "5,3"),
            session,
            commit: false);
        var third = await adapter.SubmitAsync(
            runtime,
            new CadPromptToken(CadPromptTokenType.Coordinate, "10,0"),
            session,
            commit: false);
        Assert.True(first.Handled);
        Assert.Null(first.Result);
        Assert.True(second.Handled);
        Assert.Null(second.Result);
        Assert.True(third.Handled);
        Assert.Null(third.Result);
        Assert.True(runtime.State.IsActive);

        var commit = await adapter.SubmitAsync(
            runtime,
            new CadPromptToken(CadPromptTokenType.Raw, string.Empty),
            session,
            commit: true);

        Assert.True(commit.Result?.Success);
        Assert.Contains("Created SPLINE", commit.Result?.Message, StringComparison.OrdinalIgnoreCase);
        var spline = Assert.Single(session.Document.Entities.OfType<Spline>());
        Assert.True(spline.FitPoints.Count >= 3);
        Assert.False(runtime.State.IsActive);
    }

    [Fact]
    public async Task PlineAdapter_ContinuesCollectingVerticesUntilExplicitCommit()
    {
        var commandRegistry = new CadCommandRegistry();
        commandRegistry.Register(new PlineCadCommand());
        var intellisense = new CadCommandIntellisenseService(commandRegistry);
        var runtime = new CadCommandRuntime(commandRegistry, intellisense);
        var adapter = new PlineInteractiveCommandAdapter();
        var session = new CadEditorSessionFactory().Create(new CadDocument());

        runtime.BeginCommand("PLINE");

        var first = await adapter.SubmitAsync(
            runtime,
            new CadPromptToken(CadPromptTokenType.Coordinate, "0,0"),
            session,
            commit: false);
        var second = await adapter.SubmitAsync(
            runtime,
            new CadPromptToken(CadPromptTokenType.Coordinate, "5,0"),
            session,
            commit: false);
        var third = await adapter.SubmitAsync(
            runtime,
            new CadPromptToken(CadPromptTokenType.Coordinate, "5,4"),
            session,
            commit: false);
        Assert.True(first.Handled);
        Assert.Null(first.Result);
        Assert.True(second.Handled);
        Assert.Null(second.Result);
        Assert.True(third.Handled);
        Assert.Null(third.Result);
        Assert.True(runtime.State.IsActive);

        var commit = await adapter.SubmitAsync(
            runtime,
            new CadPromptToken(CadPromptTokenType.Raw, string.Empty),
            session,
            commit: true);

        Assert.True(commit.Result?.Success);
        Assert.Contains("Created PLINE", commit.Result?.Message, StringComparison.OrdinalIgnoreCase);
        var polyline = Assert.Single(session.Document.Entities.OfType<LwPolyline>());
        Assert.Equal(3, polyline.Vertices.Count);
        Assert.False(runtime.State.IsActive);
    }

    [Fact]
    public async Task LeaderAdapter_ContinuesCollectingVerticesUntilExplicitCommit()
    {
        var commandRegistry = new CadCommandRegistry();
        commandRegistry.Register(new LeaderCadCommand());
        var intellisense = new CadCommandIntellisenseService(commandRegistry);
        var runtime = new CadCommandRuntime(commandRegistry, intellisense);
        var adapter = new LeaderInteractiveCommandAdapter();
        var session = new CadEditorSessionFactory().Create(new CadDocument());

        runtime.BeginCommand("LEADER");

        var first = await adapter.SubmitAsync(
            runtime,
            new CadPromptToken(CadPromptTokenType.Coordinate, "0,0"),
            session,
            commit: false);
        var second = await adapter.SubmitAsync(
            runtime,
            new CadPromptToken(CadPromptTokenType.Coordinate, "5,0"),
            session,
            commit: false);
        var third = await adapter.SubmitAsync(
            runtime,
            new CadPromptToken(CadPromptTokenType.Coordinate, "8,3"),
            session,
            commit: false);
        Assert.True(first.Handled);
        Assert.Null(first.Result);
        Assert.True(second.Handled);
        Assert.Null(second.Result);
        Assert.True(third.Handled);
        Assert.Null(third.Result);
        Assert.True(runtime.State.IsActive);

        var commit = await adapter.SubmitAsync(
            runtime,
            new CadPromptToken(CadPromptTokenType.Raw, string.Empty),
            session,
            commit: true);

        Assert.True(commit.Result?.Success);
        Assert.Contains("Created LEADER", commit.Result?.Message, StringComparison.OrdinalIgnoreCase);
        var polyline = Assert.Single(session.Document.Entities.OfType<LwPolyline>());
        Assert.Equal(3, polyline.Vertices.Count);
        Assert.False(runtime.State.IsActive);
    }

    [Fact]
    public async Task MLeaderAdapter_ContinuesCollectingVerticesUntilExplicitCommit()
    {
        var commandRegistry = new CadCommandRegistry();
        commandRegistry.Register(new MLeaderCadCommand());
        var intellisense = new CadCommandIntellisenseService(commandRegistry);
        var runtime = new CadCommandRuntime(commandRegistry, intellisense);
        var adapter = new MLeaderInteractiveCommandAdapter();
        var session = new CadEditorSessionFactory().Create(new CadDocument());

        runtime.BeginCommand("MLEADER");

        var first = await adapter.SubmitAsync(
            runtime,
            new CadPromptToken(CadPromptTokenType.Coordinate, "0,0"),
            session,
            commit: false);
        var second = await adapter.SubmitAsync(
            runtime,
            new CadPromptToken(CadPromptTokenType.Coordinate, "5,0"),
            session,
            commit: false);
        var third = await adapter.SubmitAsync(
            runtime,
            new CadPromptToken(CadPromptTokenType.Coordinate, "8,3"),
            session,
            commit: false);

        Assert.True(first.Handled);
        Assert.Null(first.Result);
        Assert.True(second.Handled);
        Assert.Null(second.Result);
        Assert.True(third.Handled);
        Assert.Null(third.Result);
        Assert.True(runtime.State.IsActive);

        var commit = await adapter.SubmitAsync(
            runtime,
            new CadPromptToken(CadPromptTokenType.Raw, string.Empty),
            session,
            commit: true);

        Assert.True(commit.Result?.Success);
        Assert.Contains("Created MLEADER", commit.Result?.Message, StringComparison.OrdinalIgnoreCase);
        var polyline = Assert.Single(session.Document.Entities.OfType<LwPolyline>());
        Assert.Equal(3, polyline.Vertices.Count);
        Assert.Single(session.Document.Entities.OfType<MText>());
        Assert.False(runtime.State.IsActive);
    }

    [Fact]
    public async Task OffsetAdapter_UsesSelectionAndTwoPoints()
    {
        var commandRegistry = new CadCommandRegistry();
        commandRegistry.Register(new LineCadCommand());
        commandRegistry.Register(new OffsetCadCommand());
        var intellisense = new CadCommandIntellisenseService(commandRegistry);
        var runtime = new CadCommandRuntime(commandRegistry, intellisense);
        var adapter = new OffsetInteractiveCommandAdapter();
        var session = Assert.IsType<CadDocumentSession>(new CadEditorSessionFactory().Create(new CadDocument()));

        var created = await runtime.SubmitAsync("LINE 0,0 10,0", session);
        Assert.True(created.Result?.Success);
        var sourceLine = Assert.Single(session.Document.Entities.OfType<Line>());
        session.SetSelection([sourceLine], ProCad.Editing.Selection.CadSelectionMode.Replace);

        runtime.BeginCommand("OFFSET");
        var first = await adapter.SubmitAsync(
            runtime,
            new CadPromptToken(CadPromptTokenType.Coordinate, "0,0"),
            session,
            commit: false);
        var second = await adapter.SubmitAsync(
            runtime,
            new CadPromptToken(CadPromptTokenType.Coordinate, "0,4"),
            session,
            commit: false);

        Assert.True(first.Handled);
        Assert.Null(first.Result);
        Assert.True(second.Result?.Success);
        Assert.Equal(2, session.Document.Entities.OfType<Line>().Count());
    }

    [Fact]
    public async Task JoinAdapter_UsesMultipleSelectedLines()
    {
        var commandRegistry = new CadCommandRegistry();
        commandRegistry.Register(new LineCadCommand());
        commandRegistry.Register(new JoinCadCommand());
        var intellisense = new CadCommandIntellisenseService(commandRegistry);
        var runtime = new CadCommandRuntime(commandRegistry, intellisense);
        var adapter = new JoinInteractiveCommandAdapter();
        var session = Assert.IsType<CadDocumentSession>(new CadEditorSessionFactory().Create(new CadDocument()));

        Assert.True((await runtime.SubmitAsync("LINE 0,0 5,0", session)).Result?.Success);
        Assert.True((await runtime.SubmitAsync("LINE 5,0 10,0", session)).Result?.Success);
        Assert.True((await runtime.SubmitAsync("LINE 10,0 15,0", session)).Result?.Success);
        var lines = session.Document.Entities.OfType<Line>().ToArray();
        Assert.Equal(3, lines.Length);
        session.SetSelection(lines, ProCad.Editing.Selection.CadSelectionMode.Replace);

        runtime.BeginCommand("JOIN");
        var result = await adapter.SubmitAsync(
            runtime,
            new CadPromptToken(CadPromptTokenType.Coordinate, "1,0"),
            session,
            commit: false);

        Assert.True(result.Result?.Success, result.Result?.Message);
        var merged = Assert.Single(session.Document.Entities.OfType<Line>());
        Assert.True(
            (Math.Abs(merged.StartPoint.X - 0.0) < 1e-6 && Math.Abs(merged.EndPoint.X - 15.0) < 1e-6) ||
            (Math.Abs(merged.StartPoint.X - 15.0) < 1e-6 && Math.Abs(merged.EndPoint.X - 0.0) < 1e-6));
    }

    [Fact]
    public async Task BreakAdapter_UsesSelectedLineAndSinglePointCommit()
    {
        var commandRegistry = new CadCommandRegistry();
        commandRegistry.Register(new LineCadCommand());
        commandRegistry.Register(new BreakCadCommand());
        var intellisense = new CadCommandIntellisenseService(commandRegistry);
        var runtime = new CadCommandRuntime(commandRegistry, intellisense);
        var adapter = new BreakInteractiveCommandAdapter();
        var session = Assert.IsType<CadDocumentSession>(new CadEditorSessionFactory().Create(new CadDocument()));

        var created = await runtime.SubmitAsync("LINE 0,0 10,0", session);
        Assert.True(created.Result?.Success);
        var sourceLine = Assert.Single(session.Document.Entities.OfType<Line>());
        session.SetSelection([sourceLine], ProCad.Editing.Selection.CadSelectionMode.Replace);

        runtime.BeginCommand("BREAK");
        var first = await adapter.SubmitAsync(
            runtime,
            new CadPromptToken(CadPromptTokenType.Coordinate, "5,0"),
            session,
            commit: false);
        var result = await adapter.SubmitAsync(
            runtime,
            new CadPromptToken(CadPromptTokenType.Raw, string.Empty),
            session,
            commit: true);

        Assert.True(first.Handled);
        Assert.Null(first.Result);
        Assert.True(result.Result?.Success);
        Assert.Equal(2, session.Document.Entities.OfType<Line>().Count());
    }

    [Fact]
    public async Task BreakAdapter_ProjectsPointOnAngledLine_WithoutPrecisionLoss()
    {
        var commandRegistry = new CadCommandRegistry();
        commandRegistry.Register(new LineCadCommand());
        commandRegistry.Register(new BreakCadCommand());
        var intellisense = new CadCommandIntellisenseService(commandRegistry);
        var runtime = new CadCommandRuntime(commandRegistry, intellisense);
        var adapter = new BreakInteractiveCommandAdapter();
        var session = Assert.IsType<CadDocumentSession>(new CadEditorSessionFactory().Create(new CadDocument()));

        var created = await runtime.SubmitAsync("LINE 0,0 3,2", session);
        Assert.True(created.Result?.Success);
        var sourceLine = Assert.Single(session.Document.Entities.OfType<Line>());
        session.SetSelection([sourceLine], ProCad.Editing.Selection.CadSelectionMode.Replace);

        runtime.BeginCommand("BREAK");
        var first = await adapter.SubmitAsync(
            runtime,
            new CadPromptToken(CadPromptTokenType.Coordinate, "1,1"),
            session,
            commit: false);
        var result = await adapter.SubmitAsync(
            runtime,
            new CadPromptToken(CadPromptTokenType.Raw, string.Empty),
            session,
            commit: true);

        Assert.True(first.Handled);
        Assert.Null(first.Result);
        Assert.True(result.Result?.Success, result.Result?.Message);
        Assert.Equal(2, session.Document.Entities.OfType<Line>().Count());
    }

    [Fact]
    public async Task BreakAdapter_PreservesLargeCoordinatePrecision()
    {
        var commandRegistry = new CadCommandRegistry();
        commandRegistry.Register(new LineCadCommand());
        commandRegistry.Register(new BreakCadCommand());
        var runtime = new CadCommandRuntime(commandRegistry, new CadCommandIntellisenseService(commandRegistry));
        var adapter = new BreakInteractiveCommandAdapter();
        var session = Assert.IsType<CadDocumentSession>(new CadEditorSessionFactory().Create(new CadDocument()));

        var created = await runtime.SubmitAsync("LINE 1000000000,0 1000000010,0", session);
        Assert.True(created.Result?.Success, created.Result?.Message);
        var sourceLine = Assert.Single(session.Document.Entities.OfType<Line>());
        session.SetSelection([sourceLine], ProCad.Editing.Selection.CadSelectionMode.Replace);

        runtime.BeginCommand("BREAK");
        var first = await adapter.SubmitAsync(
            runtime,
            new CadPromptToken(CadPromptTokenType.Coordinate, "1000000005,0"),
            session,
            commit: false);
        var result = await adapter.SubmitAsync(
            runtime,
            new CadPromptToken(CadPromptTokenType.Raw, string.Empty),
            session,
            commit: true);

        Assert.True(first.Handled);
        Assert.Null(first.Result);
        Assert.True(result.Result?.Success, result.Result?.Message);
        var lines = session.Document.Entities
            .OfType<Line>()
            .OrderBy(static line => Math.Min(line.StartPoint.X, line.EndPoint.X))
            .ToArray();
        Assert.Equal(2, lines.Length);
        Assert.Equal(1000000000.0, Math.Min(lines[0].StartPoint.X, lines[0].EndPoint.X), 6);
        Assert.Equal(1000000005.0, Math.Max(lines[0].StartPoint.X, lines[0].EndPoint.X), 6);
        Assert.Equal(1000000005.0, Math.Min(lines[1].StartPoint.X, lines[1].EndPoint.X), 6);
        Assert.Equal(1000000010.0, Math.Max(lines[1].StartPoint.X, lines[1].EndPoint.X), 6);
    }

    [Fact]
    public async Task BreakAdapter_UsesSelectedOpenPolylineAndProjectsPoint()
    {
        var commandRegistry = new CadCommandRegistry();
        commandRegistry.Register(new PlineCadCommand());
        commandRegistry.Register(new BreakCadCommand());
        var intellisense = new CadCommandIntellisenseService(commandRegistry);
        var runtime = new CadCommandRuntime(commandRegistry, intellisense);
        var adapter = new BreakInteractiveCommandAdapter();
        var session = Assert.IsType<CadDocumentSession>(new CadEditorSessionFactory().Create(new CadDocument()));

        var created = await runtime.SubmitAsync("PLINE 0,0 10,0 10,10", session);
        Assert.True(created.Result?.Success);
        var source = Assert.Single(session.Document.Entities.OfType<LwPolyline>());
        session.SetSelection([source], ProCad.Editing.Selection.CadSelectionMode.Replace);

        runtime.BeginCommand("BREAK");
        var first = await adapter.SubmitAsync(
            runtime,
            new CadPromptToken(CadPromptTokenType.Coordinate, "5,1"),
            session,
            commit: false);
        var result = await adapter.SubmitAsync(
            runtime,
            new CadPromptToken(CadPromptTokenType.Raw, string.Empty),
            session,
            commit: true);

        Assert.True(first.Handled);
        Assert.Null(first.Result);
        Assert.True(result.Result?.Success, result.Result?.Message);
        var polylines = session.Document.Entities.OfType<LwPolyline>().ToArray();
        Assert.Equal(2, polylines.Length);
        Assert.Contains(
            polylines,
            candidate => candidate.Vertices.Count == 2 &&
                         Math.Abs(candidate.Vertices[1].Location.X - 5.0) < 1e-6 &&
                         Math.Abs(candidate.Vertices[1].Location.Y - 0.0) < 1e-6);
    }

    [Fact]
    public async Task BreakAdapter_UsesTwoPointsToRemoveLineSpan()
    {
        var commandRegistry = new CadCommandRegistry();
        commandRegistry.Register(new LineCadCommand());
        commandRegistry.Register(new BreakCadCommand());
        var intellisense = new CadCommandIntellisenseService(commandRegistry);
        var runtime = new CadCommandRuntime(commandRegistry, intellisense);
        var adapter = new BreakInteractiveCommandAdapter();
        var session = Assert.IsType<CadDocumentSession>(new CadEditorSessionFactory().Create(new CadDocument()));

        var created = await runtime.SubmitAsync("LINE 0,0 10,0", session);
        Assert.True(created.Result?.Success);
        var sourceLine = Assert.Single(session.Document.Entities.OfType<Line>());
        session.SetSelection([sourceLine], ProCad.Editing.Selection.CadSelectionMode.Replace);

        runtime.BeginCommand("BREAK");
        var first = await adapter.SubmitAsync(
            runtime,
            new CadPromptToken(CadPromptTokenType.Coordinate, "3,0"),
            session,
            commit: false);
        var second = await adapter.SubmitAsync(
            runtime,
            new CadPromptToken(CadPromptTokenType.Coordinate, "7,0"),
            session,
            commit: false);

        Assert.True(first.Handled);
        Assert.Null(first.Result);
        Assert.True(second.Result?.Success, second.Result?.Message);
        var lines = session.Document.Entities.OfType<Line>().ToArray();
        Assert.Equal(2, lines.Length);
        Assert.Contains(
            lines,
            candidate => Math.Abs(candidate.StartPoint.X - 0.0) < 1e-6 &&
                         Math.Abs(candidate.EndPoint.X - 3.0) < 1e-6);
        Assert.Contains(
            lines,
            candidate => Math.Abs(candidate.StartPoint.X - 7.0) < 1e-6 &&
                         Math.Abs(candidate.EndPoint.X - 10.0) < 1e-6);
    }

    [Fact]
    public async Task TrimAdapter_UsesTwoSelectedLinesAndPickedEndpoint()
    {
        var commandRegistry = new CadCommandRegistry();
        commandRegistry.Register(new LineCadCommand());
        commandRegistry.Register(new TrimCadCommand());
        var intellisense = new CadCommandIntellisenseService(commandRegistry);
        var runtime = new CadCommandRuntime(commandRegistry, intellisense);
        var adapter = new TrimInteractiveCommandAdapter();
        var session = Assert.IsType<CadDocumentSession>(new CadEditorSessionFactory().Create(new CadDocument()));

        Assert.True((await runtime.SubmitAsync("LINE 0,0 10,0", session)).Result?.Success);
        Assert.True((await runtime.SubmitAsync("LINE 5,-5 5,5", session)).Result?.Success);
        var lines = session.Document.Entities.OfType<Line>().ToArray();
        Assert.Equal(2, lines.Length);
        session.SetSelection(lines, ProCad.Editing.Selection.CadSelectionMode.Replace);

        runtime.BeginCommand("TRIM");
        var result = await adapter.SubmitAsync(
            runtime,
            new CadPromptToken(CadPromptTokenType.Coordinate, "9,0"),
            session,
            commit: false);

        Assert.True(result.Result?.Success);
        Assert.Contains("Trim completed", result.Result?.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task TrimAdapter_PrefersNearestBoundaryWhenMultipleSelected()
    {
        var commandRegistry = new CadCommandRegistry();
        commandRegistry.Register(new LineCadCommand());
        commandRegistry.Register(new TrimCadCommand());
        var intellisense = new CadCommandIntellisenseService(commandRegistry);
        var runtime = new CadCommandRuntime(commandRegistry, intellisense);
        var adapter = new TrimInteractiveCommandAdapter();
        var session = Assert.IsType<CadDocumentSession>(new CadEditorSessionFactory().Create(new CadDocument()));

        Assert.True((await runtime.SubmitAsync("LINE 0,0 10,0", session)).Result?.Success);     // Target
        Assert.True((await runtime.SubmitAsync("LINE 0,5 10,5", session)).Result?.Success);     // Parallel boundary
        Assert.True((await runtime.SubmitAsync("LINE 8,-5 8,5", session)).Result?.Success);      // Intersecting boundary

        var lines = session.Document.Entities.OfType<Line>().ToArray();
        Assert.Equal(3, lines.Length);
        var target = lines[0];
        var parallelBoundary = lines[1];
        var intersectingBoundary = lines[2];
        session.SetSelection([parallelBoundary, target, intersectingBoundary], ProCad.Editing.Selection.CadSelectionMode.Replace);

        runtime.BeginCommand("TRIM");
        var result = await adapter.SubmitAsync(
            runtime,
            new CadPromptToken(CadPromptTokenType.Coordinate, "9,0"),
            session,
            commit: false);

        Assert.True(result.Result?.Success, result.Result?.Message);
        Assert.Equal(8.0, target.EndPoint.X, 6);
    }

    [Fact]
    public async Task TrimAdapter_UsesOpenPolylineTarget()
    {
        var commandRegistry = new CadCommandRegistry();
        commandRegistry.Register(new LineCadCommand());
        commandRegistry.Register(new PlineCadCommand());
        commandRegistry.Register(new TrimCadCommand());
        var intellisense = new CadCommandIntellisenseService(commandRegistry);
        var runtime = new CadCommandRuntime(commandRegistry, intellisense);
        var adapter = new TrimInteractiveCommandAdapter();
        var session = Assert.IsType<CadDocumentSession>(new CadEditorSessionFactory().Create(new CadDocument()));

        Assert.True((await runtime.SubmitAsync("PLINE 0,0 10,0 10,5", session)).Result?.Success);
        Assert.True((await runtime.SubmitAsync("LINE -5,2 15,2", session)).Result?.Success);
        var polyline = Assert.Single(session.Document.Entities.OfType<LwPolyline>());
        var boundary = Assert.Single(session.Document.Entities.OfType<Line>());
        session.SetSelection([polyline, boundary], ProCad.Editing.Selection.CadSelectionMode.Replace);

        runtime.BeginCommand("TRIM");
        var result = await adapter.SubmitAsync(
            runtime,
            new CadPromptToken(CadPromptTokenType.Coordinate, "10,4"),
            session,
            commit: false);

        Assert.True(result.Result?.Success, result.Result?.Message);
        Assert.Equal(2.0, polyline.Vertices[2].Location.Y, 6);
    }

    [Fact]
    public async Task ExtendAdapter_UsesOpenPolylineTarget()
    {
        var commandRegistry = new CadCommandRegistry();
        commandRegistry.Register(new LineCadCommand());
        commandRegistry.Register(new PlineCadCommand());
        commandRegistry.Register(new ExtendCadCommand());
        var intellisense = new CadCommandIntellisenseService(commandRegistry);
        var runtime = new CadCommandRuntime(commandRegistry, intellisense);
        var adapter = new ExtendInteractiveCommandAdapter();
        var session = Assert.IsType<CadDocumentSession>(new CadEditorSessionFactory().Create(new CadDocument()));

        Assert.True((await runtime.SubmitAsync("PLINE 4,0 10,0 10,5", session)).Result?.Success);
        Assert.True((await runtime.SubmitAsync("LINE 0,-5 0,5", session)).Result?.Success);
        var polyline = Assert.Single(session.Document.Entities.OfType<LwPolyline>());
        var boundary = Assert.Single(session.Document.Entities.OfType<Line>());
        session.SetSelection([polyline, boundary], ProCad.Editing.Selection.CadSelectionMode.Replace);

        runtime.BeginCommand("EXTEND");
        var result = await adapter.SubmitAsync(
            runtime,
            new CadPromptToken(CadPromptTokenType.Coordinate, "4,0"),
            session,
            commit: false);

        Assert.True(result.Result?.Success, result.Result?.Message);
        Assert.Equal(0.0, polyline.Vertices[0].Location.X, 6);
    }

    [Fact]
    public async Task FilletAdapter_UsesSelectedLinesAndCreatesArc()
    {
        var commandRegistry = new CadCommandRegistry();
        commandRegistry.Register(new LineCadCommand());
        commandRegistry.Register(new FilletCadCommand());
        var runtime = new CadCommandRuntime(commandRegistry, new CadCommandIntellisenseService(commandRegistry));
        var adapter = new FilletInteractiveCommandAdapter();
        var session = Assert.IsType<CadDocumentSession>(new CadEditorSessionFactory().Create(new CadDocument()));

        Assert.True((await runtime.SubmitAsync("LINE 0,0 20,0", session)).Result?.Success);
        Assert.True((await runtime.SubmitAsync("LINE 10,-10 10,10", session)).Result?.Success);
        var lines = session.Document.Entities.OfType<Line>().ToArray();
        session.SetSelection(lines, ProCad.Editing.Selection.CadSelectionMode.Replace);

        runtime.BeginCommand("FILLET");
        var result = await adapter.SubmitAsync(
            runtime,
            new CadPromptToken(CadPromptTokenType.Coordinate, "10,0"),
            session,
            commit: false);

        Assert.True(result.Result?.Success);
        Assert.Contains("Fillet completed", result.Result?.Message, StringComparison.OrdinalIgnoreCase);
        Assert.NotEmpty(session.Document.Entities.OfType<Arc>());
    }

    [Fact]
    public async Task FilletAdapter_UsesSelectedLineAndOpenPolyline()
    {
        var commandRegistry = new CadCommandRegistry();
        commandRegistry.Register(new LineCadCommand());
        commandRegistry.Register(new PlineCadCommand());
        commandRegistry.Register(new FilletCadCommand());
        var runtime = new CadCommandRuntime(commandRegistry, new CadCommandIntellisenseService(commandRegistry));
        var adapter = new FilletInteractiveCommandAdapter();
        var session = Assert.IsType<CadDocumentSession>(new CadEditorSessionFactory().Create(new CadDocument()));

        Assert.True((await runtime.SubmitAsync("PLINE 0,0 20,0 20,8", session)).Result?.Success);
        Assert.True((await runtime.SubmitAsync("LINE 0,0 0,20", session)).Result?.Success);
        var polyline = Assert.Single(session.Document.Entities.OfType<LwPolyline>());
        var line = Assert.Single(session.Document.Entities.OfType<Line>());
        session.SetSelection([polyline, line], ProCad.Editing.Selection.CadSelectionMode.Replace);

        runtime.BeginCommand("FILLET");
        var result = await adapter.SubmitAsync(
            runtime,
            new CadPromptToken(CadPromptTokenType.Coordinate, "0,0"),
            session,
            commit: false);

        Assert.True(result.Result?.Success, result.Result?.Message);
        Assert.Contains("Fillet completed", result.Result?.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(1.0, polyline.Vertices[0].Location.X, 6);
        Assert.NotEmpty(session.Document.Entities.OfType<Arc>());
    }

    [Fact]
    public async Task ChamferAdapter_UsesSelectedLinesAndCreatesChamferLine()
    {
        var commandRegistry = new CadCommandRegistry();
        commandRegistry.Register(new LineCadCommand());
        commandRegistry.Register(new ChamferCadCommand());
        var runtime = new CadCommandRuntime(commandRegistry, new CadCommandIntellisenseService(commandRegistry));
        var adapter = new ChamferInteractiveCommandAdapter();
        var session = Assert.IsType<CadDocumentSession>(new CadEditorSessionFactory().Create(new CadDocument()));

        Assert.True((await runtime.SubmitAsync("LINE 0,0 20,0", session)).Result?.Success);
        Assert.True((await runtime.SubmitAsync("LINE 10,-10 10,10", session)).Result?.Success);
        var lines = session.Document.Entities.OfType<Line>().ToArray();
        session.SetSelection(lines, ProCad.Editing.Selection.CadSelectionMode.Replace);

        runtime.BeginCommand("CHAMFER");
        var result = await adapter.SubmitAsync(
            runtime,
            new CadPromptToken(CadPromptTokenType.Coordinate, "10,0"),
            session,
            commit: false);

        Assert.True(result.Result?.Success);
        Assert.Contains("Chamfer completed", result.Result?.Message, StringComparison.OrdinalIgnoreCase);
        Assert.True(session.Document.Entities.OfType<Line>().Count() >= 3);
    }

    [Fact]
    public async Task ChamferAdapter_UsesSelectedLineAndOpenPolyline()
    {
        var commandRegistry = new CadCommandRegistry();
        commandRegistry.Register(new LineCadCommand());
        commandRegistry.Register(new PlineCadCommand());
        commandRegistry.Register(new ChamferCadCommand());
        var runtime = new CadCommandRuntime(commandRegistry, new CadCommandIntellisenseService(commandRegistry));
        var adapter = new ChamferInteractiveCommandAdapter();
        var session = Assert.IsType<CadDocumentSession>(new CadEditorSessionFactory().Create(new CadDocument()));

        Assert.True((await runtime.SubmitAsync("PLINE 0,0 20,0 20,8", session)).Result?.Success);
        Assert.True((await runtime.SubmitAsync("LINE 0,0 0,20", session)).Result?.Success);
        var polyline = Assert.Single(session.Document.Entities.OfType<LwPolyline>());
        var line = Assert.Single(session.Document.Entities.OfType<Line>());
        session.SetSelection([polyline, line], ProCad.Editing.Selection.CadSelectionMode.Replace);

        runtime.BeginCommand("CHAMFER");
        var result = await adapter.SubmitAsync(
            runtime,
            new CadPromptToken(CadPromptTokenType.Coordinate, "0,0"),
            session,
            commit: false);

        Assert.True(result.Result?.Success, result.Result?.Message);
        Assert.Contains("Chamfer completed", result.Result?.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(1.0, polyline.Vertices[0].Location.X, 6);
        Assert.True(session.Document.Entities.OfType<Line>().Count() >= 2);
    }

    [Fact]
    public async Task ArrayAdapter_RectangularModeCreatesCopiesFromTwoPickedPoints()
    {
        var commandRegistry = new CadCommandRegistry();
        commandRegistry.Register(new LineCadCommand());
        commandRegistry.Register(new ArrayCadCommand());
        var runtime = new CadCommandRuntime(commandRegistry, new CadCommandIntellisenseService(commandRegistry));
        var adapter = new ArrayInteractiveCommandAdapter();
        var session = Assert.IsType<CadDocumentSession>(new CadEditorSessionFactory().Create(new CadDocument()));

        Assert.True((await runtime.SubmitAsync("LINE 0,0 8,0", session)).Result?.Success);
        var line = Assert.Single(session.Document.Entities.OfType<Line>());
        session.SetSelection([line], ProCad.Editing.Selection.CadSelectionMode.Replace);

        runtime.BeginCommand("ARRAY");
        var first = await adapter.SubmitAsync(
            runtime,
            new CadPromptToken(CadPromptTokenType.Coordinate, "0,0"),
            session,
            commit: false);
        var second = await adapter.SubmitAsync(
            runtime,
            new CadPromptToken(CadPromptTokenType.Coordinate, "4,3"),
            session,
            commit: false);

        Assert.True(first.Handled);
        Assert.Null(first.Result);
        Assert.True(second.Result?.Success);
        Assert.Equal(4, session.Document.Entities.OfType<Line>().Count());
    }

    [Fact]
    public async Task ArrayAdapter_PathModeUsesSelectedPathAndCreatesCopies()
    {
        var commandRegistry = new CadCommandRegistry();
        commandRegistry.Register(new LineCadCommand());
        commandRegistry.Register(new CircleCadCommand());
        commandRegistry.Register(new ArrayCadCommand());
        var runtime = new CadCommandRuntime(commandRegistry, new CadCommandIntellisenseService(commandRegistry));
        var adapter = new ArrayInteractiveCommandAdapter();
        var session = Assert.IsType<CadDocumentSession>(new CadEditorSessionFactory().Create(new CadDocument()));

        Assert.True((await runtime.SubmitAsync("LINE 0,0 10,0", session)).Result?.Success);
        Assert.True((await runtime.SubmitAsync("CIRCLE 0,0 1", session)).Result?.Success);
        var path = Assert.Single(session.Document.Entities.OfType<Line>());
        var circle = Assert.Single(session.Document.Entities.OfType<Circle>());
        session.SetSelection([path, circle], ProCad.Editing.Selection.CadSelectionMode.Replace);

        runtime.BeginCommand("ARRAY");
        var mode = await adapter.SubmitAsync(
            runtime,
            new CadPromptToken(CadPromptTokenType.Keyword, "PATH"),
            session,
            commit: false);
        var result = await adapter.SubmitAsync(
            runtime,
            new CadPromptToken(CadPromptTokenType.Coordinate, "1,0"),
            session,
            commit: false);

        Assert.True(mode.Handled);
        Assert.True(result.Result?.Success);
        Assert.Equal(6, session.Document.Entities.OfType<Circle>().Count());
    }

    [Fact]
    public async Task AlignAdapter_TranslatesSelectedEntitiesWithTwoPickedPoints()
    {
        var commandRegistry = new CadCommandRegistry();
        commandRegistry.Register(new LineCadCommand());
        commandRegistry.Register(new AlignCadCommand());
        var runtime = new CadCommandRuntime(commandRegistry, new CadCommandIntellisenseService(commandRegistry));
        var adapter = new AlignInteractiveCommandAdapter();
        var session = Assert.IsType<CadDocumentSession>(new CadEditorSessionFactory().Create(new CadDocument()));

        Assert.True((await runtime.SubmitAsync("LINE 0,0 5,0", session)).Result?.Success);
        var line = Assert.Single(session.Document.Entities.OfType<Line>());
        session.SetSelection([line], ProCad.Editing.Selection.CadSelectionMode.Replace);

        runtime.BeginCommand("ALIGN");
        var first = await adapter.SubmitAsync(
            runtime,
            new CadPromptToken(CadPromptTokenType.Coordinate, "0,0"),
            session,
            commit: false);
        var second = await adapter.SubmitAsync(
            runtime,
            new CadPromptToken(CadPromptTokenType.Coordinate, "10,10"),
            session,
            commit: false);

        Assert.True(first.Handled);
        Assert.Null(first.Result);
        Assert.True(second.Result?.Success);
        Assert.Equal(10d, line.StartPoint.X, 3);
        Assert.Equal(10d, line.StartPoint.Y, 3);
    }

    [Fact]
    public async Task MatchPropAdapter_UsesNearestSelectedSourceAndAppliesToTargets()
    {
        var commandRegistry = new CadCommandRegistry();
        commandRegistry.Register(new LineCadCommand());
        commandRegistry.Register(new MatchPropCadCommand());
        var runtime = new CadCommandRuntime(commandRegistry, new CadCommandIntellisenseService(commandRegistry));
        var adapter = new MatchPropInteractiveCommandAdapter();
        var session = Assert.IsType<CadDocumentSession>(new CadEditorSessionFactory().Create(new CadDocument()));

        Assert.True((await runtime.SubmitAsync("LINE 0,0 10,0", session)).Result?.Success);
        Assert.True((await runtime.SubmitAsync("LINE 0,10 10,10", session)).Result?.Success);
        var lines = session.Document.Entities.OfType<Line>().ToArray();
        Assert.Equal(2, lines.Length);
        lines[0].LineTypeScale = 2.5;
        lines[1].LineTypeScale = 1.0;
        session.SetSelection(lines, ProCad.Editing.Selection.CadSelectionMode.Replace);

        runtime.BeginCommand("MATCHPROP");
        var result = await adapter.SubmitAsync(
            runtime,
            new CadPromptToken(CadPromptTokenType.Coordinate, "1,0"),
            session,
            commit: false);

        Assert.True(result.Result?.Success);
        Assert.Equal(lines[0].LineTypeScale, lines[1].LineTypeScale, 6);
    }

    [Fact]
    public async Task HatchSelectionCommitAdapter_UsesSolidDefaultWithExplicitSelectionHandles()
    {
        var commandRegistry = new CadCommandRegistry();
        commandRegistry.Register(new CircleCadCommand());
        commandRegistry.Register(new HatchCadCommand());
        var runtime = new CadCommandRuntime(commandRegistry, new CadCommandIntellisenseService(commandRegistry));
        var adapter = new HatchInteractiveCommandAdapter();
        var session = Assert.IsType<CadDocumentSession>(new CadEditorSessionFactory().Create(new CadDocument()));

        Assert.True((await runtime.SubmitAsync("CIRCLE 0,0 2", session)).Result?.Success);
        Assert.True((await runtime.SubmitAsync("CIRCLE 10,0 2", session)).Result?.Success);
        var circles = session.Document.Entities.OfType<Circle>().ToArray();
        Assert.Equal(2, circles.Length);
        session.SetSelection(circles, ProCad.Editing.Selection.CadSelectionMode.Replace);

        runtime.BeginCommand("HATCH");
        var result = await adapter.SubmitAsync(
            runtime,
            new CadPromptToken(CadPromptTokenType.Raw, string.Empty),
            session,
            commit: false);

        Assert.True(result.Result?.Success, result.Result?.Message);
        var hatches = session.Document.Entities.OfType<Hatch>().ToArray();
        Assert.Equal(2, hatches.Length);
        Assert.All(hatches, static hatch => Assert.True(hatch.IsSolid));
    }

    [Fact]
    public async Task TextAdapter_CommitsAfterPickWithDefaults()
    {
        var commandRegistry = new CadCommandRegistry();
        commandRegistry.Register(new TextCadCommand());
        var intellisense = new CadCommandIntellisenseService(commandRegistry);
        var runtime = new CadCommandRuntime(commandRegistry, intellisense);
        var adapter = new TextInteractiveCommandAdapter();
        var session = new CadEditorSessionFactory().Create(new CadDocument());

        runtime.BeginCommand("TEXT");
        var result = await adapter.SubmitAsync(
            runtime,
            new CadPromptToken(CadPromptTokenType.Coordinate, "4,8"),
            session,
            commit: false);

        Assert.True(result.Result?.Success);
        Assert.Contains("Created TEXT", result.Result?.Message, StringComparison.OrdinalIgnoreCase);
        Assert.False(runtime.State.IsActive);
    }

    [Fact]
    public async Task InsertAdapter_CompletesAfterBlockNameAndPickedPoint()
    {
        var commandRegistry = new CadCommandRegistry();
        commandRegistry.Register(new InsertCadCommand());
        var intellisense = new CadCommandIntellisenseService(commandRegistry);
        var runtime = new CadCommandRuntime(commandRegistry, intellisense);
        var adapter = new InsertInteractiveCommandAdapter();
        var session = Assert.IsType<CadDocumentSession>(new CadEditorSessionFactory().Create(new CadDocument()));
        var block = new ACadSharp.Tables.BlockRecord("ADAPTER_BLOCK");
        block.Entities.Add(new Line(new CSMath.XYZ(0, 0, 0), new CSMath.XYZ(1, 0, 0)));
        session.Document.BlockRecords.Add(block);

        runtime.BeginCommand("INSERT");
        var blockName = await adapter.SubmitAsync(
            runtime,
            new CadPromptToken(CadPromptTokenType.Text, "ADAPTER_BLOCK"),
            session,
            commit: false);
        var placed = await adapter.SubmitAsync(
            runtime,
            new CadPromptToken(CadPromptTokenType.Coordinate, "4,6"),
            session,
            commit: false);

        Assert.True(blockName.Handled);
        Assert.Null(blockName.Result);
        Assert.True(placed.Result?.Success);
        Assert.Contains("Created INSERT", placed.Result?.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Single(session.Document.Entities.OfType<Insert>());
        Assert.False(runtime.State.IsActive);
    }

    [Fact]
    public async Task DimLinearAdapter_CommitsAfterThirdPickedPoint()
    {
        var commandRegistry = new CadCommandRegistry();
        commandRegistry.Register(new DimLinearCadCommand());
        var intellisense = new CadCommandIntellisenseService(commandRegistry);
        var runtime = new CadCommandRuntime(commandRegistry, intellisense);
        var adapter = new DimLinearInteractiveCommandAdapter();
        var session = new CadEditorSessionFactory().Create(new CadDocument());

        runtime.BeginCommand("DIMLINEAR");

        var first = await adapter.SubmitAsync(
            runtime,
            new CadPromptToken(CadPromptTokenType.Coordinate, "0,0"),
            session,
            commit: false);
        var second = await adapter.SubmitAsync(
            runtime,
            new CadPromptToken(CadPromptTokenType.Coordinate, "10,0"),
            session,
            commit: false);
        var third = await adapter.SubmitAsync(
            runtime,
            new CadPromptToken(CadPromptTokenType.Coordinate, "10,2"),
            session,
            commit: false);

        Assert.True(first.Handled);
        Assert.Null(first.Result);
        Assert.True(second.Handled);
        Assert.Null(second.Result);
        Assert.True(third.Result?.Success);
        Assert.Contains("Created DIMLINEAR", third.Result?.Message, StringComparison.OrdinalIgnoreCase);
        var typedSession = Assert.IsType<CadDocumentSession>(session);
        Assert.NotEmpty(typedSession.Document.Entities.OfType<Line>());
        Assert.NotEmpty(typedSession.Document.Entities.OfType<TextEntity>());
        Assert.False(runtime.State.IsActive);
    }

    [Fact]
    public async Task LeaderAdapter_ProducesPreviewHintsAfterPickedPoint()
    {
        var commandRegistry = new CadCommandRegistry();
        commandRegistry.Register(new LeaderCadCommand());
        var intellisense = new CadCommandIntellisenseService(commandRegistry);
        var runtime = new CadCommandRuntime(commandRegistry, intellisense);
        var adapter = new LeaderInteractiveCommandAdapter();

        runtime.BeginCommand("LEADER");
        await adapter.SubmitAsync(
            runtime,
            new CadPromptToken(CadPromptTokenType.Coordinate, "2,3"),
            session: null,
            commit: false);

        var canPreview = ((ICadInteractiveCommandPreviewProvider)adapter).TryBuildPreview(
            session: null,
            cursorPoint: new System.Numerics.Vector2(5f, 6f),
            fallbackPrompt: runtime.State.Prompt,
            fallbackStatus: runtime.State.LastMessage,
            out var preview);

        Assert.True(canPreview);
        Assert.Equal("LEADER", preview.CommandName);
        Assert.Contains(preview.Hints, static hint => hint.Kind == "RubberBand");
    }

    public static IEnumerable<object[]> SelectionCommitPreviewAdapters()
    {
        yield return [new EraseInteractiveCommandAdapter()];
        yield return [new BoundaryInteractiveCommandAdapter()];
        yield return [new HatchInteractiveCommandAdapter()];
        yield return [new CopyClipInteractiveCommandAdapter()];
        yield return [new CutInteractiveCommandAdapter()];
        yield return [new ExplodeInteractiveCommandAdapter()];
        yield return [new JoinInteractiveCommandAdapter()];
    }

    [Theory]
    [MemberData(nameof(SelectionCommitPreviewAdapters))]
    public void SelectionCommitAdapters_ProvidePreviewForSelection(ICadInteractiveCommandAdapter adapter)
    {
        var provider = Assert.IsAssignableFrom<ICadInteractiveCommandPreviewProvider>(adapter);
        var session = Assert.IsType<CadDocumentSession>(new CadEditorSessionFactory().Create(new CadDocument()));
        var line = new Line
        {
            StartPoint = new CSMath.XYZ(0, 0, 0),
            EndPoint = new CSMath.XYZ(10, 0, 0)
        };
        session.Document.Entities.Add(line);
        session.SetSelection([line], ProCad.Editing.Selection.CadSelectionMode.Replace);

        var canPreview = provider.TryBuildPreview(
            session,
            cursorPoint: new System.Numerics.Vector2(3f, 1f),
            fallbackPrompt: null,
            fallbackStatus: null,
            out var preview);

        Assert.True(canPreview);
        Assert.Equal(adapter.CommandName, preview.CommandName);
        Assert.Contains(preview.Hints, static hint => hint.Kind == "PickPoint");
        Assert.Contains(preview.Hints, static hint => hint.Kind == "Prompt");
    }

    [Theory]
    [MemberData(nameof(SelectionCommitPreviewAdapters))]
    public void SelectionCommitAdapters_ShowSelectionPromptWithoutSelection(ICadInteractiveCommandAdapter adapter)
    {
        var provider = Assert.IsAssignableFrom<ICadInteractiveCommandPreviewProvider>(adapter);
        var session = new CadEditorSessionFactory().Create(new CadDocument());

        var canPreview = provider.TryBuildPreview(
            session,
            cursorPoint: new System.Numerics.Vector2(2f, 2f),
            fallbackPrompt: null,
            fallbackStatus: null,
            out var preview);

        Assert.True(canPreview);
        Assert.Equal(adapter.CommandName, preview.CommandName);
        Assert.Contains(preview.Hints, static hint => hint.Kind == "Prompt");
        Assert.Contains("Select entities", preview.Status, StringComparison.OrdinalIgnoreCase);
    }
}
