using System;
using System.IO;
using System.Text.Json.Nodes;
using ClaudeIsland;
using Xunit;

// The tests share static state (AppSettings.Current, environment variables), so they run one at a time.
[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace Eave.Tests;

/// A throwaway "user profile": Eave is pointed at these folders for the length of one test.
public sealed class Sandbox : IDisposable
{
    public string Root { get; } = Path.Combine(Path.GetTempPath(), "eave-tests-" + Guid.NewGuid().ToString("N"));
    public string DataDir => Path.Combine(Root, "appdata", "Eave");
    public string InstallDir => Path.Combine(Root, "local", "Eave");
    public string ClaudeSettings => Path.Combine(Root, "profile", ".claude", "settings.json");

    public Sandbox()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(ClaudeSettings)!);
        Directory.CreateDirectory(InstallDir);
        Environment.SetEnvironmentVariable("EAVE_APPDATA", Path.Combine(Root, "appdata"));
        Environment.SetEnvironmentVariable("EAVE_LOCALAPPDATA", Path.Combine(Root, "local"));
        Environment.SetEnvironmentVariable("EAVE_USERPROFILE", Path.Combine(Root, "profile"));
        AppSettings.Load();   // nothing on disk yet: a fresh set of defaults
    }

    /// Pretends Eave is installed: a relay.exe in the install folder, which is where the hooks should point.
    public string InstallRelay()
    {
        string relay = Path.Combine(InstallDir, "relay.exe");
        File.WriteAllText(relay, "x");
        return relay;
    }

    public void WriteClaudeSettings(string json) => File.WriteAllText(ClaudeSettings, json);
    public string ReadClaudeSettingsText() => File.ReadAllText(ClaudeSettings);
    public JsonNode ReadClaudeSettings() => JsonNode.Parse(ReadClaudeSettingsText())!;

    public void Dispose()
    {
        foreach (var v in new[] { "EAVE_APPDATA", "EAVE_LOCALAPPDATA", "EAVE_USERPROFILE" })
            Environment.SetEnvironmentVariable(v, null);
        try { Directory.Delete(Root, true); } catch { }
    }
}
