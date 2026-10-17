using System.Text.Json.Serialization;

namespace AccountDeck.Core;

/// <summary>An application whose accounts AccountDeck manages, e.g. Claude, Chrome or Telegram.</summary>
public sealed class AppEntry
{
    public string Id { get; set; } = NewId();
    public string Name { get; set; } = "";

    /// <summary>Known preset (see <see cref="AppPresets"/>) used for auto-detection; null for custom apps.</summary>
    public string? PresetId { get; set; }

    /// <summary>Explicit executable path. Empty means "detect through the preset".</summary>
    public string ExePath { get; set; } = "";

    /// <summary>Arguments added for isolated accounts; <c>{dataDir}</c> is replaced by the account folder.</summary>
    public string ArgsTemplate { get; set; } = "";

    /// <summary>Environment variables set for isolated accounts; values may contain <c>{dataDir}</c>.</summary>
    public Dictionary<string, string> EnvTemplate { get; set; } = new();

    public string Color { get; set; } = "#7D8590";

    public List<AccountProfile> Accounts { get; set; } = new();

    [JsonIgnore]
    public AppPreset? Preset => AppPresets.Get(PresetId);

    public static string NewId() => Guid.NewGuid().ToString("N")[..10];
}

/// <summary>One sign-in of an app, isolated by its own data folder.</summary>
public sealed class AccountProfile
{
    public string Id { get; set; } = AppEntry.NewId();
    public string Name { get; set; } = "";
    public string Note { get; set; } = "";
    public string Color { get; set; } = "#E08A5C";

    /// <summary>Isolated data folder. Empty means the app's normal, non-isolated sign-in.</summary>
    public string DataDir { get; set; } = "";

    /// <summary>Extra command-line arguments for this account only.</summary>
    public string ExtraArgs { get; set; } = "";

    /// <summary>Extra environment variables for this account only, e.g. CLAUDE_CONFIG_DIR.</summary>
    public Dictionary<string, string> Environment { get; set; } = new();

    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

    [JsonIgnore]
    public bool IsDefault => string.IsNullOrWhiteSpace(DataDir);

    [JsonIgnore]
    public string Initial => string.IsNullOrWhiteSpace(Name) ? "?" : Name.Trim()[..1].ToUpperInvariant();
}

public sealed class DeckConfig
{
    public List<AppEntry> Apps { get; set; } = new();

    /// <summary>Hide the manager after an account has been opened from it.</summary>
    public bool HideManagerAfterLaunch { get; set; }

    /// <summary>Closing the manager keeps AccountDeck running in the tray.</summary>
    public bool CloseToTray { get; set; } = true;

    public string LastAppId { get; set; } = "";

    /// <summary>Global shortcut for the quick switcher, e.g. "Ctrl+Alt+A" or "Ctrl+Shift+Space".</summary>
    public string QuickSwitchHotkey { get; set; } = "Ctrl+Alt+A";

    /// <summary>Watch the Downloads folder and import Claude data exports automatically.</summary>
    public bool AutoImportClaudeExports { get; set; } = true;

    /// <summary>Downloads older than this were already looked at.</summary>
    public DateTime LastExportCheckUtc { get; set; } = DateTime.UtcNow;
}
