using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;

namespace ClaudeIsland;

public partial class App : System.Windows.Application
{
    static readonly DateTime started = DateTime.Now;
    static string DataDir => Brand.DataDir;
    static string QuitFlag => System.IO.Path.Combine(DataDir, "quit.flag");

    Mutex? mutex;
    System.Windows.Forms.NotifyIcon? tray;
    HookServer? server;
    SessionStore store = null!;
    MainWindow main = null!;
    SettingsWindow? settingsWindow;

    protected override void OnStartup(StartupEventArgs e)
    {
        Brand.MigrateData();
        // Loaded before the command-line modes too: installing hooks saves settings, and must not save defaults over yours.
        AppSettings.Load();

        // Command-line modes used by Setup.exe and the uninstall entry; they never show UI.
        if (e.Args.Contains("--install"))
        {
            if (e.Args.Contains("--silent") || ConfirmInstall()) Try(Registration.InstallSelf);
            Shutdown();
            return;
        }
        if (e.Args.Contains("--register")) { Try(Registration.Register); Shutdown(); return; }
        if (e.Args.Contains("--uninstall")) { Try(Registration.Uninstall); Shutdown(); return; }
        if (e.Args.Contains("--install-hooks")) { Try(() => HookInstaller.Apply(false)); Shutdown(); return; }
        if (e.Args.Contains("--remove-hooks")) { Try(() => HookInstaller.Apply(true)); Shutdown(); return; }

        // A restart after a crash waits for the dead instance to release the lock.
        bool restarted = e.Args.Contains("--restarted");
        mutex = new Mutex(false, "EaveSingleton");
        bool owned;
        try { owned = mutex.WaitOne(restarted ? 5000 : 0); }
        catch (AbandonedMutexException) { owned = true; }
        bool studio = e.Args.Contains("--studio");
        if (!owned)
        {
            // Already running: ask that instance to open Eave Studio.
            if (studio)
                Try(() =>
                {
                    using var http = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromSeconds(3) };
                    http.PostAsync($"http://127.0.0.1:{HookServer.Port}/studio", new System.Net.Http.StringContent("{}")).Wait();
                });
            Shutdown();
            return;
        }

        base.OnStartup(e);
        Try(() => File.Delete(QuitFlag));

        DispatcherUnhandledException += (_, a) => { Log(a.Exception); a.Handled = true; };
        TaskScheduler.UnobservedTaskException += (_, a) => { Log(a.Exception); a.SetObserved(); };
        AppDomain.CurrentDomain.UnhandledException += (_, a) =>
        {
            Log(a.ExceptionObject as Exception);
            // Come back unless we are crash-looping right after startup.
            if ((DateTime.Now - started).TotalSeconds > 15)
                Try(() => Process.Start(new ProcessStartInfo(Environment.ProcessPath!, "--restarted") { UseShellExecute = false }));
        };

        store = new SessionStore();
        main = new MainWindow(store);
        main.Show();

        try
        {
            server = new HookServer(store, Dispatcher) { OnStudio = OpenSettings };
            server.Start();
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show(
                $"Could not listen on port {HookServer.Port}. Is an older copy (Claude Island) still running?\n\n{ex.Message}",
                Brand.Name);
        }

        tray = new System.Windows.Forms.NotifyIcon
        {
            Icon = AppIcon(),
            Text = Brand.Name,
            Visible = true,
            ContextMenuStrip = BuildMenu(),
        };
        store.Balloon += (t, m) => tray.ShowBalloonTip(4000, t, m, System.Windows.Forms.ToolTipIcon.Info);
        if (studio) OpenSettings();

        // Claude Code only reports to Eave through its hooks: fix them if an update or a deleted folder broke them.
        _ = Task.Run(HookInstaller.Repair);
    }

    static void Try(Action a) { try { a(); } catch { } }

    /// Installing changes the user's system, so say exactly what will change and let them decline.
    static bool ConfirmInstall() => System.Windows.MessageBox.Show(
        "Eave will make these changes for your Windows account:\n\n" +
        "  • copy itself to " + Brand.InstallDir + "\n" +
        "  • start with Windows (a per-user startup entry)\n" +
        "  • add Start menu shortcuts and an entry in Settings > Apps\n" +
        "  • add hooks to your Claude Code settings (~/.claude/settings.json) so it can report to Eave;\n" +
        "     a backup of that file is saved first\n\n" +
        "You can remove all of this later from Settings > Apps.\n\nContinue?",
        "Install Eave", System.Windows.MessageBoxButton.OKCancel, System.Windows.MessageBoxImage.Question)
        == System.Windows.MessageBoxResult.OK;

    /// The exe's own icon (eave.ico, embedded at build time) for the tray.
    static Icon AppIcon()
    {
        try { return Icon.ExtractAssociatedIcon(Environment.ProcessPath!) ?? SystemIcons.Application; }
        catch { return SystemIcons.Application; }
    }

    static void Log(Exception? ex)
    {
        Try(() =>
        {
            Directory.CreateDirectory(DataDir);
            File.AppendAllText(System.IO.Path.Combine(DataDir, "crash.log"), $"[{DateTime.Now:s}] {ex}\n\n");
        });
    }

    System.Windows.Forms.ContextMenuStrip BuildMenu()
    {
        var menu = new System.Windows.Forms.ContextMenuStrip();
        menu.Items.Add("Eave Studio…", null, (_, _) => OpenSettings());
        var mute = new System.Windows.Forms.ToolStripMenuItem("Mute sounds") { Checked = AppSettings.Current.Mute, CheckOnClick = true };
        mute.CheckedChanged += (_, _) => { AppSettings.Current.Mute = mute.Checked; AppSettings.Save(); };
        menu.Items.Add(mute);
        menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
        menu.Items.Add("Quit", null, (_, _) =>
        {
            // Tells the hook relay not to bring the island back until you start it again yourself.
            Try(() => { Directory.CreateDirectory(DataDir); File.WriteAllText(QuitFlag, ""); });
            Shutdown();
        });
        return menu;
    }

    void OpenSettings()
    {
        if (settingsWindow != null) { settingsWindow.WindowState = WindowState.Normal; settingsWindow.Activate(); return; }
        settingsWindow = new SettingsWindow(main);
        settingsWindow.Closed += (_, _) => settingsWindow = null;
        settingsWindow.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        server?.Stop();
        if (tray != null) { tray.Visible = false; tray.Dispose(); }
        try { mutex?.ReleaseMutex(); } catch { }
        base.OnExit(e);
    }
}
