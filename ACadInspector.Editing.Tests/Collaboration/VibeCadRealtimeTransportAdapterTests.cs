using ACadInspector.Collaboration.Transports;
using Vibe.Office.Collaboration.Transports;
using Xunit;

namespace ACadInspector.Editing.Tests.Collaboration;

public sealed class VibeCadRealtimeTransportAdapterTests
{
    [Fact]
    public async Task Adapter_ForwardsStateAndPayload()
    {
        var inner = new InMemoryLoopbackTransport();
        await using var adapter = new VibeCadRealtimeTransportAdapter(inner);

        var states = new List<CadRealtimeTransportState>();
        byte[]? received = null;
        adapter.StateChanged += (_, args) => states.Add(args.State);
        adapter.MessageReceived += (_, args) => received = args.Payload.ToArray();

        await adapter.ConnectAsync();
        await adapter.SendAsync(new byte[] { 1, 2, 3 });
        await adapter.DisconnectAsync();

        Assert.Contains(CadRealtimeTransportState.Connected, states);
        Assert.Contains(CadRealtimeTransportState.Disconnected, states);
        Assert.Equal(new byte[] { 1, 2, 3 }, received);
    }
}
