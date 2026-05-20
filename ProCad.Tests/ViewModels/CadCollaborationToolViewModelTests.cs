using System;
using System.Reactive.Threading.Tasks;
using System.Threading;
using System.Threading.Tasks;
using ProCad.Collaboration.UI;
using ProCad.ViewModels;
using Xunit;

namespace ProCad.Tests.ViewModels;

public sealed class CadCollaborationToolViewModelTests
{
    [Fact]
    public async Task Commands_InvokeControlServiceHooks()
    {
        var uiService = new CadCollabUiService();
        var controlService = new FakeCadCollabControlService();
        var viewModel = new CadCollaborationToolViewModel(uiService, controlService);

        await viewModel.JoinCommand.Execute().ToTask();
        await viewModel.ReconnectCommand.Execute().ToTask();
        await viewModel.ResyncCommand.Execute().ToTask();
        await viewModel.ReapplyConflictCommand.Execute("conflict-1").ToTask();
        await viewModel.ShareCommand.Execute().ToTask();
        await viewModel.LeaveCommand.Execute().ToTask();

        Assert.Equal(1, controlService.JoinCalls);
        Assert.Equal(1, controlService.LeaveCalls);
        Assert.Equal(1, controlService.ShareCalls);
        Assert.Equal(1, controlService.ReconnectCalls);
        Assert.Equal(1, controlService.ResyncCalls);
        Assert.Equal(1, controlService.ReapplyCalls);
        Assert.Equal("conflict-1", controlService.LastConflictId);
        Assert.Equal("Shared session ready.", viewModel.ShareStatus);
    }

    [Fact]
    public void StateUpdates_AreReflectedInViewModelRows()
    {
        var now = DateTimeOffset.UtcNow;
        var uiService = new CadCollabUiService();
        var viewModel = new CadCollaborationToolViewModel(uiService, new NullCadCollabControlService());

        uiService.UpdateConnection(
            isConnected: true,
            status: "Connected",
            authMode: "Bearer",
            transportMode: "WebSocket");
        uiService.UpdateParticipants(
        [
            new CadCollabParticipantUi(
                UserId: Guid.NewGuid(),
                DisplayName: "Remote-A",
                Color: "#ffaa00",
                IsLocal: false,
                ActiveTool: "LINE",
                ActiveCommand: "LINE",
                LastActiveUtc: now,
                PromptStage: "Specify second point")
        ]);
        uiService.UpdateDiagnostics(new CadCollabDiagnosticsUi(
            SyncLagMs: 12.5,
            QueueDepth: 3,
            SnapshotAge: TimeSpan.FromSeconds(7),
            ResyncRequired: true));
        uiService.UpdateConflicts(
        [
            new CadCollabConflictUi(
                ConflictId: "c-1",
                EntityKey: "entity:123",
                Summary: "Concurrent transform",
                ResolutionPolicy: "Transform + LWW fallback",
                TimestampUtc: now,
                CanReapply: true)
        ]);

        Assert.True(viewModel.IsConnected);
        Assert.Equal("Connected", viewModel.ConnectionStatus);
        Assert.Equal("Bearer", viewModel.AuthMode);
        Assert.Equal("WebSocket", viewModel.TransportMode);
        Assert.Equal(12.5, viewModel.SyncLagMs, 3);
        Assert.Equal(3, viewModel.QueueDepth);
        Assert.Equal("7s", viewModel.SnapshotAge);
        Assert.True(viewModel.ResyncRequired);
        var participant = Assert.Single(viewModel.Participants);
        Assert.Equal("LINE", participant.ActiveTool);
        Assert.Equal("LINE", participant.ActiveCommand);
        Assert.Equal("Specify second point", participant.PromptStage);
        Assert.Single(viewModel.Conflicts);
    }

    [Fact]
    public async Task ApplySettings_UsesCurrentEditorOptions()
    {
        var uiService = new CadCollabUiService();
        var controlService = new FakeCadCollabControlService();
        var viewModel = new CadCollaborationToolViewModel(uiService, controlService)
        {
            SelectedTransportMode = CadCollabTransportMode.WebSocket,
            SelectedAuthMode = CadCollabAuthMode.BearerToken,
            WebSocketUrl = "wss://localhost:7443/collab",
            BearerToken = "token-1"
        };

        await viewModel.ApplySettingsCommand.Execute().ToTask();

        Assert.Equal(CadCollabTransportMode.WebSocket, controlService.LastAppliedOptions?.TransportMode);
        Assert.Equal(CadCollabAuthMode.BearerToken, controlService.LastAppliedOptions?.AuthMode);
        Assert.Equal("wss://localhost:7443/collab", controlService.LastAppliedOptions?.WebSocketUrl);
        Assert.Equal("token-1", controlService.LastAppliedOptions?.BearerToken);
    }

    private sealed class FakeCadCollabControlService : ICadCollabControlService
    {
        public CadCollabConnectionOptions CurrentOptions { get; } = CadCollabConnectionOptions.Default;

        public int JoinCalls { get; private set; }
        public int LeaveCalls { get; private set; }
        public int ShareCalls { get; private set; }
        public int ReconnectCalls { get; private set; }
        public int ResyncCalls { get; private set; }
        public int ReapplyCalls { get; private set; }
        public string? LastConflictId { get; private set; }
        public CadCollabConnectionOptions? LastAppliedOptions { get; private set; }

        public ValueTask JoinAsync(CancellationToken cancellationToken = default)
        {
            JoinCalls++;
            return ValueTask.CompletedTask;
        }

        public ValueTask LeaveAsync(CancellationToken cancellationToken = default)
        {
            LeaveCalls++;
            return ValueTask.CompletedTask;
        }

        public ValueTask<string> ShareAsync(CancellationToken cancellationToken = default)
        {
            ShareCalls++;
            return ValueTask.FromResult("Shared session ready.");
        }

        public ValueTask ApplyConnectionOptionsAsync(CadCollabConnectionOptions options, CancellationToken cancellationToken = default)
        {
            LastAppliedOptions = options;
            return ValueTask.CompletedTask;
        }

        public ValueTask ReconnectAsync(CancellationToken cancellationToken = default)
        {
            ReconnectCalls++;
            return ValueTask.CompletedTask;
        }

        public ValueTask ResyncAsync(CancellationToken cancellationToken = default)
        {
            ResyncCalls++;
            return ValueTask.CompletedTask;
        }

        public ValueTask<bool> ReapplyConflictAsync(string conflictId, CancellationToken cancellationToken = default)
        {
            ReapplyCalls++;
            LastConflictId = conflictId;
            return ValueTask.FromResult(true);
        }
    }
}
