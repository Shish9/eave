using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;

namespace ClaudeIsland;

/// "Eave Studio": every look-and-feel and content option, applied live to the real island.
public partial class SettingsWindow : Window
{
    const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

    readonly MainWindow main;
    readonly Dictionary<string, StackPanel> pages = new();
    readonly DispatcherTimer saveSoon = new() { Interval = TimeSpan.FromMilliseconds(180) };
    readonly DispatcherTimer statusTimer = new() { Interval = TimeSpan.FromSeconds(1.5) };
    readonly List<Action> statusUpdaters = new();
    int groupCounter;

    AppSettings S => AppSettings.Current;

    public SettingsWindow(MainWindow main)
    {
        InitializeComponent();
        this.main = main;

        saveSoon.Tick += (_, _) => { saveSoon.Stop(); Commit(); };
        statusTimer.Tick += (_, _) => { foreach (var u in statusUpdaters) u(); };

        BuildDesign();
        BuildTheme();
        BuildGlow();
        BuildContents();
        BuildConnections();
        BuildBehavior();
        ShowPage("Design");
        statusTimer.Start();
        PinBox.IsChecked = main.IsPinned;
    }

    protected override void OnClosed(EventArgs e)
    {
        saveSoon.Stop();
        statusTimer.Stop();
        main.SetPinned(false);
        Commit();
        base.OnClosed(e);
    }

    void Commit() { AppSettings.Save(); main.SettingsChanged(); }
    void CommitSoon() { saveSoon.Stop(); saveSoon.Start(); }

    void Pin_Click(object sender, RoutedEventArgs e) => main.SetPinned(PinBox.IsChecked == true);
    void TestFlash_Click(object sender, RoutedEventArgs e) => main.TestFlash();

    // ---------------------------------------------------------------- page plumbing

    StackPanel NewPage(string name, string title, string subtitle)
    {
        var p = new StackPanel { Visibility = Visibility.Collapsed };
        p.Children.Add(new TextBlock { Text = title, FontSize = 22, FontWeight = FontWeights.SemiBold });
        p.Children.Add(new TextBlock { Text = subtitle, Foreground = (Brush)FindResource("Hint"), Margin = new Thickness(0, 4, 0, 14), TextWrapping = TextWrapping.Wrap });
        PageHost.Children.Add(p);
        pages[name] = p;

        var nav = new RadioButton { Content = name, Style = (Style)FindResource("NavStyle"), GroupName = "nav" };
        nav.Checked += (_, _) => ShowPage(name);
        NavPanel.Children.Add(nav);
        return p;
    }

    void ShowPage(string name)
    {
        foreach (var kv in pages) kv.Value.Visibility = kv.Key == name ? Visibility.Visible : Visibility.Collapsed;
        foreach (RadioButton rb in NavPanel.Children) rb.IsChecked = (string)rb.Content == name;
    }

    TextBlock Heading(StackPanel p, string text)
    {
        var t = new TextBlock { Text = text, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 22, 0, 4) };
        p.Children.Add(t);
        return t;
    }

    TextBlock Hint(string text) => new()
    {
        Text = text, Foreground = (Brush)FindResource("Hint"), FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 3, 0, 0),
    };

    CheckBox Toggle(StackPanel p, string label, string? hint, Func<bool> get, Action<bool> set)
    {
        var cb = new CheckBox { Content = label, IsChecked = get(), Style = (Style)FindResource("SwitchStyle"), Margin = new Thickness(0, 12, 0, 0) };
        cb.Click += (_, _) => { set(cb.IsChecked == true); Commit(); };
        p.Children.Add(cb);
        if (hint != null) p.Children.Add(Hint(hint));
        return cb;
    }

    void Choice(StackPanel p, string label, string[] options, Func<string> get, Action<string> set, string? hint = null)
    {
        p.Children.Add(new TextBlock { Text = label, Margin = new Thickness(0, 14, 0, 8) });
        var wrap = new WrapPanel();
        string group = "choice" + groupCounter++;
        foreach (var o in options)
        {
            var rb = new RadioButton { Content = o, Style = (Style)FindResource("ChipStyle"), GroupName = group, IsChecked = get() == o };
            rb.Checked += (_, _) => { set(o); Commit(); };
            wrap.Children.Add(rb);
        }
        p.Children.Add(wrap);
        if (hint != null) p.Children.Add(Hint(hint));
    }

    void SliderRow(StackPanel p, string label, double min, double max, Func<double> get, Action<double> set, string fmt = "0")
    {
        var g = new Grid { Margin = new Thickness(0, 12, 0, 0) };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(190) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(60) });
        g.Children.Add(new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center });
        var value = new TextBlock { Text = get().ToString(fmt), HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center, Foreground = (Brush)FindResource("Hint") };
        var sl = new Slider { Minimum = min, Maximum = max, Value = get(), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 8, 0) };
        sl.ValueChanged += (_, e) => { value.Text = e.NewValue.ToString(fmt); set(e.NewValue); CommitSoon(); };
        Grid.SetColumn(sl, 1);
        Grid.SetColumn(value, 2);
        g.Children.Add(sl);
        g.Children.Add(value);
        p.Children.Add(g);
    }

    /// Row of colour swatches plus a hex box.
    void ColorRow(StackPanel p, string label, Func<string> get, Action<string> set, string[] swatches)
    {
        p.Children.Add(new TextBlock { Text = label, Margin = new Thickness(0, 14, 0, 8) });
        var row = new WrapPanel { VerticalAlignment = VerticalAlignment.Center };
        var box = new TextBox { Text = get(), Width = 100, Margin = new Thickness(6, 0, 0, 8) };
        var chosen = new Border { Width = 28, Height = 28, CornerRadius = new CornerRadius(14), Margin = new Thickness(0, 0, 8, 8), BorderBrush = Brushes.White, BorderThickness = new Thickness(1.5) };
        void Paint() => chosen.Background = new SolidColorBrush(ThemeColors.Hex(get(), Colors.Gray));
        Paint();
        row.Children.Add(chosen);
        foreach (var hex in swatches)
        {
            var sw = new Border
            {
                Width = 24, Height = 24, CornerRadius = new CornerRadius(12), Margin = new Thickness(0, 2, 8, 8), Cursor = System.Windows.Input.Cursors.Hand,
                Background = new SolidColorBrush(ThemeColors.Hex(hex, Colors.Gray)), ToolTip = hex,
            };
            sw.MouseLeftButtonUp += (_, _) => { set(hex); box.Text = hex; Paint(); Commit(); };
            row.Children.Add(sw);
        }
        box.TextChanged += (_, _) =>
        {
            var t = box.Text.Trim();
            if (t.StartsWith('#') && (t.Length == 7 || t.Length == 9)) { set(t); Paint(); CommitSoon(); }
        };
        row.Children.Add(box);
        p.Children.Add(row);
    }

    TextBox TextRow(StackPanel p, string label, string? hint, Func<string> get, Action<string> set, bool multiline = false)
    {
        p.Children.Add(new TextBlock { Text = label, Margin = new Thickness(0, 14, 0, 6) });
        var tb = new TextBox { Text = get() };
        if (multiline) { tb.AcceptsReturn = true; tb.Height = 96; tb.VerticalScrollBarVisibility = ScrollBarVisibility.Auto; tb.TextWrapping = TextWrapping.NoWrap; }
        tb.LostFocus += (_, _) => { set(tb.Text.Trim()); Commit(); };
        p.Children.Add(tb);
        if (hint != null) p.Children.Add(Hint(hint));
        return tb;
    }

    TextBlock StatusLine(StackPanel p, Func<string> text)
    {
        var t = new TextBlock { Foreground = (Brush)FindResource("Hint"), FontSize = 12, Margin = new Thickness(0, 6, 0, 0), TextWrapping = TextWrapping.Wrap };
        statusUpdaters.Add(() => t.Text = text());
        t.Text = text();
        p.Children.Add(t);
        return t;
    }

    // ---------------------------------------------------------------- pages

    void BuildDesign()
    {
        var p = NewPage("Design", "Design", "The shape, size and position of the island.");
        Choice(p, "Shape", new[] { "Notch", "Pill" }, () => S.Shape, v => S.Shape = v,
               "Notch hangs from the top edge of the screen. Pill floats just below it.");
        SliderRow(p, "Notch corner curve", 4, 28, () => S.Flare, v => S.Flare = v);
        Choice(p, "Size", new[] { "Compact", "Normal", "Large" }, () => S.Size, v => S.Size = v);
        SliderRow(p, "Position (left / right)", -500, 500, () => S.OffsetX, v => S.OffsetX = Math.Round(v));
        SliderRow(p, "Opacity", 0.4, 1, () => S.IslandOpacity, v => S.IslandOpacity = Math.Round(v, 2), "0.00");

        var screens = System.Windows.Forms.Screen.AllScreens;
        if (screens.Length > 1)
        {
            var names = new List<string> { "Primary" };
            for (int i = 0; i < screens.Length; i++) names.Add($"Monitor {i + 1}");
            Choice(p, "Monitor", names.ToArray(),
                   () => S.Monitor >= 0 && S.Monitor < screens.Length ? names[S.Monitor + 1] : "Primary",
                   v => S.Monitor = names.IndexOf(v) - 1);
        }
    }

    void BuildTheme()
    {
        var p = NewPage("Theme", "Theme", "Colours for the island's background and text.");
        p.Children.Add(new TextBlock { Text = "Preset", Margin = new Thickness(0, 14, 0, 8) });
        var wrap = new WrapPanel();
        var custom = new StackPanel { Visibility = S.Theme == "Custom" ? Visibility.Visible : Visibility.Collapsed };

        foreach (var name in ThemeColors.Presets)
        {
            var colors = ThemeColors.For(new AppSettings { Theme = name, CustomBg = S.CustomBg, CustomText = S.CustomText, CustomAccent = S.CustomAccent });
            var swatch = new Border
            {
                Width = 26, Height = 26, CornerRadius = new CornerRadius(8), Background = new SolidColorBrush(colors.Bg),
                BorderBrush = new SolidColorBrush(Color.FromArgb(0x66, 0x88, 0x88, 0x99)), BorderThickness = new Thickness(1), Margin = new Thickness(0, 0, 8, 0),
                Child = new Border { Width = 9, Height = 9, CornerRadius = new CornerRadius(5), Background = new SolidColorBrush(colors.Accent), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
            };
            var content = new StackPanel { Orientation = Orientation.Horizontal };
            content.Children.Add(swatch);
            content.Children.Add(new TextBlock { Text = name, VerticalAlignment = VerticalAlignment.Center });
            var rb = new RadioButton { Content = content, Style = (Style)FindResource("ChipStyle"), GroupName = "theme", IsChecked = S.Theme == name };
            rb.Checked += (_, _) => { S.Theme = name; custom.Visibility = name == "Custom" ? Visibility.Visible : Visibility.Collapsed; Commit(); };
            wrap.Children.Add(rb);
        }
        p.Children.Add(wrap);

        Heading(custom, "Custom colours");
        ColorRow(custom, "Background", () => S.CustomBg, v => S.CustomBg = v, new[] { "#000000", "#0B0B0F", "#0A1022", "#1A1210", "#102016", "#201028", "#F4F4F7" });
        ColorRow(custom, "Text", () => S.CustomText, v => S.CustomText = v, new[] { "#FFFFFF", "#EAF0FF", "#F7EDE6", "#E4FFE9", "#111113" });
        ColorRow(custom, "Accent (buttons, progress)", () => S.CustomAccent, v => S.CustomAccent = v,
                 new[] { "#2E86FF", "#5B8CFF", "#D97757", "#1DD1A1", "#BB9AF7", "#FF6B9A", "#FECA57" });
        p.Children.Add(custom);
    }

    void BuildGlow()
    {
        var p = NewPage("Glow", "Glow", "The soft light around the island.");
        Toggle(p, "Glow", null, () => S.Glow, v => S.Glow = v);
        Choice(p, "Colour", new[] { "State", "Album", "Fixed" }, () => S.GlowMode, v => S.GlowMode = v,
               "State: orange while Claude works, blue on a permission prompt, green when done. " +
               "Album: the colour of the cover art while music plays. Fixed: always your colour, even when idle.");
        ColorRow(p, "Fixed colour", () => S.GlowColor, v => S.GlowColor = v,
                 new[] { "#4F6FA8", "#5B8CFF", "#BB9AF7", "#FF6B9A", "#FF9F43", "#1DD1A1", "#FFFFFF" });
        SliderRow(p, "Strength", 0.1, 1, () => S.GlowIntensity, v => S.GlowIntensity = Math.Round(v, 2), "0.00");
        SliderRow(p, "Spread", 10, 80, () => S.GlowBlur, v => S.GlowBlur = Math.Round(v));
        Toggle(p, "Pulse while working", "A slow breathing effect while Claude works or asks for permission.", () => S.GlowPulse, v => S.GlowPulse = v);
    }

    void BuildContents()
    {
        var p = NewPage("Contents", "Contents", "Choose what the island shows.");
        Heading(p, "Always visible");
        Toggle(p, "Clock", null, () => S.ShowClock, v => S.ShowClock = v);
        Toggle(p, "Battery", "In the idle bar, plus charging and low-battery notices.", () => S.ShowBattery, v => S.ShowBattery = v);

        Heading(p, "Claude Code");
        Toggle(p, "Sessions list", "Each running Claude Code session with its status.", () => S.ShowSessions, v => S.ShowSessions = v);

        Heading(p, "Widgets");
        Toggle(p, "Now playing", "Album art, controls, and the waveform while music plays.", () => S.ShowMedia, v => S.ShowMedia = v);
        Toggle(p, "Weather", "Needs a city under Connections.", () => S.ShowWeather, v => S.ShowWeather = v);
        Toggle(p, "Next meeting", "Needs a calendar link under Connections.", () => S.ShowCalendar, v => S.ShowCalendar = v);
        Toggle(p, "Clipboard", "What you copy, kept in memory only. Password-manager copies are skipped.", () => S.ShowClipboard, v => S.ShowClipboard = v);
        Toggle(p, "Downloads", "Watches your Downloads folder for in-progress browser downloads.", () => S.ShowDownloads, v => S.ShowDownloads = v);
        Toggle(p, "Timer", null, () => S.ShowTimer, v => S.ShowTimer = v);
        Toggle(p, "System stats", "CPU, memory and battery.", () => S.ShowStats, v => S.ShowStats = v);
        Toggle(p, "App closed notices", "A short \"Closed · app\" notice when an app's window goes away.", () => S.ShowAppClosed, v => S.ShowAppClosed = v);

        Heading(p, "Quick launch");
        Toggle(p, "Quick-launch row", null, () => S.ShowQuickLaunch, v => S.ShowQuickLaunch = v);
        TextRow(p, "Apps (one full path per line)", "Programs that don't exist are skipped.",
                () => string.Join(Environment.NewLine, S.QuickLaunch),
                v => S.QuickLaunch = v.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).Select(l => l.Trim()).Where(l => l.Length > 0).ToList(),
                multiline: true);
    }

    void BuildConnections()
    {
        var p = NewPage("Connections", "Connections", "Optional services. Nothing is contacted until you fill these in.");

        Heading(p, "Weather");
        TextRow(p, "City", "Looked up on open-meteo.com (free, no account). Only the city name and its coordinates are sent.",
                () => S.WeatherCity, v => S.WeatherCity = v);
        Toggle(p, "Use Fahrenheit", null, () => S.Fahrenheit, v => S.Fahrenheit = v);
        StatusLine(p, () => string.IsNullOrWhiteSpace(S.WeatherCity) ? "Weather is off." :
                            main.Weather.Available ? main.Weather.Long :
                            main.Weather.Error != "" ? main.Weather.Error : "Looking up the weather...");

        Heading(p, "Calendar (next meeting)");
        TextRow(p, "Private calendar link (.ics)",
                "Google Calendar: Settings, your calendar, \"Secret address in iCal format\". Outlook: publish the calendar and copy the ICS link. " +
                "No sign-in needed. Treat the link like a password: it is stored in your local settings file only.",
                () => S.CalendarUrl, v => S.CalendarUrl = v);
        StatusLine(p, () => string.IsNullOrWhiteSpace(S.CalendarUrl) ? "Calendar is off." : main.Calendar.Status == "" ? "Loading..." : main.Calendar.Status);

        Heading(p, "Notifications from other apps");
        Toggle(p, "Show Windows notifications in the island",
               "Experimental. Windows asks for permission the first time, and this may not be available for every kind of app on every PC.",
               () => S.ShowNotifications, v => S.ShowNotifications = v);
        StatusLine(p, () => "Status: " + main.Notifications.Status);
        var open = new Button { Content = "Open Windows notification settings", HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 8, 0, 0) };
        open.Click += (_, _) => { try { Process.Start(new ProcessStartInfo("ms-settings:notifications") { UseShellExecute = true }); } catch { } };
        p.Children.Add(open);
    }

    void BuildBehavior()
    {
        var p = NewPage("Behavior", "Behavior", "When the island shows, and how it starts.");

        Choice(p, "Hide when a fullscreen app is in front", new[] { "Never", "Games", "Any" }, () => S.FullscreenMode, v => S.FullscreenMode = v,
               "Never: always visible (recommended). Games: hide only for exclusive-fullscreen games and presentations. Any: hide for any fullscreen app, including some launchers.");
        Toggle(p, "Mute sounds", null, () => S.Mute, v => S.Mute = v);

        bool startup;
        using (var k = Registry.CurrentUser.OpenSubKey(RunKey)) startup = k?.GetValue(Brand.Name) != null;
        Toggle(p, "Start with Windows", null, () => startup, v =>
        {
            try
            {
                using var k = Registry.CurrentUser.CreateSubKey(RunKey);
                if (v) k.SetValue(Brand.Name, $"\"{Environment.ProcessPath}\""); else k.DeleteValue(Brand.Name, false);
                k.DeleteValue(Brand.LegacyProcessName, false);
                startup = v;
            }
            catch { }
        });

        SliderRow(p, "Flag a session as stuck after (seconds)", 30, 600, () => S.StuckSeconds, v => S.StuckSeconds = (int)Math.Round(v));

        Heading(p, "Claude Code hooks");
        p.Children.Add(Hint("These add entries to your Claude Code settings file (~/.claude/settings.json) so Claude Code can report to Eave. " +
                            "A timestamped backup is saved first, and \"Remove hooks\" takes them out again."));
        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 10, 0, 0) };
        var status = new TextBlock { Foreground = (Brush)FindResource("Hint"), Margin = new Thickness(0, 8, 0, 0), TextWrapping = TextWrapping.Wrap, FontSize = 12 };
        status.Text = HookInstaller.Describe(HookInstaller.Check());
        var install = new Button { Content = "Install hooks" };
        var remove = new Button { Content = "Remove hooks", Margin = new Thickness(8, 0, 0, 0) };
        install.Click += (_, _) => { try { status.Text = HookInstaller.Apply(false); } catch (Exception ex) { status.Text = "Failed: " + ex.Message; } };
        remove.Click += (_, _) => { try { status.Text = HookInstaller.Apply(true); } catch (Exception ex) { status.Text = "Failed: " + ex.Message; } };
        row.Children.Add(install);
        row.Children.Add(remove);
        p.Children.Add(row);
        p.Children.Add(status);
    }
}
