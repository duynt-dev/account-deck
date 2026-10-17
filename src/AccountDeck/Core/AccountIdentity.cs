using System.Text.Json;
using AccountDeck.Conversations;

namespace AccountDeck.Core;

/// <summary>
/// Finds which user an account is signed in as, using only non-secret sources:
/// the profile list Chromium browsers keep in "Local State", and the e-mail recorded in an
/// imported Claude data export. Cookies, tokens and app databases are never read.
/// </summary>
public static class AccountIdentity
{
    static readonly Dictionary<string, (DateTime Stamp, string? Value)> Cache = new(StringComparer.OrdinalIgnoreCase);

    static readonly Dictionary<string, string> DefaultBrowserFolders = new(StringComparer.OrdinalIgnoreCase)
    {
        ["chrome"] = @"Google\Chrome\User Data",
        ["edge"] = @"Microsoft\Edge\User Data",
        ["brave"] = @"BraveSoftware\Brave-Browser\User Data",
        ["vivaldi"] = @"Vivaldi\User Data",
    };

    public static string? Find(AppEntry app, AccountProfile account)
    {
        if (app.PresetId == "claude") return ClaudeIdentity(app, account);

        var userData = account.IsDefault
            ? app.PresetId is { } id && DefaultBrowserFolders.TryGetValue(id, out var relative)
                ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), relative)
                : null
            : AppPaths.Expand(account.DataDir);
        return userData is null ? null : FromLocalState(Path.Combine(userData, "Local State"));
    }

    /// <summary>
    /// Claude keeps no e-mail on disk, so: the e-mail from an imported export when there is one,
    /// otherwise whether the profile is signed in, with the short account id read locally.
    /// </summary>
    static string ClaudeIdentity(AppEntry app, AccountProfile account)
    {
        if (ClaudeExport.ImportedEmail(account) is { } email) return email;
        if (ClaudeLocalInfo.AccountUuid(app, account) is { } uuid) return $"Signed in · account {uuid[..8]}";
        var folder = SystemIntegration.DataFolderOnDisk(app, account);
        return folder is not null && Directory.Exists(folder) ? "Not signed in yet" : "Never opened";
    }

    /// <summary>Reads the signed-in e-mail (or Google name) of the last used profile.</summary>
    static string? FromLocalState(string file)
    {
        if (!File.Exists(file)) return null;
        var stamp = File.GetLastWriteTimeUtc(file);
        lock (Cache)
            if (Cache.TryGetValue(file, out var cached) && cached.Stamp == stamp) return cached.Value;

        string? value = null;
        try
        {
            using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var doc = JsonDocument.Parse(stream);
            if (doc.RootElement.TryGetProperty("profile", out var profile) &&
                profile.TryGetProperty("info_cache", out var profiles) && profiles.ValueKind == JsonValueKind.Object)
            {
                var lastUsed = profile.TryGetProperty("last_used", out var last) ? last.GetString() : null;
                var entry = lastUsed is not null && profiles.TryGetProperty(lastUsed, out var chosen)
                    ? chosen
                    : profiles.EnumerateObject().Select(p => p.Value).FirstOrDefault();
                if (entry.ValueKind == JsonValueKind.Object)
                    value = NonEmpty(entry, "user_name") ?? NonEmpty(entry, "gaia_name");
            }
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException or InvalidOperationException)
        {
        }

        lock (Cache) Cache[file] = (stamp, value);
        return value;
    }

    static string? NonEmpty(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String && v.GetString() is { Length: > 0 } s ? s : null;
}
