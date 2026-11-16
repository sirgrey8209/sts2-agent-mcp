using System;
using System.IO;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace Sts2AgentMcp.Host;

/// <summary>
/// Localhost-only HTTP server exposing a single MCP endpoint (POST /mcp).
/// Browser requests are rejected (any Origin header), no CORS headers are sent, and a bearer token is required.
/// </summary>
internal sealed class HttpHost
{
    private const int MaxBodyBytes = 1 << 20;

    private readonly Config _config;
    private readonly McpEndpoint _endpoint;
    private readonly byte[] _tokenBytes;
    private HttpListener? _listener;

    public HttpHost(Config config, McpEndpoint endpoint)
    {
        _config = config;
        _endpoint = endpoint;
        _tokenBytes = Encoding.UTF8.GetBytes(config.Token);
    }

    public void Start()
    {
        _listener = new HttpListener();
        _listener.Prefixes.Add($"http://127.0.0.1:{_config.Port}/");
        _listener.Prefixes.Add($"http://localhost:{_config.Port}/");
        _listener.Start();
        _ = Task.Run(AcceptLoop);
        Log.Info($"MCP endpoint listening on http://127.0.0.1:{_config.Port}/mcp");
    }

    private async Task AcceptLoop()
    {
        while (_listener is { IsListening: true })
        {
            HttpListenerContext ctx;
            try { ctx = await _listener.GetContextAsync(); }
            catch (Exception) when (_listener is not { IsListening: true }) { return; }
            catch (Exception ex)
            {
                Log.Error($"accept failed: {ex.Message}");
                continue;
            }
            _ = Task.Run(() => Handle(ctx));
        }
    }

    private async Task Handle(HttpListenerContext ctx)
    {
        var req = ctx.Request;
        var res = ctx.Response;
        try
        {
            if (!string.IsNullOrEmpty(req.Headers["Origin"]))
            {
                await Send(res, 403, "browser requests are not allowed");
                return;
            }
            if (req.Url?.AbsolutePath != "/mcp")
            {
                await Send(res, 404, "not found");
                return;
            }
            if (req.HttpMethod != "POST")
            {
                res.Headers["Allow"] = "POST";
                await Send(res, 405, "only POST is supported");
                return;
            }
            if (!IsAuthorized(req.Headers["Authorization"]))
            {
                res.Headers["WWW-Authenticate"] = "Bearer";
                await Send(res, 401, "missing or invalid bearer token");
                return;
            }
            if (req.ContentType == null || !req.ContentType.StartsWith("application/json", StringComparison.OrdinalIgnoreCase))
            {
                await Send(res, 415, "Content-Type must be application/json");
                return;
            }
            if (req.ContentLength64 > MaxBodyBytes)
            {
                await Send(res, 413, "request too large");
                return;
            }

            string body;
            using (var reader = new StreamReader(req.InputStream, Encoding.UTF8))
            {
                var buffer = new char[MaxBodyBytes + 1];
                int read = await reader.ReadBlockAsync(buffer, 0, buffer.Length);
                if (read > MaxBodyBytes)
                {
                    await Send(res, 413, "request too large");
                    return;
                }
                body = new string(buffer, 0, read);
            }

            string? response = await _endpoint.Handle(body);
            if (response == null)
            {
                res.StatusCode = 202;
                res.Close();
                return;
            }
            await Send(res, 200, response, "application/json");
        }
        catch (Exception ex)
        {
            Log.Error($"request failed: {ex}");
            try { await Send(res, 500, "internal error"); } catch { /* connection gone */ }
        }
    }

    private bool IsAuthorized(string? header)
    {
        const string scheme = "Bearer ";
        if (header == null || !header.StartsWith(scheme, StringComparison.OrdinalIgnoreCase)) return false;
        var given = Encoding.UTF8.GetBytes(header.Substring(scheme.Length).Trim());
        return CryptographicOperations.FixedTimeEquals(given, _tokenBytes);
    }

    private static async Task Send(HttpListenerResponse res, int status, string text, string contentType = "text/plain")
    {
        var bytes = Encoding.UTF8.GetBytes(text);
        res.StatusCode = status;
        res.ContentType = contentType + "; charset=utf-8";
        res.ContentLength64 = bytes.Length;
        await res.OutputStream.WriteAsync(bytes);
        res.Close();
    }
}
