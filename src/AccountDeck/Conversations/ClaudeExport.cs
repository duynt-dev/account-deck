using System.IO.Compression;
using System.Text;
using System.Text.Json;
using AccountDeck.Core;

namespace AccountDeck.Conversations;

public sealed record ExportMessage(string Sender, DateTimeOffset? At, string Text, IReadOnlyList<string> Files);

public sealed record ExportConversation(
    string Id, string Title, DateTimeOffset? Created, DateTimeOffset? Updated, IReadOnlyList<ExportMessage> Messages)
{
    string? _searchText;

    /// <summary>Lower-case title and message text, built on first use.</summary>
    public string SearchText => _searchText ??= (Title + "\n" + string.Join("\n", Messages.Select(m => m.Text))).ToLowerInvariant();
}

public sealed record ExportLibrary(IReadOnlyList<ExportConversation> Conversations, string? AccountEmail, DateTime ImportedAt);

/// <summary>
/// Reads the data export that Claude sends by e-mail (Settings → Privacy → Export data).
/// The archive contains conversations.json and users.json. Imports are kept per account
/// under AccountDeck's own folder so they can be browsed offline.
/// </summary>
public static class ClaudeExport
{
    const string ConversationsFile = "conversations.json";
    const string UsersFile = "users.json";

    static string FolderOf(AccountProfile account) => Path.Combine(AppPaths.ExportsDir, account.Id);

    public static ExportLibrary? Load(AccountProfile account)
    {
        var folder = FolderOf(account);
        var conversations = Path.Combine(folder, ConversationsFile);
        if (!File.Exists(conversations)) return null;

        using var stream = File.OpenRead(conversations);
        var list = ParseConversations(stream);
        var users = Path.Combine(folder, UsersFile);
        var email = File.Exists(users) ? ReadEmail(users) : null;
        return new ExportLibrary(list, email, File.GetLastWriteTime(conversations));
    }

    /// <summary>Copies conversations (and the account e-mail) out of a .zip or .json export.</summary>
    public static ExportLibrary Import(AccountProfile account, string source)
    {
        var folder = FolderOf(account);
        Directory.CreateDirectory(folder);
        var target = Path.Combine(folder, ConversationsFile);

        if (source.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
        {
            using var zip = ZipFile.OpenRead(source);
            var entry = FindEntry(zip, ConversationsFile)
                        ?? throw new InvalidDataException("This archive has no conversations.json. Is it a Claude data export?");
            entry.ExtractToFile(target, overwrite: true);
            var users = FindEntry(zip, UsersFile);
            if (users is not null) users.ExtractToFile(Path.Combine(folder, UsersFile), overwrite: true);
        }
        else
        {
            File.Copy(source, target, overwrite: true);
        }

        File.SetLastWriteTime(target, DateTime.Now);
        try
        {
            return Load(account) ?? throw new InvalidDataException("Nothing could be read from the export.");
        }
        catch (JsonException)
        {
            File.Delete(target);
            throw new InvalidDataException("conversations.json is not in the expected format.");
        }
    }

    /// <summary>True when the .zip looks like a Claude data export (has conversations.json).</summary>
    public static bool IsExportArchive(string zipPath)
    {
        try
        {
            using var zip = ZipFile.OpenRead(zipPath);
            return FindEntry(zip, ConversationsFile) is not null;
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>The e-mail of the account an export came from, read from its users.json.</summary>
    public static string? EmailInArchive(string zipPath)
    {
        try
        {
            using var zip = ZipFile.OpenRead(zipPath);
            var entry = FindEntry(zip, UsersFile);
            if (entry is null) return null;
            using var reader = new StreamReader(entry.Open());
            return EmailFromUsersJson(reader.ReadToEnd());
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>The e-mail recorded by the last import for this account, if any.</summary>
    public static string? ImportedEmail(AccountProfile account)
    {
        var users = Path.Combine(FolderOf(account), UsersFile);
        return File.Exists(users) ? ReadEmail(users) : null;
    }

    public static string ToMarkdown(ExportConversation conversation)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"# {conversation.Title}");
        if (conversation.Created is { } created) sb.AppendLine($"_Started {created.LocalDateTime:yyyy-MM-dd HH:mm}_");
        sb.AppendLine();
        foreach (var message in conversation.Messages)
        {
            sb.AppendLine($"**{message.Sender}:**");
            sb.AppendLine();
            sb.AppendLine(message.Text.Trim());
            foreach (var file in message.Files) sb.AppendLine($"> attachment: {file}");
            sb.AppendLine();
        }
        return sb.ToString();
    }

    /// <summary>Markdown wrapped in a short instruction, ready to paste as the first message in another account.</summary>
    public static string ToContinuationPrompt(ExportConversation conversation) =>
        "Here is an earlier conversation I had with Claude in a different account. " +
        "Read it for context, then continue helping me from where it left off.\n\n---\n\n" +
        ToMarkdown(conversation);

    static ZipArchiveEntry? FindEntry(ZipArchive zip, string name) =>
        zip.Entries.FirstOrDefault(e => e.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

    static IReadOnlyList<ExportConversation> ParseConversations(Stream stream)
    {
        using var doc = JsonDocument.Parse(stream);
        if (doc.RootElement.ValueKind != JsonValueKind.Array) throw new JsonException("Expected an array of conversations.");

        var result = new List<ExportConversation>();
        foreach (var conv in doc.RootElement.EnumerateArray())
        {
            var messages = new List<ExportMessage>();
            if (conv.TryGetProperty("chat_messages", out var chat) && chat.ValueKind == JsonValueKind.Array)
            {
                foreach (var m in chat.EnumerateArray())
                {
                    var text = TextOfContent(m) ?? Str(m, "text") ?? "";
                    messages.Add(new ExportMessage(SenderLabel(Str(m, "sender")), Date(m, "created_at"), text, FileNames(m)));
                }
            }

            var title = Str(conv, "name");
            result.Add(new ExportConversation(
                Str(conv, "uuid") ?? Guid.NewGuid().ToString(),
                string.IsNullOrWhiteSpace(title) ? "(untitled)" : title.Trim(),
                Date(conv, "created_at"),
                Date(conv, "updated_at"),
                messages));
        }
        return result.OrderByDescending(c => c.Updated ?? c.Created ?? DateTimeOffset.MinValue).ToList();
    }

    /// <summary>Joins the text blocks of a message; tool calls become short markers.</summary>
    static string? TextOfContent(JsonElement message)
    {
        if (!message.TryGetProperty("content", out var content) || content.ValueKind != JsonValueKind.Array) return null;
        var sb = new StringBuilder();
        foreach (var block in content.EnumerateArray())
        {
            switch (Str(block, "type"))
            {
                case "text":
                    if (Str(block, "text") is { Length: > 0 } text)
                    {
                        if (sb.Length > 0) sb.AppendLine().AppendLine();
                        sb.Append(text);
                    }
                    break;
                case "tool_use":
                    if (sb.Length > 0) sb.AppendLine().AppendLine();
                    sb.Append($"[used tool: {Str(block, "name") ?? "unknown"}]");
                    break;
            }
        }
        return sb.Length > 0 ? sb.ToString() : null;
    }

    static IReadOnlyList<string> FileNames(JsonElement message)
    {
        var names = new List<string>();
        foreach (var property in new[] { "attachments", "files" })
        {
            if (!message.TryGetProperty(property, out var list) || list.ValueKind != JsonValueKind.Array) continue;
            foreach (var file in list.EnumerateArray())
                if (Str(file, "file_name") is { Length: > 0 } name && !names.Contains(name)) names.Add(name);
        }
        return names;
    }

    static string SenderLabel(string? sender) => sender switch
    {
        "human" => "You",
        "assistant" => "Claude",
        null or "" => "Unknown",
        _ => sender,
    };

    static string? ReadEmail(string usersFile) => EmailFromUsersJson(File.ReadAllText(usersFile));

    static string? EmailFromUsersJson(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            var first = root.ValueKind == JsonValueKind.Array ? root.EnumerateArray().FirstOrDefault() : root;
            return first.ValueKind == JsonValueKind.Object ? Str(first, "email_address") : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    static string? Str(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    static DateTimeOffset? Date(JsonElement e, string name) =>
        DateTimeOffset.TryParse(Str(e, name), out var d) ? d : null;
}
