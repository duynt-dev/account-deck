using System.Text.Json;
using AccountDeck.Core;

namespace AccountDeck.Conversations;

/// <summary>
/// Reads which Claude account a profile folder belongs to, from small settings files keyed by
/// account id. As a last resort the single non-secret key lastKnownAccountUuid is picked out of
/// config.json (see <see cref="LastKnownAccountUuid"/>); cookies, local storage and tokens are
/// never read.
/// </summary>
public static class ClaudeLocalInfo
{
    /// <summary>Settings files whose top-level object is keyed by the account's UUID.</summary>
    static readonly (string File, string Property)[] AccountKeyedFiles =
    {
        ("ccd-session-presets.json", "byAccount"),
        ("mcp-user-tool-toggles.json", "owners"),
        ("ant-device-registry.json", ""),
    };

    /// <summary>The account UUID used in this profile, or null when unknown or ambiguous.</summary>
    public static string? AccountUuid(AppEntry app, AccountProfile account)
    {
        var folder = SystemIntegration.DataFolderOnDisk(app, account);
        if (folder is null || !Directory.Exists(folder)) return null;

        foreach (var (file, property) in AccountKeyedFiles)
        {
            var ids = ReadKeys(Path.Combine(folder, file), property);
            // Several ids means the profile was signed into more than one account over time.
            if (ids.Count == 1) return ids[0];
        }
        return LastKnownAccountUuid(Path.Combine(folder, "config.json"));
    }

    /// <summary>
    /// config.json also stores sign-in tokens, so it is read with a forward-only reader that
    /// looks at property names only and skips every value except lastKnownAccountUuid.
    /// No other value is ever turned into a string or kept.
    /// </summary>
    static string? LastKnownAccountUuid(string path)
    {
        if (!File.Exists(path)) return null;
        try
        {
            var bytes = File.ReadAllBytes(path);
            try
            {
                var reader = new Utf8JsonReader(bytes, new JsonReaderOptions { CommentHandling = JsonCommentHandling.Skip });
                if (!reader.Read() || reader.TokenType != JsonTokenType.StartObject) return null;
                while (reader.Read() && reader.TokenType == JsonTokenType.PropertyName)
                {
                    var wanted = reader.ValueTextEquals("lastKnownAccountUuid");
                    reader.Read();
                    if (wanted)
                    {
                        var value = reader.TokenType == JsonTokenType.String ? reader.GetString() : null;
                        return Guid.TryParse(value, out _) ? value : null;
                    }
                    reader.Skip();
                }
                return null;
            }
            finally
            {
                Array.Clear(bytes);
            }
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    static List<string> ReadKeys(string path, string property)
    {
        if (!File.Exists(path)) return new();
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var doc = JsonDocument.Parse(stream);
            // An empty property name means the account ids are the top-level keys.
            var map = doc.RootElement;
            return (property.Length == 0 || doc.RootElement.TryGetProperty(property, out map)) && map.ValueKind == JsonValueKind.Object
                ? map.EnumerateObject().Select(p => p.Name).Where(n => Guid.TryParse(n, out _)).ToList()
                : new();
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return new();
        }
    }
}
