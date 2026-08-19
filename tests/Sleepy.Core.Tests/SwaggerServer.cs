using System.Net;
using System.Text;

namespace Sleepy.Tests;

/// <summary>
/// A throwaway loopback HTTP server that answers with whatever the test maps to
/// a path, so discovery can be exercised end to end without the network.
/// </summary>
internal sealed class SwaggerServer : IDisposable
{
    private readonly HttpListener _listener = new();
    private readonly Dictionary<string, (string Body, string ContentType)> _routes;
    private readonly CancellationTokenSource _stopping = new();

    public SwaggerServer(Dictionary<string, (string Body, string ContentType)> routes)
    {
        _routes = routes;
        Port = FreePort();
        Prefix = $"http://127.0.0.1:{Port}";
        _listener.Prefixes.Add(Prefix + "/");
        _listener.Start();
        _ = Task.Run(ServeAsync);
    }

    public int Port { get; }

    public string Prefix { get; }

    private async Task ServeAsync()
    {
        while (!_stopping.IsCancellationRequested)
        {
            HttpListenerContext context;
            try
            {
                context = await _listener.GetContextAsync().ConfigureAwait(false);
            }
            catch (HttpListenerException)
            {
                return;
            }
            catch (ObjectDisposedException)
            {
                return;
            }

            var path = context.Request.Url?.AbsolutePath ?? "/";
            if (_routes.TryGetValue(path, out var route))
            {
                var bytes = Encoding.UTF8.GetBytes(route.Body);
                context.Response.StatusCode = 200;
                context.Response.ContentType = route.ContentType;
                context.Response.ContentLength64 = bytes.Length;
                await context.Response.OutputStream.WriteAsync(bytes).ConfigureAwait(false);
            }
            else
            {
                context.Response.StatusCode = 404;
            }

            context.Response.Close();
        }
    }

    private static int FreePort()
    {
        var socket = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
        socket.Start();
        var port = ((IPEndPoint)socket.LocalEndpoint).Port;
        socket.Stop();
        return port;
    }

    public void Dispose()
    {
        _stopping.Cancel();
        _listener.Close();
        _stopping.Dispose();
    }
}
