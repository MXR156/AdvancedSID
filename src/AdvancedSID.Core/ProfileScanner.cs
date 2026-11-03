using System.Collections.Concurrent;

namespace AdvancedSID.Core;

/// <summary>Walks the storage locations and builds one <see cref="UserRecord"/> per user.</summary>
public sealed class ProfileScanner
{
    /// <summary>How many user folders to read in parallel (network shares are latency bound).</summary>
    public int MaxParallelism { get; init; } = 16;

    public ScanResult Scan(IReadOnlyList<StorageLocation> locations, IProgress<ScanProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        var errors = new ConcurrentBag<string>();
        var work = new List<(StorageLocation Location, string Folder)>();

        foreach (var location in locations)
        {
            cancellationToken.ThrowIfCancellationRequested();
            progress?.Report(new ScanProgress($"Listing {location.Path}...", 0, 0));

            if (!Directory.Exists(location.Path))
            {
                errors.Add($"{location.Name}: path not found or not accessible - {location.Path}");
                continue;
            }

            try
            {
                work.AddRange(Directory.EnumerateDirectories(location.Path).Select(d => (location, d)));
            }
            catch (Exception ex)
            {
                errors.Add($"{location.Name}: {ex.Message}");
            }
        }

        var containers = new ConcurrentBag<ProfileContainer>();
        var done = 0;
        Parallel.ForEach(
            work,
            new ParallelOptions { MaxDegreeOfParallelism = Math.Max(1, MaxParallelism), CancellationToken = cancellationToken },
            item =>
            {
                var container = ScanFolder(item.Location, item.Folder);
                containers.Add(container);
                if (container.ScanError is not null)
                {
                    errors.Add($"{item.Location.Name}: {container.FolderName}: {container.ScanError}");
                }

                var n = Interlocked.Increment(ref done);
                if (n % 10 == 0 || n == work.Count)
                {
                    progress?.Report(new ScanProgress($"Scanning folders ({n}/{work.Count})...", n, work.Count));
                }
            });

        var result = new ScanResult
        {
            Locations = locations,
            Users = GroupByUser(containers),
        };
        result.Errors.AddRange(errors.OrderBy(e => e, StringComparer.OrdinalIgnoreCase));
        return result;
    }

    /// <summary>Reads a single user folder (also used to refresh one user after a change).</summary>
    public static ProfileContainer ScanFolder(StorageLocation location, string folder)
    {
        var (sid, userName) = FolderNameParser.Parse(Path.GetFileName(folder));
        DateTime folderTime = default;
        string? error = null;
        var files = new List<ProfileFile>();

        try
        {
            var dir = new DirectoryInfo(folder);
            folderTime = dir.LastWriteTime;
            foreach (var file in dir.EnumerateFiles())
            {
                files.Add(ReadFile(file));
            }
        }
        catch (Exception ex)
        {
            error = ex.Message;
        }

        // UPD stores the SID in the file name (UVHD-<SID>.vhdx) rather than in a folder.
        sid ??= files.Where(f => f.Kind == ProfileFileKind.Disk).Select(f => FolderNameParser.FindSid(f.Name)).FirstOrDefault(s => s is not null);

        var container = new ProfileContainer
        {
            Location = location,
            FolderPath = folder,
            Sid = sid,
            FolderUserName = userName,
            FolderLastWriteTime = folderTime,
            ScanError = error,
        };
        container.Files.AddRange(files.OrderBy(f => f.Kind).ThenBy(f => f.Name, StringComparer.OrdinalIgnoreCase));
        return container;
    }

    public static ProfileFileKind Classify(string fileName)
    {
        if (fileName.EndsWith(".lock", StringComparison.OrdinalIgnoreCase))
        {
            return ProfileFileKind.Lock;
        }

        if (fileName.EndsWith(".meta", StringComparison.OrdinalIgnoreCase))
        {
            return ProfileFileKind.Meta;
        }

        if (fileName.EndsWith(".vhdx", StringComparison.OrdinalIgnoreCase) || fileName.EndsWith(".vhd", StringComparison.OrdinalIgnoreCase))
        {
            return ProfileFileKind.Disk;
        }

        return ProfileFileKind.Other;
    }

    private static ProfileFile ReadFile(FileInfo file)
    {
        var kind = Classify(file.Name);
        long size = 0;
        DateTime modified = default;
        try
        {
            size = file.Length;
            modified = file.LastWriteTime;
        }
        catch
        {
            // Attributes unavailable (e.g. file removed mid-scan) - keep defaults.
        }

        return new ProfileFile
        {
            FullPath = file.FullName,
            Kind = kind,
            Size = size,
            LastWriteTime = modified,
            Lock = kind == ProfileFileKind.Lock ? LockFileParser.Read(file.FullName) : null,
        };
    }

    public static List<UserRecord> GroupByUser(IEnumerable<ProfileContainer> containers)
    {
        return containers
            .GroupBy(c => c.Sid ?? "name:" + c.FolderUserName.ToUpperInvariant(), StringComparer.OrdinalIgnoreCase)
            .Select(g =>
            {
                var list = g.OrderBy(c => c.Location.Key, StringComparer.Ordinal).ToList();
                var name = list
                    .Select(c => c.FolderUserName)
                    .Where(n => !string.IsNullOrWhiteSpace(n))
                    .GroupBy(n => n, StringComparer.OrdinalIgnoreCase)
                    .OrderByDescending(n => n.Count())
                    .Select(n => n.Key)
                    .FirstOrDefault() ?? list[0].FolderName;

                var user = new UserRecord { Key = g.Key, Sid = list[0].Sid, UserName = name };
                user.Containers.AddRange(list);
                return user;
            })
            .OrderBy(u => u.UserName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}
