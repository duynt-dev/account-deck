namespace AccountDeck.Core;

/// <summary>Caches the program icon of each app for the UI.</summary>
public static class AppIcons
{
    static readonly Dictionary<string, Bitmap?> Cache = new(StringComparer.OrdinalIgnoreCase);

    public static Bitmap? Get(AppEntry app)
    {
        var exe = AppPresets.ResolveExe(app);
        return exe is null ? null : ForExe(exe);
    }

    public static Bitmap? ForExe(string exe)
    {
        lock (Cache)
        {
            if (Cache.TryGetValue(exe, out var cached)) return cached;
            Bitmap? bitmap = null;
            try
            {
                using var icon = Icon.ExtractIcon(exe, 0, 64) ?? Icon.ExtractAssociatedIcon(exe);
                bitmap = icon?.ToBitmap();
            }
            catch (Exception ex) when (ex is IOException or ArgumentException or UnauthorizedAccessException) { }
            Cache[exe] = bitmap;
            return bitmap;
        }
    }

    public static void Forget(AppEntry app)
    {
        var exe = AppPresets.ResolveExe(app);
        if (exe is null) return;
        lock (Cache) Cache.Remove(exe);
    }
}
