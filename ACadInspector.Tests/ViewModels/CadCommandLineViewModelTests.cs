using System;
using System.Reactive.Threading.Tasks;
using ACadInspector.Core;
using ACadInspector.Editing.Commands;
using ACadInspector.Editing.Controllers;
using ACadInspector.Editing.Interaction;
using ACadInspector.Editing.Sessions;
using ACadInspector.Services;
using ACadInspector.ViewModels;
using ACadSharp;
using Xunit;

namespace ACadInspector.Tests.ViewModels;

public sealed class CadCommandLineViewModelTests
{
    [Fact]
    public async Task SubmitCommand_WithoutActiveDocument_ShowsStatusMessage()
    {
        var (_, viewModel) = CreateHarness(activeDocument: null);

        await viewModel.SubmitCommand.Execute().ToTask();

        Assert.Equal("No active document.", viewModel.StatusMessage);
        Assert.Contains("No active document.", viewModel.History);
    }

    [Fact]
    public async Task SubmitCommand_WithActiveDocument_ExecutesViaActiveControllerRuntime()
    {
        var document = new CadDocument();
        var activeViewModel = new CadDocumentViewModel(
            document,
            CadFileFormat.Dxf,
            path: null,
            displayName: "active.dxf",
            render: null!);
        var (_, viewModel) = CreateHarness(activeViewModel);
        viewModel.Input = "ECHO hello";

        await viewModel.SubmitCommand.Execute().ToTask();

        Assert.Equal("hello", viewModel.StatusMessage);
        Assert.Contains("> ECHO hello", viewModel.History);
        Assert.Contains("hello", viewModel.History);
    }

    private static (CadDocumentContextService Context, CadCommandLineViewModel ViewModel) CreateHarness(
        CadDocumentViewModel? activeDocument)
    {
        var documentContext = new CadDocumentContextService
        {
            ActiveDocument = activeDocument
        };
        var selectionService = new CadSelectionService();
        var sessionHost = new CadEditorSessionHostService(
            new CadEditorSessionFactory(),
            documentContext,
            selectionService);
        var registry = new CadCommandRegistry();
        registry.Register(new EchoCommand());
        var controllerFactory = new CadEditorControllerFactory(registry);
        var controllerHost = new CadEditorControllerHostService(sessionHost, controllerFactory, documentContext);
        var adapterRegistry = new CadInteractiveCommandAdapterRegistry(Array.Empty<ICadInteractiveCommandAdapter>());
        var viewModel = new CadCommandLineViewModel(
            controllerHost,
            documentContext,
            sessionHost,
            adapterRegistry,
            collaborationWorkspace: null);
        return (documentContext, viewModel);
    }

    private sealed class EchoCommand : ICadCommandHandler
    {
        public string Name => "ECHO";
        public IReadOnlyList<string> Aliases => ["EC"];

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

