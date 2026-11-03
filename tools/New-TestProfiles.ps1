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
$users = @(
    @{ Name = 'Lynne.Todd';   Sid = 'S-1-5-21-839522115-583907252-2147053123-448107'; Locks = @('Office1');                Host = 'DCLAVDPRDSH-2' },
    @{ Name = 'John.Smith';   Sid = 'S-1-5-21-839522115-583907252-2147053123-448201'; Locks = @();                         Host = '' },
    @{ Name = 'Sarah.Jones';  Sid = 'S-1-5-21-839522115-583907252-2147053123-448202'; Locks = @('Profiles1', 'Profiles2'); Host = 'DCLAVDPRDSH-5' },
    @{ Name = 'Mark.Wilson';  Sid = 'S-1-5-21-839522115-583907252-2147053123-448203'; Locks = @('Office2');                Host = 'DCLAVDPRDSH-1' },
    @{ Name = 'Old.Leaver';   Sid = 'S-1-5-21-839522115-583907252-2147053123-401999'; Locks = @();                         Host = '' }
)

if ($IncludeCurrentUser) {
    $me = [System.Security.Principal.WindowsIdentity]::GetCurrent()
    $users += @{ Name = $env:USERNAME; Sid = $me.User.Value; Locks = @('Profiles1'); Host = $env:COMPUTERNAME }
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
New-Item -ItemType Directory -Path (Join-Path $Root 'Profiles1\S-1-5-21-839522115-583907252-2147053123-448300_Empty.User') -Force | Out-Null

Write-Host "Test data created under $Root" -ForegroundColor Green
$locations.Keys | ForEach-Object { Write-Host "  $(Join-Path $Root $_)" }
