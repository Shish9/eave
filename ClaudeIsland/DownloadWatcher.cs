using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;

namespace ClaudeIsland;

public record DownloadInfo(string Path, string Name, long Bytes, double Speed);
public record CompletedDownload(string Name, string Path, long Size);

/// Watches the Downloads folder for in-progress browser files (.crdownload, .part ...) and reports
/// their size and speed. Browsers don't expose the total size to the folder, so there is no percentage.
/// Only file names and sizes in that one folder are read. Poll() should be called about once a second.
public class DownloadWatcher
{
    static readonly string[] Extensions = { ".crdownload", ".part", ".download", ".opdownload", ".partial" };

    class Track
    {
        public string Path = "", Name = "";
        public long Size;
        public DateTime LastTime, Started, LastGrowth;
        public double Speed;
    }

    readonly Dictionary<string, Track> tracks = new();
    readonly string folder = FindFolder();

    public List<DownloadInfo> Active { get; private set; } = new();

    [DllImport("shell32.dll")]
    static extern int SHGetKnownFolderPath([MarshalAs(UnmanagedType.LPStruct)] Guid id, uint flags, IntPtr token,
        [MarshalAs(UnmanagedType.LPWStr)] out string path);

    static string FindFolder()
    {
        try
        {
            if (SHGetKnownFolderPath(new Guid("374DE290-123F-4565-9164-39C4925E467B"), 0, IntPtr.Zero, out string p) == 0 && Directory.Exists(p))
                return p;
        }
        catch { }
        return System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
    }

    static string CleanName(string name) => name.StartsWith("Unconfirmed", StringComparison.OrdinalIgnoreCase) ? "Download" : name;

    /// Returns downloads that finished since the last call.
    public List<CompletedDownload> Poll()
    {
        var finished = new List<CompletedDownload>();
        var seen = new HashSet<string>();
        var now = DateTime.Now;

        try
        {
            foreach (var file in Directory.EnumerateFiles(folder))
            {
                string ext = System.IO.Path.GetExtension(file);
                if (!Extensions.Contains(ext, StringComparer.OrdinalIgnoreCase)) continue;
                seen.Add(file);
                long size = new FileInfo(file).Length;

                if (!tracks.TryGetValue(file, out var t))
                {
                    tracks[file] = new Track
                    {
                        Path = file,
                        Name = CleanName(System.IO.Path.GetFileNameWithoutExtension(file)),
                        Size = size, LastTime = now, Started = now, LastGrowth = now,
                    };
                    continue;
                }

                double dt = (now - t.LastTime).TotalSeconds;
                if (dt < 0.5) continue;
                double instant = Math.Max(0, (size - t.Size) / dt);
                t.Speed = t.Speed * 0.5 + instant * 0.5;
                if (size > t.Size) t.LastGrowth = now;
                t.Size = size;
                t.LastTime = now;
            }
        }
        catch { }

        foreach (var path in tracks.Keys.Except(seen).ToList())
        {
            var t = tracks[path];
            tracks.Remove(path);
            // A finished download is renamed to the same name without the temporary extension.
            string final = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(path)!, System.IO.Path.GetFileNameWithoutExtension(path));
            try
            {
                if (File.Exists(final) && (now - t.Started).TotalSeconds >= 2)
                    finished.Add(new CompletedDownload(System.IO.Path.GetFileName(final), final, new FileInfo(final).Length));
            }
            catch { }
        }

        // Stalled for a long time (paused or abandoned): stop showing it as active.
        Active = tracks.Values
            .Where(t => (now - t.LastGrowth).TotalSeconds < 20)
            .Select(t => new DownloadInfo(t.Path, t.Name, t.Size, t.Speed))
            .ToList();
        return finished;
    }

    public static string FormatSize(double bytes)
    {
        string[] units = { "B", "KB", "MB", "GB", "TB" };
        int u = 0;
        while (bytes >= 1024 && u < units.Length - 1) { bytes /= 1024; u++; }
        return u == 0 ? $"{bytes:0} {units[u]}" : $"{bytes:0.#} {units[u]}";
    }
}
