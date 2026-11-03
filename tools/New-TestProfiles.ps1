<#
.SYNOPSIS
    Creates a fake FSLogix storage layout for trying out AdvancedSID.

.DESCRIPTION
    Builds four folders (Profile / Office x Cloud Cache location 1 / 2) under -Root,
    each with a set of per-user "SID_username" folders containing dummy VHDX, .meta
    and .lock files. Lock files are written the same way FSLogix does (UTF-16 text
    with a binary-looking header and the session host name), so the server name can
    be picked out by AdvancedSID.

    The VHDX files are tiny dummies - they are NOT real virtual disks.

.EXAMPLE
    .\New-TestProfiles.ps1 -Root C:\FSLogixTest
    Then in AdvancedSID tick "Office" and "Cloud Cache" and point the four paths at
    C:\FSLogixTest\Profiles1, Office1, Profiles2, Office2.
#>
param(
    [string]$Root = 'C:\FSLogixTest',
    [string]$Domain = 'contoso.local',
    # Use your own SID for one user so you can see an AD lookup succeed on a domain PC.
    [switch]$IncludeCurrentUser
)

$ErrorActionPreference = 'Stop'

$locations = [ordered]@{
    Profiles1 = 'Profile'
    Office1   = 'ODFC'
    Profiles2 = 'Profile'
    Office2   = 'ODFC'
}

# name, sid, which locations get a lock (by folder name), host holding the lock
$all = @('Profiles1', 'Office1', 'Profiles2', 'Office2')
$users = @(
    # Logged on, healthy: every container locked by the same host.
    @{ Name = 'Jane.Doe';   Sid = 'S-1-5-21-1111111111-2222222222-3333333333-448107'; Locks = $all;                              Host = 'AVD-HOST-02' },
    # Logged off: no locks.
    @{ Name = 'John.Smith';   Sid = 'S-1-5-21-1111111111-2222222222-3333333333-448201'; Locks = @();                               Host = '' },
    # MISMATCH: Office locked but the user profile is not (classic logon problem).
    @{ Name = 'Sarah.Jones';  Sid = 'S-1-5-21-1111111111-2222222222-3333333333-448202'; Locks = @('Office1');                      Host = 'AVD-HOST-05' },
    # MISMATCH: one Cloud Cache copy of the Office container is not locked.
    @{ Name = 'Mark.Wilson';  Sid = 'S-1-5-21-1111111111-2222222222-3333333333-448203'; Locks = @('Profiles1', 'Office1', 'Profiles2'); Host = 'AVD-HOST-01' },
    # Leaver (orphaned SID), no Office container.
    @{ Name = 'Old.Leaver';   Sid = 'S-1-5-21-1111111111-2222222222-3333333333-401999'; Locks = @();                               Host = '' }
)

if ($IncludeCurrentUser) {
    $me = [System.Security.Principal.WindowsIdentity]::GetCurrent()
    $users += @{ Name = $env:USERNAME; Sid = $me.User.Value; Locks = $all; Host = $env:COMPUTERNAME }
}

function New-LockFile([string]$Path, [string]$HostName) {
    # 12 byte binary-ish header, then the FQDN padded with spaces, all UTF-16LE - mimics FSLogix.
    $header = [byte[]](0x20,0x00,0x20,0x00,0x2C,0x58,0xF0,0xA0,0xA1,0x01,0x20,0x00)
    $text = [System.Text.Encoding]::Unicode.GetBytes(" $HostName.$Domain" + (' ' * 480))
    [System.IO.File]::WriteAllBytes($Path, $header + $text)
}

foreach ($loc in $locations.Keys) {
    $prefix = $locations[$loc]
    foreach ($u in $users) {
        # Leave a gap so not every user exists everywhere.
        if ($u.Name -eq 'Old.Leaver' -and $loc -like 'Office*') { continue }

        $dir = Join-Path $Root "$loc\$($u.Sid)_$($u.Name)"
        New-Item -ItemType Directory -Path $dir -Force | Out-Null
        $disk = Join-Path $dir "$($prefix)_$($u.Name).VHDX"

        $fs = [System.IO.File]::Create($disk)
        $fs.SetLength((Get-Random -Minimum 50MB -Maximum 400MB))   # sparse-ish dummy file
        $fs.Close()
        Set-Content -Path "$disk.meta" -Value "dummy meta for $($u.Name)"

        if ($u.Locks -contains $loc) {
            New-LockFile "$disk.lock" $u.Host
        }
    }
}

# A folder with no disk at all, to show the "No disk" state.
New-Item -ItemType Directory -Path (Join-Path $Root 'Profiles1\S-1-5-21-1111111111-2222222222-3333333333-448300_Empty.User') -Force | Out-Null

Write-Host "Test data created under $Root" -ForegroundColor Green
$locations.Keys | ForEach-Object { Write-Host "  $(Join-Path $Root $_)" }
