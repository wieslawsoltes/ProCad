namespace ProCad.Collaboration.Transports;

public enum CadRealtimeTransportState
{
    Disconnected,
    Connecting,
    Connected,
    Error
}

public sealed class CadRealtimeMessageEventArgs : EventArgs
{
    public CadRealtimeMessageEventArgs(ReadOnlyMemory<byte> payload)
    {
        Payload = payload;
    }

    public ReadOnlyMemory<byte> Payload { get; }
}

public sealed class CadRealtimeStateChangedEventArgs : EventArgs
{
    public CadRealtimeStateChangedEventArgs(CadRealtimeTransportState state, string? message = null)
    {
        State = state;
        Message = message;
    }

    public CadRealtimeTransportState State { get; }
    public string? Message { get; }
}

public interface ICadRealtimeTransport : IAsyncDisposable
{
    event EventHandler<CadRealtimeMessageEventArgs>? MessageReceived;
    event EventHandler<CadRealtimeStateChangedEventArgs>? StateChanged;

    ValueTask ConnectAsync(CancellationToken cancellationToken = default);
    ValueTask DisconnectAsync(CancellationToken cancellationToken = default);
    ValueTask SendAsync(ReadOnlyMemory<byte> payload, CancellationToken cancellationToken = default);
}

public interface ICadRealtimeTransportFactory
{
    ICadRealtimeTransport CreateWebSocket(Uri uri);
    ICadRealtimeTransport CreateSharedFile(string basePath);
    ICadRealtimeTransport CreateLoopback();
}
