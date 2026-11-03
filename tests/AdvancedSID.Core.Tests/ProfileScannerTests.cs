using System.Text;
using AdvancedSID.Core;
using Xunit;

namespace AdvancedSID.Core.Tests;

public sealed class ProfileScannerTests : IDisposable
{
    private const string Sid = "S-1-5-21-1111111111-2222222222-3333333333-448107";
    private readonly string root = Path.Combine(Path.GetTempPath(), "advsid-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, true);
        }
    }

    private StorageLocation Location(string key, ContainerType type) =>
        new() { Key = key, Name = key, ShortName = key, Type = type, Path = Path.Combine(root, key) };

    private void MakeUser(StorageLocation loc, string prefix, string user, string? lockServer)
    {
        var dir = Directory.CreateDirectory(Path.Combine(loc.Path, $"{Sid}_{user}")).FullName;
        var disk = Path.Combine(dir, $"{prefix}_{user}.VHDX");
        File.WriteAllBytes(disk, new byte[1024]);
        File.WriteAllText(disk + ".meta", "meta");
        if (lockServer is not null)
        {
            File.WriteAllBytes(disk + ".lock", Encoding.Unicode.GetBytes("    堬ꃰơ  " + lockServer + new string(' ', 200)));
        }
    }

    [Fact]
    public void Groups_four_locations_under_one_user_and_finds_lock()
    {
        var p1 = Location("1-Profile", ContainerType.Profile);
        var o1 = Location("2-Office", ContainerType.Office);
        var p2 = Location("3-Profile", ContainerType.Profile);
        var o2 = Location("4-Office", ContainerType.Office);
        MakeUser(p1, "Profile", "Jane.Doe", null);
        MakeUser(o1, "ODFC", "Jane.Doe", "AVD-HOST-02.contoso.local");
        MakeUser(p2, "Profile", "Jane.Doe", null);
        MakeUser(o2, "ODFC", "Jane.Doe", null);
        Directory.CreateDirectory(Path.Combine(p1.Path, "S-1-5-21-1-2-3-1001_Other.User"));

        var result = new ProfileScanner().Scan(new[] { p1, o1, p2, o2 });

        Assert.Empty(result.Errors);
        Assert.Equal(2, result.Users.Count);
        var jane = result.Users.Single(u => u.Sid == Sid);
        Assert.Equal("Jane.Doe", jane.UserName);
        Assert.Equal(4, jane.Containers.Count);
        Assert.True(jane.IsLocked);
        var locked = Assert.Single(jane.Containers, c => c.IsLocked);
        Assert.Same(o1, locked.Location);
        Assert.Equal(new[] { "AVD-HOST-02" }, locked.LockServers);
        Assert.Equal(1024, locked.DiskSize);

        var other = result.Users.Single(u => u.Sid != Sid);
        Assert.False(other.Containers[0].HasDisk);
    }

    [Fact]
    public void Missing_location_is_reported_not_thrown()
    {
        var result = new ProfileScanner().Scan(new[] { Location("nope", ContainerType.Profile) });
        Assert.Single(result.Errors);
        Assert.Empty(result.Users);
    }

    [Theory]
    [InlineData("Profile_x.VHDX", ProfileFileKind.Disk)]
    [InlineData("ODFC_x.vhd", ProfileFileKind.Disk)]
    [InlineData("ODFC_x.VHDX.lock", ProfileFileKind.Lock)]
    [InlineData("ODFC_x.VHDX.meta", ProfileFileKind.Meta)]
    [InlineData("desktop.ini", ProfileFileKind.Other)]
    public void Classifies_files(string name, ProfileFileKind kind) => Assert.Equal(kind, ProfileScanner.Classify(name));
}

public sealed class LockMismatchTests
{
    private static int counter;

    private static ProfileContainer Container(string location, bool disk, string? lockServer)
    {
        var loc = new StorageLocation { Key = location, Name = location, ShortName = location, Type = ContainerType.Profile, Path = "x" };
        var c = new ProfileContainer { Location = loc, FolderPath = "x" + Interlocked.Increment(ref counter), FolderUserName = "u" };
        if (disk)
        {
            c.Files.Add(new ProfileFile { FullPath = "a.vhdx", Kind = ProfileFileKind.Disk });
        }

        if (lockServer is not null)
        {
            c.Files.Add(new ProfileFile { FullPath = "a.vhdx.lock", Kind = ProfileFileKind.Lock, Lock = new LockDetails { Server = lockServer } });
        }

        return c;
    }

    private static UserRecord User(params ProfileContainer[] containers)
    {
        var u = new UserRecord { Key = "k", UserName = "u" };
        u.Containers.AddRange(containers);
        return u;
    }

    [Fact]
    public void All_locked_by_same_server_is_fine()
    {
        var u = User(Container("User Profiles", true, "H1.x"), Container("Office Profiles", true, "H1.x"));
        Assert.Null(u.LockMismatch);
    }

    [Fact]
    public void Nothing_locked_is_fine()
    {
        Assert.Null(User(Container("User Profiles", true, null), Container("Office Profiles", true, null)).LockMismatch);
    }

    [Fact]
    public void Office_locked_but_profile_not_is_a_mismatch()
    {
        var u = User(Container("User Profiles", true, null), Container("Office Profiles", true, "H1.x"));
        Assert.True(u.HasLockMismatch);
        Assert.Contains("NOT locked: User Profiles", u.LockMismatch);
        Assert.Equal("User Profiles", Assert.Single(u.UnlockedWhileOthersLocked).Location.Name);
    }

    [Fact]
    public void Folder_without_a_disk_is_not_a_mismatch()
    {
        Assert.Null(User(Container("User Profiles", true, "H1.x"), Container("Office Profiles", false, null)).LockMismatch);
    }

    [Fact]
    public void Different_servers_is_a_mismatch()
    {
        var u = User(Container("User Profiles", true, "H1.x"), Container("Office Profiles", true, "H2.x"));
        Assert.Contains("different servers: H1, H2", u.LockMismatch);
    }
}
