using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace AccountDeck.Core;

/// <summary>Desktop shortcuts, start-with-Windows and opening folders.</summary>
public static class SystemIntegration
{
    const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    const string RunValue = "AccountDeck";

    static string SelfPath => Environment.ProcessPath ?? throw new InvalidOperationException("Cannot resolve AccountDeck's own path.");

    public static string ShortcutPath(AppEntry app, AccountProfile account) =>
        Path.Combine(AppPaths.Desktop, AppPaths.SafeFileName($"{app.Name} - {account.Name}") + ".lnk");

    /// <summary>Creates a desktop shortcut that opens the account through AccountDeck.</summary>
    public static string CreateShortcut(AppEntry app, AccountProfile account)
    {
        var path = ShortcutPath(app, account);
        var icon = BadgeIcon.WriteAccountIcon(account);

        var shellType = Type.GetTypeFromProgID("WScript.Shell") ?? throw new InvalidOperationException("WScript.Shell is not available.");
        dynamic shell = Activator.CreateInstance(shellType)!;
        try
        {
            dynamic link = shell.CreateShortcut(path);
            link.TargetPath = SelfPath;
            link.Arguments = $"--open {account.Id}";
            link.WorkingDirectory = Path.GetDirectoryName(SelfPath);
            link.IconLocation = icon + ",0";
            link.Description = $"Open {app.Name} as {account.Name}";
            link.Save();
        }
        finally
        {
            Marshal.FinalReleaseComObject(shell);
        }
        return path;
    }

    public static void DeleteShortcut(AppEntry app, AccountProfile account)
    {
        var path = ShortcutPath(app, account);
        if (File.Exists(path)) File.Delete(path);
    }

    public static bool StartWithWindows
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey);
            return key?.GetValue(RunValue) is string;
        }
        set
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKey);
            if (value) key.SetValue(RunValue, $"\"{SelfPath}\" --tray");
            else key.DeleteValue(RunValue, throwOnMissingValue: false);
        }
    }

    public static void OpenFolder(string folder)
    {
        Directory.CreateDirectory(folder);
        Process.Start(new ProcessStartInfo("explorer.exe") { ArgumentList = { folder }, UseShellExecute = true })?.Dispose();
    }

    public static void OpenUrl(string url) =>
        Process.Start(new ProcessStartInfo(url) { UseShellExecute = true })?.Dispose();

    /// <summary>The folder that physically holds the account's data (handles Store-app redirection).</summary>
    public static string? DataFolderOnDisk(AppEntry app, AccountProfile account)
    {
        var exe = AppPresets.ResolveExe(app);
        if (account.IsDefault)
        {
            if (app.PresetId != "claude" || exe is null) return null;
            var roaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            return ClaudeLocator.OnDiskFolder(Path.Combine(roaming, "Claude"), exe);
        }
        var logical = AppPaths.Expand(account.DataDir);
        return exe is null ? logical : ClaudeLocator.OnDiskFolder(logical, exe);
    }
}
