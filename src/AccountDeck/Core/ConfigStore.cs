using System.Text.Json;

namespace AccountDeck.Core;

/// <summary>Loads and saves <see cref="DeckConfig"/> as JSON (atomic writes).</summary>
public sealed class ConfigStore
{
    public static readonly string[] Palette =
    {
        "#E08A5C", "#4C8BF5", "#3BA272", "#B65FCF", "#D94F70", "#C9A227", "#3AA7B8", "#7D8590",
    };

    static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    readonly string _file;

    public ConfigStore(string? file = null)
    {
        _file = file ?? AppPaths.ConfigFile;
        Config = Load();
    }

    public DeckConfig Config { get; private set; }

    public IReadOnlyList<AppEntry> Apps => Config.Apps;

    public event EventHandler? Changed;

    public (AppEntry App, AccountProfile Account)? FindAccount(string accountId)
    {
        foreach (var app in Config.Apps)
            foreach (var account in app.Accounts)
                if (account.Id == accountId) return (app, account);
        return null;
    }

    public AppEntry? FindApp(string id) => Config.Apps.FirstOrDefault(a => a.Id == id);

    public string NextColor(int index) => Palette[index % Palette.Length];

    public void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_file)!);
        var temp = _file + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(Config, JsonOptions));
        File.Move(temp, _file, overwrite: true);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Suggests an unused folder for a new account of <paramref name="app"/>.</summary>
    public string SuggestDataDir(AppEntry app, string accountName)
    {
        var baseDir = app.Preset?.DataDirFor(accountName)
                      ?? Path.Combine(AppPaths.DataRoot, AppPaths.Slug(app.Name), AppPaths.Slug(accountName));
        var candidate = baseDir;
        for (var i = 2; IsDataDirTaken(app, candidate, null); i++) candidate = $"{baseDir}-{i}";
        return candidate;
    }

    public static bool IsDataDirTaken(AppEntry app, string dir, string? exceptAccountId)
    {
        var key = AppPaths.NormalizeDir(dir);
        return app.Accounts.Any(a => a.Id != exceptAccountId && !a.IsDefault && AppPaths.NormalizeDir(a.DataDir) == key);
    }

    DeckConfig Load()
    {
        if (File.Exists(_file))
        {
            try
            {
                var loaded = JsonSerializer.Deserialize<DeckConfig>(File.ReadAllText(_file));
                if (loaded is not null) return loaded;
            }
            catch (JsonException)
            {
                // Keep the unreadable file for inspection, then start fresh.
                File.Copy(_file, _file + ".broken-" + DateTime.Now.ToString("yyyyMMddHHmmss"), overwrite: true);
            }
        }

        var fresh = new DeckConfig();
        foreach (var preset in AppPresets.All.Where(p => p.Locate() is not null).Take(3))
            fresh.Apps.Add(preset.CreateApp());
        Config = fresh;
        Save();
        return fresh;
    }
}
