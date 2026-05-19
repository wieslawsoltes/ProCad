namespace ProCad.Collaboration.ServerHost.Hosting;

public sealed class CadCollabServerOptions
{
    public string[] Urls { get; init; } = ["http://127.0.0.1:5115"];
    public bool RequireAuthentication { get; init; } = true;
    public TimeSpan PresenceThrottleInterval { get; init; } = TimeSpan.FromMilliseconds(80);
}
