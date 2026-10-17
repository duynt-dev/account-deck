using System.Diagnostics;

namespace AccountDeck.Core;

public enum LaunchOutcome
{
    Started,
    BroughtToFront,
}

public sealed class LaunchException(string message) : Exception(message);

/// <summary>Opens an account: focuses its window when it is already running, otherwise starts it.</summary>
public static class Launcher
{
    public static LaunchOutcome Open(AppEntry app, AccountProfile account)
    {
        var exe = AppPresets.ResolveExe(app)
                  ?? throw new LaunchException($"{app.Name} was not found. Set its program path in the app settings.");

        if (InstanceTracker.Scan(new[] { app }).TryGetValue(account.Id, out var pids))
        {
            var window = FindTopWindow(pids);
            if (window != IntPtr.Zero)
            {
                Activate(window);
                return LaunchOutcome.BroughtToFront;
            }
            // Running without a visible window (e.g. minimised to the tray): starting it
            // again normally makes the app's own single-instance logic reveal it.
        }

        Start(app, account, exe);
        return LaunchOutcome.Started;
    }

    /// <summary>The account's data folder with variables expanded, or empty for the default sign-in.</summary>
    public static string DataDirOf(AccountProfile account) => account.IsDefault ? "" : AppPaths.Expand(account.DataDir);

    static void Start(AppEntry app, AccountProfile account, string exe)
    {
        var info = new ProcessStartInfo(exe)
        {
            UseShellExecute = false,
            WorkingDirectory = Path.GetDirectoryName(exe)!,
        };

        var dataDir = DataDirOf(account);
        var arguments = new List<string>();
        if (dataDir.Length > 0)
        {
            Directory.CreateDirectory(dataDir);
            if (!string.IsNullOrWhiteSpace(app.ArgsTemplate)) arguments.Add(Fill(app.ArgsTemplate, dataDir));

            foreach (var (name, template) in app.EnvTemplate)
            {
                var value = Fill(template, dataDir);
                if (AppPaths.IsUnder(value, dataDir)) Directory.CreateDirectory(value);
                info.Environment[name] = value;
            }
        }
        if (!string.IsNullOrWhiteSpace(account.ExtraArgs)) arguments.Add(Fill(account.ExtraArgs, dataDir));
        info.Arguments = string.Join(' ', arguments);

        foreach (var (name, value) in account.Environment)
            info.Environment[name] = Fill(value, dataDir);

        Process process;
        try
        {
            process = Process.Start(info) ?? throw new LaunchException($"{app.Name} did not start.");
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            throw new LaunchException($"Could not start {app.Name}: {ex.Message}");
        }

        using (process) InstanceTracker.Remember(process, account.Id);
    }

    static string Fill(string template, string dataDir) =>
        Environment.ExpandEnvironmentVariables(template.Replace("{dataDir}", dataDir, StringComparison.OrdinalIgnoreCase));

    static IntPtr FindTopWindow(IReadOnlyCollection<int> processIds)
    {
        var ids = processIds.ToHashSet();
        var found = IntPtr.Zero;
        Native.EnumWindows((hWnd, _) =>
        {
            Native.GetWindowThreadProcessId(hWnd, out var pid);
            if (!ids.Contains(pid) || !Native.IsWindowVisible(hWnd)) return true;
            if (Native.GetWindow(hWnd, Native.GwOwner) != IntPtr.Zero || Native.GetWindowTextLength(hWnd) == 0) return true;
            found = hWnd;
            return false;
        }, IntPtr.Zero);
        return found;
    }

    static void Activate(IntPtr window)
    {
        if (Native.IsIconic(window)) Native.ShowWindow(window, Native.SwRestore);
        Native.SetForegroundWindow(window);
    }
}
