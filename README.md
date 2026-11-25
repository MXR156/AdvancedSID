<p align="center">
  <img src="src/AdvancedSID/Resources/AdvancedSID.png" alt="AdvancedSID" width="128">
</p>

<h1 align="center">AdvancedSID</h1>

<p align="center">
  Find out who owns every FSLogix profile on your share, and see at a glance which containers are locked and by which server.
</p>

<p align="center">
  <a href="https://github.com/MXR156/AdvancedSID/releases/latest"><b>Download the latest version</b></a>
</p>

---

FSLogix and UPD profile shares are full of folders named after SIDs, and a stuck `.lock` file
can be in any one of up to four places: the User Profile, the Office container, and the Cloud
Cache copy of each. AdvancedSID scans them all at once and shows one row per user, so you can
see straight away:

- **who** each profile folder belongs to (name, AD account, enabled / disabled / deleted),
- **which** of their containers are locked, and **which session host** holds each lock,
- **which** users have a lock **mismatch**, a common cause of logon failures.

It only ever deletes one thing: the `.lock` files of the user you choose, when you ask it to
and confirm. It never changes, moves or deletes profile disks or anything else on your shares.

## Features

**Lock overview**
- Every storage location is a column. A locked container shows in red with the server
  holding it, for example `LOCKED  AVD-HOST-02`. Hover over it for the full server name,
  the lock file and how long it has been locked.
- The server name is read straight from the FSLogix `.lock` file, so there's no need to open
  each one in Notepad.

**Lock mismatch detection**
- When a user is logged on, *all* of their containers are locked. If some are locked and
  others aren't (for example, Office locked but User Profile not), or they're locked by
  different servers, the user is flagged **LOCK MISMATCH** in orange. The container that
  should be locked shows `NOT LOCKED (mismatch)`.

**Clear stuck locks**
- Right-click a user and choose **Remove all lock files for ...**, or right-click a single
  lock (in the grid or the files pane) and choose **Remove this lock file...**.
- Before anything is deleted, AdvancedSID:
  - re-reads that user's folders, so it works on what is on disk now,
  - asks each session host named in the locks whether the user is still logged on, and
    **refuses** if they have an active or disconnected session there,
  - shows you exactly which files will be removed, and the session check results, for confirmation.
- Only `.lock` files directly inside the selected user's own profile folders can be removed.
  VHDX and `.meta` files, and other users' files, are never touched.
- If a lock changes between the check and the deletion (for example, the user has just logged on), it is left alone.
- Every removal is recorded in `%APPDATA%\AdvancedSID\lock-removal.log`, with the time, who removed it, the user, the file and the server.

**Old profiles**
- When a profile is recreated, the old folder is often renamed to `..._old` or `....old` (also
  `-old`, `_old2`, `_old_20251125` and so on). These are shown as **Old profile** in lavender,
  so they're easy to find for auditing and clean-up, but they are **not** counted in the lock
  mismatch check.
- Renamed disk files such as `Profile_user.VHDX.old` are flagged the same way.
- The *Old profiles only* filter lists every user with old data; hover over the Status cell
  for the folders, sizes and dates.
- If AD shows the account is really named that way (for example, a surname "Old"), the folder
  is treated as a normal profile.

**User lookup**
- Each SID is resolved to the Active Directory account, with display name, email,
  enabled/disabled status and approximate last logon.
- SIDs that no longer exist in AD are flagged **Orphaned**, which makes leavers' profiles easy to find.

**Works with your layout**
- User Profiles only, User + Office Profiles, either with Cloud Cache, or all four locations.
- Folder naming `SID_username` (FSLogix default), `username_SID` (FlipFlop) and UPD `UVHD-SID.vhdx`.
- Mapped drives or UNC paths (`\\server\share\Profiles`).

**Quick actions** (right-click a user or a location cell)
- Open the user's folder in Explorer. Double-click a cell does the same.
- Open a lock file in Notepad, or show it in Explorer.
- Copy the user name, SID, AD account, server name or folder paths.
- Remove the user's lock files (see above).
- Rescan just that user.

**Finding things**
- Search by user name, display name, SID or **server name**. Type a host name to see every
  profile that host has locked.
- Filters: *Locked only*, *Lock mismatches only*, *Old profiles only*, *Orphaned / disabled only*.
- The files pane lists every VHDX, `.lock` and `.meta` file for the selected user. Tick
  *Show lock files only* to see just the locks.
- The status bar shows totals; hover over it for a count of locks per session host.
- **Export CSV** saves the current list, for reports or follow-up.

## Download and run

1. Download **AdvancedSID.exe** from the [latest release](https://github.com/MXR156/AdvancedSID/releases/latest).
2. Run it. There's nothing to install, and .NET doesn't need to be installed.
3. The first time, **Configure locations** opens. Tick the options that match your FSLogix setup
   and enter the path for each location:

   | Setting                                | Paths to enter                                   |
   |----------------------------------------|--------------------------------------------------|
   | (nothing ticked)                       | User Profiles                                    |
   | Office containers enabled              | User Profiles, Office Profiles                   |
   | Cloud Cache enabled                    | User Profiles, User Profiles Cloud Cache         |
   | Both                                   | All four                                         |

4. Click **OK**. The scan starts. Press **F5** to rescan at any time, and use
   **Configure locations...** to change paths later.

> Windows SmartScreen may warn you the first time, because the EXE isn't code-signed.
> Click **More info → Run anyway**.

## What the colours mean

| Cell                         | Meaning                                                                   |
|------------------------------|---------------------------------------------------------------------------|
| `OK  2.1 GB` (green)         | Container present, not locked                                             |
| `LOCKED  HOST` (red)         | Container locked by that session host                                     |
| `NOT LOCKED (mismatch)` (orange) | User's other containers are locked but this one isn't, so investigate     |
| `No disk` (amber)            | The user's folder exists but has no VHD/VHDX in it                        |
| `OLD  2.1 GB` (lavender)     | Only an old, renamed (`_old` / `.old`) folder in this location            |
| `... + old`                  | The location also has old profile data alongside the current one          |
| `-` (grey)                   | The user has no folder in this location                                   |

The **Status** column summarises each user: `OK`, `Locked`, `LOCK MISMATCH`, `Orphaned`, `Disabled` and/or `Old profile`.

## Requirements

- 64-bit Windows 10 / 11 or Windows Server 2016 or later.
- Read access to the profile shares (and delete permission on them to remove locks).
- To check sessions before removing a lock: permission to query sessions on the session hosts
  (the same access `quser /server:HOST` needs, normally admin on the host).
- For user details: a domain-joined machine. Without a domain, profiles and locks are still
  shown, but names can't be looked up.

## Tips and troubleshooting

- **Mapped drive "can't be reached":** if you run AdvancedSID *as administrator*, drives
  mapped in your normal session aren't visible to it. Run it normally, or use the UNC path
  (`\\server\share\...`).
- **"(unreadable)" as the lock server:** the session host has the lock file open
  exclusively. The container is still shown as locked.
- **Everyone shows as Orphaned on a test PC:** the PC isn't in the same domain as the SIDs.
- **"Could not check" when removing a lock:** the session host is offline, or you don't have
  rights to query its sessions. You can still go ahead, but only if you're sure the user is logged off.
- **A lock won't delete ("in use"):** the session host still has it open, so the user is most
  likely still connected there.
- Your locations are saved in `%APPDATA%\AdvancedSID\settings.json`.

Suggestions and bug reports are welcome. Please [open an issue](https://github.com/MXR156/AdvancedSID/issues).

---

Building from source or making a release? See [docs/DEVELOPMENT.md](docs/DEVELOPMENT.md).
