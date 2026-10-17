using Microsoft.Win32;

namespace AccountDeck.Core;

/// <summary>Finds Claude Desktop for Microsoft Store (MSIX), installer and Scoop builds.</summary>
public static class ClaudeLocator
{
    const string PackageRepositoryKey =
        @"Software\Classes\Local Settings\Software\Microsoft\Windows\CurrentVersion\AppModel\Repository\Packages";

    public static string? Find() => FromPackageRepository() ?? FromKnownFolders();

    /// <summary>
    /// Store builds live under a versioned WindowsApps folder that changes on every update;
    /// the per-user package repository in the registry always points at the current one.
    /// </summary>
    static string? FromPackageRepository()
    {
        using var key = Registry.CurrentUser.OpenSubKey(PackageRepositoryKey);
        if (key is null) return null;

        var names = key.GetSubKeyNames()
            .Where(n => n.StartsWith("Claude_", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(ParsePackageVersion);

        foreach (var name in names)
        {
            using var pkg = key.OpenSubKey(name);
            if (pkg?.GetValue("PackageRootFolder") is not string root) continue;
            var exe = Path.Combine(root, "app", "Claude.exe");
            if (File.Exists(exe)) return exe;
        }
        return null;
    }

    static string? FromKnownFolders()
    {
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return AppPresets.FirstExisting(
            Path.Combine(local, "AnthropicClaude", "claude.exe"),
            Path.Combine(local, "Programs", "Claude", "Claude.exe"),
            Path.Combine(home, "scoop", "apps", "claude", "current", "Claude.exe"));
    }

    static Version ParsePackageVersion(string fullName)
    {
        var parts = fullName.Split('_');
        return parts.Length > 1 && Version.TryParse(parts[1], out var v) ? v : new Version(0, 0);
    }

    /// <summary>"…\WindowsApps\Claude_2.4.0.0_x64__abc\app\Claude.exe" -> "Claude_abc".</summary>
    public static string? PackageFamily(string exePath)
    {
        var marker = $"{Path.DirectorySeparatorChar}WindowsApps{Path.DirectorySeparatorChar}";
        var i = exePath.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (i < 0) return null;
        var parts = exePath[(i + marker.Length)..].Split(Path.DirectorySeparatorChar)[0].Split('_');
        return parts.Length >= 5 ? $"{parts[0]}_{parts[^1]}" : null;
    }

    /// <summary>
    /// Store apps have AppData writes redirected into their package folder, so the files of a
    /// profile may physically live under %LOCALAPPDATA%\Packages\&lt;family&gt;\LocalCache.
    /// Returns whichever folder actually exists.
    /// </summary>
    public static string OnDiskFolder(string logicalFolder, string exePath)
    {
        if (PackageFamily(exePath) is { } family)
        {
            var roaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var cache = Path.Combine(local, "Packages", family, "LocalCache");
            string? redirected = null;
            if (AppPaths.IsUnder(logicalFolder, roaming))
                redirected = Path.Combine(cache, "Roaming", Path.GetRelativePath(roaming, logicalFolder));
            else if (AppPaths.IsUnder(logicalFolder, local))
                redirected = Path.Combine(cache, "Local", Path.GetRelativePath(local, logicalFolder));
            if (redirected is not null && Directory.Exists(redirected)) return redirected;
        }
        return logicalFolder;
    }
}
