using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using System.Windows.Threading;
using Microsoft.Win32;
using NAudio.CoreAudioApi;

namespace ClaudeIsland;

public partial class MainWindow : Window
{
    enum WaveMode { None, Audio, Activity }

    readonly SessionStore store;
    readonly MediaService media = new();
    readonly WeatherService weather = new();
    readonly CalendarService calendar = new();
    readonly NotificationService notifications = new();
    readonly AppWatcher appWatcher = new();
    readonly DownloadWatcher downloads = new();

    readonly DispatcherTimer tick = new() { Interval = TimeSpan.FromSeconds(1) };
    readonly DispatcherTimer volTimer = new() { Interval = TimeSpan.FromMilliseconds(250) };
    readonly DispatcherTimer waveTimer = new() { Interval = TimeSpan.FromMilliseconds(60) };
    readonly DispatcherTimer leaveDelay = new() { Interval = TimeSpan.FromMilliseconds(280) };
    readonly DispatcherTimer healTimer = new() { Interval = TimeSpan.FromMilliseconds(150) };

    // waveform
    const int BarCount = 9;
    readonly ScaleTransform[] bars = new ScaleTransform[BarCount];
    readonly Rectangle[] barRects = new Rectangle[BarCount];
    readonly Stopwatch clock = Stopwatch.StartNew();
    WaveMode waveMode = WaveMode.None;
    double audioLevel;

    // short-lived notice shown in the header (app closed, clipboard, battery ...)
    DateTime flashUntil = DateTime.MinValue;
    string flashTitle = "", flashMeta = "";
    Brush flashBrush = Brushes.Gray;
    ImageSource? flashIcon;

    readonly ObservableCollection<ClipItem> clips = new();
    bool ignoreClipboard;
    PermissionRequest? diffShownFor;
    bool lowBatteryWarned;
    bool? lastOnline;
    readonly HashSet<string> calendarNotified = new();

    public record ClipItem(string Text)
    {
        public string Preview
        {
            get
            {
                var t = Text.Replace("\r", " ").Replace("\n", " ").Trim();
                return t.Length > 60 ? t[..59] + "…" : t;
            }
        }
    }
    public record QuickItem(string Name, string Path, ImageSource? Icon);
    public record DownloadRow(string Name, string Detail, string Path, string Glyph, Brush GlyphBrush);

    readonly List<(CompletedDownload Item, DateTime At)> recentDownloads = new();
    string lastDownloadSig = "";

    IntPtr hwnd;
    int healStep, hideTicks;
    bool isHidden;

    bool hovered, pinned;
    double lastW, lastH;
    DateTime? timerEnd;
    TimeSpan timerTotal;
    DateTime timerDoneAt = DateTime.MinValue;
    ThemeColors theme = ThemeColors.For(AppSettings.Current);

    // volume popup
    MMDeviceEnumerator? audioEnum;
    MMDevice? audioDev;
    float volume = -1, lastVolume = -1;
    bool muted, lastMuted;
    DateTime volumeUntil = DateTime.MinValue;
    int volPolls;

    // exposed for the Studio window
    public WeatherService Weather => weather;
    public CalendarService Calendar => calendar;
    public NotificationService Notifications => notifications;
    public bool IsPinned => pinned;

    public MainWindow(SessionStore store)
    {
        InitializeComponent();
        this.store = store;
        SessionList.ItemsSource = store.Sessions;
        ClipList.ItemsSource = clips;
        store.Changed += Refresh;

        for (int i = 0; i < BarCount; i++)
        {
            bars[i] = new ScaleTransform(1, 0.2);
            barRects[i] = new Rectangle
            {
                Width = 3, Height = 20, RadiusX = 1.5, RadiusY = 1.5,
                Margin = new Thickness(i == 0 ? 0 : 2.5, 0, 0, 0),
                RenderTransformOrigin = new Point(0.5, 0.5),
                RenderTransform = bars[i],
                Fill = Brushes.White,
            };
            Wave.Children.Add(barRects[i]);
        }

        SourceInitialized += (_, _) =>
        {
            hwnd = new WindowInteropHelper(this).Handle;
            NativeMethods.MakeNoActivate(hwnd);
            Reposition();
            // Closing an app changes the foreground window: make sure the island is still on top and in place.
            NativeMethods.OnForegroundChanged(() => Dispatcher.BeginInvoke(ScheduleHeal));
            // Clipboard change notifications arrive as a window message.
            HwndSource.FromHwnd(hwnd)?.AddHook(WndProc);
            NativeMethods.AddClipboardFormatListener(hwnd);
        };
        SystemEvents.DisplaySettingsChanged += (_, _) => Dispatcher.Invoke(() =>
        {
            Diag.Log("display settings changed");
            Reposition();
            ScheduleHeal();
        });
        SystemEvents.SessionSwitch += (_, _) => Dispatcher.BeginInvoke(ScheduleHeal);
        SystemEvents.PowerModeChanged += (_, _) => Dispatcher.BeginInvoke(ScheduleHeal);

        healTimer.Tick += (_, _) =>
        {
            healTimer.Stop();
            Heal();
            if (healStep++ < 3) { healTimer.Interval = TimeSpan.FromMilliseconds(healStep == 1 ? 700 : 2000); healTimer.Start(); }
        };

        media.Changed += () => Dispatcher.BeginInvoke(Refresh);
        weather.Changed += () => Dispatcher.BeginInvoke(Refresh);
        calendar.Changed += () => Dispatcher.BeginInvoke(Refresh);
        notifications.Received += (app, title, body) => Dispatcher.BeginInvoke(new Action(() =>
        {
            if (!AppSettings.Current.ShowNotifications) return;
            Flash(string.IsNullOrWhiteSpace(title) ? app : $"{app} · {title}", Shorten(body, 30), Brushes.White, null, 5);
        }));
        _ = media.InitAsync();

        leaveDelay.Tick += (_, _) => { leaveDelay.Stop(); hovered = false; Refresh(); };
        tick.Tick += (_, _) => OnTick();
        tick.Start();
        volTimer.Tick += (_, _) => PollVolume();
        volTimer.Start();
        waveTimer.Tick += (_, _) => TickWave();
        waveTimer.Start();

        ApplyAppearance();
        ReloadQuickLaunch();
        Refresh();
    }

    static string Shorten(string s, int max) => s.Length <= max ? s : s[..(max - 1)] + "…";

    // ---------------------------------------------------------------- appearance

    /// Applies theme, shape, size, position, opacity and glow settings.
    void ApplyAppearance()
    {
        var s = AppSettings.Current;
        theme = ThemeColors.For(s);
        theme.ApplyTo(Resources);

        bool pill = s.Shape == "Pill";
        Notch.Floating = pill;
        Notch.FlareRadius = s.Flare;
        Root.Margin = new Thickness(0, pill ? 8 : 0, 0, 40);
        double scale = s.Size switch { "Compact" => 0.88, "Large" => 1.15, _ => 1.0 };
        Root.LayoutTransform = new ScaleTransform(scale, scale);
        Root.Opacity = Math.Clamp(s.IslandOpacity, 0.3, 1);
        GlowFx.BlurRadius = Math.Clamp(s.GlowBlur, 8, 90);

        curGlow = null;           // force the glow to be re-applied with the new settings
        lastW = lastH = 0;        // and the size to be re-fitted (the outline changes with flare/shape)
        if (hwnd != IntPtr.Zero) Reposition();
    }

    /// Called by the Studio window after any setting changes.
    public void SettingsChanged()
    {
        ApplyAppearance();
        weather.Invalidate();
        calendar.Invalidate();
        ReloadQuickLaunch();
        Refresh();
    }

    public void SetPinned(bool value) { pinned = value; Refresh(); }

    public void TestFlash() =>
        Flash("Island preview", "this is how a notice looks", new SolidColorBrush(theme.Accent), null, 4);

    public void Reposition()
    {
        var screens = System.Windows.Forms.Screen.AllScreens;
        int m = AppSettings.Current.Monitor;
        var sc = m >= 0 && m < screens.Length ? screens[m] : System.Windows.Forms.Screen.PrimaryScreen ?? screens[0];
        double sx = 1, sy = 1;
        var src = PresentationSource.FromVisual(this);
        if (src?.CompositionTarget != null)
        {
            sx = src.CompositionTarget.TransformFromDevice.M11;
            sy = src.CompositionTarget.TransformFromDevice.M22;
        }
        double left = (sc.Bounds.Left + sc.Bounds.Width / 2.0) * sx - Width / 2 + AppSettings.Current.OffsetX * sx;
        double top = sc.Bounds.Top * sy;
        if (Math.Abs(left - Left) > 2 || Math.Abs(top - Top) > 2)
            Diag.Log($"reposition ({Left:0},{Top:0}) -> ({left:0},{top:0}) on {sc.Bounds.Width}x{sc.Bounds.Height}");
        Left = left;
        Top = top;
    }

    /// Re-raise and re-place the island a few times after focus changes, once Windows has settled.
    void ScheduleHeal()
    {
        healStep = 0;
        healTimer.Stop();
        healTimer.Interval = TimeSpan.FromMilliseconds(150);
        healTimer.Start();
    }

    void Heal()
    {
        if (hwnd == IntPtr.Zero || isHidden) return;
        if (Visibility != Visibility.Visible) { Diag.Log("heal: was not visible, showing"); Visibility = Visibility.Visible; }
        Reposition();
        NativeMethods.RaiseTopmost(hwnd);
    }

    // ---------------------------------------------------------------- timers

    void PollVolume()
    {
        try
        {
            if (++volPolls % 80 == 0) { audioDev = null; }          // pick up default-device changes
            audioEnum ??= new MMDeviceEnumerator();
            audioDev ??= audioEnum.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
            var v = audioDev.AudioEndpointVolume;
            volume = v.MasterVolumeLevelScalar;
            muted = v.Mute;
            if (lastVolume >= 0 && (Math.Abs(volume - lastVolume) > 0.004f || muted != lastMuted))
            {
                volumeUntil = DateTime.Now.AddMilliseconds(1800);
                Refresh();
            }
            lastVolume = volume;
            lastMuted = muted;
        }
        catch { audioDev = null; }
    }

    /// Drives the waveform: real output level when music plays, a lively pattern while Claude works.
    void TickWave()
    {
        if (waveMode == WaveMode.None) return;
        double t = clock.Elapsed.TotalSeconds;
        double level;
        if (waveMode == WaveMode.Audio)
        {
            float peak = 0;
            try { peak = audioDev?.AudioMeterInformation.MasterPeakValue ?? 0; } catch { }
            audioLevel = audioLevel * 0.55 + Math.Sqrt(peak) * 0.45;
            level = media.Playing ? Math.Max(audioLevel, 0.12) : 0.08;
        }
        else level = 0.6;

        for (int i = 0; i < BarCount; i++)
        {
            double wobble = Math.Sin(t * (5 + i * 0.85) + i * 1.9) * 0.5 + 0.5;
            double centre = 1 - Math.Abs(i - (BarCount - 1) / 2.0) / BarCount;  // taller in the middle
            bars[i].ScaleY = Math.Clamp(0.12 + level * (0.25 + 0.75 * wobble) * (0.55 + 0.6 * centre), 0.1, 1);
        }
    }

    void OnTick()
    {
        var s = AppSettings.Current;
        store.Tick();

        if (timerEnd is DateTime end && DateTime.Now >= end)
        {
            timerEnd = null;
            timerDoneAt = DateTime.Now;
            SessionStore.Play("timer");
        }

        var closedApps = appWatcher.Poll();
        if (s.ShowAppClosed && closedApps.Count > 0)
            Flash(closedApps.Count == 1 ? $"Closed · {closedApps[0].Name}" : $"Closed · {closedApps.Count} apps", "",
                  Session.StateBrush(SessionState.Stuck), closedApps.Count == 1 ? closedApps[0].Icon : null, 3);

        weather.Tick();
        calendar.Tick();
        notifications.Tick();
        _ = notifications.Status;
        CheckBattery();
        CheckCalendar();

        if (s.ShowDownloads)
        {
            foreach (var d in downloads.Poll())
            {
                recentDownloads.Insert(0, (d, DateTime.Now));
                Flash("Download complete", Shorten(d.Name, 26), Session.StateBrush(SessionState.Done), null, 4);
            }
            recentDownloads.RemoveAll(r => (DateTime.Now - r.At).TotalMinutes > 10);
            if (recentDownloads.Count > 2) recentDownloads.RemoveRange(2, recentDownloads.Count - 2);
        }
        else if (downloads.Active.Count > 0) downloads.Active.Clear();

        // Hide only when asked to, for a real fullscreen app (never the desktop or taskbar), after two checks in a row.
        int fsState = NativeMethods.FullscreenState();
        bool fullscreen = s.FullscreenMode switch
        {
            "Any" => fsState is 2 or 3 or 4,
            "Games" => fsState is 3 or 4,
            _ => false,
        };
        bool wantHide = fullscreen && store.PendingCount == 0 && !NativeMethods.ForegroundIsShell();
        hideTicks = wantHide ? hideTicks + 1 : 0;
        bool hide = hideTicks >= 2;
        if (hide != isHidden)
        {
            isHidden = hide;
            Diag.Log(hide ? $"hiding for fullscreen app (foreground: {NativeMethods.ForegroundClass()}, state {fsState})" : "showing again");
        }
        Visibility = hide ? Visibility.Hidden : Visibility.Visible;
        if (!hide) Heal();
        Refresh();
    }

    // ---------------------------------------------------------------- refresh

    void Refresh()
    {
        var s = AppSettings.Current;
        var sessions = store.Sessions;
        var asking = store.FirstPending();
        var now = DateTime.Now;
        bool showMediaFeature = s.ShowMedia && media.HasTrack;
        bool expanded = asking != null || hovered || pinned;
        var downloadsActive = s.ShowDownloads ? downloads.Active : new List<DownloadInfo>();

        string title, meta = "";
        Brush dot;
        double compactW;
        bool art = false, downloading = false;
        ImageSource? appIcon = null;
        var wave = WaveMode.None;
        Brush waveBrush = Brushes.White;
        double headerBar = -1; // 0..1 progress bar under the header, -1 = hidden

        var needs = sessions.FirstOrDefault(x => x.State == SessionState.NeedsYou);
        var stuck = sessions.FirstOrDefault(x => x.State == SessionState.Stuck);
        var working = sessions.Where(x => x.State == SessionState.Working).ToList();
        var done = sessions.Where(x => x.State == SessionState.Done && (now - x.DoneAt).TotalSeconds < 8)
                           .OrderByDescending(x => x.DoneAt).FirstOrDefault();

        if (asking != null)
        {
            title = $"Permission · {asking.Session.Project}";
            dot = Session.StateBrush(SessionState.Asking);
            compactW = 260;
        }
        else if (needs != null)
        {
            title = $"Needs you · {needs.Project}";
            dot = Session.StateBrush(SessionState.NeedsYou);
            compactW = 230;
        }
        else if (stuck != null)
        {
            title = $"No activity · {stuck.Project}";
            dot = Session.StateBrush(SessionState.Stuck);
            compactW = 240;
        }
        else if (now < volumeUntil && volume >= 0)
        {
            title = muted || volume < 0.005f ? "Muted" : "Volume";
            meta = muted ? "" : $"{(int)Math.Round(volume * 100)}%";
            dot = new SolidColorBrush(theme.Fg);
            headerBar = muted ? 0 : volume;
            compactW = 230;
        }
        else if (now < flashUntil)
        {
            title = flashTitle;
            meta = flashMeta;
            dot = flashBrush;
            appIcon = flashIcon;
            compactW = Math.Clamp(110 + (flashTitle.Length + flashMeta.Length) * 7.5, 200, 400);
        }
        else if (working.Count > 0)
        {
            var w = working[0];
            title = working.Count > 1 ? $"Working · {working.Count} sessions" : $"{w.Project} · {(string.IsNullOrEmpty(w.Tool) ? "Working" : w.Tool)}";
            meta = Session.Fmt(now - w.Started);
            dot = Session.StateBrush(SessionState.Working);
            wave = WaveMode.Activity;
            waveBrush = Session.StateBrush(SessionState.Working);
            compactW = 290;
        }
        else if (done != null)
        {
            title = $"Done · {done.Project}";
            dot = Session.StateBrush(SessionState.Done);
            compactW = 220;
        }
        else if (downloadsActive.Count > 0)
        {
            var dl = downloadsActive;
            double speed = dl.Sum(x => x.Speed);
            title = dl.Count == 1 ? Shorten(dl[0].Name, 24) : $"{dl.Count} downloads";
            meta = speed >= 1024 ? $"{DownloadWatcher.FormatSize(speed)}/s" : DownloadWatcher.FormatSize(dl.Sum(x => x.Bytes));
            dot = new SolidColorBrush(Color.FromRgb(0x54, 0xA0, 0xFF));
            wave = WaveMode.Activity;
            waveBrush = new SolidColorBrush(Color.FromRgb(0x54, 0xA0, 0xFF));
            downloading = true;
            compactW = 310;
        }
        else if (timerEnd is DateTime te)
        {
            var left = te - now;
            title = $"{(int)left.TotalMinutes:00}:{left.Seconds:00}";
            meta = "Timer";
            dot = new SolidColorBrush(theme.Accent);
            headerBar = Math.Clamp(1 - left.TotalSeconds / timerTotal.TotalSeconds, 0, 1);
            compactW = 190;
        }
        else if ((now - timerDoneAt).TotalSeconds < 8)
        {
            title = "Timer done";
            dot = Session.StateBrush(SessionState.Done);
            compactW = 170;
        }
        else if (showMediaFeature && media.Playing)
        {
            // Compact: album art left, live waveform right (no text). Hovered: track name too.
            title = expanded ? (string.IsNullOrEmpty(media.Artist) ? media.Title : $"{media.Title} · {media.Artist}") : "";
            dot = new SolidColorBrush(theme.Fg);
            art = true;
            wave = WaveMode.Audio;
            waveBrush = new SolidColorBrush(theme.IsLight ? theme.Accent : Color.FromRgb(0xB8, 0xC7, 0xE8));
            compactW = 200;
        }
        else
        {
            string bat = s.ShowBattery ? Battery() : "";
            string wx = s.ShowWeather && weather.Available ? weather.Short : "";
            title = s.ShowClock ? now.ToString("HH:mm") : "";
            meta = string.Join("  ", new[] { wx, bat }.Where(x => x != ""));
            dot = Session.StateBrush(SessionState.Idle);
            compactW = (s.ShowClock ? 100 : 56) + (wx != "" ? 70 : 0) + (bat != "" ? 56 : 0);
        }

        // Glow: follows the island's state, the album art, or a fixed colour.
        Color? glow = null;
        bool pulse = false;
        if (asking != null) { glow = Color.FromRgb(0x54, 0xA0, 0xFF); pulse = true; }
        else if (needs != null) glow = Color.FromRgb(0xFE, 0xCA, 0x57);
        else if (stuck != null) glow = Color.FromRgb(0xFF, 0x6B, 0x6B);
        else if (working.Count > 0) { glow = Color.FromRgb(0xFF, 0x9F, 0x43); pulse = true; }
        else if (done != null) glow = Color.FromRgb(0x1D, 0xD1, 0xA1);
        else if (downloadsActive.Count > 0) glow = Color.FromRgb(0x54, 0xA0, 0xFF);
        else if (showMediaFeature && media.Playing) glow = media.ArtColor;
        if (s.GlowMode == "Album" && showMediaFeature && media.Playing) glow = media.ArtColor;
        if (s.GlowMode == "Fixed")
        {
            pulse = pulse && s.GlowPulse;
            glow = ThemeColors.Hex(s.GlowColor, Color.FromRgb(0x4F, 0x6F, 0xA8));
        }
        ApplyGlow(glow, pulse && s.GlowPulse);

        HeaderTitle.Text = title;
        HeaderMeta.Text = meta;
        Dot.Fill = dot;

        bool showMini = art || appIcon != null;
        DlIcon.Visibility = downloading ? Visibility.Visible : Visibility.Collapsed;
        Dot.Visibility = showMini || downloading ? Visibility.Collapsed : Visibility.Visible;
        ArtMini.Visibility = showMini ? Visibility.Visible : Visibility.Collapsed;
        if (appIcon != null)
        {
            if (!ReferenceEquals(ArtMini.Tag, appIcon))
            {
                ArtMini.Tag = appIcon;
                ArtMini.Background = new ImageBrush(appIcon) { Stretch = Stretch.Uniform };
            }
        }
        else if (art) SetArt(ArtMini);

        waveMode = wave;
        Wave.Visibility = wave != WaveMode.None ? Visibility.Visible : Visibility.Collapsed;
        if (wave != WaveMode.None) foreach (var r in barRects) r.Fill = waveBrush;

        HeaderBar.Visibility = headerBar >= 0 ? Visibility.Visible : Visibility.Collapsed;
        if (headerBar >= 0) HeaderScale.ScaleX = headerBar;

        Expanded.Visibility = expanded ? Visibility.Visible : Visibility.Collapsed;
        PermPanel.Visibility = asking != null ? Visibility.Visible : Visibility.Collapsed;
        SessionList.Visibility = asking == null && s.ShowSessions && sessions.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

        if (asking != null)
        {
            PermTool.Text = asking.Tool;
            PermDetail.Text = asking.Detail;
            int more = store.PendingCount - 1;
            PermMore.Text = more > 0 ? $"+{more} more waiting" : "";

            if (!ReferenceEquals(diffShownFor, asking))
            {
                diffShownFor = asking;
                PermDiff.Inlines.Clear();
                foreach (var (kind, text) in asking.Diff)
                {
                    if (PermDiff.Inlines.Count > 0) PermDiff.Inlines.Add(new System.Windows.Documents.LineBreak());
                    PermDiff.Inlines.Add(new System.Windows.Documents.Run((kind == '.' ? "  " : kind + " ") + text)
                    {
                        Foreground = kind == '-' ? new SolidColorBrush(Color.FromRgb(0xE5, 0x5B, 0x5B))
                                   : kind == '+' ? new SolidColorBrush(Color.FromRgb(0x2F, 0xB3, 0x6B))
                                   : new SolidColorBrush(theme.Fg3),
                    });
                }
                PermDiff.Visibility = asking.Diff.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            }
        }

        bool showMedia = expanded && asking == null && showMediaFeature;
        MediaPanel.Visibility = showMedia ? Visibility.Visible : Visibility.Collapsed;
        if (showMedia)
        {
            MediaTitle.Text = media.Title;
            MediaArtist.Text = media.Artist;
            PlayBtn.Content = media.Playing ? "" : "";
            SetArt(Art);
            double p = media.Progress();
            MediaBar.Visibility = p >= 0 ? Visibility.Visible : Visibility.Collapsed;
            if (p >= 0) MediaScale.ScaleX = p;
        }

        bool details = expanded && asking == null;

        // calendar: the next meeting
        var next = s.ShowCalendar ? calendar.Next(now) : null;
        CalendarText.Visibility = details && next != null ? Visibility.Visible : Visibility.Collapsed;
        if (next != null)
            CalendarText.Text = next.Start <= now ? $"Now: {next.Title}" :
                                (next.Start - now).TotalMinutes < 120 ? $"Next: {next.Title} · in {Math.Max(1, (int)Math.Round((next.Start - now).TotalMinutes))} min" :
                                $"Next: {next.Title} · {next.Start:HH:mm}";

        // downloads: active transfers first, then the last couple that finished
        var rows = new List<DownloadRow>();
        if (details && s.ShowDownloads)
        {
            var blue = new SolidColorBrush(Color.FromRgb(0x54, 0xA0, 0xFF));
            var green = new SolidColorBrush(Color.FromRgb(0x1D, 0xD1, 0xA1));
            foreach (var d in downloads.Active)
                rows.Add(new DownloadRow(d.Name, $"{DownloadWatcher.FormatSize(d.Bytes)} · {DownloadWatcher.FormatSize(d.Speed)}/s", d.Path, "", blue));
            foreach (var (item, _) in recentDownloads)
                rows.Add(new DownloadRow(item.Name, $"{DownloadWatcher.FormatSize(item.Size)} · done", item.Path, "", green));
        }
        string sig = string.Join("|", rows.Select(r => r.Name + r.Detail));
        if (sig != lastDownloadSig) { lastDownloadSig = sig; DownloadList.ItemsSource = rows; }
        DownloadPanel.Visibility = rows.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

        WeatherText.Visibility = details && s.ShowWeather && weather.Available ? Visibility.Visible : Visibility.Collapsed;
        if (weather.Available) WeatherText.Text = weather.Long;
        ClipPanel.Visibility = details && s.ShowClipboard && clips.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        QuickList.Visibility = details && s.ShowQuickLaunch && QuickList.Items.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

        TimerPanel.Visibility = details && s.ShowTimer ? Visibility.Visible : Visibility.Collapsed;
        TimerCancel.Visibility = timerEnd != null ? Visibility.Visible : Visibility.Collapsed;

        StatsText.Visibility = details && s.ShowStats ? Visibility.Visible : Visibility.Collapsed;
        if (details && s.ShowStats)
        {
            string bat = s.ShowBattery ? Battery() : "";
            StatsText.Text = $"CPU {NativeMethods.CpuPercent()}%   ·   RAM {NativeMethods.MemoryPercent()}%" +
                             (bat == "" ? "" : $"   ·   Battery {bat}");
        }

        HandleLine.Opacity = pinned ? 1 : 0.6;
        Fit(asking != null ? 440 : expanded ? 400 : compactW);
    }

    // ---------------------------------------------------------------- glow

    Color? curGlow;
    bool curPulse;

    void ApplyGlow(Color? color, bool pulse)
    {
        var s = AppSettings.Current;
        if (!s.Glow) color = null;
        if (color == curGlow && pulse == curPulse) return;
        curGlow = color;
        curPulse = pulse;
        GlowFx.BlurRadius = Math.Clamp(s.GlowBlur, 8, 90);

        if (color is not Color c)
        {
            GlowFx.BeginAnimation(DropShadowEffect.OpacityProperty, new DoubleAnimation(0, TimeSpan.FromMilliseconds(350)));
            return;
        }

        double level = Math.Clamp(s.GlowIntensity, 0.05, 1);
        GlowFx.BeginAnimation(DropShadowEffect.ColorProperty, new ColorAnimation(c, TimeSpan.FromMilliseconds(500)));
        if (pulse)
            GlowFx.BeginAnimation(DropShadowEffect.OpacityProperty,
                new DoubleAnimation(level * 0.45, Math.Min(1, level * 1.15), TimeSpan.FromMilliseconds(1300))
                {
                    AutoReverse = true,
                    RepeatBehavior = RepeatBehavior.Forever,
                    EasingFunction = new SineEase(),
                });
        else
            GlowFx.BeginAnimation(DropShadowEffect.OpacityProperty, new DoubleAnimation(level, TimeSpan.FromMilliseconds(450)));
    }

    // ---------------------------------------------------------------- helpers

    void SetArt(Border target)
    {
        if (media.Art != null)
        {
            if (!ReferenceEquals(target.Tag, media.Art))
            {
                target.Tag = media.Art;
                target.Background = new ImageBrush(media.Art) { Stretch = Stretch.UniformToFill };
                target.Child = null;
            }
        }
        else if (target.Tag != null || target.Background == null)
        {
            target.Tag = null;
            target.Background = new SolidColorBrush(theme.Track);
        }
    }

    static string Battery()
    {
        try
        {
            var p = System.Windows.Forms.SystemInformation.PowerStatus;
            if (p.BatteryLifePercent >= 1.01f || p.BatteryChargeStatus == System.Windows.Forms.BatteryChargeStatus.NoSystemBattery) return "";
            string charge = p.PowerLineStatus == System.Windows.Forms.PowerLineStatus.Online ? "⚡" : "";
            return $"{charge}{(int)Math.Round(p.BatteryLifePercent * 100)}%";
        }
        catch { return ""; }
    }

    void Fit(double w)
    {
        Body.Measure(new Size(w, double.PositiveInfinity));
        double h = Math.Max(36, Math.Ceiling(Body.DesiredSize.Height));
        Apply(w, h);

        // Item templates only report their real height after a layout pass; re-check once it has run.
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
        {
            Body.Measure(new Size(lastW, double.PositiveInfinity));
            Apply(lastW, Math.Max(36, Math.Ceiling(Body.DesiredSize.Height)));
        }));
    }

    void Apply(double w, double h)
    {
        if (Math.Abs(w - lastW) < 0.5 && Math.Abs(h - lastH) < 0.5) return;
        lastW = w;
        lastH = h;

        var spring = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.28 };
        var smooth = new CubicEase { EasingMode = EasingMode.EaseOut };
        var dw = TimeSpan.FromMilliseconds(400);
        var dh = TimeSpan.FromMilliseconds(340);

        Notch.BeginAnimation(NotchShape.BodyWidthProperty, new DoubleAnimation(w, dw) { EasingFunction = spring });
        Host.BeginAnimation(WidthProperty, new DoubleAnimation(w, dw) { EasingFunction = spring });
        Notch.BeginAnimation(NotchShape.BodyHeightProperty, new DoubleAnimation(h, dh) { EasingFunction = smooth });
        Host.BeginAnimation(HeightProperty, new DoubleAnimation(h, dh) { EasingFunction = smooth });
    }

    // ---------------------------------------------------------------- short notices

    void Flash(string title, string meta, Brush dot, ImageSource? icon, double seconds)
    {
        flashTitle = title;
        flashMeta = meta;
        flashBrush = dot;
        flashIcon = icon;
        flashUntil = DateTime.Now.AddSeconds(seconds);
        Refresh();
    }

    void CheckBattery()
    {
        if (!AppSettings.Current.ShowBattery) return;
        try
        {
            var ps = System.Windows.Forms.SystemInformation.PowerStatus;
            if (ps.BatteryChargeStatus == System.Windows.Forms.BatteryChargeStatus.NoSystemBattery || ps.BatteryLifePercent > 1.0f) return;
            int pct = (int)Math.Round(ps.BatteryLifePercent * 100);
            bool online = ps.PowerLineStatus == System.Windows.Forms.PowerLineStatus.Online;

            if (lastOnline == false && online) Flash("Charging", $"{pct}%", Session.StateBrush(SessionState.Done), null, 3);
            else if (lastOnline == true && !online) Flash("On battery", $"{pct}%", Session.StateBrush(SessionState.Idle), null, 3);
            if (!online && pct <= 20 && !lowBatteryWarned)
            {
                lowBatteryWarned = true;
                Flash("Battery low", $"{pct}%", Session.StateBrush(SessionState.Stuck), null, 6);
            }
            if (online || pct > 25) lowBatteryWarned = false;
            lastOnline = online;
        }
        catch { }
    }

    /// A heads-up ten minutes before the next meeting, and again at one minute.
    void CheckCalendar()
    {
        if (!AppSettings.Current.ShowCalendar) return;
        var next = calendar.Next(DateTime.Now);
        if (next == null) return;
        double mins = (next.Start - DateTime.Now).TotalMinutes;
        string key10 = next.Title + next.Start.ToString("s") + "|10", key1 = next.Title + next.Start.ToString("s") + "|1";
        if (mins <= 1 && mins > -1 && calendarNotified.Add(key1)) { Flash(Shorten(next.Title, 28), "starting now", new SolidColorBrush(theme.Accent), null, 6); SessionStore.Play("ask"); }
        else if (mins <= 10 && mins > 1 && calendarNotified.Add(key10)) Flash(Shorten(next.Title, 28), $"in {(int)Math.Round(mins)} min", new SolidColorBrush(theme.Accent), null, 6);
    }

    // ---------------------------------------------------------------- clipboard

    IntPtr WndProc(IntPtr h, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == 0x031D) Dispatcher.BeginInvoke(new Action(() => _ = OnClipboard()));   // WM_CLIPBOARDUPDATE
        return IntPtr.Zero;
    }

    async Task OnClipboard()
    {
        if (!AppSettings.Current.ShowClipboard || ignoreClipboard) return;
        await Task.Delay(80);   // the source app may still hold the clipboard
        try
        {
            // Password managers mark their copies; never show or keep those.
            if (Clipboard.ContainsData("ExcludeClipboardContentFromMonitorProcessing")) return;
            if (Clipboard.ContainsText())
            {
                string text = Clipboard.GetText();
                if (string.IsNullOrWhiteSpace(text)) return;
                var existing = clips.FirstOrDefault(c => c.Text == text);
                if (existing != null) clips.Remove(existing);
                clips.Insert(0, new ClipItem(text));
                while (clips.Count > 4) clips.RemoveAt(clips.Count - 1);
                Flash("Copied", Shorten(new ClipItem(text).Preview, 28), Session.StateBrush(SessionState.Done), null, 2);
            }
            else if (Clipboard.ContainsImage())
                Flash("Image copied", "", Session.StateBrush(SessionState.Done), null, 2);
        }
        catch { }
    }

    void Clip_Click(object sender, MouseButtonEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not ClipItem item) return;
        try
        {
            ignoreClipboard = true;
            Clipboard.SetText(item.Text);
            Flash("Copied again", "", Session.StateBrush(SessionState.Done), null, 1.5);
        }
        catch { }
        finally { _ = Task.Delay(600).ContinueWith(_ => ignoreClipboard = false); }
    }

    void Download_Click(object sender, MouseButtonEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not DownloadRow row || !File.Exists(row.Path)) return;
        try { Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{row.Path}\"") { UseShellExecute = true }); } catch { }
    }

    // ---------------------------------------------------------------- quick launch

    void ReloadQuickLaunch()
    {
        var list = new List<QuickItem>();
        foreach (var raw in AppSettings.Current.QuickLaunch)
        {
            string path = Environment.ExpandEnvironmentVariables((raw ?? "").Trim());
            if (path == "" || !File.Exists(path)) continue;
            list.Add(new QuickItem(System.IO.Path.GetFileNameWithoutExtension(path), path, AppWatcher.LoadIcon(path)));
        }
        QuickList.ItemsSource = list;
    }

    void Quick_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not QuickItem q) return;
        try { Process.Start(new ProcessStartInfo(q.Path) { UseShellExecute = true }); } catch { }
    }

    // ---------------------------------------------------------------- scroll on the island to change volume

    void Root_MouseWheel(object sender, MouseWheelEventArgs e)
    {
        try
        {
            audioEnum ??= new MMDeviceEnumerator();
            audioDev ??= audioEnum.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
            var v = audioDev.AudioEndpointVolume;
            v.MasterVolumeLevelScalar = Math.Clamp(v.MasterVolumeLevelScalar + (e.Delta > 0 ? 0.02f : -0.02f), 0f, 1f);
            e.Handled = true;
        }
        catch { audioDev = null; }
    }

    // ---------------------------------------------------------------- hover / pin

    void Root_MouseEnter(object sender, MouseEventArgs e) { leaveDelay.Stop(); hovered = true; Refresh(); }
    void Root_MouseLeave(object sender, MouseEventArgs e) { leaveDelay.Stop(); leaveDelay.Start(); }
    void Handle_Click(object sender, MouseButtonEventArgs e) { pinned = !pinned; if (!pinned) hovered = false; Refresh(); }

    // ---- permission buttons ----
    void Allow_Click(object s, RoutedEventArgs e) => Decide(Decision.Allow);
    void Always_Click(object s, RoutedEventArgs e) => Decide(Decision.AlwaysAllow);
    void Deny_Click(object s, RoutedEventArgs e) => Decide(Decision.Deny);
    void Decide(Decision d) { if (store.FirstPending() is { } r) store.Decide(r, d); }

    // ---- session rows ----
    void Row_Click(object sender, MouseButtonEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is Session s) NativeMethods.Focus(s.Hwnd);
    }

    // ---- media ----
    void Prev_Click(object s, RoutedEventArgs e) => media.Previous();
    void PlayPause_Click(object s, RoutedEventArgs e) => media.PlayPause();
    void Next_Click(object s, RoutedEventArgs e) => media.Next();

    // ---- timer ----
    void Timer_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string t } && int.TryParse(t, out int min))
        {
            timerTotal = TimeSpan.FromMinutes(min);
            timerEnd = DateTime.Now + timerTotal;
            Refresh();
        }
    }
    void TimerCancel_Click(object s, RoutedEventArgs e) { timerEnd = null; Refresh(); }
}
