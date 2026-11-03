namespace AdvancedSID.Core;

/// <summary>The kind of FSLogix container stored in a location.</summary>
public enum ContainerType
{
    /// <summary>Profile container (Profile_user.VHDX) or a UPD (UVHD-SID.vhdx).</summary>
    Profile,

    /// <summary>Office / ODFC container (ODFC_user.VHDX).</summary>
    Office,
}

/// <summary>A share / folder that holds one sub-folder per user.</summary>
public sealed class StorageLocation
{
    public required string Key { get; init; }

    /// <summary>Long display name, e.g. "Profile - Location 1".</summary>
    public required string Name { get; init; }

    /// <summary>Short name used for grid column headers, e.g. "Profile 1".</summary>
    public required string ShortName { get; init; }

    public required ContainerType Type { get; init; }

    public required string Path { get; init; }

    public override string ToString() => $"{Name} ({Path})";
}

public enum ProfileFileKind
{
    Disk,
    Lock,
    Meta,
    Other,
}

/// <summary>A single file inside a user's container folder.</summary>
public sealed class ProfileFile
{
    public required string FullPath { get; init; }

    public string Name => Path.GetFileName(FullPath);

    public required ProfileFileKind Kind { get; init; }

    public long Size { get; init; }

    public DateTime LastWriteTime { get; init; }

    /// <summary>Populated for <see cref="ProfileFileKind.Lock"/> files.</summary>
    public LockDetails? Lock { get; init; }
}

/// <summary>What could be read out of a .lock file.</summary>
public sealed class LockDetails
{
    /// <summary>Best guess of the host holding the lock (as written in the file, often an FQDN).</summary>
    public string? Server { get; init; }

    /// <summary>Every host-like string found in the file, best first.</summary>
    public IReadOnlyList<string> Candidates { get; init; } = Array.Empty<string>();

    /// <summary>Set when the file could not be read (e.g. held open exclusively).</summary>
    public string? ReadError { get; init; }

    /// <summary>Host name without the DNS suffix, e.g. "DCLAVDPRDSH-2".</summary>
    public string? ShortServer => Server is null ? null : Server.Split('.')[0];
}

/// <summary>One user folder inside one storage location.</summary>
public sealed class ProfileContainer
{
    public required StorageLocation Location { get; init; }

    public required string FolderPath { get; init; }

    public string FolderName => Path.GetFileName(FolderPath);

    public string? Sid { get; init; }

    public required string FolderUserName { get; init; }

    public DateTime FolderLastWriteTime { get; init; }

    public List<ProfileFile> Files { get; } = new();

    public string? ScanError { get; init; }

    public IEnumerable<ProfileFile> Disks => Files.Where(f => f.Kind == ProfileFileKind.Disk);

    public IEnumerable<ProfileFile> Locks => Files.Where(f => f.Kind == ProfileFileKind.Lock);

    public bool IsLocked => Files.Any(f => f.Kind == ProfileFileKind.Lock);

    public bool HasDisk => Files.Any(f => f.Kind == ProfileFileKind.Disk);

    public long DiskSize => Disks.Sum(d => d.Size);

    public DateTime LastWriteTime =>
        Files.Count == 0 ? FolderLastWriteTime : Files.Max(f => f.LastWriteTime);

    /// <summary>Distinct short server names holding locks on this container.</summary>
    public IEnumerable<string> LockServers =>
        Locks.Select(l => l.Lock?.ShortServer ?? "(unknown)").Distinct(StringComparer.OrdinalIgnoreCase);
}

public enum DirectoryLookupState
{
    NotLookedUp,
    Found,
    NotFound,
    Error,
}

/// <summary>Result of looking a SID up in Active Directory.</summary>
public sealed class DirectoryUser
{
    public DirectoryLookupState State { get; set; } = DirectoryLookupState.NotLookedUp;

    /// <summary>DOMAIN\sAMAccountName.</summary>
    public string? AccountName { get; set; }

    public string? DisplayName { get; set; }

    public bool? Enabled { get; set; }

    public DateTime? LastLogon { get; set; }

    public string? Email { get; set; }

    public string? DistinguishedName { get; set; }

    public string? Error { get; set; }

    public static DirectoryUser NotLookedUp { get; } = new();
}

/// <summary>All containers that belong to a single user (grouped by SID).</summary>
public sealed class UserRecord
{
    public required string Key { get; init; }

    public string? Sid { get; init; }

    /// <summary>User name as taken from the folder name(s).</summary>
    public required string UserName { get; init; }

    public List<ProfileContainer> Containers { get; } = new();

    public DirectoryUser Directory { get; set; } = DirectoryUser.NotLookedUp;

    public bool IsLocked => Containers.Any(c => c.IsLocked);

    public bool IsOrphaned => Directory.State == DirectoryLookupState.NotFound;

    public bool IsDisabled => Directory.Enabled == false;

    public IEnumerable<ProfileContainer> ContainersIn(StorageLocation location) =>
        Containers.Where(c => ReferenceEquals(c.Location, location) || c.Location.Key == location.Key);

    public IEnumerable<string> LockServers =>
        Containers.SelectMany(c => c.LockServers).Distinct(StringComparer.OrdinalIgnoreCase);

    public DateTime LastWriteTime =>
        Containers.Count == 0 ? DateTime.MinValue : Containers.Max(c => c.LastWriteTime);

    public long TotalDiskSize => Containers.Sum(c => c.DiskSize);
}

public sealed class ScanResult
{
    public required IReadOnlyList<StorageLocation> Locations { get; init; }

    public required List<UserRecord> Users { get; init; }

    public List<string> Errors { get; } = new();

    public DateTime ScannedAt { get; init; } = DateTime.Now;
}

public readonly record struct ScanProgress(string Message, int Done, int Total);
