using System.Text;
using AdvancedSID.Core;
using Xunit;

namespace AdvancedSID.Core.Tests;

public sealed class ProfileScannerTests : IDisposable
{
    private const string Sid = "S-1-5-21-839522115-583907252-2147053123-448107";
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
        MakeUser(p1, "Profile", "Lynne.Todd", null);
        MakeUser(o1, "ODFC", "Lynne.Todd", "DCLAVDPRDSH-2.dcl.co.uk");
        MakeUser(p2, "Profile", "Lynne.Todd", null);
        MakeUser(o2, "ODFC", "Lynne.Todd", null);
        Directory.CreateDirectory(Path.Combine(p1.Path, "S-1-5-21-1-2-3-1001_Other.User"));

        var result = new ProfileScanner().Scan(new[] { p1, o1, p2, o2 });

        Assert.Empty(result.Errors);
        Assert.Equal(2, result.Users.Count);
        var lynne = result.Users.Single(u => u.Sid == Sid);
        Assert.Equal("Lynne.Todd", lynne.UserName);
        Assert.Equal(4, lynne.Containers.Count);
        Assert.True(lynne.IsLocked);
        var locked = Assert.Single(lynne.Containers, c => c.IsLocked);
        Assert.Same(o1, locked.Location);
        Assert.Equal(new[] { "DCLAVDPRDSH-2" }, locked.LockServers);
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
