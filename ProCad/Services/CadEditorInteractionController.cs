using System;
using System.Collections.Generic;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using ProCad.Editing.Interaction;
using ProCad.Editing.Operations;
using ProCad.Editing.Prompt;
using ProCad.Editing.Sessions;
using ProCad.Editing.Undo;
using ACadSharp;

namespace ProCad.Services;

public sealed class CadEditorInteractionController : IDisposable
{
    private readonly CadDocument _document;
    private readonly CadEditorSessionHostService? _sessionHost;
    private readonly ICadCommandRuntime _commandRuntime;
    private readonly CadInteractionRouter _interactionRouter;
    private readonly CadCollaborationWorkspaceService? _collaborationWorkspace;
    private bool _disposed;

    public CadEditorInteractionController(
        CadDocument document,
        CadEditorSessionHostService? sessionHost,
        ICadCommandRuntime commandRuntime,
        CadInteractionRouter interactionRouter,
        CadCollaborationWorkspaceService? collaborationWorkspace = null)
    {
        _document = document ?? throw new ArgumentNullException(nameof(document));
        _sessionHost = sessionHost;
        _commandRuntime = commandRuntime ?? throw new ArgumentNullException(nameof(commandRuntime));
        _interactionRouter = interactionRouter ?? throw new ArgumentNullException(nameof(interactionRouter));
        _collaborationWorkspace = collaborationWorkspace;

        _commandRuntime.StateChanged += OnCommandRuntimeStateChanged;
        _interactionRouter.OperationsCommitted += OnInteractionOperationsCommitted;
    }

    public event EventHandler<string>? InteractionStatusChanged;

    public void UpdateScene(ProCad.Rendering.RenderScene? scene, ProCad.Rendering.RenderSpatialIndex? index)
    {
        _interactionRouter.Scene = scene;
        _interactionRouter.SpatialIndex = index;
    }

    public void UpdateSnapEnabled(bool enabled)
    {
        _interactionRouter.UpdateSnapEnabled(enabled);
    }

    public void UpdateTracking(bool trackingEnabled, bool orthoEnabled, bool polarEnabled)
    {
        _interactionRouter.UpdateTracking(trackingEnabled, orthoEnabled, polarEnabled);
    }

    public ICadEditorSession? ResolveSession()
    {
        return _sessionHost?.GetOrCreate(_document);
    }

    public void ResetTransientState()
    {
        var session = ResolveSession();
        _interactionRouter.ResetTransientState(session);
    }

    public void BeginCommand(string? commandName)
    {
        if (string.IsNullOrWhiteSpace(commandName))
        {
            return;
        }

        _commandRuntime.BeginCommand(commandName);
        PublishLocalPresence(
            session: ResolveSession(),
            state: _commandRuntime.State,
            cursorPoint: null,
            viewport: null,
            toolPreview: null,
            force: true);
    }

    public async ValueTask<CadToolVisualSnapshot> HandleInteractionAsync(
        CadInteractionEvent interactionEvent,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var session = ResolveSession();
        var revisionBefore = session?.Revision ?? -1;

        if (session is not null && _sessionHost is not null)
        {
            _sessionHost.SyncSelectionToSession(session);
        }

        if (session is not null && _collaborationWorkspace is not null)
        {
            try
            {
                await _collaborationWorkspace.EnsureSessionAsync(session, cancellationToken).ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                InteractionStatusChanged?.Invoke(this, $"Collaboration connect failed: {ex.Message}");
            }
        }

        var context = new CadInteractionContext(session, _commandRuntime.State.ActiveCommand);
        IDisposable? undoScope = null;
        if (session is not null)
        {
            undoScope = CadUndoExecutionContext.Push(new CadUndoRecordOptions(
                CommandId: _commandRuntime.State.ActiveCommand,
                Label: _commandRuntime.State.ActiveCommand,
                ActorId: session.SessionId.Value,
                Source: CadUndoSource.Tool,
                MergeKey: BuildInteractionMergeKey(interactionEvent)));
        }

        CadToolVisualSnapshot visual;
        try
        {
            visual = await _interactionRouter
                .RouteAsync(interactionEvent, context, cancellationToken)
                .ConfigureAwait(true);
        }
        finally
        {
            undoScope?.Dispose();
        }

        PublishLocalPresence(
            session,
            _commandRuntime.State,
            interactionEvent.WorldPoint,
            interactionEvent.Viewport,
            visual.Hints,
            force: false);

        if (session is not null && _sessionHost is not null)
        {
            if (session.Revision > revisionBefore)
            {
                _sessionHost.SyncSelectionToUi(session);
                _sessionHost.NotifySessionChanged(session);
            }
            else
            {
                _sessionHost.SyncSelectionToSession(session);
            }
        }

        return visual;
    }

    public CadToolVisualSnapshot RefreshVisualSnapshot()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return _interactionRouter.BuildCurrentVisualSnapshot(ResolveSession());
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _commandRuntime.StateChanged -= OnCommandRuntimeStateChanged;
        _interactionRouter.OperationsCommitted -= OnInteractionOperationsCommitted;
    }

    private void OnCommandRuntimeStateChanged(object? sender, CadPromptState state)
    {
        PublishLocalPresence(
            session: ResolveSession(),
            state: state,
            cursorPoint: null,
            viewport: null,
            toolPreview: null,
            force: true);
    }

    private void PublishLocalPresence(
        ICadEditorSession? session,
        CadPromptState state,
        Vector2? cursorPoint,
        CadInteractionViewport? viewport,
        IReadOnlyList<CadToolVisualHint>? toolPreview,
        bool force)
    {
        _collaborationWorkspace?.PublishLocalPresence(session, state, cursorPoint, viewport, toolPreview, force);
    }

    private string? BuildInteractionMergeKey(CadInteractionEvent interactionEvent)
    {
        if (string.IsNullOrWhiteSpace(_commandRuntime.State.ActiveCommand))
        {
            return null;
        }

        return interactionEvent.Kind switch
        {
            CadInteractionEventKind.PointerMove or CadInteractionEventKind.PointerDown or CadInteractionEventKind.PointerUp =>
                $"tool:{_commandRuntime.State.ActiveCommand}",
            _ => null
        };
    }

    private void OnInteractionOperationsCommitted(object? sender, IReadOnlyList<CadOperation> operations)
    {
        if (_collaborationWorkspace is null || operations.Count == 0)
        {
            return;
        }

        var session = ResolveSession();
        if (session is null)
        {
            return;
        }

        _ = PublishInteractionOperationsAsync(session, operations);
    }

    private async Task PublishInteractionOperationsAsync(
        ICadEditorSession session,
        IReadOnlyList<CadOperation> operations)
    {
        try
        {
            await _collaborationWorkspace!
                .PublishLocalOperationsAsync(session, operations)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            InteractionStatusChanged?.Invoke(this, $"Collaboration publish failed: {ex.Message}");
        }
    }
}
