using System.Threading;
using System.Threading.Tasks;
using ProCad.Editing.Commands;
using ProCad.Editing.Prompt;
using ProCad.Editing.Sessions;
using ProCad.Editing.Undo;
using ACadSharp;

namespace ProCad.Editing.Controllers;

public sealed class CadEditorController : ICadEditorController
{
    private bool _disposed;

    public CadEditorController(
        CadDocument document,
        ICadEditorSession session,
        ICadCommandRuntime commandRuntime,
        ICadEditorContextSnapshotProvider contextSnapshots)
    {
        Document = document ?? throw new ArgumentNullException(nameof(document));
        Session = session ?? throw new ArgumentNullException(nameof(session));
        CommandRuntime = commandRuntime ?? throw new ArgumentNullException(nameof(commandRuntime));
        ContextSnapshots = contextSnapshots ?? throw new ArgumentNullException(nameof(contextSnapshots));
    }

    public CadDocument Document { get; }
    public ICadEditorSession Session { get; }
    public ICadCommandRuntime CommandRuntime { get; }
    public ICadEditorContextSnapshotProvider ContextSnapshots { get; }

    public void BeginCommand(string commandName)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        CommandRuntime.BeginCommand(commandName);
    }

    public void CancelCommand()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        CommandRuntime.Cancel();
    }

    public ValueTask<CadPromptResolution> SubmitAsync(string input, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        using var scope = CadUndoExecutionContext.Push(new CadUndoRecordOptions(
            CommandId: CommandRuntime.State.ActiveCommand,
            Label: CommandRuntime.State.ActiveCommand,
            ActorId: Session.SessionId.Value,
            Source: CadUndoSource.CommandLine));
        if (string.IsNullOrWhiteSpace(input) && CommandRuntime.State.IsActive)
        {
            return CommandRuntime.SubmitTokenAsync(
                new CadPromptToken(CadPromptTokenType.Raw, string.Empty),
                Session,
                commit: true,
                cancellationToken);
        }

        return CommandRuntime.SubmitAsync(input, Session, cancellationToken);
    }

    public ValueTask<CadPromptResolution> SubmitTokenAsync(
        CadPromptToken token,
        bool commit = false,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        using var scope = CadUndoExecutionContext.Push(new CadUndoRecordOptions(
            CommandId: CommandRuntime.State.ActiveCommand,
            Label: CommandRuntime.State.ActiveCommand,
            ActorId: Session.SessionId.Value,
            Source: CadUndoSource.Tool));
        return CommandRuntime.SubmitTokenAsync(token, Session, commit, cancellationToken);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (ContextSnapshots is IDisposable disposableSnapshots)
        {
            disposableSnapshots.Dispose();
        }
    }
}

public sealed class CadEditorControllerFactory : ICadEditorControllerFactory
{
    private readonly ICadCommandRegistry _commandRegistry;

    public CadEditorControllerFactory(ICadCommandRegistry commandRegistry)
    {
        _commandRegistry = commandRegistry ?? throw new ArgumentNullException(nameof(commandRegistry));
    }

    public ICadEditorController Create(CadDocument document, ICadEditorSession session)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(session);

        var runtime = new CadCommandRuntime(
            _commandRegistry,
            new CadCommandIntellisenseService(_commandRegistry));
        var snapshots = new CadEditorContextSnapshotProvider(session, runtime);
        return new CadEditorController(document, session, runtime, snapshots);
    }

    private sealed class CadEditorContextSnapshotProvider : ICadEditorContextSnapshotProvider, IDisposable
    {
        private readonly ICadEditorSession _session;
        private readonly ICadCommandRuntime _runtime;
        private bool _disposed;

        public CadEditorContextSnapshotProvider(
            ICadEditorSession session,
            ICadCommandRuntime runtime)
        {
            _session = session ?? throw new ArgumentNullException(nameof(session));
            _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
            Current = BuildSnapshot(_runtime.State);
            _runtime.StateChanged += OnRuntimeStateChanged;
        }

        public CadEditorContextSnapshot Current { get; private set; }
        public event EventHandler<CadEditorContextSnapshot>? SnapshotChanged;

        private void OnRuntimeStateChanged(object? sender, CadPromptState state)
        {
            if (_disposed)
            {
                return;
            }

            Current = BuildSnapshot(state);
            SnapshotChanged?.Invoke(this, Current);
        }

        private CadEditorContextSnapshot BuildSnapshot(CadPromptState state)
        {
            return new CadEditorContextSnapshot(
                SessionId: _session.SessionId,
                Prompt: state.Prompt,
                ActiveCommand: state.ActiveCommand,
                IsCommandActive: state.IsActive,
                CanStartCommands: true,
                UndoDepth: _session.UndoRedo.UndoDepth,
                RedoDepth: _session.UndoRedo.RedoDepth,
                ParameterHelp: state.ParameterHelp,
                LastMessage: state.LastMessage,
                ActiveParameterIndex: state.ActiveParameterIndex,
                CompletionCount: state.Completions.Count);
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _runtime.StateChanged -= OnRuntimeStateChanged;
        }
    }
}
