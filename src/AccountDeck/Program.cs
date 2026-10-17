using AccountDeck.Core;
using AccountDeck.UI;

namespace AccountDeck;

internal static class Program
{
    const string ShowSignalName = @"Local\AccountDeck.Show";

    /// <summary>
    /// <c>AccountDeck.exe</c> opens the manager, <c>--tray</c> starts hidden in the tray, and
    /// <c>--open &lt;accountId&gt;</c> (used by desktop shortcuts) opens one account and exits.
    /// </summary>
    [STAThread]
    static int Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (_, e) => ReportCrash(e.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, e) => LogError("fatal", e.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, e) => LogError("background task", e.Exception);

        if (args.Length >= 2 && args[0].Equals("--open", StringComparison.OrdinalIgnoreCase))
            return OpenDirect(args[1]);

        // Build helper: regenerates assets/AccountDeck.ico and .png from AppIconArt.
        if (args.Length >= 2 && args[0].Equals("--export-icon", StringComparison.OrdinalIgnoreCase))
        {
            AppIconArt.Export(args[1]);
            return 0;
        }

        using var showSignal = new EventWaitHandle(false, EventResetMode.AutoReset, ShowSignalName, out var firstInstance);
        if (!firstInstance)
        {
            showSignal.Set(); // ask the running instance to show its window
            return 0;
        }

        var startHidden = args.Any(a => a.Equals("--tray", StringComparison.OrdinalIgnoreCase));
        using var host = new DeckHost(new ConfigStore(), showSignal, startHidden);
        Application.Run(host);
        return 0;
    }

    /// <summary>Keeps the app alive after a UI error and leaves a log next to the config.</summary>
    static void ReportCrash(Exception ex)
    {
        LogError("ui", ex);
        MessageBox.Show("Something went wrong: " + ex.Message + "\n\nDetails were written to errors.log in " + AppPaths.Root,
            "AccountDeck", MessageBoxButtons.OK, MessageBoxIcon.Error);
    }

    static void LogError(string source, Exception? ex)
    {
        try
        {
            Directory.CreateDirectory(AppPaths.Root);
            File.AppendAllText(Path.Combine(AppPaths.Root, "errors.log"), $"[{DateTime.Now:u}] ({source}) {ex}\n\n");
        }
        catch (IOException) { }
    }

    static int OpenDirect(string accountId)
    {
        var store = new ConfigStore();
        var found = store.FindAccount(accountId);
        if (found is null)
        {
            MessageBox.Show("This shortcut points to an account that no longer exists in AccountDeck.", "AccountDeck",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return 1;
        }
        var (app, account) = found.Value;

        try
        {
            Launcher.Open(app, account);
            return 0;
        }
        catch (Exception ex) when (ex is LaunchException or IOException or UnauthorizedAccessException)
        {
            MessageBox.Show(ex.Message, "AccountDeck", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return 1;
        }
    }
}
