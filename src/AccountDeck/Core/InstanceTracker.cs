using System.Diagnostics;
using System.Text.Json;

namespace AccountDeck.Core;

/// <summary>
/// Works out which accounts are currently running. Two signals are combined:
/// the account folder appearing on a process's command line, and a small log of the
/// processes AccountDeck itself started (needed for apps isolated through environment
/// variables, whose command lines look identical).
/// </summary>
public static class InstanceTracker
{
    sealed record LaunchMark(int Pid, DateTime StartUtc, string AccountId);

    const int MaxAncestorDepth = 8;
    static readonly object Gate = new();
    static List<LaunchMark>? _marks;

    /// <summary>Account id → ids of the processes that belong to it.</summary>
    public static Dictionary<string, List<int>> Scan(IEnumerable<AppEntry> apps)
    {
        var result = new Dictionary<string, List<int>>();
        var targets = apps
            .Select(app => (App: app, Exe: AppPresets.ResolveExe(app)))
            .Where(t => t.Exe is not null)
            .Select(t => (t.App, InstallDir: Path.GetDirectoryName(t.Exe!)!, ProcessName: Path.GetFileNameWithoutExtension(t.Exe!)))
            .ToList();
        if (targets.Count == 0) return result;

        var marks = LiveMarks();
        var factCache = new Dictionary<int, ProcessFacts?>();

        foreach (var name in targets.Select(t => t.ProcessName).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            foreach (var process in Process.GetProcessesByName(name))
            {
                using (process)
                {
                    var facts = ProcessInspector.Read(process.Id, includeCommandLine: true);
                    factCache[process.Id] = facts;
                    if (facts?.ImagePath is null) continue;

                    var owner = targets.FirstOrDefault(t =>
                        t.ProcessName.Equals(name, StringComparison.OrdinalIgnoreCase) &&
                        AppPaths.IsUnder(facts.ImagePath, t.InstallDir));
                    if (owner.App is null) continue;

                    var accountId = MatchByLaunchLog(facts, marks, factCache) ?? MatchByCommandLine(owner.App, facts);
                    if (accountId is null) continue;

                    if (!result.TryGetValue(accountId, out var pids)) result[accountId] = pids = new List<int>();
                    pids.Add(process.Id);
                }
            }
        }
        return result;
    }

    /// <summary>Records a process AccountDeck started so it (and its children) can be attributed later.</summary>
    public static void Remember(Process process, string accountId)
    {
        DateTime start;
        try { start = process.StartTime.ToUniversalTime(); }
        catch { return; }

        lock (Gate)
        {
            var marks = LiveMarks();
            marks.RemoveAll(m => m.Pid == process.Id);
            marks.Add(new LaunchMark(process.Id, start, accountId));
            try
            {
                Directory.CreateDirectory(AppPaths.Root);
                File.WriteAllText(AppPaths.LaunchLogFile, JsonSerializer.Serialize(marks));
            }
            catch (IOException) { }
        }
    }

    static string? MatchByLaunchLog(ProcessFacts facts, List<LaunchMark> marks, Dictionary<int, ProcessFacts?> cache)
    {
        if (marks.Count == 0) return null;
        ProcessFacts? current = facts;
        for (var depth = 0; current is not null && depth < MaxAncestorDepth; depth++)
        {
            var hit = marks.FirstOrDefault(m => m.Pid == current.Pid && Math.Abs((m.StartUtc - current.StartUtc).TotalSeconds) < 2);
            if (hit is not null) return hit.AccountId;
            if (current.ParentPid <= 4) break;

            var parentPid = current.ParentPid;
            if (!cache.TryGetValue(parentPid, out var parent))
                cache[parentPid] = parent = ProcessInspector.Read(parentPid, includeCommandLine: false);
            // A parent that started after its child is a recycled id, not the real parent.
            if (parent is null || parent.StartUtc > current.StartUtc) break;
            current = parent;
        }
        return null;
    }

    static string? MatchByCommandLine(AppEntry app, ProcessFacts facts)
    {
        var commandLine = facts.CommandLine?.ToUpperInvariant() ?? "";
        var isolated = app.Accounts
            .Where(a => !a.IsDefault)
            .Select(a => (Account: a, Needle: SafeNormalize(a.DataDir)))
            .Where(x => x.Needle.Length > 0)
            .OrderByDescending(x => x.Needle.Length); // "C:\x\ab" must win over "C:\x\a"

        foreach (var (account, needle) in isolated)
        {
            var at = commandLine.IndexOf(needle, StringComparison.Ordinal);
            if (at < 0) continue;
            var after = at + needle.Length;
            if (after >= commandLine.Length || commandLine[after] is '"' or ' ' or '\\' or '/')
                return account.Id;
        }

        // Nothing isolated matched (and the launch log, checked first, knew nothing):
        // the process belongs to the app's normal sign-in.
        return app.Accounts.FirstOrDefault(a => a.IsDefault)?.Id;
    }

    static string SafeNormalize(string dir)
    {
        try { return AppPaths.NormalizeDir(dir); }
        catch { return ""; }
    }

    static List<LaunchMark> LiveMarks()
    {
        lock (Gate)
        {
            if (_marks is null)
            {
                try
                {
                    _marks = File.Exists(AppPaths.LaunchLogFile)
                        ? JsonSerializer.Deserialize<List<LaunchMark>>(File.ReadAllText(AppPaths.LaunchLogFile)) ?? new()
                        : new();
                }
                catch { _marks = new(); }
            }

            _marks.RemoveAll(m =>
            {
                var facts = ProcessInspector.Read(m.Pid, includeCommandLine: false);
                return facts is null || Math.Abs((facts.StartUtc - m.StartUtc).TotalSeconds) >= 2;
            });
            return _marks;
        }
    }
}
