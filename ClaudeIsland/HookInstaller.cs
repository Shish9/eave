using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace ClaudeIsland;

/// Adds/removes Eave's hooks in ~/.claude/settings.json (with a timestamped backup).
public static class HookInstaller
{
    static readonly (string Event, string Mode)[] Events =
    {
        ("PreToolUse", "event"), ("PostToolUse", "event"), ("Notification", "event"),
        ("Stop", "event"), ("UserPromptSubmit", "event"), ("SessionEnd", "event"),
        ("PermissionRequest", "permission"),
    };

    static string SettingsPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude", "settings.json");

    static bool IsOurs(JsonNode? group)
    {
        if (group?["hooks"] is not JsonArray hooks) return false;
        foreach (var h in hooks)
        {
            string cmd = h?["command"]?.ToString() ?? "";
            // relay.exe (or the older relay.js) inside an Eave folder, or a folder from before the rename ("...island...")
            if (cmd.Contains("relay.", StringComparison.OrdinalIgnoreCase) &&
                (cmd.Contains("/Eave/", StringComparison.OrdinalIgnoreCase) || cmd.Contains("island", StringComparison.OrdinalIgnoreCase))) return true;
        }
        return false;
    }

    public static string Apply(bool remove)
    {
        string file = SettingsPath;
        string relay = Path.Combine(Registration.InstallDir, "relay.exe").Replace('\\', '/');
        if (!remove && !File.Exists(relay)) return "relay.exe not found next to the app.";

        JsonObject root = File.Exists(file) ? (JsonNode.Parse(File.ReadAllText(file)) as JsonObject ?? new JsonObject()) : new JsonObject();
        if (File.Exists(file)) File.Copy(file, $"{file}.bak-{DateTimeOffset.Now.ToUnixTimeMilliseconds()}");

        var hooks = root["hooks"] as JsonObject ?? new JsonObject();
        foreach (var (ev, mode) in Events)
        {
            var kept = new JsonArray();
            if (hooks[ev] is JsonArray existing)
                foreach (var g in existing) if (!IsOurs(g)) kept.Add(g!.DeepClone());
            if (!remove)
                kept.Add(new JsonObject
                {
                    ["matcher"] = "",
                    ["hooks"] = new JsonArray(new JsonObject
                    {
                        ["type"] = "command",
                        ["command"] = $"\"{relay}\" {mode}",
                        ["timeout"] = mode == "permission" ? 120 : 5,
                    }),
                });
            hooks.Remove(ev);
            if (kept.Count > 0) hooks[ev] = kept;
        }
        root.Remove("hooks");
        if (hooks.Count > 0) root["hooks"] = hooks;

        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        File.WriteAllText(file, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        return remove ? "Hooks removed." : "Hooks installed. New Claude Code sessions will report to the island.";
    }
}
