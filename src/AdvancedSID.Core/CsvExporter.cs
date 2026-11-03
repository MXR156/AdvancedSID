using System.Globalization;
using System.Text;

namespace AdvancedSID.Core;

public static class CsvExporter
{
    public static void Export(ScanResult result, IEnumerable<UserRecord> users, string path)
    {
        var sb = new StringBuilder();
        sb.AppendLine(string.Join(",", new[]
        {
            "UserName", "DisplayName", "Account", "AccountStatus", "SID", "Location", "ContainerType",
            "Folder", "Disks", "DiskSizeGB", "LastModified", "Locked", "LockServers", "LockFiles", "LockMismatch",
        }.Select(Escape)));

        foreach (var user in users)
        {
            foreach (var c in user.Containers)
            {
                sb.AppendLine(string.Join(",", new[]
                {
                    user.UserName,
                    user.Directory.DisplayName ?? string.Empty,
                    user.Directory.AccountName ?? string.Empty,
                    AccountStatus(user),
                    user.Sid ?? string.Empty,
                    c.Location.Name,
                    c.Location.Type.ToString(),
                    c.FolderPath,
                    string.Join("; ", c.Disks.Select(d => d.Name)),
                    (c.DiskSize / 1024d / 1024d / 1024d).ToString("0.00", CultureInfo.InvariantCulture),
                    c.LastWriteTime.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
                    c.IsLocked ? "Yes" : "No",
                    string.Join("; ", c.Locks.Select(l => l.Lock?.Server ?? "(unknown)")),
                    string.Join("; ", c.Locks.Select(l => l.Name)),
                    user.LockMismatch ?? string.Empty,
                }.Select(Escape)));
            }
        }

        File.WriteAllText(path, sb.ToString(), new UTF8Encoding(true));
    }

    public static string AccountStatus(UserRecord user) => user.Directory.State switch
    {
        DirectoryLookupState.NotLookedUp => "Not looked up",
        DirectoryLookupState.NotFound => "Orphaned",
        DirectoryLookupState.Error => "Unknown",
        _ => user.Directory.Enabled switch
        {
            false => "Disabled",
            true => "Enabled",
            null => "Found",
        },
    };

    private static string Escape(string value) =>
        value.IndexOfAny(new[] { ',', '"', '\n', '\r' }) >= 0 ? "\"" + value.Replace("\"", "\"\"") + "\"" : value;
}
