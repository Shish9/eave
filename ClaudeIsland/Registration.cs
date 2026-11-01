using System;
using System.Diagnostics;
using System.IO;
using Microsoft.Win32;

namespace ClaudeIsland;

/// Install-time registration (--install / --register) and removal (--uninstall).
public static class Registration
{
    const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    const string UninstallRoot = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\";

    /// The folder this copy of the app is running from.
    public static string InstallDir => Path.GetDirectoryName(Environment.ProcessPath)!;

    static string Programs => Environment.GetFolderPath(Environment.SpecialFolder.Programs);
    static string ShortcutPath => Path.Combine(Programs, "Eave.lnk");
    static string StudioShortcutPath => Path.Combine(Programs, "Eave Studio.lnk");

    static void StopOtherInstances()
    {
        foreach (var name in new[] { Brand.ProcessName, Brand.LegacyProcessName })
            foreach (var p in Process.GetProcessesByName(name))
            {
                if (p.Id == Environment.ProcessId) continue;
                try { p.Kill(); p.WaitForExit(5000); } catch { }
            }
    }

    /// Copies the app and relay to %LOCALAPPDATA%\Eave, registers that copy and starts it.
    public static void InstallSelf()
    {
        string target = Brand.InstallDir;
        string self = Environment.ProcessPath!;
        if (string.Equals(Path.GetDirectoryName(self), target, StringComparison.OrdinalIgnoreCase)) { Register(); return; }

        StopOtherInstances();
        Directory.CreateDirectory(target);
        string newExe = Path.Combine(target, Brand.ExeName);
        File.Copy(self, newExe, true);
        File.Copy(Path.Combine(InstallDir, "relay.exe"), Path.Combine(target, "relay.exe"), true);

        Process.Start(new ProcessStartInfo(newExe, "--register") { UseShellExecute = false, CreateNoWindow = true })!
            .WaitForExit(30000);
        Process.Start(new ProcessStartInfo(newExe) { UseShellExecute = false });
    }

    public static void Register()
    {
        string exe = Environment.ProcessPath!;
        RemoveLegacy();

        using (var k = Registry.CurrentUser.CreateSubKey(RunKey)) k.SetValue(Brand.Name, $"\"{exe}\"");

        using (var k = Registry.CurrentUser.CreateSubKey(UninstallRoot + Brand.Name))
        {
            k.SetValue("DisplayName", Brand.Name);
            k.SetValue("DisplayVersion", Brand.Version);
            k.SetValue("Publisher", Brand.Publisher);
            k.SetValue("InstallLocation", InstallDir);
            k.SetValue("DisplayIcon", exe);
            k.SetValue("UninstallString", $"\"{exe}\" --uninstall");
            k.SetValue("NoModify", 1, RegistryValueKind.DWord);
            k.SetValue("NoRepair", 1, RegistryValueKind.DWord);
        }

        try
        {
            dynamic shell = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell")!)!;
            dynamic lnk = shell.CreateShortcut(ShortcutPath);
            lnk.TargetPath = exe;
            lnk.WorkingDirectory = InstallDir;
            lnk.Save();

            dynamic studio = shell.CreateShortcut(StudioShortcutPath);
            studio.TargetPath = exe;
            studio.Arguments = "--studio";
            studio.WorkingDirectory = InstallDir;
            studio.Description = "Customize Eave";
            studio.Save();
        }
        catch { }

        HookInstaller.Apply(false);
    }

    /// Removes what the app installed under its old name ("Claude Island"). Settings were copied over by Brand.MigrateData.
    static void RemoveLegacy()
    {
        try { using var k = Registry.CurrentUser.OpenSubKey(RunKey, true); k?.DeleteValue(Brand.LegacyProcessName, false); } catch { }
        try { Registry.CurrentUser.DeleteSubKey(UninstallRoot + Brand.LegacyProcessName, false); } catch { }
        try { File.Delete(Path.Combine(Programs, "Claude Island.lnk")); } catch { }
        try { File.Delete(Path.Combine(Programs, "Island Studio.lnk")); } catch { }
        try
        {
            if (Directory.Exists(Brand.LegacyInstallDir) &&
                !string.Equals(Brand.LegacyInstallDir, InstallDir, StringComparison.OrdinalIgnoreCase))
            {
                foreach (var p in Process.GetProcessesByName(Brand.LegacyProcessName)) { try { p.Kill(); p.WaitForExit(3000); } catch { } }
                Directory.Delete(Brand.LegacyInstallDir, true);
            }
        }
        catch { }
    }

    public static void Unregister()
    {
        try { HookInstaller.Apply(true); } catch { }
        try { using var k = Registry.CurrentUser.OpenSubKey(RunKey, true); k?.DeleteValue(Brand.Name, false); } catch { }
        try { Registry.CurrentUser.DeleteSubKey(UninstallRoot + Brand.Name, false); } catch { }
        try { File.Delete(ShortcutPath); } catch { }
        try { File.Delete(StudioShortcutPath); } catch { }
        RemoveLegacy();
    }

    /// Stops other running instances, then deletes the install folder once this process has exited.
    public static void Uninstall()
    {
        Unregister();
        StopOtherInstances();
        Process.Start(new ProcessStartInfo("cmd.exe", $"/c ping -n 4 127.0.0.1 >nul & rmdir /s /q \"{InstallDir}\"")
        {
            CreateNoWindow = true,
            UseShellExecute = false,
        });
    }
}
