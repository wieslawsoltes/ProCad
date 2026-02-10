using ACadInspector.Core;
using ACadInspector.Editing.Commands;
using ACadInspector.Editing.Controllers;
using ACadInspector.Editing.Interaction;
using ACadInspector.Editing.Prompt;
using ACadInspector.Editing.Sessions;
using ACadInspector.Services;
using ACadInspector.ViewModels;
using ACadSharp;
using System;
using System.Collections.Generic;
using System.Reactive.Threading.Tasks;
using System.Threading.Tasks;
using Xunit;

namespace ACadInspector.Tests.ViewModels;

public sealed class CadEditorToolPanelViewModelTests
{
    [Fact]
    public void StartToolCommand_BeginsRuntimeCommand()
    {
        var document = new CadDocument();
        var documentContext = new CadDocumentContextService();
        documentContext.ActiveDocument = new CadDocumentViewModel(
            document,
            CadFileFormat.Dxf,
            path: null,
            displayName: "test.dxf",
            render: null!);

        var selectionService = new CadSelectionService();
        var sessionHost = new CadEditorSessionHostService(
            new CadEditorSessionFactory(),
            documentContext,
            selectionService);
        var registry = new CadCommandRegistry();
        registry.Register(new LineCadCommand());
        var controllerFactory = new CadEditorControllerFactory(registry);
        var controllerHost = new CadEditorControllerHostService(sessionHost, controllerFactory, documentContext);
        var runtime = controllerHost.GetOrCreate(document).CommandRuntime;
        var viewModel = new CadEditorToolPanelViewModel(controllerHost, documentContext);

        using var subscription = viewModel.StartToolCommand.Execute("LINE").Subscribe();

        Assert.True(viewModel.CanStartTools);
        Assert.True(runtime.State.IsActive);
        Assert.Equal("LINE", runtime.State.ActiveCommand);
        Assert.Equal("LINE", runtime.State.Prompt);
    }

    [Fact]
    public void StartToolCommand_UpdatesPromptHelpAndKeywordCompletions()
    {
        var (_, viewModel, _) = CreateHarness(registerKeywordCommand: true);

        using var subscription = viewModel.StartToolCommand.Execute("KWTEST").Subscribe();

        Assert.True(viewModel.HasCompletions);
        Assert.Contains(viewModel.Completions, static item => item.Kind == "Keyword");
        Assert.Contains("KWTEST", viewModel.ParameterHelp, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ApplyCompletionCommand_KeywordRoutesThroughRuntimeSession()
    {
        var (_, viewModel, runtime) = CreateHarness(registerKeywordCommand: true);
        using var subscription = viewModel.StartToolCommand.Execute("KWTEST").Subscribe();
        var completion = Assert.Single(
            viewModel.Completions,
            static item => string.Equals(item.Value, "MODE", StringComparison.OrdinalIgnoreCase));

        await viewModel.ApplyCompletionCommand.Execute(completion).ToTask();

        Assert.True(runtime.State.IsActive);
        Assert.Equal("KWTEST", runtime.State.ActiveCommand);
        Assert.Equal("KWTEST", viewModel.ActiveCommand);
    }

    [Fact]
    public void IdleRuntimePreview_DoesNotSurfaceCommandCompletionsInToolPanel()
    {
        var document = new CadDocument();
        var documentContext = new CadDocumentContextService();
        documentContext.ActiveDocument = new CadDocumentViewModel(
            document,
            CadFileFormat.Dxf,
            path: null,
            displayName: "test.dxf",
            render: null!);

        var selectionService = new CadSelectionService();
        var sessionHost = new CadEditorSessionHostService(
            new CadEditorSessionFactory(),
            documentContext,
            selectionService);
        var registry = new CadCommandRegistry();
        registry.Register(new LineCadCommand());
        var controllerFactory = new CadEditorControllerFactory(registry);
        var controllerHost = new CadEditorControllerHostService(sessionHost, controllerFactory, documentContext);
        var adapterRegistry = new CadInteractiveCommandAdapterRegistry(Array.Empty<ICadInteractiveCommandAdapter>());

        var toolPanel = new CadEditorToolPanelViewModel(controllerHost, documentContext);
        using var commandLine = new CadCommandLineViewModel(
            controllerHost,
            documentContext,
            sessionHost,
            adapterRegistry,
            collaborationWorkspace: null);

        commandLine.Input = "L";
        commandLine.Input = string.Empty;

        Assert.False(toolPanel.HasCompletions);
        Assert.Empty(toolPanel.Completions);
    }

    [Fact]
    public void IdleRuntimeState_ShowsSelectionAndHidesPromptHelp()
    {
        var (_, viewModel, runtime) = CreateHarness(registerKeywordCommand: false);

        Assert.False(runtime.State.IsActive);
        Assert.False(viewModel.IsCommandActive);
        Assert.Equal("Selection", viewModel.ActiveCommand);
        Assert.True(string.IsNullOrWhiteSpace(viewModel.ParameterHelp));
    }

    private static (CadDocumentContextService Context, CadEditorToolPanelViewModel ViewModel, ICadCommandRuntime Runtime) CreateHarness(bool registerKeywordCommand)
    {
        var document = new CadDocument();
        var documentContext = new CadDocumentContextService();
        documentContext.ActiveDocument = new CadDocumentViewModel(
            document,
            CadFileFormat.Dxf,
            path: null,
            displayName: "test.dxf",
            render: null!);

        var selectionService = new CadSelectionService();
        var sessionHost = new CadEditorSessionHostService(
            new CadEditorSessionFactory(),
            documentContext,
            selectionService);
        var registry = new CadCommandRegistry();
        registry.Register(new LineCadCommand());
        if (registerKeywordCommand)
        {
            registry.Register(new KeywordPromptCommand());
        }

        var controllerFactory = new CadEditorControllerFactory(registry);
        var controllerHost = new CadEditorControllerHostService(sessionHost, controllerFactory, documentContext);
        var runtime = controllerHost.GetOrCreate(document).CommandRuntime;
        var viewModel = new CadEditorToolPanelViewModel(controllerHost, documentContext);
        return (documentContext, viewModel, runtime);
    }

    private sealed class KeywordPromptCommand : ICadDescribedCommandHandler
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
                    Description: "Keyword test",
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
