using System;
using System.IO;
using System.Linq;
using ClaudeIsland;
using Xunit;

namespace Eave.Tests;

public class SettingsTests
{
    [Fact]
    public void A_missing_file_gives_defaults()
    {
        using var s = new Sandbox();
        Assert.Equal("", AppSettings.Current.WeatherCity);
        Assert.False(AppSettings.Current.ShowNotifications);
        Assert.Equal(AppSettings.CurrentVersion, AppSettings.Current.SettingsVersion);
    }

    [Fact]
    public void Settings_survive_a_save_and_load()
    {
        using var s = new Sandbox();
        AppSettings.Current.WeatherCity = "Riyadh";
        AppSettings.Current.ShowNotifications = true;
        AppSettings.Save();

        AppSettings.Load();

        Assert.Equal("Riyadh", AppSettings.Current.WeatherCity);
        Assert.True(AppSettings.Current.ShowNotifications);
    }

    [Fact]
    public void A_damaged_file_falls_back_to_the_last_good_copy_and_is_kept_aside()
    {
        using var s = new Sandbox();
        AppSettings.Current.WeatherCity = "Jeddah";
        AppSettings.Save();
        AppSettings.Current.WeatherCity = "Dammam";
        AppSettings.Save();                                  // the file now says Dammam, the .bak says Jeddah
        File.WriteAllText(Path.Combine(s.DataDir, "settings.json"), "{\"WeatherCity\": \"Dam");   // half-written

        AppSettings.Load();

        Assert.Equal("Jeddah", AppSettings.Current.WeatherCity);
        Assert.Single(Directory.GetFiles(s.DataDir, "settings.json.damaged-*"));
    }

    [Fact]
    public void Saving_goes_through_a_temp_file_that_does_not_linger()
    {
        using var s = new Sandbox();
        AppSettings.Save();
        AppSettings.Save();
        Assert.True(File.Exists(Path.Combine(s.DataDir, "settings.json")));
        Assert.False(File.Exists(Path.Combine(s.DataDir, "settings.json.tmp")));
    }
}
