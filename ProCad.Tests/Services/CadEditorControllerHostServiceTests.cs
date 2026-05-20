using ProCad.Editing.Commands;
using ProCad.Editing.Controllers;
using ProCad.Editing.Identifiers;
using ProCad.Editing.Prompt;
using ProCad.Editing.Sessions;
using ProCad.Editing.Undo;
using ProCad.Services;
using ProCad.ViewModels;
using ACadSharp;
using Xunit;

namespace ProCad.Tests.Services;

public sealed class CadEditorControllerHostServiceTests
{
    [Fact]
    public void GetOrCreate_ReturnsDocumentScopedControllersAndRuntimes()
    {
        var context = new CadDocumentContextService();
        var selection = new CadSelectionService();
        var sessionHost = new CadEditorSessionHostService(new CadEditorSessionFactory(), context, selection);
        var registry = new CadCommandRegistry();
        registry.Register(new LineCadCommand());
        var factory = new CadEditorControllerFactory(registry);
        var host = new CadEditorControllerHostService(sessionHost, factory, context);

        var documentA = new CadDocument();
        var documentB = new CadDocument();
        var firstA = host.GetOrCreate(documentA);
        var secondA = host.GetOrCreate(documentA);
        var firstB = host.GetOrCreate(documentB);

        Assert.Same(firstA, secondA);
        Assert.NotSame(firstA, firstB);
        Assert.NotSame(firstA.CommandRuntime, firstB.CommandRuntime);
        Assert.NotEqual(firstA.Session.SessionId, firstB.Session.SessionId);
        Assert.NotEqual(firstA.ContextSnapshots.Current.SessionId, firstB.ContextSnapshots.Current.SessionId);
    }

    [Fact]
    public void GetActiveController_ResolvesFromActiveDocumentContext()
    {
        var context = new CadDocumentContextService();
        var selection = new CadSelectionService();
        var sessionHost = new CadEditorSessionHostService(new CadEditorSessionFactory(), context, selection);
        var registry = new CadCommandRegistry();
        registry.Register(new LineCadCommand());
        var factory = new CadEditorControllerFactory(registry);
        var host = new CadEditorControllerHostService(sessionHost, factory, context);
        var document = new CadDocument();
        context.ActiveDocument = new CadDocumentViewModel(
            document,
            Core.CadFileFormat.Dxf,
            path: null,
            displayName: "active.dxf",
            render: null!);

        var active = host.GetActiveController();

        Assert.NotNull(active);
        Assert.Same(document, active!.Document);
    }

    [Fact]
    public void ContextSnapshot_TracksPromptMetadata()
    {
        var context = new CadDocumentContextService();
        var selection = new CadSelectionService();
        var sessionHost = new CadEditorSessionHostService(new CadEditorSessionFactory(), context, selection);
        var registry = new CadCommandRegistry();
        registry.Register(new LineCadCommand());
        var factory = new CadEditorControllerFactory(registry);
        var host = new CadEditorControllerHostService(sessionHost, factory, context);
        var document = new CadDocument();

        var controller = host.GetOrCreate(document);
        controller.CommandRuntime.BeginCommand("LINE");

        var snapshot = controller.ContextSnapshots.Current;
        Assert.True(snapshot.IsCommandActive);
        Assert.Equal("LINE", snapshot.ActiveCommand);
        Assert.NotNull(snapshot.ParameterHelp);
        Assert.True(snapshot.CompletionCount >= 0);
    }

    [Fact]
    public async Task SubmitAsync_AppliesCommandLineUndoMetadata()
    {
        var context = new CadDocumentContextService();
        var selection = new CadSelectionService();
        var sessionHost = new CadEditorSessionHostService(new CadEditorSessionFactory(), context, selection);
        var registry = new CadCommandRegistry();
        registry.Register(new LineCadCommand());
        var factory = new CadEditorControllerFactory(registry);
        var host = new CadEditorControllerHostService(sessionHost, factory, context);
        var document = new CadDocument();

        var controller = host.GetOrCreate(document);
        var resolution = await controller.SubmitAsync("LINE 0,0 1,1");

        Assert.True(resolution.Handled);
        var undoUnits = controller.Session.UndoRedo.GetUndoUnits();
        Assert.Single(undoUnits);
        Assert.Equal(CadUndoSource.CommandLine, undoUnits[0].Metadata.Source);
        Assert.Equal(controller.Session.SessionId.Value, undoUnits[0].Metadata.ActorId);
    }

    [Fact]
    public void Remove_DisposesControllerInstance()
    {
        var context = new CadDocumentContextService();
        var selection = new CadSelectionService();
        var sessionHost = new CadEditorSessionHostService(new CadEditorSessionFactory(), context, selection);
        var factory = new DisposableControllerFactory();
        var host = new CadEditorControllerHostService(sessionHost, factory, context);
        var document = new CadDocument();

        var controller = host.GetOrCreate(document);
        Assert.False(((DisposableController)controller).Disposed);

        var removed = host.Remove(document);

        Assert.True(removed);
        Assert.True(((DisposableController)controller).Disposed);
    }

    [Fact]
    public void SessionRemove_DisposesControllerInstance()
    {
        var context = new CadDocumentContextService();
        var selection = new CadSelectionService();
        var sessionHost = new CadEditorSessionHostService(new CadEditorSessionFactory(), context, selection);
        var factory = new DisposableControllerFactory();
        var host = new CadEditorControllerHostService(sessionHost, factory, context);
        var document = new CadDocument();

        var controller = host.GetOrCreate(document);
        Assert.False(((DisposableController)controller).Disposed);

        var removed = sessionHost.Remove(document);

        Assert.True(removed);
        Assert.True(((DisposableController)controller).Disposed);
        Assert.False(host.TryGet(document, out _));
    }

    private sealed class DisposableControllerFactory : ICadEditorControllerFactory
    {
        public ICadEditorController Create(CadDocument document, ICadEditorSession session)
        {
            return new DisposableController(document, session);
        }
    }

    private sealed class DisposableController : ICadEditorController
    {
        public DisposableController(CadDocument document, ICadEditorSession session)
        {
            Document = document;
            Session = session;
            CommandRuntime = new NoopRuntime();
            ContextSnapshots = new NoopSnapshotProvider(session.SessionId);
        }

        public CadDocument Document { get; }
        public ICadEditorSession Session { get; }
        public ICadCommandRuntime CommandRuntime { get; }
        public ICadEditorContextSnapshotProvider ContextSnapshots { get; }
        public bool Disposed { get; private set; }

        public void BeginCommand(string commandName)
        {
        }

        public void CancelCommand()
        {
        }

        public ValueTask<CadPromptResolution> SubmitAsync(string input, CancellationToken cancellationToken = default)
        {
            return ValueTask.FromResult(new CadPromptResolution(false, null, CommandRuntime.State));
        }

        public ValueTask<CadPromptResolution> SubmitTokenAsync(CadPromptToken token, bool commit = false, CancellationToken cancellationToken = default)
        {
            return ValueTask.FromResult(new CadPromptResolution(false, null, CommandRuntime.State));
        }

        public void Dispose()
        {
            Disposed = true;
        }
    }

    private sealed class NoopRuntime : ICadCommandRuntime
    {
        public CadPromptState State => CadPromptState.Idle;
        public string? LastCommandInput => null;
        public event EventHandler<CadPromptState>? StateChanged
        {
            add { }
            remove { }
        }
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
            return State;
        }

        public ValueTask<CadPromptResolution> SubmitAsync(string input, ICadEditorSession? session, CancellationToken cancellationToken = default)
        {
            return ValueTask.FromResult(new CadPromptResolution(false, null, State));
        }

        public ValueTask<CadPromptResolution> SubmitTokenAsync(CadPromptToken token, ICadEditorSession? session, bool commit = false, CancellationToken cancellationToken = default)
        {
            return ValueTask.FromResult(new CadPromptResolution(false, null, State));
        }
    }

    private sealed class NoopSnapshotProvider : ICadEditorContextSnapshotProvider
    {
        public NoopSnapshotProvider(CadDocumentSessionId sessionId)
        {
            Current = new CadEditorContextSnapshot(
                SessionId: sessionId,
                Prompt: "Command",
                ActiveCommand: null,
                IsCommandActive: false,
                CanStartCommands: true,
                UndoDepth: 0,
                RedoDepth: 0,
                ParameterHelp: null,
                LastMessage: null,
                ActiveParameterIndex: 0,
                CompletionCount: 0);
        }

        public CadEditorContextSnapshot Current { get; }
        public event EventHandler<CadEditorContextSnapshot>? SnapshotChanged
        {
            add { }
            remove { }
        }
    }
}
