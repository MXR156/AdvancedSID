using System.Globalization;
using System.Text;

namespace AdvancedSID.Core;

public enum ServerCheckStatus
{
    /// <summary>The server answered and the user has no session there.</summary>
    NotLoggedOn,

    /// <summary>The user has a session (active or disconnected) on the server. Removal is blocked.</summary>
    LoggedOn,

    /// <summary>The server could not be asked (offline, access denied...).</summary>
    CouldNotCheck,

    /// <summary>The lock file did not contain a readable server name.</summary>
    NoServerName,
}

public sealed record ServerCheck(string? Server, ServerCheckStatus Status, string Detail);

public sealed record LockTarget(ProfileContainer Container, ProfileFile File);

public sealed record LockRemovalOutcome(LockTarget Target, bool Deleted, string? Error);

/// <summary>
/// Safely removes FSLogix .lock files for ONE user. Every file is re-validated immediately before
/// deletion: it must be a .lock file, directly inside one of that user's container folders (exact
/// SID match), which is directly inside a configured storage location. Nothing else is touched.
/// </summary>
public static class LockRemoval
{
    /// <summary>The user's lock files, optionally narrowed to specific paths.</summary>
    public static IReadOnlyList<LockTarget> FindTargets(UserRecord user, IReadOnlyCollection<string>? onlyPaths = null) =>
        user.Containers
            .SelectMany(c => c.Locks.Select(f => new LockTarget(c, f)))
            .Where(t => onlyPaths is null || onlyPaths.Contains(t.File.FullPath, StringComparer.OrdinalIgnoreCase))
            .ToList();

    /// <summary>Asks each server named in the locks whether the user is logged on there.</summary>
    public static IReadOnlyList<ServerCheck> CheckServers(IEnumerable<LockTarget> targets, UserRecord user, Func<string, SessionQueryResult> query)
    {
        var names = UserNames(user);
        var checks = new List<ServerCheck>();
        var servers = targets
            .Select(t => t.File.Lock?.Server)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        foreach (var server in servers)
        {
            if (string.IsNullOrWhiteSpace(server))
            {
                checks.Add(new ServerCheck(null, ServerCheckStatus.NoServerName, "The lock file doesn't name a server, so the user's sessions can't be checked."));
                continue;
            }

            SessionQueryResult result;
            try
            {
                result = query(server);
            }
            catch (Exception ex)
            {
                result = SessionQueryResult.Failed(ex.Message);
            }

            if (!result.Success)
            {
                checks.Add(new ServerCheck(server, ServerCheckStatus.CouldNotCheck, result.Error!));
                continue;
            }

            var sessions = result.Sessions.Where(s => names.Contains(s.UserName)).ToList();
            checks.Add(sessions.Count > 0
                ? new ServerCheck(server, ServerCheckStatus.LoggedOn, string.Join(", ", sessions.Select(s => $"session {s.SessionId} ({s.State})")))
                : new ServerCheck(server, ServerCheckStatus.NotLoggedOn, "No session for this user."));
        }

        return checks;
    }

    /// <summary>Returns null if the path is safe to delete for this user, otherwise the reason it isn't.</summary>
    public static string? Validate(string lockPath, UserRecord user, IReadOnlyList<StorageLocation> locations)
    {
        string full;
        try
        {
            full = Path.GetFullPath(lockPath);
        }
        catch (Exception ex)
        {
            return "Invalid path: " + ex.Message;
        }

        if (!full.EndsWith(".lock", StringComparison.OrdinalIgnoreCase))
        {
            return "Not a .lock file.";
        }

        if (Directory.Exists(full))
        {
            return "Path is a folder, not a file.";
        }

        if (!File.Exists(full))
        {
            return "File no longer exists.";
        }

        if (File.GetAttributes(full).HasFlag(FileAttributes.ReparsePoint))
        {
            return "File is a link, not a real lock file.";
        }

        var folder = Path.GetDirectoryName(full);
        if (folder is null)
        {
            return "File has no parent folder.";
        }

        // Must be directly inside one of this user's container folders...
        if (!user.Containers.Any(c => SamePath(c.FolderPath, folder)))
        {
            return "File is not directly inside one of this user's profile folders.";
        }

        // ...whose name carries exactly this user's SID (not just one that starts the same)...
        var (folderSid, folderUser) = FolderNameParser.Parse(Path.GetFileName(folder));
        if (user.Sid is not null
            ? !string.Equals(folderSid, user.Sid, StringComparison.OrdinalIgnoreCase)
            : folderSid is not null || !string.Equals(folderUser, user.UserName, StringComparison.OrdinalIgnoreCase))
        {
            return "Folder name doesn't match this user's SID.";
        }

        // ...which is directly inside a configured storage location.
        var root = Path.GetDirectoryName(folder);
        if (root is null || !locations.Any(l => SamePath(l.Path, root)))
        {
            return "Folder is not directly inside a configured storage location.";
        }

        return null;
    }

    /// <summary>Validates then deletes each target. Never throws for a single file.</summary>
    public static IReadOnlyList<LockRemovalOutcome> Remove(IEnumerable<LockTarget> targets, UserRecord user, IReadOnlyList<StorageLocation> locations)
    {
        var outcomes = new List<LockRemovalOutcome>();
        foreach (var target in targets)
        {
            var problem = Validate(target.File.FullPath, user, locations);
            if (problem is not null)
            {
                outcomes.Add(new LockRemovalOutcome(target, false, problem));
                continue;
            }

            try
            {
                // If FSLogix has touched the lock since it was checked, the user may have just
                // logged on: leave it alone.
                var full = Path.GetFullPath(target.File.FullPath);
                if (target.File.LastWriteTime != default && File.GetLastWriteTime(full) != target.File.LastWriteTime)
                {
                    outcomes.Add(new LockRemovalOutcome(target, false, "Lock file changed after it was checked (the user may have just logged on). Not removed."));
                    continue;
                }

                File.Delete(full);
                outcomes.Add(new LockRemovalOutcome(target, true, null));
            }
            catch (IOException ex)
            {
                outcomes.Add(new LockRemovalOutcome(target, false, "In use or unavailable: " + ex.Message));
            }
            catch (UnauthorizedAccessException ex)
            {
                outcomes.Add(new LockRemovalOutcome(target, false, "Access denied: " + ex.Message));
            }
        }

        return outcomes;
    }

    /// <summary>Appends one line per file to a tab-separated audit log.</summary>
    public static void AppendLog(string logPath, UserRecord user, IEnumerable<LockRemovalOutcome> outcomes, IEnumerable<ServerCheck> checks)
    {
        var dir = Path.GetDirectoryName(logPath);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }

        var serverInfo = string.Join("; ", checks.Select(c => $"{c.Server ?? "(none)"}={c.Status}"));
        var sb = new StringBuilder();
        if (!File.Exists(logPath))
        {
            sb.AppendLine("Time\tRemovedBy\tUser\tSID\tLockServer\tResult\tFile\tServerChecks");
        }

        foreach (var o in outcomes)
        {
            sb.Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)).Append('\t')
              .Append(Environment.UserDomainName).Append('\\').Append(Environment.UserName).Append('\t')
              .Append(user.UserName).Append('\t')
              .Append(user.Sid).Append('\t')
              .Append(o.Target.File.Lock?.Server).Append('\t')
              .Append(o.Deleted ? "Deleted" : "FAILED: " + o.Error).Append('\t')
              .Append(o.Target.File.FullPath).Append('\t')
              .AppendLine(serverInfo);
        }

        File.AppendAllText(logPath, sb.ToString());
    }

    /// <summary>Names the user could appear under in a session list.</summary>
    private static HashSet<string> UserNames(UserRecord user)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { user.UserName };
        if (user.Directory.AccountName is { } account)
        {
            names.Add(account.Contains('\\') ? account[(account.IndexOf('\\') + 1)..] : account);
        }

        names.RemoveWhere(string.IsNullOrWhiteSpace);
        return names;
    }

    private static bool SamePath(string a, string b) =>
        string.Equals(
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(a)),
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(b)),
            StringComparison.OrdinalIgnoreCase);
}
