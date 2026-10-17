namespace AccountDeck.Core;

/// <summary>How an app keeps two sign-ins apart.</summary>
public sealed record IsolationMode(string Name, string ArgsTemplate, IReadOnlyDictionary<string, string> Env, string Hint)
{
    public static readonly IsolationMode Chromium = new(
        "Chromium / Electron app",
        "--user-data-dir=\"{dataDir}\"",
        new Dictionary<string, string>(),
        "Browsers and desktop apps built on Chromium or Electron.");

    public static readonly IsolationMode Firefox = new(
        "Firefox profile",
        "-profile \"{dataDir}\" -no-remote",
        new Dictionary<string, string>(),
        "Firefox and browsers based on it.");

    public static readonly IsolationMode AppData = new(
        "Redirect AppData",
        "",
        new Dictionary<string, string>
        {
            ["APPDATA"] = @"{dataDir}\Roaming",
            ["LOCALAPPDATA"] = @"{dataDir}\Local",
        },
        "Classic apps that store their sign-in under %APPDATA% or %LOCALAPPDATA%.");

    public static readonly IsolationMode Custom = new(
        "Custom arguments",
        "",
        new Dictionary<string, string>(),
        "Write your own arguments; {dataDir} is the account folder.");

    public static IReadOnlyList<IsolationMode> All { get; } = new[] { Chromium, Firefox, AppData, Custom };

    /// <summary>Best guess of which mode an app's current templates correspond to.</summary>
    public static IsolationMode Detect(AppEntry app)
    {
        foreach (var mode in new[] { Chromium, Firefox, AppData })
        {
            var sameEnv = mode.Env.Count == app.EnvTemplate.Count && mode.Env.All(kv => app.EnvTemplate.TryGetValue(kv.Key, out var v) && v == kv.Value);
            if (mode.ArgsTemplate == app.ArgsTemplate && sameEnv) return mode;
        }
        return Custom;
    }
}

/// <summary>A known app: how to find it and how to isolate its accounts.</summary>
public sealed record AppPreset(
    string Id,
    string Name,
    string Color,
    string ArgsTemplate,
    IReadOnlyDictionary<string, string> Env,
    Func<string?> Locate,
    Func<string, string>? CustomDataDir = null)
{
    public string? DataDirFor(string accountName) => CustomDataDir?.Invoke(accountName);

    public AppEntry CreateApp() => new()
    {
        Name = Name,
        PresetId = Id,
        ArgsTemplate = ArgsTemplate,
        EnvTemplate = new Dictionary<string, string>(Env),
        Color = Color,
        Accounts = { new AccountProfile { Name = "Default", Note = "Normal sign-in", Color = Color } },
    };
}

public static class AppPresets
{
    static readonly string Pf = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
    static readonly string Pf86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
    static readonly string Local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
    static readonly string Roaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);

    static readonly IReadOnlyDictionary<string, string> NoEnv = new Dictionary<string, string>();

    public static IReadOnlyList<AppPreset> All { get; } = new AppPreset[]
    {
        new("claude", "Claude", "#D97757", IsolationMode.Chromium.ArgsTemplate, NoEnv, ClaudeLocator.Find,
            // Kept beside Claude's own folder in Roaming AppData, where the app expects its data.
            name => Path.Combine(Roaming, "Claude-" + AppPaths.Slug(name))),

        new("chrome", "Google Chrome", "#4C8BF5", IsolationMode.Chromium.ArgsTemplate, NoEnv, () => FirstExisting(
            Path.Combine(Pf, @"Google\Chrome\Application\chrome.exe"),
            Path.Combine(Pf86, @"Google\Chrome\Application\chrome.exe"),
            Path.Combine(Local, @"Google\Chrome\Application\chrome.exe"))),

        new("edge", "Microsoft Edge", "#2F9BD9", IsolationMode.Chromium.ArgsTemplate, NoEnv, () => FirstExisting(
            Path.Combine(Pf86, @"Microsoft\Edge\Application\msedge.exe"),
            Path.Combine(Pf, @"Microsoft\Edge\Application\msedge.exe"))),

        new("brave", "Brave", "#F25B2A", IsolationMode.Chromium.ArgsTemplate, NoEnv, () => FirstExisting(
            Path.Combine(Pf, @"BraveSoftware\Brave-Browser\Application\brave.exe"),
            Path.Combine(Local, @"BraveSoftware\Brave-Browser\Application\brave.exe"))),

        new("vivaldi", "Vivaldi", "#EF3939", IsolationMode.Chromium.ArgsTemplate, NoEnv, () => FirstExisting(
            Path.Combine(Local, @"Vivaldi\Application\vivaldi.exe"),
            Path.Combine(Pf, @"Vivaldi\Application\vivaldi.exe"))),

        new("vscode", "VS Code", "#3B82C4",
            "--user-data-dir=\"{dataDir}\\data\" --extensions-dir=\"{dataDir}\\extensions\"", NoEnv, () => FirstExisting(
            Path.Combine(Local, @"Programs\Microsoft VS Code\Code.exe"),
            Path.Combine(Pf, @"Microsoft VS Code\Code.exe"))),

        new("firefox", "Firefox", "#FF7139", IsolationMode.Firefox.ArgsTemplate, NoEnv, () => FirstExisting(
            Path.Combine(Pf, @"Mozilla Firefox\firefox.exe"),
            Path.Combine(Pf86, @"Mozilla Firefox\firefox.exe"))),

        new("telegram", "Telegram", "#2AABEE", "-workdir \"{dataDir}\"", NoEnv, () => FirstExisting(
            Path.Combine(Roaming, @"Telegram Desktop\Telegram.exe"),
            Path.Combine(Pf, @"Telegram Desktop\Telegram.exe"))),
    };

    public static AppPreset? Get(string? id) =>
        id is null ? null : All.FirstOrDefault(p => p.Id.Equals(id, StringComparison.OrdinalIgnoreCase));

    public static string? ResolveExe(AppEntry app)
    {
        if (!string.IsNullOrWhiteSpace(app.ExePath))
        {
            var path = AppPaths.Expand(app.ExePath);
            return File.Exists(path) ? path : null;
        }
        return app.Preset?.Locate();
    }

    public static string? FirstExisting(params string[] paths) => paths.FirstOrDefault(File.Exists);
}
