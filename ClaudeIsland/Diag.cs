using System;
using System.IO;

namespace ClaudeIsland;

/// Small diagnostic log (%APPDATA%\Eave\island.log) for window visibility and position changes.
public static class Diag
{
    static string File_ => Path.Combine(Brand.DataDir, "island.log");

    public static void Log(string line)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(File_)!);
            if (File.Exists(File_) && new FileInfo(File_).Length > 200_000) File.Delete(File_);
            File.AppendAllText(File_, $"[{DateTime.Now:HH:mm:ss}] {line}\n");
        }
        catch { }
    }
}
