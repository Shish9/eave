using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Media;
using System.Text.Json.Nodes;
using System.Threading.Tasks;

namespace ClaudeIsland;

public enum Decision { Allow, AlwaysAllow, Deny }

/// All members must be called on the UI thread.
public class SessionStore
{
    public ObservableCollection<Session> Sessions { get; } = new();
    public event Action? Changed;
    public event Action<string, string>? Balloon;

    readonly Dictionary<string, Session> byId = new();
    int created;

    static string S(JsonNode? n) => n?.GetValueKind() == System.Text.Json.JsonValueKind.String ? n.GetValue<string>() : "";

    public PermissionRequest? FirstPending() => Sessions.SelectMany(s => s.Pending).FirstOrDefault();
    public int PendingCount => Sessions.Sum(s => s.Pending.Count);

    Session Get(JsonObject o)
    {
        string id = S(o["session_id"]);
        if (id == "") id = "default";
        if (!byId.TryGetValue(id, out var s))
        {
            s = new Session { Id = id, Accent = Session.AccentPalette[created++ % Session.AccentPalette.Length] };
            byId[id] = s;
            Sessions.Add(s);
        }
        string cwd = S(o["cwd"]);
        if (cwd != "") s.Cwd = cwd;
        if (s.Hwnd == IntPtr.Zero && o["_ppid"] is JsonNode pp && int.TryParse(pp.ToString(), out int pid))
            s.Hwnd = NativeMethods.FindTerminalWindow(pid);
        return s;
    }

    static string Describe(JsonNode? input)
    {
        if (input is not JsonObject i) return "";
        foreach (var key in new[] { "command", "file_path", "path", "pattern", "url", "description", "prompt" })
            if (S(i[key]) is { Length: > 0 } v) return v.Replace("\r", " ").Replace("\n", " ");
        return i.ToJsonString();
    }

    public void HandleEvent(JsonObject o)
    {
        string name = S(o["hook_event_name"]);
        if (name == "SessionEnd")
        {
            string id = S(o["session_id"]);
            if (byId.Remove(id, out var gone)) { Sessions.Remove(gone); CancelPending(gone); }
            Changed?.Invoke();
            return;
        }

        var s = Get(o);
        s.LastEvent = DateTime.Now;
        switch (name)
        {
            case "UserPromptSubmit":
                CancelPending(s);
                s.State = SessionState.Working;
                s.Started = DateTime.Now;
                s.ToolCount = 0;
                s.Tool = "Thinking";
                s.Detail = "";
                s.LastMessage = "";
                break;
            case "PreToolUse":
                s.State = SessionState.Working;
                s.Tool = S(o["tool_name"]);
                s.Detail = Describe(o["tool_input"]);
                s.ToolCount++;
                break;
            case "PostToolUse":
                CancelPending(s);
                s.State = SessionState.Working;
                s.Tool = "Thinking";
                s.Detail = "";
                break;
            case "Notification":
                if (s.Pending.Count == 0)
                {
                    s.State = SessionState.NeedsYou;
                    s.Detail = S(o["message"]);
                    Play("ask");
                    Balloon?.Invoke("Claude needs you", $"{s.Project}: {s.Detail}");
                }
                break;
            case "Stop":
                CancelPending(s);
                s.State = SessionState.Done;
                s.DoneAt = DateTime.Now;
                (s.LastMessage, s.ContextTokens) = ReadTranscript(S(o["transcript_path"]));
                Play("done");
                Balloon?.Invoke("Claude is done", s.Project);
                break;
        }
        s.Touch();
        Changed?.Invoke();
    }

    public Task<string> RequestPermission(JsonObject o)
    {
        var s = Get(o);
        s.LastEvent = DateTime.Now;
        var req = new PermissionRequest
        {
            Session = s,
            Tool = S(o["tool_name"]),
            Detail = Describe(o["tool_input"]),
            Diff = BuildDiff(S(o["tool_name"]), o["tool_input"]),
            Suggestions = o["permission_suggestions"]?.DeepClone(),
        };
        s.Pending.Add(req);
        s.State = SessionState.Asking;
        Play("ask");
        s.Touch();
        Changed?.Invoke();

        // Give up before the relay's own timeout so Claude falls back to its terminal prompt.
        _ = Task.Delay(TimeSpan.FromSeconds(115)).ContinueWith(_ =>
            System.Windows.Application.Current.Dispatcher.Invoke(() => Resolve(req, "{}")));
        return req.Tcs.Task;
    }

    public void Decide(PermissionRequest req, Decision d)
    {
        JsonObject decision = d == Decision.Deny
            ? new JsonObject { ["behavior"] = "deny", ["message"] = "Denied from Eave" }
            : new JsonObject { ["behavior"] = "allow" };

        if (d == Decision.AlwaysAllow)
        {
            JsonNode rules = req.Suggestions is JsonArray { Count: > 0 } sug
                ? sug.DeepClone()
                : new JsonArray(new JsonObject
                {
                    ["type"] = "addRules",
                    ["rules"] = new JsonArray(new JsonObject { ["toolName"] = req.Tool }),
                    ["behavior"] = "allow",
                    ["destination"] = "localSettings",
                });
            decision["updatedPermissions"] = rules;
        }

        var result = new JsonObject
        {
            ["hookSpecificOutput"] = new JsonObject { ["hookEventName"] = "PermissionRequest", ["decision"] = decision },
        };
        Resolve(req, result.ToJsonString());
    }

    void Resolve(PermissionRequest req, string json)
    {
        var s = req.Session;
        if (!s.Pending.Remove(req)) return;
        req.Tcs.TrySetResult(json);
        if (s.Pending.Count == 0 && s.State == SessionState.Asking) s.State = SessionState.Working;
        s.LastEvent = DateTime.Now;
        s.Touch();
        Changed?.Invoke();
    }

    /// The user answered in the terminal (or the tool already ran): drop the island's prompt.
    void CancelPending(Session s)
    {
        foreach (var r in s.Pending.ToList()) { s.Pending.Remove(r); r.Tcs.TrySetResult("{}"); }
    }

    /// Called every second: stuck detection and pruning.
    public void Tick()
    {
        var now = DateTime.Now;
        foreach (var s in Sessions.ToList())
        {
            if (s.State == SessionState.Working && (now - s.LastEvent).TotalSeconds > AppSettings.Current.StuckSeconds)
                s.State = SessionState.Stuck;
            if (s.State != SessionState.Working && s.State != SessionState.Asking && s.State != SessionState.Stuck
                && (now - s.LastEvent).TotalMinutes > 30)
            {
                byId.Remove(s.Id);
                Sessions.Remove(s);
                continue;
            }
            s.Touch();
        }
    }

    /// Last assistant text and the context size (input + cached tokens of the latest turn) from the end of the transcript.
    static (string Text, int Tokens) ReadTranscript(string path)
    {
        string text = "";
        int tokens = 0;
        try
        {
            if (path == "" || !File.Exists(path)) return ("", 0);
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            // Only the tail matters; skip the (possibly partial) first line after seeking.
            long start = Math.Max(0, fs.Length - 400_000);
            fs.Seek(start, SeekOrigin.Begin);
            using var sr = new StreamReader(fs);
            if (start > 0) sr.ReadLine();
            var lines = new List<string>();
            string? line;
            while ((line = sr.ReadLine()) != null) lines.Add(line);

            for (int i = lines.Count - 1; i >= 0 && (text == "" || tokens == 0); i--)
            {
                if (JsonNode.Parse(lines[i]) is not JsonObject root || S(root["type"]) != "assistant") continue;
                var msg = root["message"];
                if (tokens == 0 && msg?["usage"] is JsonObject u)
                    tokens = Num(u["input_tokens"]) + Num(u["cache_read_input_tokens"]) + Num(u["cache_creation_input_tokens"]);
                if (text == "" && msg?["content"] is JsonArray content)
                    foreach (var part in content)
                        if (S(part?["type"]) == "text" && S(part?["text"]).Trim() is { Length: > 0 } t)
                        { text = t.Replace("\r", " ").Replace("\n", " "); break; }
            }
        }
        catch { }
        return (text, tokens);
    }

    static int Num(JsonNode? n) { try { return n == null ? 0 : n.GetValue<int>(); } catch { return 0; } }

    static string Trunc(string s, int max) => s.Length <= max ? s : s[..(max - 1)] + "…";

    /// A few lines of what an edit would change, for the permission card.
    static List<(char Kind, string Text)> BuildDiff(string tool, JsonNode? input)
    {
        var d = new List<(char, string)>();
        if (input is not JsonObject i) return d;

        void Add(char kind, string text, int max)
        {
            if (string.IsNullOrWhiteSpace(text)) return;
            var lines = text.Replace("\r", "").Split('\n');
            for (int n = 0; n < Math.Min(max, lines.Length); n++) d.Add((kind, Trunc(lines[n], 64)));
            if (lines.Length > max) d.Add(('.', $"… {lines.Length - max} more lines"));
        }

        if (tool == "Edit") { Add('-', S(i["old_string"]), 3); Add('+', S(i["new_string"]), 3); }
        else if (tool == "Write") Add('+', S(i["content"]), 4);
        else if (tool == "MultiEdit" && i["edits"] is JsonArray edits && edits.Count > 0 && edits[0] is JsonObject first)
        {
            Add('-', S(first["old_string"]), 2);
            Add('+', S(first["new_string"]), 2);
            if (edits.Count > 1) d.Add(('.', $"+ {edits.Count - 1} more edits"));
        }
        return d;
    }

    public static void Play(string kind)
    {
        if (AppSettings.Current.Mute) return;
        try
        {
            switch (kind)
            {
                case "done": SystemSounds.Asterisk.Play(); break;
                case "ask": SystemSounds.Exclamation.Play(); break;
                case "timer": SystemSounds.Beep.Play(); break;
            }
        }
        catch { }
    }
}
