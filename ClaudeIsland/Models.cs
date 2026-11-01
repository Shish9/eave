using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using System.Windows.Media;

namespace ClaudeIsland;

public enum SessionState { Idle, Working, Asking, NeedsYou, Done, Stuck }

public class PermissionRequest
{
    public Session Session = null!;
    public string Tool = "";
    public string Detail = "";
    public List<(char Kind, string Text)> Diff = new();
    public JsonNode? Suggestions;
    public TaskCompletionSource<string> Tcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
}

public class Session : INotifyPropertyChanged
{
    public string Id = "";
    public string Cwd = "";
    public IntPtr Hwnd;
    public SessionState State = SessionState.Idle;
    public string Tool = "", Detail = "", LastMessage = "";
    public int ToolCount;
    public int ContextTokens;
    public Brush Accent = Brushes.Gray;
    public DateTime Started = DateTime.Now, LastEvent = DateTime.Now, DoneAt;
    public List<PermissionRequest> Pending = new();

    public event PropertyChangedEventHandler? PropertyChanged;
    public void Touch() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));

    public string Project
    {
        get
        {
            var name = System.IO.Path.GetFileName(Cwd.TrimEnd('\\', '/'));
            return string.IsNullOrEmpty(name) ? "Claude" : name;
        }
    }

    public string Line => State switch
    {
        SessionState.Working => $"{Tool} {Detail}".Trim(),
        SessionState.Asking => "Waiting for your permission",
        SessionState.NeedsYou => string.IsNullOrEmpty(Detail) ? "Needs your attention" : Detail,
        SessionState.Done => string.IsNullOrEmpty(LastMessage) ? "Finished" : LastMessage,
        SessionState.Stuck => $"No activity for {Fmt(DateTime.Now - LastEvent)}",
        _ => "Idle",
    };

    public string Meta => State switch
    {
        SessionState.Working or SessionState.Stuck or SessionState.Asking => $"{Fmt(DateTime.Now - Started)} · {ToolCount} tools",
        SessionState.Done => $"{Fmt(DoneAt - Started)} · {ToolCount} tools" +
                             (ContextTokens > 0 ? $" · {(ContextTokens >= 1000 ? ContextTokens / 1000 + "k" : ContextTokens.ToString())} ctx" : ""),
        _ => "",
    };

    static Brush B(string hex)
    {
        var b = (SolidColorBrush)new BrushConverter().ConvertFromString(hex)!;
        b.Freeze();
        return b;
    }

    static readonly Brush Orange = B("#FF9F43"), Blue = B("#54A0FF"), Yellow = B("#FECA57"),
        Green = B("#1DD1A1"), Red = B("#FF6B6B"), Gray = B("#777777");

    public Brush Brush => StateBrush(State);

    /// Distinct accent per session so rows are easy to tell apart.
    public static readonly Brush[] AccentPalette =
    {
        B("#7AA2F7"), B("#E0AF68"), B("#BB9AF7"), B("#73DACA"), B("#F7768E"), B("#9ECE6A"),
    };

    public static Brush StateBrush(SessionState s) => s switch
    {
        SessionState.Working => Orange,
        SessionState.Asking => Blue,
        SessionState.NeedsYou => Yellow,
        SessionState.Done => Green,
        SessionState.Stuck => Red,
        _ => Gray,
    };

    public static string Fmt(TimeSpan t) =>
        t.TotalHours >= 1 ? $"{(int)t.TotalHours}h {t.Minutes}m" :
        t.TotalMinutes >= 1 ? $"{(int)t.TotalMinutes}m {t.Seconds}s" : $"{Math.Max(0, (int)t.TotalSeconds)}s";
}
