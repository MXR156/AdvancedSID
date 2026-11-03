# Development

## Building

Requires the [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) on Windows.

```powershell
.\build.ps1              # runs the unit tests, then builds publish\AdvancedSID.exe
.\build.ps1 -SkipTests   # build only
```

The output is a single self-contained EXE (~50 MB) that runs without .NET installed.

## Project layout

```
src/AdvancedSID.Core     scanning, lock-file parsing, AD lookup, CSV export (no UI)
src/AdvancedSID          WinForms app (MainForm, LocationsDialog)
tests/                   unit tests (xUnit)
tools/                   test data generator
.github/workflows        CI build and release publishing
```

## Releases

The **Build** workflow runs on every push and pull request, and the EXE can be downloaded
from the run's *Artifacts*. The EXE is attached to a GitHub release automatically (in about
2 minutes) when:

- a release is published on the website (*Releases > Draft a new release*, create a tag such as `v0.2.0`), or
- a tag is pushed: `git tag v0.2.0` then `git push origin v0.2.0`.

If a release is missing its EXE, go to *Actions > Build > Run workflow* and enter the release's tag.

Before tagging, update `<Version>` in `src/AdvancedSID/AdvancedSID.csproj` to match. It is
shown in the window title and the EXE's file properties.

## Test data

`tools/New-TestProfiles.ps1` creates a fake four-location FSLogix layout with dummy VHDX,
`.meta` and `.lock` files. It includes healthy, unlocked and mismatched lock patterns.

```powershell
.\tools\New-TestProfiles.ps1 -Root C:\FSLogixTest -IncludeCurrentUser
```

In *Configure locations*, tick *Office* and *Cloud Cache* and use
`C:\FSLogixTest\Profiles1`, `Office1`, `Profiles2` and `Office2`.
`-IncludeCurrentUser` adds a folder with your own SID so an AD lookup succeeds; the other
SIDs are made up and show as *Orphaned* on a domain PC.

## Notes

- Lock files are UTF-16 text with a binary header; the session host name is pulled out with
  a host-name pattern (see `LockFileParser`).
- Only the top level of each user folder is examined, which is where FSLogix keeps the VHDX, lock and meta files.
- SIDs are resolved with the Windows LSA (works across trusts), then LDAP / the global
  catalog for display name, enabled state and last logon.
