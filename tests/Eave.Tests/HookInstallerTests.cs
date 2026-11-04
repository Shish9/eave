using System;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using ClaudeIsland;
using Xunit;

namespace Eave.Tests;

public class HookInstallerTests
{
    static string Relay(Sandbox s) => s.InstallDir.Replace('\\', '/') + "/relay.exe";

    [Fact]
    public void Without_eave_hooks_the_state_is_missing()
    {
        using var s = new Sandbox();
        s.InstallRelay();
        Assert.Equal(HookInstaller.HookState.Missing, HookInstaller.Check());
    }

    [Fact]
    public void Repair_installs_the_hooks_and_keeps_everything_else()
    {
        using var s = new Sandbox();
        s.InstallRelay();
        s.WriteClaudeSettings("{\"model\":\"opus\",\"hooks\":{\"Stop\":[{\"matcher\":\"\",\"hooks\":[{\"type\":\"command\",\"command\":\"my-own-script.sh\"}]}]}}");

        HookInstaller.Repair();

        Assert.Equal(HookInstaller.HookState.Installed, HookInstaller.Check());
        var root = s.ReadClaudeSettings();
        Assert.Equal("opus", root["model"]!.ToString());
        var stop = root["hooks"]!["Stop"]!.AsArray();
        Assert.Equal(2, stop.Count);
        Assert.Equal("my-own-script.sh", stop[0]!["hooks"]![0]!["command"]!.ToString());
        Assert.Contains(Relay(s), stop[1]!["hooks"]![0]!["command"]!.ToString());
    }

    [Fact]
    public void Hooks_pointing_at_a_deleted_download_folder_are_found_and_repaired_without_duplicates()
    {
        using var s = new Sandbox();
        s.InstallRelay();
        HookInstaller.Apply(false);
        s.WriteClaudeSettings(s.ReadClaudeSettingsText().Replace(Relay(s), "C:/Users/me/Downloads/Eave-v0.3.1-win-x64/relay.exe"));
        Assert.Equal(HookInstaller.HookState.Broken, HookInstaller.Check());

        HookInstaller.Repair();

        Assert.Equal(HookInstaller.HookState.Installed, HookInstaller.Check());
        Assert.DoesNotContain("Downloads", s.ReadClaudeSettingsText());
        Assert.Single(s.ReadClaudeSettings()["hooks"]!["PreToolUse"]!.AsArray());
    }

    [Fact]
    public void Removing_the_hooks_takes_only_eaves_and_startup_repair_respects_it()
    {
        using var s = new Sandbox();
        s.InstallRelay();
        s.WriteClaudeSettings("{\"hooks\":{\"Stop\":[{\"matcher\":\"\",\"hooks\":[{\"type\":\"command\",\"command\":\"my-own-script.sh\"}]}]}}");
        HookInstaller.Apply(false);

        HookInstaller.Apply(true);
        HookInstaller.Repair();

        Assert.Equal(HookInstaller.HookState.Missing, HookInstaller.Check());
        Assert.True(AppSettings.Current.HooksRemoved);
        Assert.Single(s.ReadClaudeSettings()["hooks"]!["Stop"]!.AsArray());
    }

    [Fact]
    public void Another_tools_relay_hook_is_never_touched()
    {
        using var s = new Sandbox();
        s.InstallRelay();
        s.WriteClaudeSettings("{\"hooks\":{\"PreToolUse\":[{\"matcher\":\"\",\"hooks\":[{\"type\":\"command\",\"command\":\"\\\"C:/tools/relay.exe\\\" event\"}]}]}}");

        HookInstaller.Apply(false);
        Assert.Contains("C:/tools/relay.exe", s.ReadClaudeSettingsText());
        Assert.Equal(2, s.ReadClaudeSettings()["hooks"]!["PreToolUse"]!.AsArray().Count);

        HookInstaller.Apply(true);
        Assert.Contains("C:/tools/relay.exe", s.ReadClaudeSettingsText());
        Assert.Single(s.ReadClaudeSettings()["hooks"]!["PreToolUse"]!.AsArray());
    }

    [Fact]
    public void Old_settings_backups_are_pruned()
    {
        using var s = new Sandbox();
        s.InstallRelay();
        s.WriteClaudeSettings("{}");
        string folder = Path.GetDirectoryName(s.ClaudeSettings)!;
        File.WriteAllText(Path.Combine(folder, "settings.json.bak-my-own-copy"), "{}");   // not ours: must survive

        for (int i = 0; i < 9; i++) HookInstaller.Apply(false);   // back to back, often within the same millisecond

        var ours = Directory.GetFiles(folder, "settings.json.bak-*").Where(f => !f.EndsWith("my-own-copy")).ToArray();
        Assert.Equal(5, ours.Length);
        Assert.True(File.Exists(Path.Combine(folder, "settings.json.bak-my-own-copy")));
    }

    [Fact]
    public void Two_installs_in_the_same_millisecond_do_not_collide()
    {
        using var s = new Sandbox();
        s.InstallRelay();
        s.WriteClaudeSettings("{\"model\":\"opus\"}");
        for (int i = 0; i < 40; i++) Assert.Contains("Hooks installed", HookInstaller.Apply(false));
        Assert.Equal("opus", s.ReadClaudeSettings()["model"]!.ToString());
    }

    [Fact]
    public void Installing_without_a_relay_says_so_instead_of_writing_broken_hooks()
    {
        using var s = new Sandbox();   // no relay.exe anywhere
        string result = HookInstaller.Apply(false);
        Assert.Contains("relay.exe not found", result);
        Assert.False(File.Exists(s.ClaudeSettings));
    }
}
