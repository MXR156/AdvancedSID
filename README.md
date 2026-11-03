# AdvancedSID

A Windows tool for FSLogix (and UPD) admins - like SIDDER, with more features.

Point it at your profile share(s) and it lists every user folder, maps the SID in the
folder name to the AD account, and shows **at a glance which container is locked and by
which session host** - across Profile, Office (ODFC) and both Cloud Cache locations at once.

## Features

- **Up to 4 storage locations** (User Profiles, Office Profiles, and their Cloud Cache copies), one row per user:
  - Profile only (1 path)
  - Profile + Office (2 paths)
  - Profile with Cloud Cache (2 paths)
  - Profile + Office with Cloud Cache (4 paths)
- **Lock view** - each location is a column. A locked container shows in red as
  `LOCKED  DCLAVDPRDSH-2`; hover for the full lock details (FQDN, lock file, lock age).
  The server name is read from the `.lock` file (the same text you see in Notepad).
- **SID -> user** - uses Windows/LSA to resolve the SID to `DOMAIN\user`, then LDAP for
  display name, enabled/disabled and last logon. SIDs that no longer resolve are flagged
  **Orphaned** (leavers whose profiles can be cleaned up).
- **Right-click** a user / cell:
  - Open the folder for that location in Explorer (or double-click the cell)
  - Open the lock file in Notepad / show it in Explorer
  - Copy user name, SID, account, server name, folder paths
  - Rescan just that user (e.g. after a lock has been cleared)
- **Files pane** - every file (VHDX, .lock, .meta) for the selected user with size,
  modified time, age and the server holding the lock.
- **Filter** by user, display name, SID or **server name** (e.g. type a host name to see
  every profile it has locked). "Locked only" and "Orphaned / disabled only" toggles.
- Status bar shows totals and (on hover) number of locks per session host.
- **Export CSV** of the current (filtered) list.
- Remembers your paths in `%APPDATA%\AdvancedSID\settings.json`.
- Read-only: it never modifies or opens the VHDX files. (Lock removal is planned for a later version.)

Folder names supported: `SID_username` (default), `username_SID` (FlipFlop) and UPD `UVHD-SID.vhdx`.

## Getting the EXE

- **GitHub Actions**: every push builds `AdvancedSID.exe` - open the latest run under the
  *Actions* tab and download the `AdvancedSID` artifact. Pushing a tag like `v0.1.0` attaches
  the EXE to a GitHub release.
- **Build locally** (needs the [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)): `.\build.ps1` -> `publish\AdvancedSID.exe` (add `-SkipTests` to skip the unit tests)

The EXE is a single self-contained file (~50 MB) - no .NET runtime needed on the machine.

## Testing with fake data

```powershell
.\tools\New-TestProfiles.ps1 -Root C:\FSLogixTest -IncludeCurrentUser
```

Then tick *Office* and *Cloud Cache* and set the paths to
`C:\FSLogixTest\Profiles1`, `Office1`, `Profiles2`, `Office2`.
`-IncludeCurrentUser` adds a folder with your own SID so you can see an AD lookup succeed;
the other SIDs are made up so they will show as *Orphaned* on a domain PC.

## Notes

- Mapped drives: if you run the EXE **as administrator**, drives mapped in your normal
  session are not visible to it - use the UNC path (`\\server\share\...`) instead, or run it
  without elevation.
- Lock files can be held open by the session host; if one can't be read it is still shown
  as locked, with "(unreadable)" as the server.
- Only the top level of each user folder is examined (where FSLogix puts the VHDX/lock/meta files).

## Project layout

```
src/AdvancedSID.Core     scanning, lock-file parsing, AD lookup, CSV (no UI)
src/AdvancedSID          WinForms app
tests/                   unit tests (xUnit)
tools/                   test data generator
```

## Roadmap

- **Lock removal** (not yet implemented). Planned rule: a lock is only removed when the
  server named in the lock no longer has that user logged on. If the user still has a
  session there, removal is refused.
