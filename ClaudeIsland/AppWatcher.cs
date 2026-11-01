using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace ClaudeIsland;

public record ClosedApp(string Name, ImageSource? Icon);

/// Notices when an app's last visible window goes away and its process has exited.
/// Poll() must be called on the UI thread (about once a second).
public class AppWatcher
{
    class Info
    {
        public string Name = "";
        public ImageSource? Icon;
        public DateTime FirstSeen;
    }

    static readonly HashSet<string> Ignored = new(StringComparer.OrdinalIgnoreCase)
    {
        "explorer", "ApplicationFrameHost", "ClaudeIsland", "Eave", "TextInputHost", "ShellExperienceHost", "SearchHost",
        "StartMenuExperienceHost", "LockApp", "dwm", "ctfmon", "Widgets", "WidgetBoard", "msedgewebview2",
        "RuntimeBroker", "CompPkgSrv", "PickerHost", "SystemSettingsBroker", "relay", "conhost", "OpenConsole",
    };

    readonly Dictionary<int, Info> seen = new();
    bool first = true;

    delegate bool EnumProc(IntPtr hwnd, IntPtr lParam);
    [DllImport("user32.dll")] static extern bool EnumWindows(EnumProc cb, IntPtr lParam);
    [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll")] static extern IntPtr GetWindow(IntPtr h, uint cmd);
    [DllImport("user32.dll")] static extern int GetWindowLong(IntPtr h, int i);
    [DllImport("user32.dll")] static extern int GetWindowTextLength(IntPtr h);
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    [DllImport("dwmapi.dll")] static extern int DwmGetWindowAttribute(IntPtr h, int attr, out int value, int size);
    [DllImport("user32.dll")] static extern bool DestroyIcon(IntPtr h);

    const int GWL_EXSTYLE = -20, WS_EX_TOOLWINDOW = 0x80, WS_EX_APPWINDOW = 0x40000, DWMWA_CLOAKED = 14;
    const uint GW_OWNER = 4;

    public List<ClosedApp> Poll()
    {
        var current = new HashSet<int>();
        EnumWindows((h, _) =>
        {
            if (!IsWindowVisible(h) || GetWindow(h, GW_OWNER) != IntPtr.Zero) return true;
            int ex = GetWindowLong(h, GWL_EXSTYLE);
            if ((ex & WS_EX_TOOLWINDOW) != 0 && (ex & WS_EX_APPWINDOW) == 0) return true;
            if (GetWindowTextLength(h) == 0) return true;
            if (DwmGetWindowAttribute(h, DWMWA_CLOAKED, out int cloaked, 4) == 0 && cloaked != 0) return true;

            GetWindowThreadProcessId(h, out uint pid);
            if (pid == 0 || pid == Environment.ProcessId) return true;
            current.Add((int)pid);
            if (!seen.ContainsKey((int)pid)) Register((int)pid);
            return true;
        }, IntPtr.Zero);

        var closed = new List<ClosedApp>();
        foreach (int pid in seen.Keys.Except(current).ToList())
        {
            var info = seen[pid];
            seen.Remove(pid);
            bool exited;
            try { using var p = Process.GetProcessById(pid); exited = p.HasExited; }
            catch { exited = true; }
            // Windows that merely hid (minimised to tray) don't count; neither do short-lived splash windows.
            if (exited && (DateTime.Now - info.FirstSeen).TotalSeconds > 4) closed.Add(new ClosedApp(info.Name, info.Icon));
        }
        first = false;
        return closed;
    }

    void Register(int pid)
    {
        var info = new Info { FirstSeen = first ? DateTime.MinValue : DateTime.Now };
        try
        {
            using var p = Process.GetProcessById(pid);
            if (Ignored.Contains(p.ProcessName)) { info.Name = ""; }
            else
            {
                info.Name = p.ProcessName;
                try
                {
                    string? path = p.MainModule?.FileName;
                    if (path != null)
                    {
                        string desc = FileVersionInfo.GetVersionInfo(path).FileDescription ?? "";
                        if (desc.Trim().Length > 0) info.Name = desc.Trim();
                        info.Icon = LoadIcon(path);
                    }
                }
                catch { }
            }
        }
        catch { info.Name = ""; }

        // Ignored or unreadable processes are tracked as seen but never reported.
        if (info.Name == "") info.FirstSeen = DateTime.Now.AddYears(1);
        seen[pid] = info;
    }

    public static ImageSource? LoadIcon(string path)
    {
        try
        {
            using var icon = System.Drawing.Icon.ExtractAssociatedIcon(path);
            if (icon == null) return null;
            var src = Imaging.CreateBitmapSourceFromHIcon(icon.Handle, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            src.Freeze();
            return src;
        }
        catch { return null; }
    }
}
