using AdvancedSID.Core;
using Xunit;

namespace AdvancedSID.Core.Tests;

public class OldProfileTests
{
    private const string Sid = "S-1-5-21-1111111111-2222222222-3333333333-1111";
    private static readonly StorageLocation Profiles = new() { Key = "1", Name = "User Profiles", ShortName = "P", Type = ContainerType.Profile, Path = "p" };
    private static readonly StorageLocation Office = new() { Key = "2", Name = "Office Profiles", ShortName = "O", Type = ContainerType.Office, Path = "o" };

    private static ProfileContainer Container(StorageLocation loc, string user, string? lockServer, params string[] extraFiles)
    {
        var c = new ProfileContainer { Location = loc, FolderPath = $"{loc.Path}/{Sid}_{user}", Sid = Sid, FolderUserName = user };
        c.Files.Add(new ProfileFile { FullPath = "d.VHDX", Kind = ProfileFileKind.Disk, Size = 100 });
        if (lockServer is not null)
        {
            c.Files.Add(new ProfileFile { FullPath = "d.VHDX.lock", Kind = ProfileFileKind.Lock, Lock = new LockDetails { Server = lockServer } });
        }

        foreach (var f in extraFiles)
        {
            c.Files.Add(new ProfileFile { FullPath = f, Kind = ProfileScanner.Classify(f), Size = 50 });
        }

        return c;
    }

    [Theory]
    [InlineData("Jane.Doe_old", true)]
    [InlineData("Jane.Doe.old", true)]
    [InlineData("Jane.Doe-OLD", true)]
    [InlineData("Jane.Doe_old2", true)]
    [InlineData("Jane.Doe_old_20251125", true)]
    [InlineData("Jane.Doe.old.1", true)]
    [InlineData("Jane.Doe", false)]
    [InlineData("Harold", false)]
    [InlineData("Jane.Oldfield", false)]
    [InlineData("old", false)]
    public void Recognises_old_folder_names(string name, bool expected) => Assert.Equal(expected, FolderNameParser.IsOldName(name));

    [Theory]
    [InlineData("Profile_Jane.Doe.VHDX.old", ProfileFileKind.OldDisk)]
    [InlineData("ODFC_Jane.Doe.vhdx_old2", ProfileFileKind.OldDisk)]
    [InlineData("Profile_Jane.Doe.vhd.old", ProfileFileKind.OldDisk)]
    [InlineData("Profile_Jane.Doe.VHDX", ProfileFileKind.Disk)]
    [InlineData("Profile_Jane.Doe.VHDX.lock", ProfileFileKind.Lock)]
    public void Recognises_old_disk_files(string name, ProfileFileKind kind) => Assert.Equal(kind, ProfileScanner.Classify(name));

    [Fact]
    public void Old_folder_is_not_a_lock_mismatch_but_is_reported()
    {
        var users = ProfileScanner.GroupByUser(new[]
        {
            Container(Profiles, "Jane.Doe_old", null),
            Container(Profiles, "Jane.Doe", "H1"),
            Container(Office, "Jane.Doe", "H1"),
        });

        var user = Assert.Single(users);
        Assert.Equal("Jane.Doe", user.UserName);
        Assert.False(user.HasLockMismatch);
        Assert.True(user.HasOldProfiles);
        Assert.Equal("Jane.Doe_old", Assert.Single(user.OldContainers).FolderUserName);
        Assert.Equal(300, user.TotalDiskSize);
        Assert.Equal(100, user.OldDataSize);
    }

    [Fact]
    public void Real_mismatch_is_still_found_alongside_an_old_folder()
    {
        var user = Assert.Single(ProfileScanner.GroupByUser(new[]
        {
            Container(Profiles, "Jane.Doe_old", null),
            Container(Profiles, "Jane.Doe", null),
            Container(Office, "Jane.Doe", "H1"),
        }));

        Assert.True(user.HasLockMismatch);
        Assert.Equal(Profiles, Assert.Single(user.UnlockedWhileOthersLocked).Location);
    }

    [Fact]
    public void Surname_old_is_not_treated_as_old_when_ad_confirms_the_account()
    {
        var user = Assert.Single(ProfileScanner.GroupByUser(new[] { Container(Profiles, "Ben.Old", "H1"), Container(Office, "Ben.Old", null) }));
        user.Directory = new DirectoryUser { State = DirectoryLookupState.Found, AccountName = @"CONTOSO\Ben.Old" };

        Assert.False(user.HasOldProfiles);
        Assert.True(user.HasLockMismatch); // a genuine mismatch is not hidden
    }

    [Fact]
    public void Renamed_disk_file_is_reported_as_old()
    {
        var user = Assert.Single(ProfileScanner.GroupByUser(new[] { Container(Profiles, "Jane.Doe", "H1", "Profile_Jane.Doe.VHDX.old") }));

        Assert.True(user.HasOldProfiles);
        Assert.Empty(user.OldContainers);
        Assert.Equal(50, user.OldDataSize);
        Assert.False(user.HasLockMismatch);
    }
}
