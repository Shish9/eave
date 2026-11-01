using System;
using System.IO;
using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using System.Windows.Threading;

namespace ClaudeIsland;

/// Local HTTP endpoint that the hook relay posts Claude Code events to.
public class HookServer
{
    public const int Port = 47821;
    readonly HttpListener listener = new();
    readonly SessionStore store;
    readonly Dispatcher ui;

    /// Set by the app: opens Eave Studio (used by "Eave.exe --studio" when the island is already running).
    public Action? OnStudio;

    public HookServer(SessionStore store, Dispatcher ui)
    {
        this.store = store;
        this.ui = ui;
        listener.Prefixes.Add($"http://127.0.0.1:{Port}/");
    }

    public void Start()
    {
        listener.Start();
        _ = Task.Run(Loop);
    }

    public void Stop() { try { listener.Stop(); } catch { } }

    async Task Loop()
    {
        while (listener.IsListening)
        {
            HttpListenerContext ctx;
            try { ctx = await listener.GetContextAsync(); }
            catch { break; }
            _ = Task.Run(() => Handle(ctx));
        }
    }

    async Task Handle(HttpListenerContext ctx)
    {
        string reply = "{}";
        try
        {
            string body;
            using (var sr = new StreamReader(ctx.Request.InputStream, Encoding.UTF8)) body = await sr.ReadToEndAsync();
            var obj = JsonNode.Parse(body) as JsonObject ?? new JsonObject();
            string path = ctx.Request.Url?.AbsolutePath ?? "";

            if (path == "/studio")
                await ui.InvokeAsync(() => OnStudio?.Invoke());
            else if (path == "/permission")
                reply = await await ui.InvokeAsync(() => store.RequestPermission(obj));
            else
                await ui.InvokeAsync(() => store.HandleEvent(obj));
        }
        catch { }

        try
        {
            var bytes = Encoding.UTF8.GetBytes(reply);
            ctx.Response.ContentType = "application/json";
            ctx.Response.ContentLength64 = bytes.Length;
            await ctx.Response.OutputStream.WriteAsync(bytes);
            ctx.Response.Close();
        }
        catch { }
    }
}
