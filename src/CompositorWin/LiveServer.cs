using System.IO;
using System.Net;
using System.Text;
using System.Text.Json;
using CompositorWin.Core;

namespace CompositorWin;

public sealed class LiveServer : IDisposable
{
    private readonly HttpListener _listener = new();
    private readonly Action<string, bool> _onPath;
    private CancellationTokenSource? _cts;

    public LiveServer(Action<string, bool> onPath)
    {
        _onPath = onPath;
        _listener.Prefixes.Add(AgentBridge.BaseUrl);
    }

    public void Start()
    {
        _listener.Start();
        _cts = new CancellationTokenSource();
        _ = Task.Run(() => Loop(_cts.Token));
    }

    private async Task Loop(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            HttpListenerContext context;
            try
            {
                context = await _listener.GetContextAsync();
            }
            catch
            {
                return;
            }

            _ = Task.Run(() => Handle(context), token);
        }
    }

    private void Handle(HttpListenerContext context)
    {
        try
        {
            var path = context.Request.Url?.AbsolutePath.Trim('/').ToLowerInvariant() ?? "";
            if (path == "ping")
            {
                Write(context, 200, "{\"ok\":true}");
                return;
            }

            using var reader = new StreamReader(context.Request.InputStream, Encoding.UTF8);
            var body = reader.ReadToEnd();
            string? package = null;
            if (!string.IsNullOrWhiteSpace(body))
            {
                using var doc = JsonDocument.Parse(body);
                if (doc.RootElement.TryGetProperty("path", out var p))
                {
                    package = p.GetString();
                }
            }

            if (!string.IsNullOrWhiteSpace(package))
            {
                _onPath(package, path == "open");
            }

            Write(context, 200, "{\"ok\":true}");
        }
        catch (Exception ex)
        {
            try
            {
                Write(context, 500, JsonSerializer.Serialize(new { error = ex.Message }));
            }
            catch
            {
                // ignored
            }
        }
    }

    private static void Write(HttpListenerContext context, int status, string json)
    {
        context.Response.StatusCode = status;
        context.Response.ContentType = "application/json; charset=utf-8";
        var bytes = Encoding.UTF8.GetBytes(json);
        context.Response.OutputStream.Write(bytes);
        context.Response.Close();
    }

    public void Dispose()
    {
        _cts?.Cancel();
        try
        {
            _listener.Stop();
        }
        catch
        {
            // ignored
        }

        _listener.Close();
        _cts?.Dispose();
    }
}
