using Vibe.Office.Collaboration.Transports;
using Vibe.Office.Collaboration.Transports.SharedFile;
using Vibe.Office.Collaboration.Transports.WebSocket;

namespace ACadInspector.Collaboration.Transports;

public sealed class VibeCadRealtimeTransportFactory : ICadRealtimeTransportFactory
{
    public ICadRealtimeTransport CreateWebSocket(Uri uri)
    {
        ArgumentNullException.ThrowIfNull(uri);
        return new VibeCadRealtimeTransportAdapter(new WebSocketClientTransport(uri));
    }

    public ICadRealtimeTransport CreateSharedFile(string basePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(basePath);
        return new VibeCadRealtimeTransportAdapter(SharedFileTransport.CreateForBasePath(basePath));
    }

    public ICadRealtimeTransport CreateLoopback()
    {
        return new VibeCadRealtimeTransportAdapter(new InMemoryLoopbackTransport());
    }
}
