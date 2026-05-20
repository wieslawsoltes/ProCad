namespace ProCad.Collaboration.UI;

public enum CadCollabTransportMode
{
    Loopback = 0,
    WebSocket,
    SharedFile
}

public enum CadCollabAuthMode
{
    Anonymous = 0,
    BearerToken
}

public sealed record CadCollabConnectionOptions(
    CadCollabTransportMode TransportMode,
    CadCollabAuthMode AuthMode,
    string? WebSocketUrl = null,
    string? SharedFilePath = null,
    string? BearerToken = null)
{
    public static readonly CadCollabConnectionOptions Default = new(
        TransportMode: CadCollabTransportMode.Loopback,
        AuthMode: CadCollabAuthMode.Anonymous);
}

public interface ICadCollabConnectionOptionsProvider
{
    CadCollabConnectionOptions Current { get; }
    event EventHandler<CadCollabConnectionOptions>? Changed;

    void Update(CadCollabConnectionOptions options);
}

public sealed class CadCollabConnectionOptionsProvider : ICadCollabConnectionOptionsProvider
{
    private readonly object _sync = new();
    private CadCollabConnectionOptions _current = CadCollabConnectionOptions.Default;

    public CadCollabConnectionOptions Current
    {
        get
        {
            lock (_sync)
            {
                return _current;
            }
        }
    }

    public event EventHandler<CadCollabConnectionOptions>? Changed;

    public void Update(CadCollabConnectionOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        lock (_sync)
        {
            _current = options;
        }

        Changed?.Invoke(this, options);
    }
}
