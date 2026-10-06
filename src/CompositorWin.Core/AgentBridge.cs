using System.Net;
using System.Net.Sockets;
using System.Text;

namespace CompositorWin.Core;

public static class AgentBridge
{
    public const int DefaultPort = 47321;

    public static int Port { get; set; } = DefaultPort;

    public static string BaseUrl => $"http://127.0.0.1:{Port}/";

    public static bool Ping()
    {
        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromMilliseconds(400) };
            var response = client.GetAsync(BaseUrl + "ping").GetAwaiter().GetResult();
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    public static bool NotifyReload(string packagePath)
    {
        return Post("reload", packagePath);
    }

    public static bool NotifyOpen(string packagePath)
    {
        return Post("open", packagePath);
    }

    private static bool Post(string action, string packagePath)
    {
        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
            var json = $"{{\"path\":{System.Text.Json.JsonSerializer.Serialize(Path.GetFullPath(packagePath))}}}";
            var content = new StringContent(json, Encoding.UTF8, "application/json");
            var response = client.PostAsync(BaseUrl + action, content).GetAwaiter().GetResult();
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    public static bool PortAvailable(int port)
    {
        try
        {
            var listener = new TcpListener(IPAddress.Loopback, port);
            listener.Start();
            listener.Stop();
            return true;
        }
        catch
        {
            return false;
        }
    }
}
