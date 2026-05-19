using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace ProCad.Collaboration.ServerHost.Hosting;

public sealed class CadCollabServerHost : IAsyncDisposable
{
    private IHost? _host;

    public async ValueTask StartAsync(CadCollabServerOptions options, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (_host is not null)
        {
            return;
        }

        var builder = Host.CreateDefaultBuilder()
            .ConfigureWebHostDefaults(webBuilder =>
            {
                webBuilder.UseUrls(options.Urls);
                webBuilder.ConfigureServices(services =>
                {
                    services.AddRouting();
                });

                webBuilder.Configure(app =>
                {
                    app.UseRouting();
                    app.UseWebSockets();
                    app.UseEndpoints(endpoints =>
                    {
                        endpoints.MapGet("/health", static async context =>
                        {
                            context.Response.StatusCode = StatusCodes.Status200OK;
                            await context.Response.WriteAsync("OK");
                        });

                        endpoints.Map("/collab/ws", static async context =>
                        {
                            if (!context.WebSockets.IsWebSocketRequest)
                            {
                                context.Response.StatusCode = StatusCodes.Status400BadRequest;
                                return;
                            }

                            using var socket = await context.WebSockets.AcceptWebSocketAsync();
                            var buffer = new byte[8 * 1024];
                            while (socket.State == System.Net.WebSockets.WebSocketState.Open)
                            {
                                var result = await socket.ReceiveAsync(buffer, context.RequestAborted);
                                if (result.MessageType == System.Net.WebSockets.WebSocketMessageType.Close)
                                {
                                    await socket.CloseAsync(System.Net.WebSockets.WebSocketCloseStatus.NormalClosure, "closing", context.RequestAborted);
                                    break;
                                }

                                await socket.SendAsync(
                                    buffer.AsMemory(0, result.Count),
                                    result.MessageType,
                                    result.EndOfMessage,
                                    context.RequestAborted);
                            }
                        });
                    });
                });
            });

        _host = builder.Build();
        await _host.StartAsync(cancellationToken);
    }

    public async ValueTask StopAsync(CancellationToken cancellationToken = default)
    {
        if (_host is null)
        {
            return;
        }

        await _host.StopAsync(cancellationToken);
        _host.Dispose();
        _host = null;
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync();
    }
}
