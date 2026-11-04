using System;
using System.IO;
using ClaudeIsland;
using Xunit;

namespace Eave.Tests;

public class LocalGuardTests
{
    const int Port = 47821;
    const string Token = "0123456789abcdef0123456789abcdef";
    static int? Check(string? host = "127.0.0.1:47821", string? origin = null, string? token = Token, string expected = Token) =>
        LocalGuard.Check(host, origin, token, expected, Port);

    [Fact] public void The_relay_with_the_right_secret_is_let_in() => Assert.Null(Check());
    [Fact] public void A_request_without_the_secret_is_refused() => Assert.Equal(401, Check(token: null));
    [Fact] public void A_request_with_the_wrong_secret_is_refused() => Assert.Equal(401, Check(token: "nope"));
    [Fact] public void A_web_page_is_refused_even_with_the_secret() => Assert.Equal(403, Check(origin: "https://evil.example"));
    [Fact] public void A_page_sending_Origin_null_is_refused() => Assert.Equal(403, Check(origin: "null"));
    [Fact] public void DNS_rebinding_with_another_host_name_is_refused() => Assert.Equal(403, Check(host: "evil.example:47821"));
    [Fact] public void A_missing_host_is_refused() => Assert.Equal(403, Check(host: null));
    [Fact] public void Without_a_secret_file_the_origin_and_host_checks_still_apply()
    {
        Assert.Null(Check(token: null, expected: ""));
        Assert.Equal(403, Check(origin: "https://evil.example", token: null, expected: ""));
    }
}

public class LocalTokenTests
{
    [Fact]
    public void The_secret_is_created_once_and_then_reused()
    {
        using var s = new Sandbox();
        string first = LocalToken.LoadOrCreate();
        string second = LocalToken.LoadOrCreate();
        Assert.True(first.Length >= 32);
        Assert.Equal(first, second);
        Assert.Equal(first, File.ReadAllText(LocalToken.FilePath).Trim());
        Assert.True(LocalToken.Matches(first, second));
        Assert.False(LocalToken.Matches(first + "x", second));
    }

    [Fact]
    public void A_damaged_secret_file_is_replaced()
    {
        using var s = new Sandbox();
        Directory.CreateDirectory(s.DataDir);
        File.WriteAllText(LocalToken.FilePath, "short");
        Assert.True(LocalToken.LoadOrCreate().Length >= 32);
    }
}

public class CalendarLinkTests
{
    const string Url = "https://calendar.example.com/private/abc123secret/basic.ics";

    [Fact]
    public void The_link_survives_a_save_and_load_and_is_not_plain_text_on_disk_on_Windows()
    {
        using var s = new Sandbox();
        AppSettings.Current.CalendarUrl = Url;
        AppSettings.Save();
        string file = File.ReadAllText(Path.Combine(s.DataDir, "settings.json"));

        AppSettings.Load();

        Assert.Equal(Url, AppSettings.Current.CalendarUrl);
        if (OperatingSystem.IsWindows()) Assert.DoesNotContain("abc123secret", file);   // DPAPI only exists on Windows
    }

    [Fact]
    public void A_plain_text_link_from_an_older_version_is_read_and_then_saved_protected()
    {
        using var s = new Sandbox();
        Directory.CreateDirectory(s.DataDir);
        File.WriteAllText(Path.Combine(s.DataDir, "settings.json"), "{ \"WeatherCity\": \"Riyadh\", \"CalendarUrl\": \"" + Url + "\" }");

        AppSettings.Load();
        Assert.Equal(Url, AppSettings.Current.CalendarUrl);
        Assert.Equal("Riyadh", AppSettings.Current.WeatherCity);

        AppSettings.Save();
        string file = File.ReadAllText(Path.Combine(s.DataDir, "settings.json"));
        Assert.DoesNotContain("\"CalendarUrl\"", file);                  // the old plain-text field is gone
        Assert.Contains("CalendarUrlProtected", file);
        if (OperatingSystem.IsWindows()) Assert.DoesNotContain("abc123secret", file);
        AppSettings.Load();
        Assert.Equal(Url, AppSettings.Current.CalendarUrl);
    }

    [Fact]
    public void An_empty_link_stays_empty()
    {
        using var s = new Sandbox();
        AppSettings.Save();
        AppSettings.Load();
        Assert.Equal("", AppSettings.Current.CalendarUrl);
    }
}
