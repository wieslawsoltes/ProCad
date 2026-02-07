using Vibe.Office.Collaboration;

namespace ACadInspector.Collaboration.Transports;

public sealed class VibeCadRealtimeTransportAdapter : ICadRealtimeTransport
{
    private readonly ICollabTransportConnection _transport;
    private bool _disposed;

    public VibeCadRealtimeTransportAdapter(ICollabTransportConnection transport)
    {
        _transport = transport ?? throw new ArgumentNullException(nameof(transport));
        _transport.MessageReceived += OnMessageReceived;
        _transport.StateChanged += OnStateChanged;
    }

    public event EventHandler<CadRealtimeMessageEventArgs>? MessageReceived;
    public event EventHandler<CadRealtimeStateChangedEventArgs>? StateChanged;

    public ValueTask ConnectAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        return _transport.ConnectAsync(cancellationToken);
    }

    public ValueTask DisconnectAsync(CancellationToken cancellationToken = default)
    {
        if (_disposed)
        {
            return ValueTask.CompletedTask;
        }

        return _transport.DisconnectAsync(cancellationToken);
    }

    public ValueTask SendAsync(ReadOnlyMemory<byte> payload, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        return _transport.SendAsync(payload, cancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _transport.MessageReceived -= OnMessageReceived;
        _transport.StateChanged -= OnStateChanged;
        await _transport.DisconnectAsync();
        if (_transport is IAsyncDisposable asyncDisposable)
        {
            await asyncDisposable.DisposeAsync();
        }
    }

    private void OnMessageReceived(object? sender, CollabTransportMessageEventArgs args)
    {
        MessageReceived?.Invoke(this, new CadRealtimeMessageEventArgs(args.Payload));
    }

    private void OnStateChanged(object? sender, CollabTransportStateChangedEventArgs args)
    {
        StateChanged?.Invoke(this, new CadRealtimeStateChangedEventArgs(MapState(args.State), args.Message));
    }

    private static CadRealtimeTransportState MapState(CollabTransportState state)
    {
        return state switch
        {
            CollabTransportState.Disconnected => CadRealtimeTransportState.Disconnected,
            CollabTransportState.Connecting => CadRealtimeTransportState.Connecting,
            CollabTransportState.Connected => CadRealtimeTransportState.Connected,
            CollabTransportState.Error => CadRealtimeTransportState.Error,
            _ => CadRealtimeTransportState.Error
        };
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }
}
