using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

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

    public enum HookState { Installed, Missing, Broken, Outdated }

    static string SettingsPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude", "settings.json");

    /// The relay the hooks should call: the installed copy in %LOCALAPPDATA%\Eave when there is one,
    /// so hooks added from an unzipped download keep working after that folder is deleted.
    static string RelayPath
    {
        get
        {
            string installed = Path.Combine(Brand.InstallDir, "relay.exe");
            return (File.Exists(installed) ? installed : Path.Combine(Registration.InstallDir, "relay.exe")).Replace('\\', '/');
        }
    }

    /// Matches  "<path>/relay.exe" event|permission  — the command Eave writes — wherever relay.exe lives.
    static readonly Regex OurCommand = new(@"^""(?<path>[^""]*relay\.(exe|js))""\s+(event|permission)\s*$", RegexOptions.IgnoreCase);

    static string? OurRelayIn(JsonNode? group)
    {
        if (group?["hooks"] is not JsonArray hooks) return null;
        foreach (var h in hooks)
        {
            string cmd = h?["command"]?.ToString() ?? "";
            var m = OurCommand.Match(cmd.Trim());
            if (m.Success) return m.Groups["path"].Value;
            // Older formats: relay.exe / relay.js inside an Eave folder, or a folder from before the rename ("...island...").
            if (cmd.Contains("relay.", StringComparison.OrdinalIgnoreCase) &&
                (cmd.Contains("/Eave", StringComparison.OrdinalIgnoreCase) || cmd.Contains("island", StringComparison.OrdinalIgnoreCase))) return cmd;
        }
        return null;
    }

    static JsonObject ReadSettings() =>
        File.Exists(SettingsPath) ? (JsonNode.Parse(File.ReadAllText(SettingsPath)) as JsonObject ?? new JsonObject()) : new JsonObject();

    /// Whether every event Eave needs has a hook, and whether those hooks call a relay.exe that exists.
    public static HookState Check()
    {
        try
        {
            var hooks = ReadSettings()["hooks"] as JsonObject;
            string want = RelayPath;
            bool anyMissing = false, anyBroken = false, anyOther = false;
            foreach (var (ev, _) in Events)
            {
                var paths = (hooks?[ev] as JsonArray)?.Select(OurRelayIn).Where(p => p != null).ToList() ?? new();
                if (paths.Count == 0) { anyMissing = true; continue; }
                foreach (var p in paths)
                {
                    if (!File.Exists(p)) anyBroken = true;
                    else if (!string.Equals(Path.GetFullPath(p!), Path.GetFullPath(want), StringComparison.OrdinalIgnoreCase)) anyOther = true;
                }
            }
            return anyBroken ? HookState.Broken : anyMissing ? HookState.Missing : anyOther ? HookState.Outdated : HookState.Installed;
        }
        catch { return HookState.Broken; }
    }

    public static string Describe(HookState s) => s switch
    {
        HookState.Installed => "Hooks are installed. Claude Code sessions report to Eave.",
        HookState.Missing => "Hooks are not installed, so Claude Code can't report to Eave. Click \"Install hooks\".",
        HookState.Broken => "Hooks point to a relay.exe that no longer exists (for example a deleted download folder). Click \"Install hooks\" to fix them.",
        _ => "Hooks point to another copy of Eave. Click \"Install hooks\" to point them at this one.",
    };

    /// At startup: put the hooks back if they're broken, outdated or missing, unless the user removed them on purpose.
    public static void Repair()
    {
        try
        {
            var state = Check();
            if (state == HookState.Installed) return;
            if (state == HookState.Missing && AppSettings.Current.HooksRemoved) return;
            if (!File.Exists(RelayPath)) return;
            Diag.Log($"hooks {state}: reinstalling ({Apply(false)})");
        }
        catch (Exception ex) { Diag.Log("hook repair failed: " + ex.Message); }
    }

    public static string Apply(bool remove)
    {
        string file = SettingsPath;
        string relay = RelayPath;
        if (!remove && !File.Exists(relay)) return "relay.exe not found next to the app.";

        JsonObject root = ReadSettings();
        if (File.Exists(file)) File.Copy(file, $"{file}.bak-{DateTimeOffset.Now.ToUnixTimeMilliseconds()}");

        var hooks = root["hooks"] as JsonObject ?? new JsonObject();
        foreach (var (ev, mode) in Events)
        {
            var kept = new JsonArray();
            if (hooks[ev] is JsonArray existing)
                foreach (var g in existing) if (OurRelayIn(g) == null) kept.Add(g!.DeepClone());
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
        string tmp = file + ".eave-tmp";
        File.WriteAllText(tmp, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        File.Move(tmp, file, true);

        AppSettings.Current.HooksRemoved = remove;
        AppSettings.Save();
        return remove ? "Hooks removed." : "Hooks installed. New Claude Code sessions will report to the island.";
    }
}
