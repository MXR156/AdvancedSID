using System.Text;
using AdvancedSID.Core;
using Xunit;

namespace AdvancedSID.Core.Tests;

public sealed class LockRemovalTests : IDisposable
{
    private const string Sid = "S-1-5-21-1111111111-2222222222-3333333333-1111";

    // Starts with the same digits as Sid - a naive "*SID*" match would catch this user too.
    private const string OtherSid = "S-1-5-21-1111111111-2222222222-3333333333-11112";

    private readonly string root = Path.Combine(Path.GetTempPath(), "advsid-lock-" + Guid.NewGuid().ToString("N"));
    private readonly StorageLocation profiles;
    private readonly StorageLocation office;

    public LockRemovalTests()
    {
        profiles = new StorageLocation { Key = "1", Name = "User Profiles", ShortName = "P", Type = ContainerType.Profile, Path = Path.Combine(root, "Profiles") };
        office = new StorageLocation { Key = "2", Name = "Office Profiles", ShortName = "O", Type = ContainerType.Office, Path = Path.Combine(root, "Office") };
    }

    private StorageLocation[] Locations => new[] { profiles, office };

    public void Dispose()
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, true);
        }
    }

    private static string MakeContainer(StorageLocation loc, string sid, string user, string prefix, string? lockServer)
    {
        var dir = Directory.CreateDirectory(Path.Combine(loc.Path, $"{sid}_{user}")).FullName;
        var disk = Path.Combine(dir, $"{prefix}_{user}.VHDX");
        File.WriteAllBytes(disk, new byte[16]);
        File.WriteAllText(disk + ".meta", "meta");
        if (lockServer is not null)
        {
            File.WriteAllBytes(disk + ".lock", Encoding.Unicode.GetBytes("  " + lockServer + new string(' ', 50)));
        }

        return dir;
    }

    private UserRecord ScanUser(string sid) =>
        new ProfileScanner().Scan(Locations).Users.Single(u => u.Sid == sid);

    [Fact]
    public void Removes_only_the_lock_files_of_the_selected_user()
    {
        var mine1 = MakeContainer(profiles, Sid, "Jane.Doe", "Profile", "HOST-1.contoso.local");
        var mine2 = MakeContainer(office, Sid, "Jane.Doe", "ODFC", "HOST-1.contoso.local");
        var other = MakeContainer(profiles, OtherSid, "John.Smith", "Profile", "HOST-2.contoso.local");

        var user = ScanUser(Sid);
        var outcomes = LockRemoval.Remove(LockRemoval.FindTargets(user), user, Locations);

        Assert.Equal(2, outcomes.Count);
        Assert.All(outcomes, o => Assert.True(o.Deleted, o.Error));
        Assert.Empty(Directory.GetFiles(mine1, "*.lock"));
        Assert.Empty(Directory.GetFiles(mine2, "*.lock"));

        // Disks and meta files untouched; the other user's lock untouched.
        Assert.Equal(2, Directory.GetFiles(mine1).Length);
        Assert.Equal(2, Directory.GetFiles(mine2).Length);
        Assert.Single(Directory.GetFiles(other, "*.lock"));
    }

    [Fact]
    public void Can_remove_a_single_lock()
    {
        MakeContainer(profiles, Sid, "Jane.Doe", "Profile", "HOST-1");
        var officeDir = MakeContainer(office, Sid, "Jane.Doe", "ODFC", "HOST-1");
        var user = ScanUser(Sid);
        var only = Path.Combine(officeDir, "ODFC_Jane.Doe.VHDX.lock");

        var targets = LockRemoval.FindTargets(user, new[] { only });

        Assert.Equal(only, Assert.Single(targets).File.FullPath);
        Assert.True(Assert.Single(LockRemoval.Remove(targets, user, Locations)).Deleted);
        Assert.Single(ScanUser(Sid).Containers.Where(c => c.IsLocked));
    }

    [Fact]
    public void Rescan_does_not_pick_up_a_user_whose_sid_starts_the_same()
    {
        MakeContainer(profiles, Sid, "Jane.Doe", "Profile", "HOST-1");
        MakeContainer(profiles, OtherSid, "John.Smith", "Profile", "HOST-2");

        var fresh = ProfileScanner.RescanUser(ScanUser(Sid), Locations);

        var container = Assert.Single(fresh.Containers);
        Assert.Equal(Sid, container.Sid);
    }

    [Fact]
    public void Refuses_another_users_lock_even_if_asked()
    {
        MakeContainer(profiles, Sid, "Jane.Doe", "Profile", "HOST-1");
        var other = MakeContainer(profiles, OtherSid, "John.Smith", "Profile", "HOST-2");
        var user = ScanUser(Sid);
        var otherLock = Path.Combine(other, "Profile_John.Smith.VHDX.lock");

        Assert.NotNull(LockRemoval.Validate(otherLock, user, Locations));
        Assert.Empty(LockRemoval.FindTargets(user, new[] { otherLock }));
    }

    [Fact]
    public void Refuses_non_lock_files()
    {
        var dir = MakeContainer(profiles, Sid, "Jane.Doe", "Profile", "HOST-1");
        var user = ScanUser(Sid);

        Assert.Equal("Not a .lock file.", LockRemoval.Validate(Path.Combine(dir, "Profile_Jane.Doe.VHDX"), user, Locations));
        Assert.Equal("Not a .lock file.", LockRemoval.Validate(Path.Combine(dir, "Profile_Jane.Doe.VHDX.meta"), user, Locations));
    }

    [Fact]
    public void Refuses_lock_in_a_subfolder()
    {
        var dir = MakeContainer(profiles, Sid, "Jane.Doe", "Profile", "HOST-1");
        var user = ScanUser(Sid);
        var nested = Directory.CreateDirectory(Path.Combine(dir, "sub")).FullName;
        var nestedLock = Path.Combine(nested, "x.lock");
        File.WriteAllText(nestedLock, "x");

        Assert.NotNull(LockRemoval.Validate(nestedLock, user, Locations));
        Assert.False(Assert.Single(LockRemoval.Remove(new[] { new LockTarget(user.Containers[0], new ProfileFile { FullPath = nestedLock, Kind = ProfileFileKind.Lock }) }, user, Locations)).Deleted);
        Assert.True(File.Exists(nestedLock));
    }

    [Fact]
    public void Refuses_folder_outside_configured_locations()
    {
        MakeContainer(profiles, Sid, "Jane.Doe", "Profile", "HOST-1");
        var user = ScanUser(Sid);

        // Same user, but only the Office location is configured now.
        var path = user.Containers[0].Locks.Single().FullPath;
        Assert.Equal("Folder is not directly inside a configured storage location.", LockRemoval.Validate(path, user, new[] { office }));
    }

    [Fact]
    public void Missing_file_is_reported_not_thrown()
    {
        var dir = MakeContainer(profiles, Sid, "Jane.Doe", "Profile", "HOST-1");
        var user = ScanUser(Sid);
        File.Delete(Path.Combine(dir, "Profile_Jane.Doe.VHDX.lock"));

        var outcome = Assert.Single(LockRemoval.Remove(LockRemoval.FindTargets(user), user, Locations));
        Assert.False(outcome.Deleted);
        Assert.Equal("File no longer exists.", outcome.Error);
    }

    [Fact]
    public void Skips_lock_that_changed_after_it_was_checked()
    {
        var dir = MakeContainer(profiles, Sid, "Jane.Doe", "Profile", "HOST-1");
        var user = ScanUser(Sid);
        var lockPath = Path.Combine(dir, "Profile_Jane.Doe.VHDX.lock");
        File.SetLastWriteTime(lockPath, DateTime.Now.AddMinutes(1)); // FSLogix rewrote it

        var outcome = Assert.Single(LockRemoval.Remove(LockRemoval.FindTargets(user), user, Locations));

        Assert.False(outcome.Deleted);
        Assert.Contains("changed after it was checked", outcome.Error);
        Assert.True(File.Exists(lockPath));
    }

    [Fact]
    public void Server_checks_detect_logged_on_user()
    {
        MakeContainer(profiles, Sid, "Jane.Doe", "Profile", "HOST-1.contoso.local");
        MakeContainer(office, Sid, "Jane.Doe", "ODFC", "HOST-2.contoso.local");
        var user = ScanUser(Sid);

        var checks = LockRemoval.CheckServers(LockRemoval.FindTargets(user), user, server => server.StartsWith("HOST-1")
            ? new SessionQueryResult { Sessions = new[] { new RemoteSession(3, "jane.doe", "CONTOSO", "Disconnected") } }
            : new SessionQueryResult { Sessions = new[] { new RemoteSession(4, "John.Smith", "CONTOSO", "Active") } });

        Assert.Equal(ServerCheckStatus.LoggedOn, checks.Single(c => c.Server!.StartsWith("HOST-1")).Status);
        Assert.Equal(ServerCheckStatus.NotLoggedOn, checks.Single(c => c.Server!.StartsWith("HOST-2")).Status);
    }

    [Fact]
    public void Server_checks_match_ad_account_name()
    {
        MakeContainer(profiles, Sid, "Jane.Doe", "Profile", "HOST-1");
        var user = ScanUser(Sid);
        user.Directory = new DirectoryUser { State = DirectoryLookupState.Found, AccountName = @"CONTOSO\jdoe" };

        var checks = LockRemoval.CheckServers(LockRemoval.FindTargets(user), user, _ =>
            new SessionQueryResult { Sessions = new[] { new RemoteSession(2, "JDOE", "CONTOSO", "Active") } });

        Assert.Equal(ServerCheckStatus.LoggedOn, Assert.Single(checks).Status);
    }

    [Fact]
    public void Server_checks_report_unreachable_and_unknown_servers()
    {
        var dir = MakeContainer(profiles, Sid, "Jane.Doe", "Profile", "HOST-1");
        File.WriteAllBytes(Path.Combine(dir, "Profile_Jane.Doe.VHDX.lock"), new byte[] { 0, 1, 2, 3 }); // no name inside
        MakeContainer(office, Sid, "Jane.Doe", "ODFC", "HOST-9");
        var user = ScanUser(Sid);

        var checks = LockRemoval.CheckServers(LockRemoval.FindTargets(user), user, _ => SessionQueryResult.Failed("RPC server unavailable"));

        Assert.Contains(checks, c => c.Status == ServerCheckStatus.NoServerName);
        Assert.Contains(checks, c => c.Status == ServerCheckStatus.CouldNotCheck && c.Server == "HOST-9");
    }

    [Fact]
    public void Writes_audit_log()
    {
        MakeContainer(profiles, Sid, "Jane.Doe", "Profile", "HOST-1");
        var user = ScanUser(Sid);
        var outcomes = LockRemoval.Remove(LockRemoval.FindTargets(user), user, Locations);
        var log = Path.Combine(root, "logs", "lock-removal.log");

        LockRemoval.AppendLog(log, user, outcomes, Array.Empty<ServerCheck>());

        var lines = File.ReadAllLines(log);
        Assert.Equal(2, lines.Length);
        Assert.Contains("Deleted", lines[1]);
        Assert.Contains("Jane.Doe", lines[1]);
    }
}
