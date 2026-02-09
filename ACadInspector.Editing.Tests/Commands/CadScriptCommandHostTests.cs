using System.Linq;
using ACadInspector.Editing.Commands;
using ACadInspector.Editing.Sessions;
using ACadSharp;
using ACadSharp.Entities;
using Xunit;

namespace ACadInspector.Editing.Tests.Commands;

public sealed class CadScriptCommandHostTests
{
    [Fact]
    public async Task ExecuteAsync_StopOnError_StopsAtFirstFailure()
    {
        var (session, host) = CreateHarness();
        var script = """
LINE 0,0 1,0
UNKNOWN
POINT 2,2
""";

        var result = await host.ExecuteAsync(script, session);

        Assert.False(result.Success);
        Assert.Equal(2, result.ExecutedCount);
        Assert.Equal(1, result.SucceededCount);
        Assert.Equal(1, result.FailedCount);
        Assert.Equal(2, result.Entries.Count);
        Assert.Single(session.Document.Entities.OfType<Line>());
        Assert.Empty(session.Document.Entities.OfType<Point>());
    }

    [Fact]
    public async Task ExecuteAsync_ContinueOnError_ExecutesRemainingCommands()
    {
        var (session, host) = CreateHarness();
        var script = """
LINE 0,0 1,0
UNKNOWN
POINT 2,2
""";

        var result = await host.ExecuteAsync(
            script,
            session,
            new CadScriptCommandPlaybackOptions(StopOnError: false));

        Assert.False(result.Success);
        Assert.Equal(3, result.ExecutedCount);
        Assert.Equal(2, result.SucceededCount);
        Assert.Equal(1, result.FailedCount);
        Assert.Equal(3, result.Entries.Count);
        Assert.Single(session.Document.Entities.OfType<Line>());
        Assert.Single(session.Document.Entities.OfType<Point>());
    }

    [Fact]
    public async Task ExecuteAsync_SkipsCommentAndBlankLines()
    {
        var (session, host) = CreateHarness();
        var script = """

# comment
; comment
// comment
POINT 10,20
""";

        var result = await host.ExecuteAsync(script, session);

        Assert.True(result.Success);
        Assert.Equal(1, result.ExecutedCount);
        Assert.Equal(1, result.SucceededCount);
        Assert.Equal(0, result.FailedCount);
        Assert.Single(result.Entries);
        Assert.Equal(5, result.Entries[0].LineNumber);
        var point = Assert.Single(session.Document.Entities.OfType<Point>());
        Assert.Equal(10.0, point.Location.X);
        Assert.Equal(20.0, point.Location.Y);
    }

    [Fact]
    public async Task ExecuteAsync_StartLine_SkipsCommandsBeforeConfiguredLine()
    {
        var (session, host) = CreateHarness();
        var script = """
POINT 1,1
POINT 2,2
POINT 3,3
""";

        var result = await host.ExecuteAsync(
            script,
            session,
            new CadScriptCommandPlaybackOptions(StartLine: 3));

        Assert.True(result.Success);
        Assert.Equal(1, result.ExecutedCount);
        var point = Assert.Single(session.Document.Entities.OfType<Point>());
        Assert.Equal(3.0, point.Location.X);
        Assert.Equal(3.0, point.Location.Y);
    }

    [Fact]
    public async Task ExecuteAsync_MaxCommands_StopsAfterConfiguredCount()
    {
        var (session, host) = CreateHarness();
        var script = """
POINT 1,1
POINT 2,2
POINT 3,3
""";

        var result = await host.ExecuteAsync(
            script,
            session,
            new CadScriptCommandPlaybackOptions(StopOnError: false, MaxCommands: 2));

        Assert.True(result.Success);
        Assert.Equal(2, result.ExecutedCount);
        var points = session.Document.Entities.OfType<Point>().ToList();
        Assert.Equal(2, points.Count);
        Assert.Equal(1.0, points[0].Location.X);
        Assert.Equal(2.0, points[1].Location.X);
    }

    private static (CadDocumentSession Session, ICadScriptCommandHost Host) CreateHarness()
    {
        var document = new CadDocument();
        var session = (CadDocumentSession)new CadEditorSessionFactory().Create(document);

        var registry = new CadCommandRegistry();
        registry.Register(new LineCadCommand());
        registry.Register(new PointCadCommand());

        return (session, new CadScriptCommandHost(registry));
    }
}
