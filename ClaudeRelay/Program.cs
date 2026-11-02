// Called by Claude Code hooks: relay.exe event|permission  (hook JSON on stdin).
// Forwards the event to Eave. Never blocks or fails Claude Code: exits 0 whatever happens.
using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json.Nodes;

const int Port = 47821;
string mode = args.Length > 0 && args[0] == "permission" ? "permission" : "event";
string dataDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Eave");
// The secret Eave created in %APPDATA%\Eave\token: without it Eave refuses the request.
string token = "";
try { token = File.ReadAllText(Path.Combine(dataDir, "token")).Trim(); } catch { }

Console.InputEncoding = new UTF8Encoding(false);
string body = Console.In.ReadToEnd().TrimStart('﻿');
string eventName = "";
try
{
    if (JsonNode.Parse(body) is JsonObject o)
    {
        eventName = o["hook_event_name"]?.ToString() ?? "";
        o["_ppid"] = Native.ParentPid();
        body = o.ToJsonString();
    }
}
catch (Exception ex) { Log($"parse failed: {ex.GetType().Name}: {ex.Message} (len {body.Length})"); }
if (string.IsNullOrWhiteSpace(body)) body = "{}";

// A short connect timeout keeps hooks fast when the island isn't running (loopback connects instantly otherwise).
using var http = new HttpClient(new SocketsHttpHandler { ConnectTimeout = TimeSpan.FromMilliseconds(400) })
{
    Timeout = TimeSpan.FromSeconds(mode == "permission" ? 118 : 2),
};
try
{
    using var req = new HttpRequestMessage(HttpMethod.Post, $"http://127.0.0.1:{Port}/{mode}")
    {
        Content = new StringContent(body, Encoding.UTF8, "application/json"),
    };
    if (token != "") req.Headers.Add("X-Eave-Token", token);
    var res = await http.SendAsync(req);
    string text = await res.Content.ReadAsStringAsync();
    if (!res.IsSuccessStatusCode) Log($"Eave refused {eventName}: {(int)res.StatusCode} (is relay.exe the same version as Eave.exe?)");
    else if (mode == "permission" && text.Length > 0 && text != "{}") Console.Out.Write(text);
}
catch (Exception ex)
{
    // The island isn't reachable (crashed or never started): bring it back on the next prompt,
    // unless the user quit it from the tray on purpose.
    Log($"unreachable ({eventName}): {ex.GetType().Name}");
    if (mode == "event" && eventName == "UserPromptSubmit") TryStartIsland();
}
return 0;

void Log(string line)
{
    try
    {
        Directory.CreateDirectory(dataDir);
        string file = Path.Combine(dataDir, "relay.log");
        if (File.Exists(file) && new FileInfo(file).Length > 100_000) File.Delete(file);
        File.AppendAllText(file, $"[{DateTime.Now:s}] {line}\n");
    }
    catch { }
}

void TryStartIsland()
{
    try
    {
        if (File.Exists(Path.Combine(dataDir, "quit.flag"))) return;
        string exe = Path.Combine(AppContext.BaseDirectory, "Eave.exe");
        Log($"starting island: {exe} exists={File.Exists(exe)}");
        if (File.Exists(exe))
            Process.Start(new ProcessStartInfo(exe) { UseShellExecute = false, CreateNoWindow = true });
    }
    catch (Exception ex) { Log($"start failed: {ex.Message}"); }
}

static class Native
{
    [DllImport("kernel32.dll")] static extern IntPtr CreateToolhelp32Snapshot(uint flags, uint pid);
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

    public static int ParentPid()
    {
        uint me = (uint)Environment.ProcessId;
        IntPtr snap = CreateToolhelp32Snapshot(0x2, 0);
        if (snap == IntPtr.Zero || snap == (IntPtr)(-1)) return 0;
        try
        {
            var e = new PROCESSENTRY32 { dwSize = (uint)Marshal.SizeOf<PROCESSENTRY32>() };
            for (bool ok = Process32FirstW(snap, ref e); ok; ok = Process32NextW(snap, ref e))
                if (e.th32ProcessID == me) return (int)e.th32ParentProcessID;
        }
        finally { CloseHandle(snap); }
        return 0;
    }
}
