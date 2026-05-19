using ProCad.Core;
using ProCad.Editing.Commands;
using ProCad.Editing.Controllers;
using ProCad.Editing.Sessions;
using ProCad.Services;
using ProCad.ViewModels;
using ACadSharp;

namespace ProCad.Tests.Services;

public sealed class CadCommandScriptRecordingServiceTests
{
    [Fact]
    public async Task RecordsSuccessfulCommands_FromActiveRuntime()
    {
        var harness = CreateHarness();
        harness.Service.Start(clearExisting: true);

        var controller = harness.ControllerHost.GetActiveController();
        Assert.NotNull(controller);
        var resolution = await controller.SubmitAsync("ECHO one");

        Assert.True(resolution.Result?.Success);
        Assert.Equal(1, harness.Service.EntryCount);
        Assert.Contains("ECHO one", harness.Service.BuildScript(includeHeader: false), StringComparison.Ordinal);
    }

    [Fact]
    public async Task DoesNotRecordFailedCommands_WhenIncludeFailedDisabled()
    {
        var harness = CreateHarness();
        harness.Service.IncludeFailedCommands = false;
        harness.Service.Start(clearExisting: true);

        var controller = harness.ControllerHost.GetActiveController();
        Assert.NotNull(controller);
        var resolution = await controller.SubmitAsync("UNKNOWN");

        Assert.False(resolution.Result?.Success);
        Assert.Equal(0, harness.Service.EntryCount);
    }

    [Fact]
    public async Task SaveAsync_PersistsRecordedScript()
    {
        var harness = CreateHarness();
        harness.Service.Start(clearExisting: true);
        var controller = harness.ControllerHost.GetActiveController();
        Assert.NotNull(controller);
        await controller.SubmitAsync("ECHO save-test");

        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.scr");
        try
        {
            var save = await harness.Service.SaveAsync(path, includeHeader: true);
            var content = await File.ReadAllTextAsync(path);

            Assert.True(File.Exists(path));
            Assert.Equal(1, save.EntryCount);
            Assert.Contains("ECHO save-test", content, StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task BuildScript_TimestampCommentsRespectToggle()
    {
        var harness = CreateHarness();
        harness.Service.Start(clearExisting: true);
        var controller = harness.ControllerHost.GetActiveController();
        Assert.NotNull(controller);
        await controller.SubmitAsync("ECHO timestamp");

        harness.Service.IncludeMetadataComments = true;
        harness.Service.IncludeTimestampComments = true;
        var withTimestamp = harness.Service.BuildScript(includeHeader: false);
        Assert.Contains("; UTC ", withTimestamp, StringComparison.Ordinal);

        harness.Service.IncludeTimestampComments = false;
        var withoutTimestamp = harness.Service.BuildScript(includeHeader: false);
        Assert.DoesNotContain("; UTC ", withoutTimestamp, StringComparison.Ordinal);
    }

    private static Harness CreateHarness()
    {
        var document = new CadDocument();
        var active = new CadDocumentViewModel(
            document,
            CadFileFormat.Dxf,
            path: null,
            displayName: "active.dxf",
            render: null!);
        var context = new CadDocumentContextService
        {
            ActiveDocument = active
        };
        var selection = new CadSelectionService();
        var sessionHost = new CadEditorSessionHostService(new CadEditorSessionFactory(), context, selection);
        var registry = new CadCommandRegistry();
        registry.Register(new EchoCommand());
        var controllerFactory = new CadEditorControllerFactory(registry);
        var controllerHost = new CadEditorControllerHostService(sessionHost, controllerFactory, context);
        var service = new CadCommandScriptRecordingService();
        var tracker = new CadCommandScriptRecordingTracker(controllerHost, context, service);
        return new Harness(service, controllerHost, tracker);
    }

    private sealed record Harness(
        CadCommandScriptRecordingService Service,
        CadEditorControllerHostService ControllerHost,
        CadCommandScriptRecordingTracker Tracker);

    private sealed class EchoCommand : ICadCommandHandler
    {
        public string Name => "ECHO";
        public IReadOnlyList<string> Aliases => [];

        public bool CanExecute(CadCommandContext context)
        {
            return true;
        }

        public ValueTask<CadCommandResult> ExecuteAsync(CadCommandContext context)
        {
            return ValueTask.FromResult(CadCommandResult.Ok(string.Join(' ', context.Arguments)));
        }
    }
}
