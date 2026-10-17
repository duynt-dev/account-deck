using Microsoft.Win32;

namespace AccountDeck.Conversations;

/// <summary>
/// Watches the user's Downloads folder for Claude data exports (.zip files that contain
/// conversations.json) and reports each one once, on the UI thread.
/// </summary>
public sealed class ExportWatcher : IDisposable
{
    const string DownloadsFolderId = "{374DE290-123F-4565-9164-39C4925E467B}";
    static readonly TimeSpan SettleTimeout = TimeSpan.FromMinutes(2);

    readonly SynchronizationContext _ui;
    readonly HashSet<string> _seen = new(StringComparer.OrdinalIgnoreCase);
    FileSystemWatcher? _watcher;

    public ExportWatcher(SynchronizationContext ui) => _ui = ui;

    /// <summary>Raised with the path of a Claude export that appeared in Downloads.</summary>
    public event Action<string>? ExportFound;

    public string Folder { get; } = DownloadsFolder();

    public bool Running => _watcher is not null;

    /// <summary>Starts watching, after checking files that arrived since <paramref name="sinceUtc"/>.</summary>
    public void Start(DateTime sinceUtc)
    {
        if (_watcher is not null || !Directory.Exists(Folder)) return;

        foreach (var zip in Directory.EnumerateFiles(Folder, "*.zip"))
            if (File.GetLastWriteTimeUtc(zip) > sinceUtc) Consider(zip);

        _watcher = new FileSystemWatcher(Folder, "*.zip") { NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite };
        // Browsers download to a temporary name and rename to .zip when done, so Renamed matters most.
        _watcher.Created += (_, e) => Consider(e.FullPath);
        _watcher.Renamed += (_, e) => Consider(e.FullPath);
        _watcher.EnableRaisingEvents = true;
    }

    public void Stop()
    {
        _watcher?.Dispose();
        _watcher = null;
    }

    public void Dispose() => Stop();

    void Consider(string path)
    {
        if (!path.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)) return;
        lock (_seen)
            if (!_seen.Add(path)) return;

        _ = Task.Run(async () =>
        {
            if (!await WaitUntilReadableAsync(path)) return;
            if (ClaudeExport.IsExportArchive(path)) _ui.Post(_ => ExportFound?.Invoke(path), null);
        });
    }

    /// <summary>Waits until the browser has finished writing the file.</summary>
    static async Task<bool> WaitUntilReadableAsync(string path)
    {
        var deadline = DateTime.UtcNow + SettleTimeout;
        while (DateTime.UtcNow < deadline)
        {
            try
            {
                using (File.Open(path, FileMode.Open, FileAccess.Read, FileShare.Read)) return true;
            }
            catch (FileNotFoundException) { return false; }
            catch (IOException) { await Task.Delay(1000); }
            catch (UnauthorizedAccessException) { return false; }
        }
        return false;
    }

    static string DownloadsFolder()
    {
        using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\User Shell Folders");
        if (key?.GetValue(DownloadsFolderId) is string configured && Directory.Exists(configured)) return configured;
        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
    }
}
