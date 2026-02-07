using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using ACadInspector.Collaboration.Contracts;
using ACadInspector.Collaboration.Presence;
using ACadInspector.Collaboration.Services;
using ACadInspector.Collaboration.Transports;
using ACadInspector.Collaboration.UI;
using ACadInspector.Editing.Identifiers;
using ACadInspector.Editing.Interaction;
using ACadInspector.Editing.Operations;
using ACadInspector.Editing.Sessions;
using ACadInspector.Editing.Prompt;
using ACadSharp.Entities;

namespace ACadInspector.Services;

public sealed class CadCollaborationWorkspaceService : ICadCollabControlService, IDisposable
{
    private static readonly TimeSpan PresenceThrottle = TimeSpan.FromMilliseconds(80);
    private static readonly TimeSpan PresenceTimeToLive = TimeSpan.FromSeconds(10);
    private const int MaxSelectionBoundsCacheEntries = 16_384;

    private readonly ICadCollabService _collabService;
    private readonly ICadRealtimeTransportFactory _transportFactory;
    private readonly ICadCollabUiService _uiService;
    private readonly ICadCollabConnectionOptionsProvider _connectionOptionsProvider;
    private readonly CadCollabPresenceRegistry _presenceRegistry;
    private readonly CadEditorSessionHostService? _sessionHost;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly object _sync = new();
    private readonly object _selectionBoundsSync = new();

    private readonly Dictionary<Guid, SessionContext> _sessions = new();
    private readonly Dictionary<SelectionBoundsCacheKey, SelectionBoundsCacheEntry> _selectionBoundsCache = new();
    private Guid? _activeSessionId;
    private ICadEditorSession? _lastActiveSession;
    private bool _collaborationEnabled = true;
    private bool _disposed;

    private sealed class SessionContext
    {
        public required ICadEditorSession Session { get; init; }
        public required ICadRealtimeSession RealtimeSession { get; init; }
        public required string AuthModeDisplay { get; init; }
        public required string TransportModeDisplay { get; init; }
        public DateTimeOffset LastPresenceSentUtc { get; set; } = DateTimeOffset.MinValue;
        public DateTimeOffset LastSyncUtc { get; set; } = DateTimeOffset.UtcNow;
    }

    private readonly record struct SelectionBoundsCacheKey(Guid SessionId, Guid EntityId);
    private readonly record struct SelectionBoundsCacheEntry(long Revision, Vector2 Min, Vector2 Max);

    public CadCollaborationWorkspaceService(
        ICadCollabService collabService,
        ICadRealtimeTransportFactory transportFactory,
        ICadCollabUiService uiService,
        ICadCollabConnectionOptionsProvider connectionOptionsProvider,
        CadCollabPresenceRegistry presenceRegistry,
        CadEditorSessionHostService? sessionHost = null)
    {
        _collabService = collabService ?? throw new ArgumentNullException(nameof(collabService));
        _transportFactory = transportFactory ?? throw new ArgumentNullException(nameof(transportFactory));
        _uiService = uiService ?? throw new ArgumentNullException(nameof(uiService));
        _connectionOptionsProvider = connectionOptionsProvider ?? throw new ArgumentNullException(nameof(connectionOptionsProvider));
        _presenceRegistry = presenceRegistry ?? throw new ArgumentNullException(nameof(presenceRegistry));
        _sessionHost = sessionHost;
        if (_sessionHost is not null)
        {
            _sessionHost.SessionRemoved += OnSessionRemoved;
        }
    }

    public Guid LocalUserId { get; } = Guid.NewGuid();
    public string LocalDisplayName { get; } = $"User-{Environment.UserName}";
    public string LocalColor { get; } = "#35C2FF";
    public CadCollabConnectionOptions CurrentOptions => _connectionOptionsProvider.Current;

    public event EventHandler? PresenceChanged;

    public async ValueTask EnsureSessionAsync(ICadEditorSession session, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(session);

        if (!_collaborationEnabled)
        {
            _uiService.UpdateConnection(
                isConnected: false,
                status: "Offline",
                authMode: FormatAuthMode(_connectionOptionsProvider.Current.AuthMode),
                transportMode: FormatTransportMode(_connectionOptionsProvider.Current.TransportMode),
                canReconnect: false);
            return;
        }

        var sessionId = session.SessionId.Value;
        _lastActiveSession = session;
        SessionContext? createdContext = null;
        var connected = false;
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            lock (_sync)
            {
                _activeSessionId = sessionId;
            }

            if (TryGetSessionContext(sessionId, out _))
            {
                return;
            }

            var options = _connectionOptionsProvider.Current;
            if (options.AuthMode == CadCollabAuthMode.BearerToken &&
                string.IsNullOrWhiteSpace(options.BearerToken))
            {
                throw new InvalidOperationException("Bearer token authentication requires a non-empty token.");
            }

            var transport = CreateTransport(options);
            var realtime = _collabService.CreateSession(session, transport, LocalUserId);
            createdContext = new SessionContext
            {
                Session = session,
                RealtimeSession = realtime,
                AuthModeDisplay = FormatAuthMode(options.AuthMode),
                TransportModeDisplay = FormatTransportMode(options.TransportMode)
            };

            realtime.TransportStateChanged += OnTransportStateChanged;
            realtime.PresenceReceived += OnPresenceReceived;
            realtime.ConflictsChanged += OnConflictsChanged;
            realtime.OperationsApplied += OnOperationsApplied;

            lock (_sync)
            {
                _sessions[sessionId] = createdContext;
            }

            await realtime.ConnectAsync(cancellationToken).ConfigureAwait(false);
            connected = true;
            createdContext.LastSyncUtc = DateTimeOffset.UtcNow;

            _uiService.UpdateConnection(
                isConnected: true,
                status: "Connected",
                authMode: createdContext.AuthModeDisplay,
                transportMode: createdContext.TransportModeDisplay,
                canReconnect: true);
            _uiService.UpdateConflicts(Array.Empty<CadCollabConflictUi>());
            UpdateParticipantsUi();
            RaisePresenceChanged();
        }
        catch (Exception ex)
        {
            if (!connected && createdContext is not null)
            {
                lock (_sync)
                {
                    _sessions.Remove(sessionId);
                }

                createdContext.RealtimeSession.TransportStateChanged -= OnTransportStateChanged;
                createdContext.RealtimeSession.PresenceReceived -= OnPresenceReceived;
                createdContext.RealtimeSession.ConflictsChanged -= OnConflictsChanged;
                createdContext.RealtimeSession.OperationsApplied -= OnOperationsApplied;
                await createdContext.RealtimeSession.DisposeAsync().ConfigureAwait(false);
            }

            _uiService.UpdateConnection(
                isConnected: false,
                status: $"Connection error: {ex.Message}",
                authMode: FormatAuthMode(_connectionOptionsProvider.Current.AuthMode),
                transportMode: FormatTransportMode(_connectionOptionsProvider.Current.TransportMode),
                canReconnect: true);
            throw;
        }
        finally
        {
            _gate.Release();
        }
    }

    public void PublishLocalPresence(
        ICadEditorSession? session,
        CadPromptState promptState,
        Vector2? cursorPoint,
        CadInteractionViewport? viewport,
        IReadOnlyList<CadToolVisualHint>? toolPreview = null,
        bool force = false)
    {
        if (_disposed || session is null || !_collaborationEnabled)
        {
            return;
        }

        SessionContext? context;
        lock (_sync)
        {
            _activeSessionId = session.SessionId.Value;
            _sessions.TryGetValue(session.SessionId.Value, out context);
        }

        if (context is null)
        {
            return;
        }

        var now = DateTimeOffset.UtcNow;
        var shouldSend = force || (now - context.LastPresenceSentUtc) >= PresenceThrottle;

        var presence = BuildLocalPresence(session, promptState, cursorPoint, viewport, toolPreview, now);
        _presenceRegistry.Update(presence, PresenceTimeToLive, now);
        UpdateParticipantsUi(now);
        RaisePresenceChanged();

        if (!shouldSend)
        {
            return;
        }

        context.LastPresenceSentUtc = now;
        var realtime = context.RealtimeSession;

        _ = PublishPresenceAsync(realtime, presence);
    }

    public async ValueTask PublishLocalOperationsAsync(
        ICadEditorSession? session,
        IReadOnlyList<CadOperation>? operations,
        CancellationToken cancellationToken = default)
    {
        if (_disposed || !_collaborationEnabled || session is null || operations is null || operations.Count == 0)
        {
            return;
        }

        await EnsureSessionAsync(session, cancellationToken).ConfigureAwait(false);

        if (!TryGetRealtimeSession(session.SessionId.Value, out var realtime))
        {
            return;
        }

        if (realtime is null)
        {
            return;
        }

        await realtime.SubmitLocalAppliedAsync(operations, cancellationToken).ConfigureAwait(false);
    }

    public IReadOnlyList<CadToolVisualHint> GetRemoteGhostHints(
        ICadEditorSession? session = null,
        DateTimeOffset? now = null)
    {
        var nowValue = now ?? DateTimeOffset.UtcNow;
        var sessionId = session?.SessionId.Value;
        var active = _presenceRegistry.GetActive(nowValue);
        if (active.Count == 0)
        {
            return Array.Empty<CadToolVisualHint>();
        }

        var hints = new List<CadToolVisualHint>(active.Count * 3);
        foreach (var presence in active)
        {
            if (presence.UserId == LocalUserId)
            {
                continue;
            }

            if (sessionId.HasValue &&
                presence.SessionId.HasValue &&
                presence.SessionId.Value != sessionId.Value)
            {
                continue;
            }

            if (presence.CursorPoint is { } cursor)
            {
                var anchor = new Vector2((float)cursor.X, (float)cursor.Y);
                var label = string.IsNullOrWhiteSpace(presence.ActiveTool)
                    ? presence.DisplayName
                    : $"{presence.DisplayName} ({presence.ActiveTool})";

                hints.Add(new CadToolVisualHint(
                    Kind: "RemoteCursor",
                    Anchor: anchor,
                    SecondaryAnchor: null,
                    Text: label,
                    Color: presence.Color));

                if (presence.SelectedEntityIds is { Count: > 0 })
                {
                    if (TryResolveSelectionBounds(session, presence.SelectedEntityIds, out var min, out var max))
                    {
                        hints.Add(new CadToolVisualHint(
                            Kind: "RemoteSelectionBounds",
                            Anchor: min,
                            SecondaryAnchor: max,
                            Text: $"Sel:{presence.SelectedEntityIds.Count}",
                            Color: presence.Color));
                    }
                    else
                    {
                        hints.Add(new CadToolVisualHint(
                            Kind: "RemoteSelection",
                            Anchor: anchor + new Vector2(6f, -6f),
                            SecondaryAnchor: null,
                            Text: $"Sel:{presence.SelectedEntityIds.Count}",
                            Color: presence.Color));
                    }
                }
            }

            if (presence.Viewport is { } viewport)
            {
                var center = new Vector2((float)viewport.Center.X, (float)viewport.Center.Y);
                var half = new Vector2((float)(viewport.Width * 0.5), (float)(viewport.Height * 0.5));
                hints.Add(new CadToolVisualHint(
                    Kind: "RemoteViewport",
                    Anchor: center - half,
                    SecondaryAnchor: center + half,
                    Text: null,
                    Color: presence.Color));
            }

            if (presence.ToolPreview is { Count: > 0 } toolPreview)
            {
                AppendRemoteToolPreviewHints(toolPreview, presence, hints);
            }
        }

        return hints;
    }

    private bool TryResolveSelectionBounds(
        ICadEditorSession? session,
        IReadOnlyList<Guid> selectedEntityIds,
        out Vector2 min,
        out Vector2 max)
    {
        min = default;
        max = default;
        if (session is null || selectedEntityIds.Count == 0)
        {
            return false;
        }

        var hasBounds = false;
        var minX = float.PositiveInfinity;
        var minY = float.PositiveInfinity;
        var maxX = float.NegativeInfinity;
        var maxY = float.NegativeInfinity;

        foreach (var selectedId in selectedEntityIds)
        {
            if (!TryGetCachedEntityBounds(session, selectedId, out var entityMin, out var entityMax))
            {
                continue;
            }

            hasBounds = true;
            minX = Math.Min(minX, entityMin.X);
            minY = Math.Min(minY, entityMin.Y);
            maxX = Math.Max(maxX, entityMax.X);
            maxY = Math.Max(maxY, entityMax.Y);
        }

        if (!hasBounds)
        {
            return false;
        }

        min = new Vector2(minX, minY);
        max = new Vector2(maxX, maxY);
        return true;
    }

    private bool TryGetCachedEntityBounds(
        ICadEditorSession session,
        Guid entityGuid,
        out Vector2 min,
        out Vector2 max)
    {
        min = default;
        max = default;
        var key = new SelectionBoundsCacheKey(session.SessionId.Value, entityGuid);
        lock (_selectionBoundsSync)
        {
            if (_selectionBoundsCache.TryGetValue(key, out var cached) &&
                cached.Revision == session.Revision)
            {
                min = cached.Min;
                max = cached.Max;
                return true;
            }
        }

        if (!session.EntityIndex.TryGetEntity(new CadEntityId(entityGuid), out var entity) ||
            !TryGetEntityBounds(entity, out min, out max))
        {
            lock (_selectionBoundsSync)
            {
                _selectionBoundsCache.Remove(key);
            }

            return false;
        }

        lock (_selectionBoundsSync)
        {
            _selectionBoundsCache[key] = new SelectionBoundsCacheEntry(session.Revision, min, max);
            TrimSelectionBoundsCache_NoLock(session.SessionId.Value);
        }

        return true;
    }

    private void TrimSelectionBoundsCache_NoLock(Guid activeSessionId)
    {
        if (_selectionBoundsCache.Count <= MaxSelectionBoundsCacheEntries)
        {
            return;
        }

        var overflow = _selectionBoundsCache.Count - MaxSelectionBoundsCacheEntries;
        var removed = 0;
        foreach (var key in _selectionBoundsCache.Keys.ToArray())
        {
            if (key.SessionId == activeSessionId)
            {
                continue;
            }

            _selectionBoundsCache.Remove(key);
            removed++;
            if (removed >= overflow)
            {
                return;
            }
        }

        foreach (var key in _selectionBoundsCache.Keys.Take(overflow - removed).ToArray())
        {
            _selectionBoundsCache.Remove(key);
        }
    }

    private void RemoveSelectionBoundsCache(Guid sessionId)
    {
        lock (_selectionBoundsSync)
        {
            foreach (var key in _selectionBoundsCache.Keys.ToArray())
            {
                if (key.SessionId == sessionId)
                {
                    _selectionBoundsCache.Remove(key);
                }
            }
        }
    }

    private static bool TryGetEntityBounds(Entity entity, out Vector2 min, out Vector2 max)
    {
        min = default;
        max = default;

        if (TryConvertBoundingBox(entity.GetBoundingBox(), out min, out max))
        {
            return true;
        }

        switch (entity)
        {
            case Line line:
                return TryBuildBounds(
                    [
                        new Vector2((float)line.StartPoint.X, (float)line.StartPoint.Y),
                        new Vector2((float)line.EndPoint.X, (float)line.EndPoint.Y)
                    ],
                    out min,
                    out max);
            case Arc arc:
            {
                var center = new Vector2((float)arc.Center.X, (float)arc.Center.Y);
                var radius = (float)Math.Abs(arc.Radius);
                var points = new List<Vector2>(6)
                {
                    PointAtAngle(center, radius, (float)arc.StartAngle),
                    PointAtAngle(center, radius, (float)arc.EndAngle)
                };

                foreach (var angle in new[] { 0f, MathF.PI * 0.5f, MathF.PI, MathF.PI * 1.5f })
                {
                    if (IsAngleOnArc(angle, (float)arc.StartAngle, (float)arc.EndAngle))
                    {
                        points.Add(PointAtAngle(center, radius, angle));
                    }
                }

                return TryBuildBounds(points, out min, out max);
            }
            case Circle circle:
            {
                var center = new Vector2((float)circle.Center.X, (float)circle.Center.Y);
                var radius = (float)Math.Abs(circle.Radius);
                min = center - new Vector2(radius, radius);
                max = center + new Vector2(radius, radius);
                return true;
            }
            case LwPolyline polyline:
            {
                if (polyline.Vertices.Count == 0)
                {
                    return false;
                }

                var points = new List<Vector2>(polyline.Vertices.Count);
                foreach (var vertex in polyline.Vertices)
                {
                    points.Add(new Vector2((float)vertex.Location.X, (float)vertex.Location.Y));
                }

                return TryBuildBounds(points, out min, out max);
            }
            case Point point:
            {
                var location = new Vector2((float)point.Location.X, (float)point.Location.Y);
                min = location;
                max = location;
                return true;
            }
            case TextEntity text:
            {
                var insert = new Vector2((float)text.InsertPoint.X, (float)text.InsertPoint.Y);
                min = insert - new Vector2(4f, 2f);
                max = insert + new Vector2(20f, 8f);
                return true;
            }
            case MText mtext:
            {
                var insert = new Vector2((float)mtext.InsertPoint.X, (float)mtext.InsertPoint.Y);
                var width = (float)Math.Max(1.0, mtext.RectangleWidth);
                var height = (float)Math.Max(1.0, mtext.Height);
                min = insert;
                max = insert + new Vector2(width, height);
                return true;
            }
            default:
                return false;
        }
    }

    private static bool TryConvertBoundingBox(CSMath.BoundingBox bounds, out Vector2 min, out Vector2 max)
    {
        min = default;
        max = default;

        var minX = (float)bounds.Min.X;
        var minY = (float)bounds.Min.Y;
        var maxX = (float)bounds.Max.X;
        var maxY = (float)bounds.Max.Y;

        if (!float.IsFinite(minX) ||
            !float.IsFinite(minY) ||
            !float.IsFinite(maxX) ||
            !float.IsFinite(maxY))
        {
            return false;
        }

        if (maxX < minX)
        {
            (minX, maxX) = (maxX, minX);
        }

        if (maxY < minY)
        {
            (minY, maxY) = (maxY, minY);
        }

        min = new Vector2(minX, minY);
        max = new Vector2(maxX, maxY);
        return true;
    }

    private static bool TryBuildBounds(IEnumerable<Vector2> points, out Vector2 min, out Vector2 max)
    {
        min = default;
        max = default;

        var hasValue = false;
        var minX = float.PositiveInfinity;
        var minY = float.PositiveInfinity;
        var maxX = float.NegativeInfinity;
        var maxY = float.NegativeInfinity;

        foreach (var point in points)
        {
            hasValue = true;
            minX = Math.Min(minX, point.X);
            minY = Math.Min(minY, point.Y);
            maxX = Math.Max(maxX, point.X);
            maxY = Math.Max(maxY, point.Y);
        }

        if (!hasValue)
        {
            return false;
        }

        min = new Vector2(minX, minY);
        max = new Vector2(maxX, maxY);
        return true;
    }

    private static Vector2 PointAtAngle(Vector2 center, float radius, float angle)
    {
        return center + new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * radius;
    }

    private static bool IsAngleOnArc(float test, float start, float end)
    {
        const float twoPi = MathF.PI * 2f;
        start = NormalizeAngle(start);
        end = NormalizeAngle(end);
        test = NormalizeAngle(test);

        if (end < start)
        {
            end += twoPi;
        }

        if (test < start)
        {
            test += twoPi;
        }

        return test >= start && test <= end;
    }

    private static float NormalizeAngle(float angle)
    {
        const float twoPi = MathF.PI * 2f;
        angle %= twoPi;
        if (angle < 0f)
        {
            angle += twoPi;
        }

        return angle;
    }

    public async ValueTask JoinAsync(CancellationToken cancellationToken = default)
    {
        if (_disposed)
        {
            return;
        }

        _collaborationEnabled = true;
        var session = ResolveActiveSession();
        if (session is null)
        {
            _uiService.UpdateConnection(
                isConnected: false,
                status: "Disconnected",
                authMode: FormatAuthMode(_connectionOptionsProvider.Current.AuthMode),
                transportMode: FormatTransportMode(_connectionOptionsProvider.Current.TransportMode),
                canReconnect: true);
            return;
        }

        await EnsureSessionAsync(session, cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask LeaveAsync(CancellationToken cancellationToken = default)
    {
        if (_disposed)
        {
            return;
        }

        _collaborationEnabled = false;
        await CloseAllSessionsAsync(cancellationToken).ConfigureAwait(false);
        _presenceRegistry.RemoveByUser(LocalUserId);

        _uiService.UpdateParticipants(Array.Empty<CadCollabParticipantUi>());
        _uiService.UpdateConflicts(Array.Empty<CadCollabConflictUi>());
        _uiService.UpdateDiagnostics(new CadCollabDiagnosticsUi(
            SyncLagMs: 0,
            QueueDepth: 0,
            SnapshotAge: TimeSpan.Zero,
            ResyncRequired: false));
        _uiService.UpdateConnection(
            isConnected: false,
            status: "Offline",
            authMode: FormatAuthMode(_connectionOptionsProvider.Current.AuthMode),
            transportMode: FormatTransportMode(_connectionOptionsProvider.Current.TransportMode),
            canReconnect: false);
        RaisePresenceChanged();
    }

    public async ValueTask<string> ShareAsync(CancellationToken cancellationToken = default)
    {
        if (_disposed)
        {
            return "Collaboration service is disposed.";
        }

        var session = ResolveActiveSession();
        if (session is null)
        {
            return "No active drawing session to share.";
        }

        if (!_collaborationEnabled)
        {
            _collaborationEnabled = true;
        }

        await EnsureSessionAsync(session, cancellationToken).ConfigureAwait(false);
        var options = _connectionOptionsProvider.Current;
        var sessionTag = session.SessionId.Value.ToString("D");
        return options.TransportMode switch
        {
            CadCollabTransportMode.WebSocket => $"Session {sessionTag} via WebSocket: {options.WebSocketUrl ?? "(not configured)"}",
            CadCollabTransportMode.SharedFile => $"Session {sessionTag} shared path: {options.SharedFilePath ?? "(not configured)"}",
            _ => $"Session {sessionTag} uses loopback transport (local machine only)."
        };
    }

    public async ValueTask ApplyConnectionOptionsAsync(CadCollabConnectionOptions options, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(options);

        _connectionOptionsProvider.Update(options);
        if (!_collaborationEnabled)
        {
            _uiService.UpdateConnection(
                isConnected: false,
                status: "Offline",
                authMode: FormatAuthMode(options.AuthMode),
                transportMode: FormatTransportMode(options.TransportMode),
                canReconnect: false);
            return;
        }

        await CloseAllSessionsAsync(cancellationToken).ConfigureAwait(false);
        var session = ResolveActiveSession();
        if (session is null)
        {
            _uiService.UpdateConnection(
                isConnected: false,
                status: "Disconnected",
                authMode: FormatAuthMode(options.AuthMode),
                transportMode: FormatTransportMode(options.TransportMode),
                canReconnect: true);
            return;
        }

        await EnsureSessionAsync(session, cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask ReconnectAsync(CancellationToken cancellationToken = default)
    {
        if (!_collaborationEnabled)
        {
            _uiService.UpdateConnection(
                isConnected: false,
                status: "Offline",
                authMode: FormatAuthMode(_connectionOptionsProvider.Current.AuthMode),
                transportMode: FormatTransportMode(_connectionOptionsProvider.Current.TransportMode),
                canReconnect: false);
            return;
        }

        if (!TryGetActiveRealtimeSession(out var realtime))
        {
            _uiService.UpdateConnection(
                isConnected: false,
                status: "No active collaboration session.",
                authMode: FormatAuthMode(_connectionOptionsProvider.Current.AuthMode),
                transportMode: FormatTransportMode(_connectionOptionsProvider.Current.TransportMode),
                canReconnect: true);
            return;
        }

        if (realtime is null)
        {
            return;
        }

        await realtime.ReconnectAsync(cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask ResyncAsync(CancellationToken cancellationToken = default)
    {
        if (!_collaborationEnabled)
        {
            return;
        }

        if (!TryGetActiveRealtimeSession(out var realtime))
        {
            return;
        }

        if (realtime is null)
        {
            return;
        }

        await realtime.ResyncAsync(cancellationToken).ConfigureAwait(false);
        MarkSessionSynced(realtime, DateTimeOffset.UtcNow);
        _uiService.UpdateDiagnostics(_uiService.Current.Diagnostics with
        {
            ResyncRequired = false,
            QueueDepth = 0,
            SnapshotAge = TimeSpan.Zero
        });
    }

    public async ValueTask<bool> ReapplyConflictAsync(string conflictId, CancellationToken cancellationToken = default)
    {
        if (!_collaborationEnabled)
        {
            return false;
        }

        if (!TryGetActiveRealtimeSession(out var realtime))
        {
            return false;
        }

        if (realtime is null)
        {
            return false;
        }

        var reapplied = await realtime.ReapplyConflictAsync(conflictId, cancellationToken).ConfigureAwait(false);
        if (reapplied)
        {
            var mapped = MapConflicts(realtime.GetConflicts());
            _uiService.UpdateConflicts(mapped);
            _uiService.UpdateDiagnostics(_uiService.Current.Diagnostics with
            {
                QueueDepth = mapped.Length,
                ResyncRequired = mapped.Length > 0
            });
        }

        return reapplied;
    }

    public ValueTask CloseSessionAsync(ICadEditorSession? session, CancellationToken cancellationToken = default)
    {
        if (_disposed || session is null)
        {
            return ValueTask.CompletedTask;
        }

        return CloseSessionAsync(session.SessionId.Value, cancellationToken);
    }

    public async ValueTask CloseSessionAsync(Guid sessionId, CancellationToken cancellationToken = default)
    {
        if (_disposed || sessionId == Guid.Empty)
        {
            return;
        }

        SessionContext? context;
        lock (_sync)
        {
            if (!_sessions.Remove(sessionId, out context))
            {
                return;
            }

            if (_activeSessionId == sessionId)
            {
                _activeSessionId = _sessions.Count == 0 ? null : _sessions.Keys.FirstOrDefault();
            }
        }

        if (context is null)
        {
            return;
        }

        context.RealtimeSession.TransportStateChanged -= OnTransportStateChanged;
        context.RealtimeSession.PresenceReceived -= OnPresenceReceived;
        context.RealtimeSession.ConflictsChanged -= OnConflictsChanged;
        context.RealtimeSession.OperationsApplied -= OnOperationsApplied;
        await context.RealtimeSession.DisposeAsync().ConfigureAwait(false);

        RemoveSelectionBoundsCache(sessionId);
        _presenceRegistry.RemoveBySession(sessionId);
        UpdateParticipantsUi();
        RaisePresenceChanged();

        var hasActiveSessions = false;
        lock (_sync)
        {
            hasActiveSessions = _sessions.Count > 0;
        }

        if (!hasActiveSessions)
        {
            _uiService.UpdateConnection(
                isConnected: false,
                status: _collaborationEnabled ? "Disconnected" : "Offline",
                authMode: FormatAuthMode(_connectionOptionsProvider.Current.AuthMode),
                transportMode: FormatTransportMode(_connectionOptionsProvider.Current.TransportMode),
                canReconnect: _collaborationEnabled);
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (_sessionHost is not null)
        {
            _sessionHost.SessionRemoved -= OnSessionRemoved;
        }
        _ = DisposeRealtimeSessionsAsync();
    }

    private static async Task PublishPresenceAsync(ICadRealtimeSession realtime, CadCollabPresence presence)
    {
        try
        {
            await realtime
                .PublishPresenceAsync(presence, PresenceTimeToLive)
                .ConfigureAwait(false);
        }
        catch
        {
            // Presence sending is best-effort and should not fail user interactions.
        }
    }

    private CadCollabPresence BuildLocalPresence(
        ICadEditorSession session,
        CadPromptState promptState,
        Vector2? cursorPoint,
        CadInteractionViewport? viewport,
        IReadOnlyList<CadToolVisualHint>? toolPreview,
        DateTimeOffset updatedAtUtc)
    {
        var selectedEntityIds = new List<Guid>();
        foreach (var selected in session.SelectionSet.Items)
        {
            if (selected is Entity entity && session.EntityIndex.TryGetId(entity, out var id))
            {
                selectedEntityIds.Add(id.Value);
            }
        }

        return new CadCollabPresence(
            UserId: LocalUserId,
            DisplayName: LocalDisplayName,
            Color: LocalColor,
            Status: promptState.IsActive ? "Editing" : "Idle",
            ActiveTool: promptState.ActiveCommand,
            PromptStage: promptState.ParameterHelp,
            CursorPoint: cursorPoint is { } point
                ? new CadCollabPoint(point.X, point.Y)
                : null,
            Viewport: viewport is { } view
                ? new CadCollabViewportSummary(
                    Center: new CadCollabPoint(view.Center.X, view.Center.Y),
                    Zoom: view.Zoom,
                    Width: view.Width,
                    Height: view.Height)
                : null,
            SelectedEntityIds: selectedEntityIds,
            UpdatedAtUtc: updatedAtUtc,
            ToolPreview: BuildToolPreview(toolPreview),
            SessionId: session.SessionId.Value);
    }

    private ICadEditorSession? ResolveActiveSession()
    {
        lock (_sync)
        {
            if (_activeSessionId.HasValue &&
                _sessions.TryGetValue(_activeSessionId.Value, out var context))
            {
                return context.Session;
            }
        }

        if (_lastActiveSession is not null)
        {
            return _lastActiveSession;
        }

        return _sessionHost?.GetActiveSession();
    }

    private async ValueTask CloseAllSessionsAsync(CancellationToken cancellationToken = default)
    {
        List<Guid> sessionIds;
        lock (_sync)
        {
            sessionIds = _sessions.Keys.ToList();
        }

        foreach (var sessionId in sessionIds)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await CloseSessionAsync(sessionId, cancellationToken).ConfigureAwait(false);
        }
    }

    private bool TryGetSessionContext(Guid sessionId, out SessionContext? context)
    {
        lock (_sync)
        {
            return _sessions.TryGetValue(sessionId, out context);
        }
    }

    private bool TryGetRealtimeSession(Guid sessionId, out ICadRealtimeSession? realtimeSession)
    {
        realtimeSession = null;
        if (!TryGetSessionContext(sessionId, out var context) || context is null)
        {
            return false;
        }

        realtimeSession = context.RealtimeSession;
        return true;
    }

    private bool TryGetSessionContext(ICadRealtimeSession realtimeSession, out SessionContext? context)
    {
        lock (_sync)
        {
            foreach (var entry in _sessions.Values)
            {
                if (ReferenceEquals(entry.RealtimeSession, realtimeSession))
                {
                    context = entry;
                    return true;
                }
            }
        }

        context = null;
        return false;
    }

    private bool TryGetActiveRealtimeSession(out ICadRealtimeSession? realtimeSession)
    {
        realtimeSession = null;
        Guid? activeSessionId;
        lock (_sync)
        {
            activeSessionId = _activeSessionId;
        }

        if (!activeSessionId.HasValue)
        {
            return false;
        }

        return TryGetRealtimeSession(activeSessionId.Value, out realtimeSession);
    }

    private void OnTransportStateChanged(object? sender, CadRealtimeStateChangedEventArgs args)
    {
        if (!_collaborationEnabled)
        {
            return;
        }

        var connected = args.State == CadRealtimeTransportState.Connected;
        var status = args.Message ?? args.State.ToString();
        var authMode = FormatAuthMode(_connectionOptionsProvider.Current.AuthMode);
        var transportMode = FormatTransportMode(_connectionOptionsProvider.Current.TransportMode);
        if (sender is ICadRealtimeSession realtime &&
            TryGetSessionContext(realtime, out var context) &&
            context is not null)
        {
            authMode = context.AuthModeDisplay;
            transportMode = context.TransportModeDisplay;
        }

        _uiService.UpdateConnection(
            isConnected: connected,
            status: status,
            authMode: authMode,
            transportMode: transportMode,
            canReconnect: _collaborationEnabled);
    }

    private void OnPresenceReceived(object? sender, CadCollabPresence presence)
    {
        if (!_collaborationEnabled)
        {
            return;
        }

        _presenceRegistry.Update(presence, PresenceTimeToLive);
        UpdateParticipantsUi();
        RaisePresenceChanged();
    }

    private void OnConflictsChanged(object? sender, IReadOnlyList<CadRealtimeConflict> conflicts)
    {
        if (!_collaborationEnabled)
        {
            return;
        }

        var mapped = MapConflicts(conflicts);
        _uiService.UpdateConflicts(mapped);

        var resyncRequired = mapped.Length > 0;
        _uiService.UpdateDiagnostics(_uiService.Current.Diagnostics with
        {
            ResyncRequired = resyncRequired,
            QueueDepth = mapped.Length
        });
    }

    private void OnOperationsApplied(object? sender, CadRealtimeOperationsAppliedEventArgs args)
    {
        if (_disposed ||
            !_collaborationEnabled ||
            sender is not ICadRealtimeSession realtime ||
            !TryGetSessionContext(realtime, out var context) ||
            context is null)
        {
            return;
        }

        context.LastSyncUtc = DateTimeOffset.UtcNow;
        UpdateSnapshotAgeDiagnostics(context.LastSyncUtc);

        if (!args.IsRemote || _sessionHost is null)
        {
            return;
        }

        _sessionHost.NotifySessionChanged(context.Session);
    }

    private void MarkSessionSynced(ICadRealtimeSession realtime, DateTimeOffset nowUtc)
    {
        if (!TryGetSessionContext(realtime, out var context) || context is null)
        {
            return;
        }

        context.LastSyncUtc = nowUtc;
        UpdateSnapshotAgeDiagnostics(nowUtc);
    }

    private void UpdateSnapshotAgeDiagnostics(DateTimeOffset? now = null)
    {
        SessionContext? activeContext = null;
        lock (_sync)
        {
            if (_activeSessionId.HasValue)
            {
                _sessions.TryGetValue(_activeSessionId.Value, out activeContext);
            }
        }

        if (activeContext is null)
        {
            _uiService.UpdateDiagnostics(_uiService.Current.Diagnostics with
            {
                SnapshotAge = TimeSpan.Zero
            });
            return;
        }

        var nowValue = now ?? DateTimeOffset.UtcNow;
        var snapshotAge = nowValue - activeContext.LastSyncUtc;
        if (snapshotAge < TimeSpan.Zero)
        {
            snapshotAge = TimeSpan.Zero;
        }

        _uiService.UpdateDiagnostics(_uiService.Current.Diagnostics with
        {
            SnapshotAge = snapshotAge
        });
    }

    private void UpdateParticipantsUi(DateTimeOffset? now = null)
    {
        var nowValue = now ?? DateTimeOffset.UtcNow;
        var participants = _presenceRegistry
            .GetActive(nowValue)
            .Select(presence => new CadCollabParticipantUi(
                UserId: presence.UserId,
                DisplayName: presence.DisplayName,
                Color: presence.Color,
                IsLocal: presence.UserId == LocalUserId,
                ActiveTool: presence.ActiveTool,
                ActiveCommand: presence.ActiveTool,
                LastActiveUtc: presence.UpdatedAtUtc,
                PromptStage: presence.PromptStage))
            .OrderByDescending(static participant => participant.LastActiveUtc)
            .ToArray();

        _uiService.UpdateParticipants(participants);

        var lagMs = participants.Length == 0
            ? 0
            : Math.Max(0, (nowValue - participants[0].LastActiveUtc).TotalMilliseconds);
        _uiService.UpdateDiagnostics(_uiService.Current.Diagnostics with
        {
            SyncLagMs = lagMs
        });
        UpdateSnapshotAgeDiagnostics(nowValue);
    }

    private async Task DisposeRealtimeSessionsAsync()
    {
        List<(Guid SessionId, ICadRealtimeSession Realtime)> realtimeSessions;
        lock (_sync)
        {
            realtimeSessions = _sessions.Values
                .Select(static context => (context.Session.SessionId.Value, context.RealtimeSession))
                .ToList();
            _sessions.Clear();
            _activeSessionId = null;
        }

        foreach (var (sessionId, realtime) in realtimeSessions)
        {
            _presenceRegistry.RemoveBySession(sessionId);
            RemoveSelectionBoundsCache(sessionId);
            realtime.TransportStateChanged -= OnTransportStateChanged;
            realtime.PresenceReceived -= OnPresenceReceived;
            realtime.ConflictsChanged -= OnConflictsChanged;
            realtime.OperationsApplied -= OnOperationsApplied;
            await realtime.DisposeAsync().ConfigureAwait(false);
        }

        UpdateParticipantsUi();
        RaisePresenceChanged();
        _uiService.UpdateConnection(
            isConnected: false,
            status: _collaborationEnabled ? "Disconnected" : "Offline",
            authMode: FormatAuthMode(_connectionOptionsProvider.Current.AuthMode),
            transportMode: FormatTransportMode(_connectionOptionsProvider.Current.TransportMode),
            canReconnect: _collaborationEnabled);
    }

    private void RaisePresenceChanged()
    {
        PresenceChanged?.Invoke(this, EventArgs.Empty);
    }

    private void OnSessionRemoved(object? sender, ICadEditorSession session)
    {
        if (ReferenceEquals(_lastActiveSession, session))
        {
            _lastActiveSession = null;
        }

        _ = CloseSessionSafelyAsync(session);
    }

    private async Task CloseSessionSafelyAsync(ICadEditorSession session)
    {
        try
        {
            await CloseSessionAsync(session).ConfigureAwait(false);
        }
        catch
        {
            // Session teardown should not surface background exceptions to the UI thread.
        }
    }

    private ICadRealtimeTransport CreateTransport(CadCollabConnectionOptions options)
    {
        return options.TransportMode switch
        {
            CadCollabTransportMode.WebSocket => CreateWebSocketTransport(options),
            CadCollabTransportMode.SharedFile => CreateSharedFileTransport(options),
            _ => _transportFactory.CreateLoopback()
        };
    }

    private ICadRealtimeTransport CreateWebSocketTransport(CadCollabConnectionOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.WebSocketUrl) ||
            !Uri.TryCreate(options.WebSocketUrl, UriKind.Absolute, out var uri))
        {
            throw new InvalidOperationException("WebSocket transport requires a valid absolute URL.");
        }

        return _transportFactory.CreateWebSocket(uri);
    }

    private ICadRealtimeTransport CreateSharedFileTransport(CadCollabConnectionOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.SharedFilePath))
        {
            throw new InvalidOperationException("Shared-file transport requires a base path.");
        }

        return _transportFactory.CreateSharedFile(options.SharedFilePath);
    }

    private static string FormatTransportMode(CadCollabTransportMode mode)
    {
        return mode switch
        {
            CadCollabTransportMode.WebSocket => "WebSocket",
            CadCollabTransportMode.SharedFile => "Shared File",
            _ => "Loopback"
        };
    }

    private static string FormatAuthMode(CadCollabAuthMode mode)
    {
        return mode switch
        {
            CadCollabAuthMode.BearerToken => "Bearer",
            _ => "Anonymous"
        };
    }

    private static CadCollabConflictUi[] MapConflicts(IReadOnlyList<CadRealtimeConflict> conflicts)
    {
        if (conflicts.Count == 0)
        {
            return Array.Empty<CadCollabConflictUi>();
        }

        return conflicts
            .Select(static conflict => new CadCollabConflictUi(
                ConflictId: conflict.ConflictId,
                EntityKey: conflict.EntityKey,
                Summary: conflict.Summary,
                ResolutionPolicy: conflict.ResolutionPolicy,
                TimestampUtc: conflict.TimestampUtc,
                CanReapply: true))
            .ToArray();
    }

    private static IReadOnlyList<CadCollabToolPreviewPrimitive>? BuildToolPreview(IReadOnlyList<CadToolVisualHint>? hints)
    {
        if (hints is null || hints.Count == 0)
        {
            return null;
        }

        var limit = Math.Min(hints.Count, 48);
        var preview = new List<CadCollabToolPreviewPrimitive>(limit);
        for (var index = 0; index < limit; index++)
        {
            var hint = hints[index];
            preview.Add(new CadCollabToolPreviewPrimitive(
                Kind: hint.Kind,
                Start: new CadCollabPoint(hint.Anchor.X, hint.Anchor.Y),
                End: hint.SecondaryAnchor is { } secondary
                    ? new CadCollabPoint(secondary.X, secondary.Y)
                    : null,
                Text: hint.Text,
                Mid: hint.TertiaryAnchor is { } tertiary
                    ? new CadCollabPoint(tertiary.X, tertiary.Y)
                    : null,
                Scalar: hint.Scalar));
        }

        return preview;
    }

    private static void AppendRemoteToolPreviewHints(
        IReadOnlyList<CadCollabToolPreviewPrimitive> toolPreview,
        CadCollabPresence presence,
        ICollection<CadToolVisualHint> target)
    {
        var limit = Math.Min(toolPreview.Count, 48);
        for (var index = 0; index < limit; index++)
        {
            var primitive = toolPreview[index];
            var start = new Vector2((float)primitive.Start.X, (float)primitive.Start.Y);
            var end = primitive.End is { } endPoint
                ? new Vector2((float)endPoint.X, (float)endPoint.Y)
                : (Vector2?)null;
            var tertiary = primitive.Mid is { } midPoint
                ? new Vector2((float)midPoint.X, (float)midPoint.Y)
                : (Vector2?)null;
            var text = string.IsNullOrWhiteSpace(primitive.Text)
                ? index == 0 ? presence.DisplayName : null
                : $"{presence.DisplayName}: {primitive.Text}";

            target.Add(new CadToolVisualHint(
                Kind: $"RemoteTool{primitive.Kind}",
                Anchor: start,
                SecondaryAnchor: end,
                Text: text,
                Color: presence.Color,
                TertiaryAnchor: tertiary,
                Scalar: primitive.Scalar is { } scalar
                    ? (float)scalar
                    : null));
        }
    }
}
