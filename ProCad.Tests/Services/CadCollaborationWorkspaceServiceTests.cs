using System.Diagnostics;
using ProCad.Collaboration.Contracts;
using ProCad.Collaboration.Presence;
using ProCad.Collaboration.Services;
using ProCad.Collaboration.Transports;
using ProCad.Collaboration.UI;
using ProCad.Editing.Identifiers;
using ProCad.Editing.Operations;
using ProCad.Editing.Sessions;
using ProCad.Services;
using ACadSharp;
using CSMath;
using Xunit;

namespace ProCad.Tests.Services;

public sealed class CadCollaborationWorkspaceServiceTests
{
    [Fact]
    public async Task GetRemoteGhostHints_BulkPresence_CompletesWithinBudget()
    {
        const int participantCount = 400;
        const int budgetMilliseconds = 450;

        var realtime = new FakeRealtimeSession();
        var service = CreateService(realtime, out _);
        var session = new CadEditorSessionFactory().Create(new CadDocument());
        await service.EnsureSessionAsync(session);

        for (var index = 0; index < participantCount; index++)
        {
            realtime.RaisePresence(new CadCollabPresence(
                UserId: Guid.NewGuid(),
                DisplayName: $"Remote-{index}",
                Color: "#44AAFF",
                Status: "Editing",
                ActiveTool: "LINE",
                PromptStage: "Specify next point",
                CursorPoint: new CadCollabPoint(index, index),
                Viewport: null,
                SelectedEntityIds: null,
                UpdatedAtUtc: DateTimeOffset.UtcNow,
                SessionId: session.SessionId.Value));
        }

        var stopwatch = Stopwatch.StartNew();
        var hints = service.GetRemoteGhostHints(session);
        stopwatch.Stop();

        Assert.True(hints.Count >= participantCount);
        Assert.True(
            stopwatch.ElapsedMilliseconds <= budgetMilliseconds,
            $"Presence hint budget exceeded: {stopwatch.ElapsedMilliseconds} ms > {budgetMilliseconds} ms.");
    }

    [Fact]
    public async Task RemotePresence_BuildsCursorSelectionAndViewportGhostHints()
    {
        var realtime = new FakeRealtimeSession();
        var service = CreateService(realtime, out _);
        var session = new CadEditorSessionFactory().Create(new CadDocument());
        await service.EnsureSessionAsync(session);

        var remotePresence = new CadCollabPresence(
            UserId: Guid.NewGuid(),
            DisplayName: "Remote User",
            Color: "#FF5500",
            Status: "Editing",
            ActiveTool: "LINE",
            PromptStage: "Specify next point",
            CursorPoint: new CadCollabPoint(8, 12),
            Viewport: new CadCollabViewportSummary(
                Center: new CadCollabPoint(5, 5),
                Zoom: 1.0,
                Width: 20,
                Height: 10),
            SelectedEntityIds: [Guid.NewGuid(), Guid.NewGuid()],
            UpdatedAtUtc: DateTimeOffset.UtcNow);
        realtime.RaisePresence(remotePresence);

        var hints = service.GetRemoteGhostHints();
        Assert.Contains(hints, static hint => hint.Kind == "RemoteCursor");
        Assert.Contains(hints, static hint => hint.Kind == "RemoteSelection");
        Assert.Contains(hints, static hint => hint.Kind == "RemoteViewport");
    }

    [Fact]
    public async Task RemotePresence_MapsActiveCommandAndPromptStage_ForParticipantsUi()
    {
        var realtime = new FakeRealtimeSession();
        var service = CreateService(realtime, out var uiService);
        var session = new CadEditorSessionFactory().Create(new CadDocument());
        await service.EnsureSessionAsync(session);

        realtime.RaisePresence(new CadCollabPresence(
            UserId: Guid.NewGuid(),
            DisplayName: "Remote User",
            Color: "#ffaa33",
            Status: "Editing",
            ActiveTool: "LINE",
            PromptStage: "Specify second point",
            CursorPoint: null,
            Viewport: null,
            SelectedEntityIds: null,
            UpdatedAtUtc: DateTimeOffset.UtcNow));

        var participant = Assert.Single(uiService.Current.Participants);
        Assert.Equal("LINE", participant.ActiveTool);
        Assert.Equal("LINE", participant.ActiveCommand);
        Assert.Equal("Specify second point", participant.PromptStage);
    }

    [Fact]
    public async Task RemoteOperationsApplied_RefreshesSnapshotAgeDiagnostics()
    {
        var realtime = new FakeRealtimeSession();
        var service = CreateService(realtime, out var uiService);
        var session = new CadEditorSessionFactory().Create(new CadDocument());
        await service.EnsureSessionAsync(session);

        await Task.Delay(30);
        realtime.RaisePresence(new CadCollabPresence(
            UserId: Guid.NewGuid(),
            DisplayName: "Remote User",
            Color: "#33aaff",
            Status: "Editing",
            ActiveTool: "MOVE",
            PromptStage: "Specify displacement",
            CursorPoint: null,
            Viewport: null,
            SelectedEntityIds: null,
            UpdatedAtUtc: DateTimeOffset.UtcNow));

        var before = uiService.Current.Diagnostics.SnapshotAge;
        Assert.True(before > TimeSpan.Zero);

        realtime.RaiseOperationsApplied(isRemote: true);

        var after = uiService.Current.Diagnostics.SnapshotAge;
        Assert.True(after >= TimeSpan.Zero);
        Assert.True(after < before);
    }

    [Fact]
    public async Task ReconnectResyncAndReapply_AreForwardedToRealtimeSession()
    {
        var realtime = new FakeRealtimeSession
        {
            ReapplyResult = true
        };
        var service = CreateService(realtime, out var uiService);
        var session = new CadEditorSessionFactory().Create(new CadDocument());
        await service.EnsureSessionAsync(session);

        var conflictId = Guid.NewGuid().ToString("D");
        realtime.RaiseConflicts(
        [
            new CadRealtimeConflict(
                ConflictId: conflictId,
                EntityKey: "entity:1",
                Summary: "Concurrent update requires action.",
                ResolutionPolicy: "Transform + LWW fallback",
                TimestampUtc: DateTimeOffset.UtcNow)
        ]);

        Assert.True(uiService.Current.Diagnostics.ResyncRequired);
        Assert.Single(uiService.Current.Conflicts);

        await service.ReconnectAsync();
        await service.ResyncAsync();
        var reapplied = await service.ReapplyConflictAsync(conflictId);

        Assert.True(reapplied);
        Assert.Equal(1, realtime.ReconnectCalls);
        Assert.Equal(1, realtime.ResyncCalls);
        Assert.Equal(conflictId, realtime.LastReapplyConflictId);
        Assert.False(uiService.Current.Diagnostics.ResyncRequired);
    }

    [Fact]
    public async Task ReconnectResyncReapply_TargetOnlyActiveSession_WhenMultipleSessionsExist()
    {
        var factory = new FactoryCollabService();
        var service = CreateService(factory, out var uiService);
        var sessionFactory = new CadEditorSessionFactory();
        var sessionA = sessionFactory.Create(new CadDocument());
        var sessionB = sessionFactory.Create(new CadDocument());

        await service.EnsureSessionAsync(sessionA);
        await service.EnsureSessionAsync(sessionB);

        Assert.Equal(2, factory.Sessions.Count);
        var realtimeA = factory.Sessions[0];
        var realtimeB = factory.Sessions[1];
        realtimeA.ReapplyResult = true;
        var conflictA = Guid.NewGuid().ToString("D");
        var conflictB = Guid.NewGuid().ToString("D");
        realtimeA.RaiseConflicts(
        [
            new CadRealtimeConflict(
                ConflictId: conflictA,
                EntityKey: "entity:a",
                Summary: "A conflict",
                ResolutionPolicy: "Transform + LWW fallback",
                TimestampUtc: DateTimeOffset.UtcNow)
        ]);
        realtimeB.RaiseConflicts(
        [
            new CadRealtimeConflict(
                ConflictId: conflictB,
                EntityKey: "entity:b",
                Summary: "B conflict",
                ResolutionPolicy: "Transform + LWW fallback",
                TimestampUtc: DateTimeOffset.UtcNow)
        ]);

        // Switch active context to sessionA before invoking control actions.
        await service.EnsureSessionAsync(sessionA);

        await service.ReconnectAsync();
        await service.ResyncAsync();
        var reapplied = await service.ReapplyConflictAsync(conflictA);

        Assert.True(reapplied);
        Assert.Equal(1, realtimeA.ReconnectCalls);
        Assert.Equal(1, realtimeA.ResyncCalls);
        Assert.Equal(conflictA, realtimeA.LastReapplyConflictId);

        Assert.Equal(0, realtimeB.ReconnectCalls);
        Assert.Equal(0, realtimeB.ResyncCalls);
        Assert.Null(realtimeB.LastReapplyConflictId);
        Assert.False(uiService.Current.Diagnostics.ResyncRequired);
        Assert.Equal(0, uiService.Current.Diagnostics.QueueDepth);
    }

    [Fact]
    public async Task ReconnectAsync_WithoutActiveRealtimeSession_UsesLastActiveSession()
    {
        var factory = new FactoryCollabService();
        var service = CreateService(factory, out var uiService);
        var session = new CadEditorSessionFactory().Create(new CadDocument());
        await service.EnsureSessionAsync(session);
        await service.CloseSessionAsync(session);

        await service.ReconnectAsync();

        Assert.Equal(2, factory.Sessions.Count);
        var realtime = factory.Sessions[1];
        Assert.Equal(1, realtime.ReconnectCalls);
        Assert.True(uiService.Current.IsConnected);
        Assert.Equal("Reconnected", uiService.Current.ConnectionStatus);
    }

    [Fact]
    public async Task Leave_DisablesAutoConnect_UntilJoin()
    {
        var factory = new FactoryCollabService();
        var service = CreateService(factory, out var uiService);
        var session = new CadEditorSessionFactory().Create(new CadDocument());

        await service.EnsureSessionAsync(session);
        Assert.Single(factory.Sessions);

        await service.LeaveAsync();

        Assert.Equal("Offline", uiService.Current.ConnectionStatus);
        Assert.False(uiService.Current.CanReconnect);
        Assert.Equal(1, factory.Sessions[0].DisposeCalls);

        await service.EnsureSessionAsync(session);
        Assert.Single(factory.Sessions);

        await service.JoinAsync();
        Assert.Equal(2, factory.Sessions.Count);
    }

    [Fact]
    public async Task ApplyConnectionOptions_RecreatesRealtimeSession_UsingUpdatedModes()
    {
        var factory = new FactoryCollabService();
        var options = new CadCollabConnectionOptionsProvider();
        options.Update(new CadCollabConnectionOptions(
            TransportMode: CadCollabTransportMode.Loopback,
            AuthMode: CadCollabAuthMode.Anonymous));

        var service = CreateService(factory, out var uiService, optionsProvider: options);
        var session = new CadEditorSessionFactory().Create(new CadDocument());
        await service.EnsureSessionAsync(session);
        Assert.Single(factory.Sessions);

        await service.ApplyConnectionOptionsAsync(new CadCollabConnectionOptions(
            TransportMode: CadCollabTransportMode.WebSocket,
            AuthMode: CadCollabAuthMode.BearerToken,
            WebSocketUrl: "wss://localhost:7443/collab",
            BearerToken: "token"));

        Assert.Equal(2, factory.Sessions.Count);
        Assert.Equal("Bearer", uiService.Current.AuthMode);
        Assert.Equal("WebSocket", uiService.Current.TransportMode);
    }

    [Fact]
    public async Task MultipleSessions_KeepIndependentRealtimePipelines()
    {
        var collabFactory = new FactoryCollabService();
        var service = CreateService(collabFactory, out _);
        var sessionFactory = new CadEditorSessionFactory();
        var sessionA = sessionFactory.Create(new CadDocument());
        var sessionB = sessionFactory.Create(new CadDocument());

        await service.EnsureSessionAsync(sessionA);
        await service.EnsureSessionAsync(sessionB);
        Assert.Equal(2, collabFactory.Sessions.Count);

        await service.PublishLocalOperationsAsync(sessionA, [new CadOperation(CadOperationKind.UpdateProperty, null)]);
        await service.PublishLocalOperationsAsync(sessionB, [new CadOperation(CadOperationKind.UpdateProperty, null)]);

        Assert.Equal(1, collabFactory.Sessions[0].SubmitLocalAppliedCalls);
        Assert.Equal(1, collabFactory.Sessions[1].SubmitLocalAppliedCalls);
    }

    [Fact]
    public async Task RemotePresence_WithResolvedSelectionIds_BuildsSelectionBoundsGhost()
    {
        var realtime = new FakeRealtimeSession();
        var service = CreateService(realtime, out _);
        var session = new CadEditorSessionFactory().Create(new CadDocument());
        await service.EnsureSessionAsync(session);

        var entityId = CadEntityId.New();
        session.Apply(CadOperationBatch.Create(
            actorId: session.SessionId.Value,
            baseVersion: session.Revision,
            sequence: session.Revision + 1,
            operations:
            [
                CadOperationPayloadCodec.CreatePoint(entityId, new XYZ(3, 4, 0))
            ]));

        realtime.RaisePresence(new CadCollabPresence(
            UserId: Guid.NewGuid(),
            DisplayName: "Remote User",
            Color: "#22AAFF",
            Status: "Editing",
            ActiveTool: "MOVE",
            PromptStage: "Specify base point",
            CursorPoint: new CadCollabPoint(8, 12),
            Viewport: null,
            SelectedEntityIds: [entityId.Value],
            UpdatedAtUtc: DateTimeOffset.UtcNow,
            SessionId: session.SessionId.Value));

        var hints = service.GetRemoteGhostHints(session);
        Assert.Contains(hints, static hint => hint.Kind == "RemoteSelectionBounds");
    }

    [Fact]
    public async Task RemotePresence_WithEllipseSelectionId_BuildsSelectionBoundsGhost()
    {
        var realtime = new FakeRealtimeSession();
        var service = CreateService(realtime, out _);
        var session = new CadEditorSessionFactory().Create(new CadDocument());
        await service.EnsureSessionAsync(session);

        var entityId = CadEntityId.New();
        session.Apply(CadOperationBatch.Create(
            actorId: session.SessionId.Value,
            baseVersion: session.Revision,
            sequence: session.Revision + 1,
            operations:
            [
                CadOperationPayloadCodec.CreateEllipse(
                    entityId,
                    new XYZ(10, 5, 0),
                    new XYZ(4, 0, 0),
                    radiusRatio: 0.5,
                    startParameter: 0.0,
                    endParameter: Math.PI * 2.0,
                    normal: new XYZ(0, 0, 1))
            ]));

        realtime.RaisePresence(new CadCollabPresence(
            UserId: Guid.NewGuid(),
            DisplayName: "Remote User",
            Color: "#2288FF",
            Status: "Editing",
            ActiveTool: "MOVE",
            PromptStage: "Specify displacement",
            CursorPoint: new CadCollabPoint(11, 5),
            Viewport: null,
            SelectedEntityIds: [entityId.Value],
            UpdatedAtUtc: DateTimeOffset.UtcNow,
            SessionId: session.SessionId.Value));

        var hints = service.GetRemoteGhostHints(session);
        Assert.Contains(hints, static hint => hint.Kind == "RemoteSelectionBounds");
    }

    [Fact]
    public async Task RemotePresence_WithEntityTransform_RefreshesSelectionBoundsGhost()
    {
        var realtime = new FakeRealtimeSession();
        var service = CreateService(realtime, out _);
        var session = new CadEditorSessionFactory().Create(new CadDocument());
        await service.EnsureSessionAsync(session);

        var entityId = CadEntityId.New();
        session.Apply(CadOperationBatch.Create(
            actorId: session.SessionId.Value,
            baseVersion: session.Revision,
            sequence: session.Revision + 1,
            operations:
            [
                CadOperationPayloadCodec.CreateLine(
                    entityId,
                    new XYZ(0, 0, 0),
                    new XYZ(2, 0, 0))
            ]));

        var remoteUserId = Guid.NewGuid();
        realtime.RaisePresence(new CadCollabPresence(
            UserId: remoteUserId,
            DisplayName: "Remote User",
            Color: "#2288FF",
            Status: "Editing",
            ActiveTool: "MOVE",
            PromptStage: "Specify displacement",
            CursorPoint: new CadCollabPoint(1, 0),
            Viewport: null,
            SelectedEntityIds: [entityId.Value],
            UpdatedAtUtc: DateTimeOffset.UtcNow,
            SessionId: session.SessionId.Value));

        var initialHint = service.GetRemoteGhostHints(session)
            .First(hint => hint.Kind == "RemoteSelectionBounds");

        session.Apply(CadOperationBatch.Create(
            actorId: session.SessionId.Value,
            baseVersion: session.Revision,
            sequence: session.Revision + 1,
            operations:
            [
                CadOperationPayloadCodec.TransformLine(
                    entityId,
                    fromStart: new XYZ(0, 0, 0),
                    fromEnd: new XYZ(2, 0, 0),
                    toStart: new XYZ(10, 0, 0),
                    toEnd: new XYZ(12, 0, 0))
            ]));

        realtime.RaisePresence(new CadCollabPresence(
            UserId: remoteUserId,
            DisplayName: "Remote User",
            Color: "#2288FF",
            Status: "Editing",
            ActiveTool: "MOVE",
            PromptStage: "Specify displacement",
            CursorPoint: new CadCollabPoint(11, 0),
            Viewport: null,
            SelectedEntityIds: [entityId.Value],
            UpdatedAtUtc: DateTimeOffset.UtcNow.AddMilliseconds(50),
            SessionId: session.SessionId.Value));

        var updatedHint = service.GetRemoteGhostHints(session)
            .First(hint => hint.Kind == "RemoteSelectionBounds");

        Assert.True(updatedHint.Anchor.X > initialHint.Anchor.X);
        Assert.True(updatedHint.Anchor.X >= 10f);
        Assert.True(updatedHint.SecondaryAnchor.HasValue);
        Assert.True(updatedHint.SecondaryAnchor!.Value.X >= 12f);
    }

    [Fact]
    public async Task SessionHostRemove_ClosesRealtimeSessionContext()
    {
        var context = new CadDocumentContextService();
        var selection = new CadSelectionService();
        var sessionHost = new CadEditorSessionHostService(new CadEditorSessionFactory(), context, selection);
        var realtime = new FakeRealtimeSession();
        var service = CreateService(new FakeCollabService(realtime), out _, sessionHost);
        var document = new CadDocument();
        var session = sessionHost.GetOrCreate(document);
        await service.EnsureSessionAsync(session);

        var removed = sessionHost.Remove(document);
        Assert.True(removed);

        for (var attempt = 0; attempt < 20 && realtime.DisposeCalls == 0; attempt++)
        {
            await Task.Delay(10);
        }

        Assert.Equal(1, realtime.DisposeCalls);
    }

    [Fact]
    public async Task RemoteOperationsApplied_NotifiesSessionHost()
    {
        var context = new CadDocumentContextService();
        var selection = new CadSelectionService();
        var sessionHost = new CadEditorSessionHostService(new CadEditorSessionFactory(), context, selection);
        var realtime = new FakeRealtimeSession();
        var service = CreateService(new FakeCollabService(realtime), out _, sessionHost);
        var document = new CadDocument();
        var session = sessionHost.GetOrCreate(document);
        await service.EnsureSessionAsync(session);

        var changed = 0;
        sessionHost.SessionChanged += (_, args) =>
        {
            if (ReferenceEquals(args.Document, document))
            {
                changed++;
            }
        };

        realtime.RaiseOperationsApplied(isRemote: true);
        realtime.RaiseOperationsApplied(isRemote: false);

        Assert.Equal(1, changed);
    }

    [Fact]
    public async Task EnsureSessionAsync_RemoteReplayDuringConnect_NotifiesSessionHost()
    {
        var context = new CadDocumentContextService();
        var selection = new CadSelectionService();
        var sessionHost = new CadEditorSessionHostService(new CadEditorSessionFactory(), context, selection);
        var realtime = new FakeRealtimeSession
        {
            RaiseRemoteOperationsOnConnect = true
        };

        var service = CreateService(new FakeCollabService(realtime), out _, sessionHost);
        var document = new CadDocument();
        var session = sessionHost.GetOrCreate(document);
        var changed = 0;
        sessionHost.SessionChanged += (_, args) =>
        {
            if (ReferenceEquals(args.Document, document))
            {
                changed++;
            }
        };

        await service.EnsureSessionAsync(session);

        Assert.Equal(1, changed);
    }

    [Fact]
    public async Task RemotePresence_WithToolPreview_PrintsRemoteGeometryHints()
    {
        var realtime = new FakeRealtimeSession();
        var service = CreateService(realtime, out _);
        var session = new CadEditorSessionFactory().Create(new CadDocument());
        await service.EnsureSessionAsync(session);

        realtime.RaisePresence(new CadCollabPresence(
            UserId: Guid.NewGuid(),
            DisplayName: "Remote User",
            Color: "#00CC88",
            Status: "Editing",
            ActiveTool: "LINE",
            PromptStage: "Specify next point",
            CursorPoint: new CadCollabPoint(2, 3),
            Viewport: null,
            SelectedEntityIds: null,
            UpdatedAtUtc: DateTimeOffset.UtcNow,
            ToolPreview:
            [
                new CadCollabToolPreviewPrimitive(
                    Kind: "PreviewArc",
                    Start: new CadCollabPoint(1, 1),
                    End: new CadCollabPoint(8, 5),
                    Text: "Δ",
                    Mid: new CadCollabPoint(4, 7),
                    Scalar: 23.5)
            ],
            SessionId: session.SessionId.Value));

        var hints = service.GetRemoteGhostHints(session);
        var preview = Assert.Single(
            hints,
            static hint => string.Equals(hint.Kind, "RemoteToolPreviewArc", StringComparison.Ordinal));
        Assert.Equal(23.5f, preview.Scalar);
        Assert.True(preview.TertiaryAnchor.HasValue);
        Assert.Contains(hints, static hint => hint.Kind.StartsWith("RemoteTool", StringComparison.Ordinal));
    }

    [Theory]
    [MemberData(nameof(RemoteToolPreviewFamilyCases))]
    public async Task RemotePresence_WithToolPreviewFamilies_MapsToRemoteToolHints(
        string kind,
        bool includeEnd,
        bool includeMid,
        double? scalar)
    {
        var realtime = new FakeRealtimeSession();
        var service = CreateService(realtime, out _);
        var session = new CadEditorSessionFactory().Create(new CadDocument());
        await service.EnsureSessionAsync(session);

        var preview = new CadCollabToolPreviewPrimitive(
            Kind: kind,
            Start: new CadCollabPoint(1.5, 2.5),
            End: includeEnd ? new CadCollabPoint(8.5, 9.5) : null,
            Text: $"{kind}-preview",
            Mid: includeMid ? new CadCollabPoint(4.5, 5.5) : null,
            Scalar: scalar);

        realtime.RaisePresence(new CadCollabPresence(
            UserId: Guid.NewGuid(),
            DisplayName: "Remote User",
            Color: "#55AAEE",
            Status: "Editing",
            ActiveTool: "TEST",
            PromptStage: "Specify next point",
            CursorPoint: new CadCollabPoint(2, 3),
            Viewport: null,
            SelectedEntityIds: null,
            UpdatedAtUtc: DateTimeOffset.UtcNow,
            ToolPreview: [preview],
            SessionId: session.SessionId.Value));

        var hints = service.GetRemoteGhostHints(session);
        var mapped = Assert.Single(
            hints,
            hint => string.Equals(hint.Kind, $"RemoteTool{kind}", StringComparison.Ordinal));
        Assert.Equal(1.5f, mapped.Anchor.X);
        Assert.Equal(2.5f, mapped.Anchor.Y);
        Assert.Equal("#55AAEE", mapped.Color);
        Assert.NotNull(mapped.Text);
        Assert.Contains("Remote User", mapped.Text!, StringComparison.Ordinal);

        if (includeEnd)
        {
            Assert.True(mapped.SecondaryAnchor.HasValue);
            Assert.Equal(8.5f, mapped.SecondaryAnchor!.Value.X);
            Assert.Equal(9.5f, mapped.SecondaryAnchor!.Value.Y);
        }
        else
        {
            Assert.Null(mapped.SecondaryAnchor);
        }

        if (includeMid)
        {
            Assert.True(mapped.TertiaryAnchor.HasValue);
            Assert.Equal(4.5f, mapped.TertiaryAnchor!.Value.X);
            Assert.Equal(5.5f, mapped.TertiaryAnchor!.Value.Y);
        }
        else
        {
            Assert.Null(mapped.TertiaryAnchor);
        }

        if (scalar is { } scalarValue)
        {
            Assert.Equal((float)scalarValue, mapped.Scalar);
        }
        else
        {
            Assert.Null(mapped.Scalar);
        }
    }

    [Fact]
    public async Task RemotePresence_ToolPreviewAcrossPromptStages_UsesLatestPromptAndPreview()
    {
        var realtime = new FakeRealtimeSession();
        var service = CreateService(realtime, out var uiService);
        var session = new CadEditorSessionFactory().Create(new CadDocument());
        await service.EnsureSessionAsync(session);

        var remoteUserId = Guid.NewGuid();
        realtime.RaisePresence(new CadCollabPresence(
            UserId: remoteUserId,
            DisplayName: "Remote User",
            Color: "#9966FF",
            Status: "Editing",
            ActiveTool: "ARC",
            PromptStage: "Specify second point",
            CursorPoint: new CadCollabPoint(1, 1),
            Viewport: null,
            SelectedEntityIds: null,
            UpdatedAtUtc: DateTimeOffset.UtcNow,
            ToolPreview:
            [
                new CadCollabToolPreviewPrimitive(
                    Kind: "PreviewLine",
                    Start: new CadCollabPoint(0, 0),
                    End: new CadCollabPoint(4, 0))
            ],
            SessionId: session.SessionId.Value));

        realtime.RaisePresence(new CadCollabPresence(
            UserId: remoteUserId,
            DisplayName: "Remote User",
            Color: "#9966FF",
            Status: "Editing",
            ActiveTool: "ARC",
            PromptStage: "Specify end angle",
            CursorPoint: new CadCollabPoint(2, 3),
            Viewport: null,
            SelectedEntityIds: null,
            UpdatedAtUtc: DateTimeOffset.UtcNow.AddMilliseconds(10),
            ToolPreview:
            [
                new CadCollabToolPreviewPrimitive(
                    Kind: "PreviewArc",
                    Start: new CadCollabPoint(1, 1),
                    End: new CadCollabPoint(5, 3),
                    Mid: new CadCollabPoint(3, 4))
            ],
            SessionId: session.SessionId.Value));

        var hints = service.GetRemoteGhostHints(session);
        Assert.DoesNotContain(hints, static hint => string.Equals(hint.Kind, "RemoteToolPreviewLine", StringComparison.Ordinal));
        Assert.Contains(hints, static hint => string.Equals(hint.Kind, "RemoteToolPreviewArc", StringComparison.Ordinal));

        var participant = Assert.Single(uiService.Current.Participants, participant => participant.UserId == remoteUserId);
        Assert.Equal("Specify end angle", participant.PromptStage);
    }

    [Fact]
    public async Task ConnectionState_UsesConfiguredAuthAndTransportModes()
    {
        var realtime = new FakeRealtimeSession();
        var options = new CadCollabConnectionOptionsProvider();
        options.Update(new CadCollabConnectionOptions(
            TransportMode: CadCollabTransportMode.WebSocket,
            AuthMode: CadCollabAuthMode.BearerToken,
            WebSocketUrl: "wss://localhost:443/collab",
            BearerToken: "token"));

        var service = CreateService(new FakeCollabService(realtime), out var uiService, sessionHost: null, optionsProvider: options);
        var session = new CadEditorSessionFactory().Create(new CadDocument());
        await service.EnsureSessionAsync(session);

        Assert.Equal("Bearer", uiService.Current.AuthMode);
        Assert.Equal("WebSocket", uiService.Current.TransportMode);

        await service.ReconnectAsync();

        Assert.Equal("Bearer", uiService.Current.AuthMode);
        Assert.Equal("WebSocket", uiService.Current.TransportMode);
    }

    public static IEnumerable<object?[]> RemoteToolPreviewFamilyCases()
    {
        yield return ["PreviewLine", true, false, (double?)null];
        yield return ["PreviewArc", true, true, 22.5];
        yield return ["PreviewCircle", true, false, 5.0];
        yield return ["PreviewPolyline", true, true, (double?)null];
        yield return ["PreviewText", false, false, (double?)null];
        yield return ["PreviewDim", true, false, 120.0];
        yield return ["PreviewLeader", true, true, (double?)null];
        yield return ["PreviewHatch", true, false, (double?)null];
        yield return ["PreviewInsert", false, false, (double?)null];
        yield return ["PreviewEllipse", true, true, 0.75];
        yield return ["PreviewSpline", true, true, (double?)null];
    }

    private static CadCollaborationWorkspaceService CreateService(
        FakeRealtimeSession realtime,
        out ICadCollabUiService uiService)
    {
        return CreateService(new FakeCollabService(realtime), out uiService);
    }

    private static CadCollaborationWorkspaceService CreateService(
        ICadCollabService collabService,
        out ICadCollabUiService uiService,
        CadEditorSessionHostService? sessionHost = null,
        ICadCollabConnectionOptionsProvider? optionsProvider = null)
    {
        uiService = new CadCollabUiService();
        var transportFactory = new FakeTransportFactory();
        return new CadCollaborationWorkspaceService(
            collabService,
            transportFactory,
            uiService,
            optionsProvider ?? new CadCollabConnectionOptionsProvider(),
            new CadCollabPresenceRegistry(),
            sessionHost);
    }

    private sealed class FactoryCollabService : ICadCollabService
    {
        public List<FakeRealtimeSession> Sessions { get; } = new();

        public ICadRealtimeSession CreateSession(ICadEditorSession session, ICadRealtimeTransport transport, Guid actorId)
        {
            var created = new FakeRealtimeSession();
            Sessions.Add(created);
            return created;
        }
    }

    private sealed class FakeCollabService : ICadCollabService
    {
        private readonly ICadRealtimeSession _session;

        public FakeCollabService(ICadRealtimeSession session)
        {
            _session = session;
        }

        public ICadRealtimeSession CreateSession(ICadEditorSession session, ICadRealtimeTransport transport, Guid actorId)
        {
            return _session;
        }
    }

    private sealed class FakeTransportFactory : ICadRealtimeTransportFactory
    {
        public ICadRealtimeTransport CreateWebSocket(Uri uri)
        {
            return new FakeTransport();
        }

        public ICadRealtimeTransport CreateSharedFile(string basePath)
        {
            return new FakeTransport();
        }

        public ICadRealtimeTransport CreateLoopback()
        {
            return new FakeTransport();
        }
    }

    private sealed class FakeTransport : ICadRealtimeTransport
    {
        public event EventHandler<CadRealtimeMessageEventArgs>? MessageReceived;
        public event EventHandler<CadRealtimeStateChangedEventArgs>? StateChanged;

        public ValueTask ConnectAsync(CancellationToken cancellationToken = default)
        {
            StateChanged?.Invoke(this, new CadRealtimeStateChangedEventArgs(CadRealtimeTransportState.Connected, "Connected"));
            return ValueTask.CompletedTask;
        }

        public ValueTask DisconnectAsync(CancellationToken cancellationToken = default)
        {
            StateChanged?.Invoke(this, new CadRealtimeStateChangedEventArgs(CadRealtimeTransportState.Disconnected, "Disconnected"));
            return ValueTask.CompletedTask;
        }

        public ValueTask SendAsync(ReadOnlyMemory<byte> payload, CancellationToken cancellationToken = default)
        {
            MessageReceived?.Invoke(this, new CadRealtimeMessageEventArgs(payload));
            return ValueTask.CompletedTask;
        }

        public ValueTask DisposeAsync()
        {
            return ValueTask.CompletedTask;
        }
    }

    private sealed class FakeRealtimeSession : ICadRealtimeSession
    {
        private readonly List<CadRealtimeConflict> _conflicts = new();

        public Guid ActorId { get; } = Guid.NewGuid();
        public long Version => 1;
        public bool ReapplyResult { get; set; }
        public bool RaiseRemoteOperationsOnConnect { get; set; }
        public int ReconnectCalls { get; private set; }
        public int ResyncCalls { get; private set; }
        public int SubmitLocalAppliedCalls { get; private set; }
        public int DisposeCalls { get; private set; }
        public string? LastReapplyConflictId { get; private set; }
        public event EventHandler<CadRealtimeStateChangedEventArgs>? TransportStateChanged;
        public event EventHandler<CadCollabPresence>? PresenceReceived;
        public event EventHandler<IReadOnlyList<CadRealtimeConflict>>? ConflictsChanged;
        public event EventHandler<CadRealtimeOperationsAppliedEventArgs>? OperationsApplied;

        public ValueTask ConnectAsync(CancellationToken cancellationToken = default)
        {
            if (RaiseRemoteOperationsOnConnect)
            {
                RaiseOperationsApplied(isRemote: true);
            }

            TransportStateChanged?.Invoke(this, new CadRealtimeStateChangedEventArgs(CadRealtimeTransportState.Connected, "Connected"));
            return ValueTask.CompletedTask;
        }

        public ValueTask DisconnectAsync(CancellationToken cancellationToken = default)
        {
            TransportStateChanged?.Invoke(this, new CadRealtimeStateChangedEventArgs(CadRealtimeTransportState.Disconnected, "Disconnected"));
            return ValueTask.CompletedTask;
        }

        public ValueTask ReconnectAsync(CancellationToken cancellationToken = default)
        {
            ReconnectCalls++;
            TransportStateChanged?.Invoke(this, new CadRealtimeStateChangedEventArgs(CadRealtimeTransportState.Connected, "Reconnected"));
            return ValueTask.CompletedTask;
        }

        public ValueTask ResyncAsync(CancellationToken cancellationToken = default)
        {
            ResyncCalls++;
            _conflicts.Clear();
            ConflictsChanged?.Invoke(this, _conflicts.ToArray());
            return ValueTask.CompletedTask;
        }

        public ValueTask SubmitLocalAsync(IReadOnlyList<CadOperation> operations, CancellationToken cancellationToken = default)
        {
            return ValueTask.CompletedTask;
        }

        public ValueTask SubmitLocalAppliedAsync(IReadOnlyList<CadOperation> operations, CancellationToken cancellationToken = default)
        {
            SubmitLocalAppliedCalls++;
            return ValueTask.CompletedTask;
        }

        public ValueTask PublishPresenceAsync(
            CadCollabPresence presence,
            TimeSpan? timeToLive = null,
            CancellationToken cancellationToken = default)
        {
            PresenceReceived?.Invoke(this, presence);
            return ValueTask.CompletedTask;
        }

        public ValueTask<bool> ReapplyConflictAsync(string conflictId, CancellationToken cancellationToken = default)
        {
            LastReapplyConflictId = conflictId;
            return ValueTask.FromResult(ReapplyResult);
        }

        public IReadOnlyList<CadRealtimeConflict> GetConflicts()
        {
            return _conflicts.ToArray();
        }

        public ValueTask DisposeAsync()
        {
            DisposeCalls++;
            return ValueTask.CompletedTask;
        }

        public void RaisePresence(CadCollabPresence presence)
        {
            PresenceReceived?.Invoke(this, presence);
        }

        public void RaiseConflicts(IReadOnlyList<CadRealtimeConflict> conflicts)
        {
            _conflicts.Clear();
            _conflicts.AddRange(conflicts);
            ConflictsChanged?.Invoke(this, conflicts);
        }

        public void RaiseOperationsApplied(bool isRemote, IReadOnlyList<CadOperation>? operations = null)
        {
            var value = operations ?? [new CadOperation(CadOperationKind.UpdateProperty, null)];
            OperationsApplied?.Invoke(this, new CadRealtimeOperationsAppliedEventArgs(
                IsRemote: isRemote,
                ActorId: ActorId,
                Version: Version + 1,
                Operations: value));
        }
    }
}
