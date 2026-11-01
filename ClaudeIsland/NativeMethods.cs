using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace ClaudeIsland;

static class NativeMethods
{
    const int GWL_EXSTYLE = -20;
    const int WS_EX_TOOLWINDOW = 0x80, WS_EX_NOACTIVATE = 0x08000000;

    [DllImport("user32.dll")] static extern int GetWindowLong(IntPtr h, int i);
    [DllImport("user32.dll")] static extern int SetWindowLong(IntPtr h, int i, int v);
    [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr h);
    [DllImport("user32.dll")] static extern bool ShowWindow(IntPtr h, int cmd);
    [DllImport("user32.dll")] static extern bool IsIconic(IntPtr h);
    [DllImport("user32.dll")] static extern bool IsWindow(IntPtr h);
    [DllImport("user32.dll")] static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extra);
    [DllImport("shell32.dll")] static extern int SHQueryUserNotificationState(out int state);

    [DllImport("kernel32.dll", SetLastError = true)] static extern IntPtr CreateToolhelp32Snapshot(uint flags, uint pid);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern bool Process32FirstW(IntPtr s, ref PROCESSENTRY32 e);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern bool Process32NextW(IntPtr s, ref PROCESSENTRY32 e);
    [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr h);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct PROCESSENTRY32
    {
        public uint dwSize, cntUsage, th32ProcessID;
        public IntPtr th32DefaultHeapID;
        public uint th32ModuleID, cntThreads, th32ParentProcessID;
        public int pcPriClassBase;
        public uint dwFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string szExeFile;
    }

    [DllImport("kernel32.dll")] static extern bool GetSystemTimes(out long idle, out long kernel, out long user);
    [DllImport("kernel32.dll")] static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX m);

    [StructLayout(LayoutKind.Sequential)]
    struct MEMORYSTATUSEX
    {
        public uint dwLength, dwMemoryLoad;
        public ulong ullTotalPhys, ullAvailPhys, ullTotalPageFile, ullAvailPageFile, ullTotalVirtual, ullAvailVirtual, ullAvailExtendedVirtual;
    }

    static long lastIdle, lastBusy;

    /// CPU use since the previous call, 0..100.
    public static int CpuPercent()
    {
        if (!GetSystemTimes(out long idle, out long kernel, out long user)) return 0;
        long busy = kernel + user, dIdle = idle - lastIdle, dTotal = busy - lastBusy;
        lastIdle = idle; lastBusy = busy;
        return dTotal <= 0 ? 0 : (int)Math.Clamp(100 - dIdle * 100 / dTotal, 0, 100);
    }

    public static int MemoryPercent()
    {
        var m = new MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>() };
        return GlobalMemoryStatusEx(ref m) ? (int)m.dwMemoryLoad : 0;
    }

    [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint flags);
    [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetClassName(IntPtr h, System.Text.StringBuilder sb, int n);
    [DllImport("user32.dll")] static extern IntPtr SetWinEventHook(uint min, uint max, IntPtr mod, WinEventProc proc, uint pid, uint tid, uint flags);

    delegate void WinEventProc(IntPtr hook, uint ev, IntPtr hwnd, int idObj, int idChild, uint thread, uint time);
    static WinEventProc? foregroundProc; // kept alive for the lifetime of the hook

    [DllImport("user32.dll")] public static extern bool AddClipboardFormatListener(IntPtr hwnd);

    /// Put the window back at the top of the always-on-top band without activating it.
    public static void RaiseTopmost(IntPtr hwnd) =>
        SetWindowPos(hwnd, new IntPtr(-1), 0, 0, 0, 0, 0x0001 | 0x0002 | 0x0010 | 0x0040); // NOSIZE NOMOVE NOACTIVATE SHOWWINDOW

    /// Calls back whenever the foreground window changes (for example when an app closes).
    public static void OnForegroundChanged(Action callback)
    {
        foregroundProc = (_, _, _, _, _, _, _) => callback();
        SetWinEventHook(0x0003, 0x0003, IntPtr.Zero, foregroundProc, 0, 0, 0); // EVENT_SYSTEM_FOREGROUND, out of context
    }

    public static string ForegroundClass()
    {
        var sb = new System.Text.StringBuilder(128);
        GetClassName(GetForegroundWindow(), sb, 128);
        return sb.ToString();
    }

    /// The desktop and taskbar can look "fullscreen" to Windows; they should never hide the island.
    public static bool ForegroundIsShell() =>
        ForegroundClass() is "Progman" or "WorkerW" or "Shell_TrayWnd" or "Shell_SecondaryTrayWnd";

    /// Keep the island from stealing focus or showing in Alt+Tab.
    public static void MakeNoActivate(IntPtr hwnd) =>
        SetWindowLong(hwnd, GWL_EXSTYLE, GetWindowLong(hwnd, GWL_EXSTYLE) | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE);

    /// QUERY_USER_NOTIFICATION_STATE: 2 = fullscreen/busy, 3 = fullscreen Direct3D game, 4 = presentation, 5 = normal. 0 if unknown.
    public static int FullscreenState() => SHQueryUserNotificationState(out int s) == 0 ? s : 0;

    public static void Focus(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero || !IsWindow(hwnd)) return;
        if (IsIconic(hwnd)) ShowWindow(hwnd, 9);
        keybd_event(0x12, 0, 0, UIntPtr.Zero);      // Alt press/release lets SetForegroundWindow succeed
        keybd_event(0x12, 0, 2, UIntPtr.Zero);
        SetForegroundWindow(hwnd);
    }

    /// Walk up from a process to the first ancestor that owns a top-level window (the terminal).
    public static IntPtr FindTerminalWindow(int pid)
    {
        var parents = new Dictionary<uint, uint>();
        IntPtr snap = CreateToolhelp32Snapshot(0x2, 0);
        if (snap == IntPtr.Zero || snap == (IntPtr)(-1)) return IntPtr.Zero;
        try
        {
            var e = new PROCESSENTRY32 { dwSize = (uint)Marshal.SizeOf<PROCESSENTRY32>() };
            for (bool ok = Process32FirstW(snap, ref e); ok; ok = Process32NextW(snap, ref e))
                parents[e.th32ProcessID] = e.th32ParentProcessID;
        }
        finally { CloseHandle(snap); }

        uint p = (uint)pid;
        for (int i = 0; i < 16 && p != 0; i++)
        {
            try
            {
                using var proc = Process.GetProcessById((int)p);
                if (proc.ProcessName.Equals("explorer", StringComparison.OrdinalIgnoreCase)) break;
                if (proc.MainWindowHandle != IntPtr.Zero) return proc.MainWindowHandle;
            }
            catch { }
            if (!parents.TryGetValue(p, out p)) break;
        }
        return IntPtr.Zero;
    }
}
