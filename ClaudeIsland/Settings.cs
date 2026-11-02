using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace ClaudeIsland;

public class AppSettings
{
    /// Layout version of settings.json. Raise this and add a step to Migrate() whenever a setting changes meaning.
    public const int CurrentVersion = 1;
    public int SettingsVersion { get; set; } = CurrentVersion;

    // ---- behavior ----
    public bool Mute { get; set; }
    /// Never | Games | Any. "Games" hides only for exclusive-fullscreen games and presentations.
    public string FullscreenMode { get; set; } = "Never";
    public int Monitor { get; set; } = -1; // -1 = primary
    public int StuckSeconds { get; set; } = 120;

    // ---- design ----
    public string Shape { get; set; } = "Notch";      // Notch | Pill
    public double Flare { get; set; } = 16;            // notch corner flare, 4..28
    public string Size { get; set; } = "Normal";       // Compact | Normal | Large
    public double OffsetX { get; set; }                // pixels left/right of centre
    public double IslandOpacity { get; set; } = 1;

    // ---- theme ----
    public string Theme { get; set; } = "Dark";        // Dark | Midnight | Graphite | Clay | Light | Custom
    public string CustomBg { get; set; } = "#0B0B0F";
    public string CustomText { get; set; } = "#FFFFFF";
    public string CustomAccent { get; set; } = "#2E86FF";

    // ---- glow ----
    public bool Glow { get; set; } = true;
    public string GlowMode { get; set; } = "State";    // State | Album | Fixed
    public string GlowColor { get; set; } = "#4F6FA8";
    public double GlowIntensity { get; set; } = 0.6;   // 0..1
    public double GlowBlur { get; set; } = 40;         // 10..80
    public bool GlowPulse { get; set; } = true;

    // ---- contents ----
    public bool ShowClock { get; set; } = true;
    public bool ShowBattery { get; set; } = true;
    public bool ShowSessions { get; set; } = true;
    public bool ShowMedia { get; set; } = true;
    public bool ShowWeather { get; set; } = true;
    public bool ShowClipboard { get; set; } = true;
    public bool ShowDownloads { get; set; } = true;
    public bool ShowQuickLaunch { get; set; } = true;
    public bool ShowTimer { get; set; } = true;
    public bool ShowStats { get; set; } = true;
    public bool ShowAppClosed { get; set; } = true;
    public bool ShowCalendar { get; set; } = true;
    public bool ShowNotifications { get; set; }        // opt-in: needs Windows permission

    // ---- connections ----
    public string WeatherCity { get; set; } = "";
    public bool Fahrenheit { get; set; }
    public double? WeatherLat { get; set; }
    public double? WeatherLon { get; set; }
    public string WeatherResolvedFor { get; set; } = "";
    public string WeatherName { get; set; } = "";
    public string CalendarUrl { get; set; } = "";      // a calendar's private iCal (.ics) link

    public List<string> QuickLaunch { get; set; } = new()
    {
        @"C:\Windows\explorer.exe",
        @"%LOCALAPPDATA%\Programs\Microsoft VS Code\Code.exe",
        @"C:\Program Files\Google\Chrome\Application\chrome.exe",
        @"C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe",
        @"C:\Windows\System32\notepad.exe",
    };

    /// Set when the user removed Eave's Claude Code hooks on purpose, so startup doesn't put them back.
    public bool HooksRemoved { get; set; }

    static string PathFile => System.IO.Path.Combine(Brand.DataDir, "settings.json");
    static string BackupFile => PathFile + ".bak";

    public static AppSettings Current { get; private set; } = new();

    /// Reads settings.json. A file that can't be read is never silently replaced by defaults:
    /// a locked file is retried, and a damaged one is kept aside and the last good copy (settings.json.bak) is used.
    public static void Load()
    {
        if (!File.Exists(PathFile)) { Current = TryRead(BackupFile) ?? new(); return; }
        for (int attempt = 0; attempt < 5; attempt++)
        {
            try
            {
                Current = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(PathFile)) ?? throw new JsonException("empty file");
                Current.Migrate();
                return;
            }
            catch (IOException) { System.Threading.Thread.Sleep(200); }   // briefly locked (antivirus, sync, another instance)
            catch (Exception ex)
            {
                Diag.Log($"settings.json could not be read ({ex.GetType().Name}: {ex.Message}); using the backup");
                try { File.Copy(PathFile, $"{PathFile}.damaged-{DateTime.Now:yyyyMMdd-HHmmss}", true); } catch { }
                break;
            }
        }
        Current = TryRead(BackupFile) ?? new();
    }

    void Migrate()
    {
        if (SettingsVersion > CurrentVersion)
            Diag.Log($"settings.json comes from a newer Eave (layout {SettingsVersion}); settings this version doesn't know are ignored");
        // if (SettingsVersion < 2) { ...convert the old setting... }
        SettingsVersion = CurrentVersion;
    }

    static AppSettings? TryRead(string file)
    {
        try
        {
            var s = File.Exists(file) ? JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(file)) : null;
            s?.Migrate();
            return s;
        }
        catch { return null; }
    }

    /// Writes to a temporary file first and then swaps it in, so a crash or a kill mid-save can't leave a half-written file.
    /// The previous file is kept as settings.json.bak.
    public static void Save()
    {
        try
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(PathFile)!);
            string tmp = PathFile + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(Current, new JsonSerializerOptions { WriteIndented = true }));
            if (File.Exists(PathFile)) File.Replace(tmp, PathFile, BackupFile, true);
            else File.Move(tmp, PathFile);
        }
        catch (Exception ex) { Diag.Log($"settings save failed: {ex.Message}"); }
    }
}
