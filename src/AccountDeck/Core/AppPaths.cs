namespace AccountDeck.Core;

/// <summary>Where AccountDeck keeps its own state, plus small path helpers.</summary>
internal static class AppPaths
{
    public static string Root { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AccountDeck");

    public static string ConfigFile => Path.Combine(Root, "config.json");
    public static string LaunchLogFile => Path.Combine(Root, "launched.json");
    public static string DataRoot => Path.Combine(Root, "data");
    public static string ExportsDir => Path.Combine(Root, "exports");
    public static string IconsDir => Path.Combine(Root, "icons");

    public static string Desktop => Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);

    public static string Expand(string path) => Environment.ExpandEnvironmentVariables(path.Trim().Trim('"'));

    /// <summary>Upper-case full path without a trailing separator, for comparisons.</summary>
    public static string NormalizeDir(string path)
    {
        var full = Path.GetFullPath(Expand(path));
        return full.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).ToUpperInvariant();
    }

    public static bool IsUnder(string path, string parent) =>
        NormalizeDir(path).StartsWith(NormalizeDir(parent) + Path.DirectorySeparatorChar, StringComparison.Ordinal);

    /// <summary>Turns a display name into a folder-safe lower-case slug.</summary>
    public static string Slug(string name)
    {
        var chars = name.Trim().ToLowerInvariant()
            .Select(c => char.IsLetterOrDigit(c) && c < 128 ? c : '-')
            .ToArray();
        var slug = string.Join('-', new string(chars).Split('-', StringSplitOptions.RemoveEmptyEntries));
        return slug.Length == 0 ? "account" : slug;
    }

    public static string SafeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var cleaned = new string(name.Select(c => invalid.Contains(c) ? '_' : c).ToArray()).Trim();
        return cleaned.Length == 0 ? "untitled" : cleaned;
    }
}
