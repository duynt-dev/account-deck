using System.Runtime.InteropServices;

namespace AccountDeck.Core;

/// <summary>How confident AccountDeck is that an app can keep accounts apart.</summary>
public enum AppSupport
{
    /// <summary>A built-in preset that is known to work.</summary>
    Preset,

    /// <summary>An Electron app; these accept --user-data-dir and usually run side by side.</summary>
    Electron,

    /// <summary>Anything else; AppData redirection is tried, which may or may not work.</summary>
    Other,
}

/// <summary>An installed app that AccountDeck could manage.</summary>
public sealed record FoundApp(string Name, string ExePath, AppSupport Support, AppPreset? Preset = null)
{
    public AppEntry ToAppEntry()
    {
        if (Preset is not null) return Preset.CreateApp();
        var mode = Support == AppSupport.Electron ? IsolationMode.Chromium : IsolationMode.AppData;
        var app = new AppEntry
        {
            Name = Name,
            ExePath = ExePath,
            ArgsTemplate = mode.ArgsTemplate,
            EnvTemplate = new Dictionary<string, string>(mode.Env),
            Color = ConfigStore.Palette[^1],
        };
        app.Accounts.Add(new AccountProfile { Name = "Default", Note = "Normal sign-in", Color = app.Color });
        return app;
    }
}

/// <summary>
/// Lists installed apps from the known presets and from Start menu shortcuts, sorted by how
/// likely multi-account use is to work.
/// </summary>
public static class AppScanner
{
    static readonly string[] SkippedNameParts =
    {
        "uninstall", "setup", "installer", "update", "readme", "help", "documentation", "release notes",
        "configuration", "settings", "benchmark", "reload", "command prompt", "shell", "console",
    };

    public static List<FoundApp> Scan(IEnumerable<AppEntry> existing)
    {
        var known = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var knownPresets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var app in existing)
        {
            if (AppPresets.ResolveExe(app) is { } exe) known.Add(Path.GetFullPath(exe));
            if (app.PresetId is { } id) knownPresets.Add(id);
        }

        // Exes of presets, even ones already added, so their shortcuts are not offered again as "other".
        foreach (var preset in AppPresets.All)
            if (knownPresets.Contains(preset.Id) && preset.Locate() is { } exe) known.Add(Path.GetFullPath(exe));

        var found = new List<FoundApp>();
        foreach (var preset in AppPresets.All)
        {
            if (knownPresets.Contains(preset.Id) || preset.Locate() is not { } exe) continue;
            if (known.Add(Path.GetFullPath(exe))) found.Add(new FoundApp(preset.Name, exe, AppSupport.Preset, preset));
        }

        foreach (var (name, exe) in StartMenuApps())
        {
            if (!known.Add(Path.GetFullPath(exe))) continue;
            found.Add(new FoundApp(name, exe, IsElectron(exe) ? AppSupport.Electron : AppSupport.Other));
        }

        return found
            .OrderBy(f => f.Support)
            .ThenBy(f => f.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    /// <summary>Electron apps keep their code in resources\app.asar, either next to the exe or in an app-x.y.z folder.</summary>
    public static bool IsElectron(string exePath)
    {
        var dir = Path.GetDirectoryName(exePath);
        if (dir is null) return false;
        if (File.Exists(Path.Combine(dir, "resources", "app.asar"))) return true;
        try
        {
            return Directory.EnumerateDirectories(dir, "app-*")
                .Any(d => File.Exists(Path.Combine(d, "resources", "app.asar")));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    static IEnumerable<(string Name, string Exe)> StartMenuApps()
    {
        var roots = new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.StartMenu), "Programs"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu), "Programs"),
        };
        var windowsDir = Environment.GetFolderPath(Environment.SpecialFolder.Windows);

        var shellType = Type.GetTypeFromProgID("WScript.Shell");
        if (shellType is null) yield break;
        dynamic shell = Activator.CreateInstance(shellType)!;
        try
        {
            foreach (var root in roots.Where(Directory.Exists))
            {
                List<string> links;
                try { links = Directory.EnumerateFiles(root, "*.lnk", SearchOption.AllDirectories).ToList(); }
                catch (UnauthorizedAccessException) { continue; }

                foreach (var link in links)
                {
                    var name = Path.GetFileNameWithoutExtension(link);
                    if (SkippedNameParts.Any(p => name.Contains(p, StringComparison.OrdinalIgnoreCase))) continue;

                    string target;
                    try { target = (string)shell.CreateShortcut(link).TargetPath; }
                    catch (COMException) { continue; }

                    if (string.IsNullOrWhiteSpace(target) || !target.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) continue;
                    if (AppPaths.IsUnder(target, windowsDir) || !File.Exists(target)) continue;
                    yield return (name, target);
                }
            }
        }
        finally
        {
            Marshal.FinalReleaseComObject(shell);
        }
    }
}
