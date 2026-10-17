namespace AccountDeck.Core;

public enum AppCategory
{
    AI,
    Browsers,
    Chat,
    Developer,
    Games,
    Office,
    Other,
}

/// <summary>Sorts apps into broad groups for the "Manage apps" grid, by preset or by name.</summary>
public static class AppCategories
{
    static readonly Dictionary<string, AppCategory> Presets = new(StringComparer.OrdinalIgnoreCase)
    {
        ["claude"] = AppCategory.AI,
        ["chrome"] = AppCategory.Browsers,
        ["edge"] = AppCategory.Browsers,
        ["brave"] = AppCategory.Browsers,
        ["vivaldi"] = AppCategory.Browsers,
        ["firefox"] = AppCategory.Browsers,
        ["telegram"] = AppCategory.Chat,
        ["vscode"] = AppCategory.Developer,
    };

    static readonly (AppCategory Category, string[] Words)[] Keywords =
    {
        (AppCategory.AI, new[] { "claude", "chatgpt", "openai", "copilot", "gemini", "perplexity", "cursor", "windsurf", "ollama", "lm studio" }),
        (AppCategory.Browsers, new[] { "chrome", "edge", "firefox", "brave", "vivaldi", "opera", "browser", "arc", "zen" }),
        (AppCategory.Chat, new[] { "discord", "telegram", "zalo", "slack", "teams", "whatsapp", "messenger", "skype", "signal", "viber", "line", "zoom", "voip", "mail", "thunderbird" }),
        (AppCategory.Developer, new[] { "code", "rider", "visual studio", "jetbrains", "postman", "dbeaver", "git", "node", "pgadmin", "docker", "erlang", "stack builder", "terminal", "github", "insomnia", "datagrip", "android studio" }),
        (AppCategory.Games, new[] { "steam", "riot", "roblox", "epic games", "battle.net", "ubisoft", "ea app", "origin", "gog", "minecraft", "league", "valorant", "gearup", "launcher" }),
        (AppCategory.Office, new[] { "word", "excel", "powerpoint", "outlook", "onenote", "access", "publisher", "notion", "obsidian", "office", "acrobat", "sticky notes" }),
    };

    public static AppCategory Classify(string name, string? exePath, string? presetId)
    {
        if (presetId is not null && Presets.TryGetValue(presetId, out var preset)) return preset;
        var text = $"{name} {Path.GetFileNameWithoutExtension(exePath ?? "")}".ToLowerInvariant();
        foreach (var (category, words) in Keywords)
            if (words.Any(w => ContainsWord(text, w))) return category;
        return AppCategory.Other;
    }

    public static string Title(AppCategory category) => category switch
    {
        AppCategory.AI => "AI assistants",
        AppCategory.Browsers => "Browsers",
        AppCategory.Chat => "Chat & social",
        AppCategory.Developer => "Developer tools",
        AppCategory.Games => "Games & launchers",
        AppCategory.Office => "Office & notes",
        _ => "Other apps",
    };

    /// <summary>Whole-word match, so "line" does not match "pipeline" and "code" does not match "unicode".</summary>
    static bool ContainsWord(string text, string word)
    {
        var at = 0;
        while ((at = text.IndexOf(word, at, StringComparison.Ordinal)) >= 0)
        {
            var before = at == 0 || !char.IsLetterOrDigit(text[at - 1]);
            var end = at + word.Length;
            var after = end >= text.Length || !char.IsLetterOrDigit(text[end]);
            if (before && after) return true;
            at = end;
        }
        return false;
    }
}
