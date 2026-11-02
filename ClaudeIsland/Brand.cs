using System;
using System.IO;

namespace ClaudeIsland;

/// Product name and the places it lives on disk. The app was called "Claude Island" before it was renamed;
/// the Legacy* values let a new install carry over settings and clean up the old one.
public static class Brand
{
    public const string Name = "Eave";
    public const string StudioName = "Eave Studio";
    public const string Publisher = "Eave contributors";
    public const string Version = "0.3.2";
    public const string ExeName = "Eave.exe";
    public const string ProcessName = "Eave";
    public const string LegacyProcessName = "ClaudeIsland";

    static string AppData => Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
    static string LocalAppData => Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

    /// Settings and logs.
    public static string DataDir => Path.Combine(AppData, "Eave");
    public static string LegacyDataDir => Path.Combine(AppData, "ClaudeIsland");

    /// Where --install puts the app.
    public static string InstallDir => Path.Combine(LocalAppData, "Eave");
    public static string LegacyInstallDir => Path.Combine(LocalAppData, "ClaudeIsland");

    /// First run after the rename: bring the old settings across.
    public static void MigrateData()
    {
        try
        {
            string from = Path.Combine(LegacyDataDir, "settings.json"), to = Path.Combine(DataDir, "settings.json");
            if (!File.Exists(to) && File.Exists(from))
            {
                Directory.CreateDirectory(DataDir);
                File.Copy(from, to);
            }
        }
        catch { }
    }
}
