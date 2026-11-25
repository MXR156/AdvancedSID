using System.Reflection;
using System.Text;
using AdvancedSID.Core;

namespace AdvancedSID;

internal sealed class MainForm : Form
{
    private static readonly Color LockedBack = Color.FromArgb(255, 212, 212);
    private static readonly Color LockedSelectedBack = Color.FromArgb(200, 60, 60);
    private static readonly Color LockedFore = Color.FromArgb(150, 0, 0);
    private static readonly Color WarnBack = Color.FromArgb(255, 238, 196);
    private static readonly Color WarnSelectedBack = Color.FromArgb(200, 150, 40);
    private static readonly Color OkFore = Color.FromArgb(0, 110, 0);
    private static readonly Color MutedFore = Color.FromArgb(150, 150, 150);
    private static readonly Color MismatchBack = Color.FromArgb(255, 160, 60);
    private static readonly Color MismatchSelectedBack = Color.FromArgb(205, 95, 0);
    private static readonly Color MismatchFore = Color.Black;
    private static readonly Color OldBack = Color.FromArgb(232, 226, 250);
    private static readonly Color OldSelectedBack = Color.FromArgb(110, 90, 170);
    private static readonly Color OldFore = Color.FromArgb(75, 55, 140);
    private static readonly Font BoldFont = new(Control.DefaultFont, FontStyle.Bold);

    private readonly AppSettings settings = AppSettings.Load();
    private readonly DirectoryLookup directory = new();

    // Configuration controls
    private readonly Button btnLocations = new() { Text = "Configure locations...", AutoSize = true };
    private readonly Label locationsSummary = new() { Dock = DockStyle.Top, AutoSize = false, AutoEllipsis = true, TextAlign = ContentAlignment.MiddleLeft, ForeColor = SystemColors.GrayText };
    private readonly ToolTip summaryTip = new();

    // Toolbar
    private readonly Button btnScan = new() { Text = "Scan  (F5)", AutoSize = true };
    private readonly Button btnCancel = new() { Text = "Cancel", AutoSize = true, Enabled = false };
    private readonly Button btnExport = new() { Text = "Export CSV...", AutoSize = true, Enabled = false };
    private readonly TextBox txtFilter = new() { Width = 260, PlaceholderText = "Filter: user, name, SID, server... (Ctrl+F)" };
    private readonly CheckBox chkLockedOnly = new() { Text = "Locked only", AutoSize = true };
    private readonly CheckBox chkProblemsOnly = new() { Text = "Orphaned / disabled only", AutoSize = true };
    private readonly CheckBox chkMismatchOnly = new() { Text = "Lock mismatches only", AutoSize = true };
    private readonly CheckBox chkOldOnly = new() { Text = "Old profiles only", AutoSize = true };
    private readonly CheckBox chkLockFilesOnly = new() { Text = "Show lock files only", AutoSize = true };

    // Results
    private readonly DataGridView usersGrid = CreateGrid();
    private readonly DataGridView filesGrid = CreateGrid();
    private readonly SplitContainer split = new() { Dock = DockStyle.Fill, Orientation = Orientation.Horizontal };
    private readonly ContextMenuStrip usersMenu = new();
    private readonly ContextMenuStrip filesMenu = new();

    // Status bar
    private readonly ToolStripStatusLabel statusLabel = new() { Spring = true, TextAlign = ContentAlignment.MiddleLeft, Text = "Ready." };
    private readonly ToolStripStatusLabel warningsLabel = new() { IsLink = true, Visible = false, ForeColor = Color.DarkOrange };
    private readonly ToolStripStatusLabel countsLabel = new();
    private readonly ToolStripProgressBar progressBar = new() { Visible = false, Width = 160 };

    private readonly System.Windows.Forms.Timer filterTimer = new() { Interval = 250 };
    private readonly Dictionary<int, StorageLocation> locationColumns = new();

    private ScanResult? result;
    private CancellationTokenSource? scanCts;
    private int menuColumn = -1;
    private bool removingLocks;

    private static string LockRemovalLogPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AdvancedSID", "lock-removal.log");

    public MainForm()
    {
        var version = typeof(MainForm).Assembly.GetName().Version;
        Text = $"AdvancedSID v{version?.ToString(3)} - FSLogix / UPD profile explorer";
        Width = 1400;
        Height = 860;
        StartPosition = FormStartPosition.CenterScreen;
        KeyPreview = true;
        using (var iconStream = typeof(MainForm).Assembly.GetManifestResourceStream("AdvancedSID.ico"))
        {
            if (iconStream is not null)
            {
                Icon = new Icon(iconStream);
            }
        }

        BuildLayout();
        LoadSettingsIntoUi();
        WireEvents();
        UpdateLocationsSummary();
    }

    // ------------------------------------------------------------------ layout

    private static DataGridView CreateGrid()
    {
        var grid = new DataGridView
        {
            Dock = DockStyle.Fill,
            ReadOnly = true,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            AllowUserToResizeRows = false,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            MultiSelect = false,
            RowHeadersVisible = false,
            BackgroundColor = SystemColors.Window,
            BorderStyle = BorderStyle.None,
            ShowCellToolTips = true,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None,
            ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.AutoSize,
        };
        grid.RowTemplate.Height = 24;
        grid.AlternatingRowsDefaultCellStyle.BackColor = Color.FromArgb(248, 248, 250);

        // Reduce flicker on big lists.
        typeof(DataGridView).GetProperty("DoubleBuffered", BindingFlags.Instance | BindingFlags.NonPublic)?.SetValue(grid, true);
        return grid;
    }

    private void BuildLayout()
    {
        // --- toolbar
        var toolbar = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(6, 4, 6, 4), WrapContents = false };
        var filterLabel = new Label { Text = "Filter:", AutoSize = true, Margin = new Padding(16, 8, 3, 3) };
        chkLockedOnly.Margin = new Padding(12, 6, 3, 3);
        chkProblemsOnly.Margin = new Padding(6, 6, 3, 3);
        chkMismatchOnly.Margin = new Padding(6, 6, 3, 3);
        chkOldOnly.Margin = new Padding(6, 6, 3, 3);
        txtFilter.Margin = new Padding(3, 5, 3, 3);
        btnLocations.Margin = new Padding(3, 3, 12, 3);
        toolbar.Controls.AddRange(new Control[] { btnLocations, btnScan, btnCancel, btnExport, filterLabel, txtFilter, chkLockedOnly, chkMismatchOnly, chkOldOnly, chkProblemsOnly });

        // --- grids
        var filesHeader = new Panel { Dock = DockStyle.Top, Height = LogicalToDeviceUnits(26), BackColor = SystemColors.ControlLight };
        var filesHeaderText = new Label
        {
            Text = "Files for selected user  (hover a lock for details, right-click for options)",
            Dock = DockStyle.Fill,
            AutoSize = false,
            TextAlign = ContentAlignment.MiddleLeft,
            Padding = new Padding(6, 0, 0, 0),
        };
        chkLockFilesOnly.Dock = DockStyle.Right;
        chkLockFilesOnly.Padding = new Padding(0, 0, 10, 0);
        filesHeader.Controls.Add(filesHeaderText);
        filesHeader.Controls.Add(chkLockFilesOnly);
        split.Panel1.Controls.Add(usersGrid);
        split.Panel2.Controls.Add(filesGrid);
        split.Panel2.Controls.Add(filesHeader);

        var status = new StatusStrip();
        status.Items.AddRange(new ToolStripItem[] { statusLabel, warningsLabel, countsLabel, progressBar });

        // Dock order: Fill first, outermost last.
        Controls.Add(split);
        locationsSummary.Height = LogicalToDeviceUnits(20);
        locationsSummary.Padding = new Padding(LogicalToDeviceUnits(10), 0, LogicalToDeviceUnits(10), LogicalToDeviceUnits(2));
        Controls.Add(locationsSummary);
        Controls.Add(toolbar);
        Controls.Add(status);

        BuildFilesColumns();
        usersGrid.ContextMenuStrip = usersMenu;
        filesGrid.ContextMenuStrip = filesMenu;
    }

    private void BuildFilesColumns()
    {
        filesGrid.Columns.Add(TextColumn("Location", "Location", 120));
        filesGrid.Columns.Add(TextColumn("Kind", "Type", 70));
        filesGrid.Columns.Add(TextColumn("File", "File", 300));
        var size = TextColumn("Size", "Size", 90);
        size.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleRight;
        filesGrid.Columns.Add(size);
        filesGrid.Columns.Add(TextColumn("Modified", "Modified", 130));
        filesGrid.Columns.Add(TextColumn("Age", "Age", 90));
        filesGrid.Columns.Add(TextColumn("LockedBy", "Locked by", 220));
        filesGrid.Columns.Add(TextColumn("Folder", "Folder", 500));
    }

    private static DataGridViewTextBoxColumn TextColumn(string name, string header, int width) =>
        new() { Name = name, HeaderText = header, Width = width, SortMode = DataGridViewColumnSortMode.Automatic };

    // ------------------------------------------------------------------ settings

    private void LoadSettingsIntoUi()
    {
        chkLockFilesOnly.Checked = settings.LockFilesOnly;
    }

    private void SaveSettingsFromUi()
    {
        settings.LockFilesOnly = chkLockFilesOnly.Checked;
        settings.Save();
    }

    /// <summary>Opens the Configure locations window; returns true if the settings were changed.</summary>
    private bool ConfigureLocations()
    {
        var before = System.Text.Json.JsonSerializer.Serialize(settings);
        using var dialog = new LocationsDialog(settings) { Icon = Icon };
        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return false;
        }

        UpdateLocationsSummary();
        return before != System.Text.Json.JsonSerializer.Serialize(settings);
    }

    private void UpdateLocationsSummary()
    {
        if (!settings.IsConfigured)
        {
            locationsSummary.Text = "No locations configured yet - click \"Configure locations...\" to add your profile share(s).";
            summaryTip.SetToolTip(locationsSummary, null);
            return;
        }

        var locations = settings.BuildLocations();
        locationsSummary.Text = string.Join("     ", locations.Select(l => $"{l.ShortName}: {l.Path}"))
            + (settings.DirectoryLookupEnabled ? "     |  AD lookup on" : "     |  AD lookup off");
        summaryTip.SetToolTip(locationsSummary, string.Join("\n", locations.Select(l => $"{l.Name}:  {l.Path}")) + "\n\nClick to change.");
    }

    // ------------------------------------------------------------------ events

    private void WireEvents()
    {
        btnLocations.Click += async (_, _) =>
        {
            if (ConfigureLocations() && btnScan.Enabled)
            {
                await ScanAsync();
            }
        };
        locationsSummary.Cursor = Cursors.Hand;
        locationsSummary.Click += (_, _) => btnLocations.PerformClick();
        btnScan.Click += async (_, _) => await ScanAsync();
        btnCancel.Click += (_, _) => scanCts?.Cancel();
        btnExport.Click += (_, _) => ExportCsv();
        txtFilter.TextChanged += (_, _) =>
        {
            filterTimer.Stop();
            filterTimer.Start();
        };
        filterTimer.Tick += (_, _) =>
        {
            filterTimer.Stop();
            PopulateUsers();
        };
        chkLockedOnly.CheckedChanged += (_, _) => PopulateUsers();
        chkProblemsOnly.CheckedChanged += (_, _) => PopulateUsers();
        chkMismatchOnly.CheckedChanged += (_, _) => PopulateUsers();
        chkOldOnly.CheckedChanged += (_, _) => PopulateUsers();
        chkLockFilesOnly.CheckedChanged += (_, _) => PopulateFiles();
        warningsLabel.Click += (_, _) => ShowWarnings();

        usersGrid.SelectionChanged += (_, _) => PopulateFiles();
        usersGrid.CellFormatting += UsersGridCellFormatting;
        usersGrid.CellMouseDown += (_, e) => SelectOnRightClick(usersGrid, e, isUsers: true);
        usersGrid.CellDoubleClick += (_, e) => UsersDoubleClick(e);
        usersMenu.Opening += (_, e) => BuildUsersMenu(e);

        filesGrid.CellFormatting += FilesGridCellFormatting;
        filesGrid.CellMouseDown += (_, e) => SelectOnRightClick(filesGrid, e, isUsers: false);
        filesGrid.CellDoubleClick += (_, e) =>
        {
            if (e.RowIndex >= 0 && filesGrid.Rows[e.RowIndex].Tag is FileRow row)
            {
                if (row.File is null)
                {
                    Shell.OpenFolder(row.Container.FolderPath);
                }
                else
                {
                    Shell.SelectInExplorer(row.File.FullPath);
                }
            }
        };
        filesMenu.Opening += (_, e) => BuildFilesMenu(e);

        Shown += (_, _) =>
        {
            split.SplitterDistance = (int)(split.Height * 0.62);
            if (!settings.IsConfigured)
            {
                btnLocations.PerformClick();
            }
        };
        FormClosing += (_, _) => SaveSettingsFromUi();
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        switch (keyData)
        {
            case Keys.F5:
                if (btnScan.Enabled)
                {
                    _ = ScanAsync();
                }

                return true;
            case Keys.Control | Keys.F:
                txtFilter.Focus();
                txtFilter.SelectAll();
                return true;
            case Keys.Escape when scanCts is not null:
                scanCts.Cancel();
                return true;
        }

        return base.ProcessCmdKey(ref msg, keyData);
    }

    private void SelectOnRightClick(DataGridView grid, DataGridViewCellMouseEventArgs e, bool isUsers)
    {
        if (e.Button != MouseButtons.Right || e.RowIndex < 0)
        {
            if (isUsers)
            {
                menuColumn = e.ColumnIndex;
            }

            return;
        }

        grid.ClearSelection();
        grid.Rows[e.RowIndex].Selected = true;
        grid.CurrentCell = grid.Rows[e.RowIndex].Cells[Math.Max(0, e.ColumnIndex)];
        if (isUsers)
        {
            menuColumn = e.ColumnIndex;
        }
    }

    // ------------------------------------------------------------------ scanning

    private async Task ScanAsync()
    {
        if (settings.BuildLocations().Any(l => string.IsNullOrWhiteSpace(l.Path)))
        {
            ConfigureLocations();
        }

        var locations = settings.BuildLocations();
        if (locations.Any(l => string.IsNullOrWhiteSpace(l.Path)))
        {
            return;
        }

        SaveSettingsFromUi();
        var lookup = settings.DirectoryLookupEnabled;
        scanCts = new CancellationTokenSource();
        var token = scanCts.Token;
        SetBusy(true);

        var progress = new Progress<ScanProgress>(p =>
        {
            statusLabel.Text = p.Message;
            if (p.Total > 0)
            {
                progressBar.Style = ProgressBarStyle.Continuous;
                progressBar.Maximum = p.Total;
                progressBar.Value = Math.Min(p.Done, p.Total);
            }
            else
            {
                progressBar.Style = ProgressBarStyle.Marquee;
            }
        });

        try
        {
            var started = DateTime.Now;
            var scan = await Task.Run(
                () =>
                {
                    var r = new ProfileScanner().Scan(locations, progress, token);
                    if (lookup)
                    {
                        directory.ClearCache();
                        directory.LookupAll(r.Users, progress, token);
                    }

                    return r;
                },
                token);

            result = scan;
            BuildUserColumns(scan.Locations);
            PopulateUsers();
            btnExport.Enabled = true;
            statusLabel.Text = $"Scanned {scan.Users.Sum(u => u.Containers.Count)} folders in {(DateTime.Now - started).TotalSeconds:0.0}s at {scan.ScannedAt:HH:mm:ss}."
                + (lookup && !directory.IsDomainJoined ? "  (This PC is not domain joined - AD details unavailable.)" : string.Empty);
            warningsLabel.Text = $"{scan.Errors.Count} warning(s)";
            warningsLabel.Visible = scan.Errors.Count > 0;
        }
        catch (OperationCanceledException)
        {
            statusLabel.Text = "Scan cancelled.";
        }
        catch (Exception ex)
        {
            statusLabel.Text = "Scan failed: " + ex.Message;
            MessageBox.Show(this, ex.ToString(), "Scan failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            scanCts.Dispose();
            scanCts = null;
            SetBusy(false);
        }
    }

    private void SetBusy(bool busy)
    {
        btnScan.Enabled = !busy;
        btnCancel.Enabled = busy;
        progressBar.Visible = busy;
        progressBar.Style = ProgressBarStyle.Marquee;
        UseWaitCursor = busy;
        btnCancel.UseWaitCursor = false;

        if (!busy)
        {
            // DataGridView remembers whatever cursor was showing when the mouse last crossed a
            // column edge, so if that happened during the scan it keeps the wait cursor forever.
            // Reset the grids explicitly once we're done.
            foreach (var grid in new[] { usersGrid, filesGrid })
            {
                grid.UseWaitCursor = false;
                grid.Cursor = Cursors.Default;
            }

            Cursor.Current = Cursors.Default;
        }
    }

    private async Task RescanUserAsync(UserRecord user)
    {
        if (result is null)
        {
            return;
        }

        var locations = result.Locations;
        var lookup = settings.DirectoryLookupEnabled;
        statusLabel.Text = $"Rescanning {user.UserName}...";
        var updated = await Task.Run(() =>
        {
            var fresh = ProfileScanner.RescanUser(user, locations);
            if (lookup && user.Sid is not null)
            {
                fresh.Directory = directory.Lookup(user.Sid);
            }

            return fresh;
        });

        ReplaceUser(user, updated);
        statusLabel.Text = $"Rescanned {user.UserName} at {DateTime.Now:HH:mm:ss}.";
    }

    /// <summary>Swaps in a freshly scanned copy of one user and redraws, keeping them selected.</summary>
    private void ReplaceUser(UserRecord old, UserRecord updated)
    {
        if (result is null)
        {
            return;
        }

        var index = result.Users.FindIndex(u => string.Equals(u.Key, old.Key, StringComparison.OrdinalIgnoreCase));
        if (index >= 0)
        {
            result.Users[index] = updated;
        }

        PopulateUsers(updated.Key);
    }

    /// <summary>
    /// Removes .lock files for ONE user: all of them, or just <paramref name="onlyPaths"/>.
    /// Re-reads the user's folders first, refuses if the user is logged on to a lock's server,
    /// asks for confirmation, then deletes and logs. Never touches other files or other users.
    /// </summary>
    private async Task RemoveLocksAsync(UserRecord user, IReadOnlyCollection<string>? onlyPaths)
    {
        if (result is null || removingLocks || scanCts is not null)
        {
            return;
        }

        var locations = result.Locations;
        removingLocks = true;
        btnScan.Enabled = false;
        progressBar.Style = ProgressBarStyle.Marquee;
        progressBar.Visible = true;
        try
        {
            statusLabel.Text = $"Checking {user.UserName}'s lock files and sessions...";
            var (fresh, targets, checks) = await Task.Run(() =>
            {
                var current = ProfileScanner.RescanUser(user, locations);
                var found = LockRemoval.FindTargets(current, onlyPaths);
                var serverChecks = found.Count == 0
                    ? (IReadOnlyList<ServerCheck>)Array.Empty<ServerCheck>()
                    : LockRemoval.CheckServers(found, current, RemoteSessions.Query);
                return (current, found, serverChecks);
            });
            ReplaceUser(user, fresh);

            if (targets.Count == 0)
            {
                statusLabel.Text = "No lock files to remove.";
                MessageBox.Show(this, $"No lock files were found for {fresh.UserName}. They may already have been cleared.", "Remove lock files", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            var loggedOn = checks.Where(c => c.Status == ServerCheckStatus.LoggedOn).ToList();
            if (loggedOn.Count > 0)
            {
                statusLabel.Text = $"{fresh.UserName} is still logged on - no locks removed.";
                MessageBox.Show(
                    this,
                    $"{fresh.UserName} is still logged on, so no lock files were removed:\n\n"
                    + string.Join("\n", loggedOn.Select(c => $"   {c.Server}:  {c.Detail}"))
                    + "\n\nLog the user off (or end the disconnected session) first.",
                    "Remove lock files",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Stop);
                return;
            }

            var unverified = checks.Where(c => c.Status is ServerCheckStatus.CouldNotCheck or ServerCheckStatus.NoServerName).ToList();
            var text = new StringBuilder();
            text.AppendLine($"Remove {(targets.Count == 1 ? "this lock file" : $"these {targets.Count} lock files")} for {fresh.UserName}"
                + (fresh.Directory.DisplayName is { } dn ? $" ({dn})" : string.Empty) + "?");
            text.AppendLine();
            foreach (var t in targets)
            {
                text.AppendLine($"   {t.Container.Location.Name}:  {t.File.Name}");
                text.AppendLine($"        locked by {t.File.Lock?.Server ?? "(unknown)"}, {Format.Age(t.File.LastWriteTime)}");
            }

            text.AppendLine();
            text.AppendLine("Session check:");
            foreach (var c in checks)
            {
                text.AppendLine(c.Status switch
                {
                    ServerCheckStatus.NotLoggedOn => $"   OK   {c.Server}: user is not logged on",
                    ServerCheckStatus.CouldNotCheck => $"   ??   {c.Server}: could not check ({c.Detail})",
                    _ => "   ??   " + c.Detail,
                });
            }

            if (unverified.Count > 0)
            {
                text.AppendLine();
                text.AppendLine("WARNING: the user's sessions could not be checked on every server.");
                text.AppendLine("Only continue if you are sure the user is logged off everywhere.");
            }

            text.AppendLine();
            text.Append("Only the .lock files listed above will be deleted. The profile disks are not touched.");

            if (MessageBox.Show(this, text.ToString(), "Remove lock files", MessageBoxButtons.YesNo, unverified.Count > 0 ? MessageBoxIcon.Warning : MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) != DialogResult.Yes)
            {
                statusLabel.Text = "Lock removal cancelled.";
                return;
            }

            statusLabel.Text = $"Removing lock files for {fresh.UserName}...";
            var outcomes = await Task.Run(() => LockRemoval.Remove(targets, fresh, locations));
            string? logError = null;
            try
            {
                LockRemoval.AppendLog(LockRemovalLogPath, fresh, outcomes, checks);
            }
            catch (Exception ex)
            {
                logError = ex.Message;
            }

            var after = await Task.Run(() => ProfileScanner.RescanUser(fresh, locations));
            ReplaceUser(fresh, after);

            var deleted = outcomes.Count(o => o.Deleted);
            var failed = outcomes.Where(o => !o.Deleted).ToList();
            statusLabel.Text = $"Removed {deleted} of {outcomes.Count} lock file(s) for {fresh.UserName} at {DateTime.Now:HH:mm:ss}.";
            if (failed.Count > 0 || logError is not null)
            {
                MessageBox.Show(
                    this,
                    $"Removed {deleted} of {outcomes.Count} lock file(s)."
                    + (failed.Count > 0 ? "\n\nNot removed:\n" + string.Join("\n", failed.Select(o => $"   {o.Target.File.Name}: {o.Error}")) : string.Empty)
                    + (logError is not null ? "\n\nCould not write the log: " + logError : string.Empty),
                    "Remove lock files",
                    MessageBoxButtons.OK,
                    failed.Count > 0 ? MessageBoxIcon.Warning : MessageBoxIcon.Information);
            }
        }
        catch (Exception ex)
        {
            statusLabel.Text = "Lock removal failed: " + ex.Message;
            MessageBox.Show(this, ex.Message, "Remove lock files", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            removingLocks = false;
            btnScan.Enabled = scanCts is null;
            progressBar.Visible = scanCts is not null;
        }
    }

    // ------------------------------------------------------------------ users grid

    private void BuildUserColumns(IReadOnlyList<StorageLocation> locations)
    {
        usersGrid.Rows.Clear();
        usersGrid.Columns.Clear();
        locationColumns.Clear();

        usersGrid.Columns.Add(TextColumn("Status", "Status", 90));
        usersGrid.Columns.Add(TextColumn("User", "User (folder)", 170));
        usersGrid.Columns.Add(TextColumn("DisplayName", "Display name", 170));
        usersGrid.Columns.Add(TextColumn("Account", "AD account", 160));
        usersGrid.Columns.Add(TextColumn("AccountStatus", "Account", 80));
        foreach (var location in locations)
        {
            var column = TextColumn("Loc_" + location.Key, location.ShortName, 175);
            column.ToolTipText = location.Path;
            column.HeaderCell.Style.Font = BoldFont;
            locationColumns[usersGrid.Columns.Add(column)] = location;
        }

        usersGrid.Columns.Add(TextColumn("LastModified", "Last modified", 120));
        var size = TextColumn("Size", "Total size", 85);
        size.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleRight;
        usersGrid.Columns.Add(size);
        usersGrid.Columns.Add(TextColumn("Sid", "SID", 330));
    }

    private IEnumerable<UserRecord> FilteredUsers()
    {
        if (result is null)
        {
            return Enumerable.Empty<UserRecord>();
        }

        var filter = txtFilter.Text.Trim();
        return result.Users.Where(u =>
            (!chkLockedOnly.Checked || u.IsLocked)
            && (!chkMismatchOnly.Checked || u.HasLockMismatch)
            && (!chkOldOnly.Checked || u.HasOldProfiles)
            && (!chkProblemsOnly.Checked || u.IsOrphaned || u.IsDisabled)
            && (filter.Length == 0 || Matches(u, filter)));
    }

    private static bool Matches(UserRecord u, string filter)
    {
        bool Has(string? s) => s?.Contains(filter, StringComparison.OrdinalIgnoreCase) == true;
        return Has(u.UserName) || Has(u.Sid) || Has(u.Directory.DisplayName) || Has(u.Directory.AccountName)
            || u.Containers.Any(c => Has(c.FolderName) || c.Locks.Any(l => Has(l.Lock?.Server)));
    }

    private void PopulateUsers(string? selectKey = null)
    {
        if (result is null)
        {
            return;
        }

        selectKey ??= (usersGrid.CurrentRow?.Tag as UserRecord)?.Key;
        var sortColumn = usersGrid.SortedColumn;
        var sortOrder = usersGrid.SortOrder;
        var firstRow = usersGrid.FirstDisplayedScrollingRowIndex;

        usersGrid.SuspendLayout();
        usersGrid.Rows.Clear();
        var rows = new List<DataGridViewRow>();
        foreach (var user in FilteredUsers())
        {
            var row = new DataGridViewRow { Tag = user };
            row.CreateCells(usersGrid);
            FillUserRow(row, user);
            rows.Add(row);
        }

        usersGrid.Rows.AddRange(rows.ToArray());
        if (sortColumn is not null && sortOrder != SortOrder.None)
        {
            usersGrid.Sort(sortColumn, sortOrder == SortOrder.Ascending ? System.ComponentModel.ListSortDirection.Ascending : System.ComponentModel.ListSortDirection.Descending);
        }

        usersGrid.ResumeLayout();

        var selected = selectKey is null ? null : usersGrid.Rows.Cast<DataGridViewRow>().FirstOrDefault(r => ((UserRecord)r.Tag!).Key == selectKey);
        if (selected is not null)
        {
            usersGrid.CurrentCell = selected.Cells[0];
        }
        else if (firstRow >= 0 && firstRow < usersGrid.RowCount)
        {
            usersGrid.FirstDisplayedScrollingRowIndex = firstRow;
        }

        UpdateCounts(rows.Count);
        PopulateFiles();
    }

    private void FillUserRow(DataGridViewRow row, UserRecord user)
    {
        var cells = row.Cells;

        var flags = new List<string>();
        if (user.HasLockMismatch)
        {
            flags.Add("LOCK MISMATCH");
        }
        else if (user.IsLocked)
        {
            flags.Add("Locked");
        }

        if (user.IsOrphaned)
        {
            flags.Add("Orphaned");
        }
        else if (user.IsDisabled)
        {
            flags.Add("Disabled");
        }

        if (user.HasOldProfiles)
        {
            flags.Add("Old profile");
        }

        var statusCell = cells[usersGrid.Columns["Status"]!.Index];
        statusCell.Value = flags.Count == 0 ? "OK" : string.Join(", ", flags);
        if (user.HasLockMismatch)
        {
            StyleCell(statusCell, MismatchBack, MismatchSelectedBack, MismatchFore, bold: true);
            statusCell.ToolTipText = "Lock mismatch - this will likely cause a logon problem.\n\n" + user.LockMismatch
                + "\n\nWhen a user is logged on, ALL of their containers are locked.";
        }
        else if (user.IsLocked)
        {
            StyleCell(statusCell, LockedBack, LockedSelectedBack, LockedFore, bold: true);
            statusCell.ToolTipText = "Locked by: " + string.Join(", ", user.LockServers);
        }
        else if (user.IsOrphaned || user.IsDisabled)
        {
            StyleCell(statusCell, WarnBack, WarnSelectedBack, Color.FromArgb(120, 70, 0), bold: true);
        }
        else if (user.HasOldProfiles)
        {
            StyleCell(statusCell, OldBack, OldSelectedBack, OldFore, bold: true);
        }
        else
        {
            statusCell.Style.ForeColor = OkFore;
        }

        if (user.HasOldProfiles)
        {
            statusCell.ToolTipText = (string.IsNullOrEmpty(statusCell.ToolTipText) ? string.Empty : statusCell.ToolTipText + "\n\n") + OldSummary(user);
        }

        cells[usersGrid.Columns["User"]!.Index].Value = user.UserName;
        cells[usersGrid.Columns["DisplayName"]!.Index].Value = user.Directory.DisplayName ?? string.Empty;
        cells[usersGrid.Columns["Account"]!.Index].Value = user.Directory.AccountName ?? string.Empty;

        var accountCell = cells[usersGrid.Columns["AccountStatus"]!.Index];
        accountCell.Value = CsvExporter.AccountStatus(user);
        accountCell.ToolTipText = DirectoryTooltip(user);
        if (user.IsOrphaned || user.IsDisabled)
        {
            accountCell.Style.ForeColor = Color.FromArgb(170, 90, 0);
        }
        else if (user.Directory.State != DirectoryLookupState.Found)
        {
            accountCell.Style.ForeColor = MutedFore;
        }

        foreach (var (columnIndex, location) in locationColumns)
        {
            FillLocationCell(cells[columnIndex], user, location);
        }

        cells[usersGrid.Columns["LastModified"]!.Index].Value = Format.Time(user.LastWriteTime);
        cells[usersGrid.Columns["LastModified"]!.Index].ToolTipText = Format.Age(user.LastWriteTime);
        cells[usersGrid.Columns["Size"]!.Index].Value = user.TotalDiskSize;
        cells[usersGrid.Columns["Sid"]!.Index].Value = user.Sid ?? "(no SID in folder name)";
    }

    private static string OldSummary(UserRecord user)
    {
        var lines = new List<string> { "Old profiles (ignored by the lock mismatch check):" };
        foreach (var c in user.OldContainers)
        {
            lines.Add($"   {c.Location.Name}:  {c.FolderName}   {Format.Size(c.DiskSize + c.OldDiskSize)}, last modified {Format.Time(c.LastWriteTime)}");
        }

        foreach (var c in user.ActiveContainers.Where(c => c.HasOldDisk))
        {
            foreach (var f in c.Files.Where(f => f.Kind == ProfileFileKind.OldDisk))
            {
                lines.Add($"   {c.Location.Name}:  {f.Name}   {Format.Size(f.Size)}, last modified {Format.Time(f.LastWriteTime)}");
            }
        }

        lines.Add($"Total: {Format.Size(user.OldDataSize)}");
        return string.Join("\n", lines);
    }

    private static void FillLocationCell(DataGridViewCell cell, UserRecord user, StorageLocation location)
    {
        var all = user.ContainersIn(location).ToList();
        var containers = all.Where(c => !user.IsOld(c)).ToList();
        var old = all.Where(user.IsOld).ToList();
        if (all.Count == 0)
        {
            cell.Value = "-";
            cell.Style.ForeColor = MutedFore;
            cell.Style.Alignment = DataGridViewContentAlignment.MiddleCenter;
            cell.ToolTipText = $"No folder for this user in {location.Name}\n{location.Path}";
            return;
        }

        var tip = new StringBuilder();
        foreach (var c in all)
        {
            tip.AppendLine((user.IsOld(c) ? "[OLD]  " : string.Empty) + c.FolderPath);
            if (c.ScanError is not null)
            {
                tip.AppendLine("  ERROR: " + c.ScanError);
            }

            foreach (var f in c.Files)
            {
                tip.AppendLine($"  {f.Name}   {Format.Size(f.Size)}   {Format.Time(f.LastWriteTime)}");
                if (f.Lock is { } l)
                {
                    tip.AppendLine(l.ReadError is not null
                        ? $"      LOCK - could not read ({l.ReadError})"
                        : $"      LOCKED BY: {l.Server ?? "(server name not found in file)"}   since {Format.Time(f.LastWriteTime)} ({Format.Age(f.LastWriteTime)})");
                }
            }

            if (c.Files.Count == 0)
            {
                tip.AppendLine("  (folder is empty)");
            }
        }

        cell.ToolTipText = tip.ToString().TrimEnd() + "\n\nDouble-click to open folder - right-click for more.";

        if (containers.Count == 0)
        {
            // Only an old, renamed-out-of-the-way folder here.
            if (old.Any(c => c.IsLocked))
            {
                cell.Value = "LOCKED (old)  " + string.Join(", ", old.SelectMany(c => c.LockServers).Distinct(StringComparer.OrdinalIgnoreCase));
                StyleCell(cell, LockedBack, LockedSelectedBack, LockedFore, bold: true);
            }
            else
            {
                cell.Value = "OLD  " + Format.Size(old.Sum(c => c.DiskSize + c.OldDiskSize));
                StyleCell(cell, OldBack, OldSelectedBack, OldFore, bold: false);
            }

            return;
        }

        if (containers.Any(c => c.IsLocked))
        {
            var servers = containers.SelectMany(c => c.LockServers).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            cell.Value = "LOCKED  " + string.Join(", ", servers);
            StyleCell(cell, LockedBack, LockedSelectedBack, LockedFore, bold: true);
        }
        else if (user.UnlockedWhileOthersLocked.Any(c => containers.Contains(c)))
        {
            cell.Value = "NOT LOCKED (mismatch)  " + Format.Size(containers.Sum(c => c.DiskSize));
            StyleCell(cell, MismatchBack, MismatchSelectedBack, MismatchFore, bold: true);
            cell.ToolTipText = $"MISMATCH: this container has no lock, but the user's other containers are locked by {string.Join(", ", user.LockServers)}.\n\n"
                + cell.ToolTipText;
        }
        else if (!containers.Any(c => c.HasDisk))
        {
            cell.Value = containers.Any(c => c.ScanError is not null) ? "Access error" : "No disk";
            StyleCell(cell, WarnBack, WarnSelectedBack, Color.FromArgb(120, 70, 0), bold: false);
        }
        else
        {
            cell.Value = "OK  " + Format.Size(containers.Sum(c => c.DiskSize));
            cell.Style.ForeColor = OkFore;
        }

        if (containers.Count > 1)
        {
            cell.Value += $"  ({containers.Count} folders)";
        }

        if (old.Count > 0 || containers.Any(c => c.HasOldDisk))
        {
            cell.Value += "  + old";
            cell.ToolTipText = "This location also has old (renamed) profile data - it's ignored by the lock mismatch check.\n\n" + cell.ToolTipText;
        }
    }

    private static void StyleCell(DataGridViewCell cell, Color back, Color selectedBack, Color fore, bool bold)
    {
        cell.Style.BackColor = back;
        cell.Style.ForeColor = fore;
        cell.Style.SelectionBackColor = selectedBack;
        cell.Style.SelectionForeColor = Color.White;
        if (bold)
        {
            cell.Style.Font = BoldFont;
        }
    }

    private static string DirectoryTooltip(UserRecord user)
    {
        var d = user.Directory;
        var sb = new StringBuilder();
        sb.AppendLine("Lookup: " + d.State);
        if (d.AccountName is not null)
        {
            sb.AppendLine("Account: " + d.AccountName);
        }

        if (d.DisplayName is not null)
        {
            sb.AppendLine("Name: " + d.DisplayName);
        }

        if (d.Email is not null)
        {
            sb.AppendLine("Mail: " + d.Email);
        }

        if (d.Enabled is not null)
        {
            sb.AppendLine("Enabled: " + (d.Enabled.Value ? "Yes" : "No"));
        }

        if (d.LastLogon is not null)
        {
            sb.AppendLine($"Last logon (approx): {Format.Time(d.LastLogon.Value)} ({Format.Age(d.LastLogon.Value)})");
        }

        if (d.DistinguishedName is not null)
        {
            sb.AppendLine("DN: " + d.DistinguishedName);
        }

        if (d.Error is not null)
        {
            sb.AppendLine(d.Error);
        }

        return sb.ToString().TrimEnd();
    }

    private void UsersGridCellFormatting(object? sender, DataGridViewCellFormattingEventArgs e)
    {
        if (e.ColumnIndex >= 0 && usersGrid.Columns[e.ColumnIndex].Name == "Size" && e.Value is long bytes)
        {
            e.Value = bytes == 0 ? string.Empty : Format.Size(bytes);
            e.FormattingApplied = true;
        }
    }

    private void UpdateCounts(int shown)
    {
        if (result is null)
        {
            return;
        }

        var users = result.Users;
        var locked = users.Count(u => u.IsLocked);
        var mismatched = users.Count(u => u.HasLockMismatch);
        var lockFiles = users.Sum(u => u.Containers.Sum(c => c.Locks.Count()));
        var orphaned = users.Count(u => u.IsOrphaned);
        var disabled = users.Count(u => u.IsDisabled);
        var withOld = users.Count(u => u.HasOldProfiles);
        var servers = users
            .SelectMany(u => u.Containers.SelectMany(c => c.LockServers))
            .GroupBy(s => s, StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(g => g.Count())
            .Select(g => $"{g.Key} ({g.Count()})")
            .ToList();

        countsLabel.Text = $"{shown} of {users.Count} users  |  {locked} locked ({lockFiles} lock files)  |  {mismatched} lock mismatches  |  {orphaned} orphaned  |  {disabled} disabled  |  {withOld} with old profiles";
        countsLabel.ForeColor = mismatched > 0 ? MismatchSelectedBack : SystemColors.ControlText;
        countsLabel.ToolTipText = servers.Count == 0 ? "No locks." : "Locks per server:\n" + string.Join("\n", servers);
    }

    private void UsersDoubleClick(DataGridViewCellEventArgs e)
    {
        if (e.RowIndex < 0 || usersGrid.Rows[e.RowIndex].Tag is not UserRecord user)
        {
            return;
        }

        var container = locationColumns.TryGetValue(e.ColumnIndex, out var location)
            ? user.ContainersIn(location).FirstOrDefault()
            : user.Containers.FirstOrDefault(c => c.IsLocked) ?? user.Containers.FirstOrDefault();
        if (container is not null)
        {
            Shell.OpenFolder(container.FolderPath);
        }
    }

    private void BuildUsersMenu(System.ComponentModel.CancelEventArgs e)
    {
        usersMenu.Items.Clear();
        if (usersGrid.CurrentRow?.Tag is not UserRecord user)
        {
            e.Cancel = true;
            return;
        }

        // Actions for the specific storage location that was right-clicked.
        if (locationColumns.TryGetValue(menuColumn, out var location))
        {
            var here = user.ContainersIn(location).ToList();
            foreach (var c in here)
            {
                var open = new ToolStripMenuItem($"Open {location.Name} folder", null, (_, _) => Shell.OpenFolder(c.FolderPath));
                open.Font = BoldFont;
                usersMenu.Items.Add(open);
                foreach (var lockFile in c.Locks)
                {
                    var server = lockFile.Lock?.Server ?? "unknown server";
                    usersMenu.Items.Add(new ToolStripMenuItem($"Open {lockFile.Name} in Notepad  ({server})", null, (_, _) => Shell.OpenInNotepad(lockFile.FullPath)));
                    usersMenu.Items.Add(new ToolStripMenuItem("Show lock file in Explorer", null, (_, _) => Shell.SelectInExplorer(lockFile.FullPath)));
                    if (lockFile.Lock?.Server is { } s)
                    {
                        usersMenu.Items.Add(new ToolStripMenuItem($"Copy server name ({s})", null, (_, _) => Shell.Copy(s)));
                    }

                    usersMenu.Items.Add(new ToolStripMenuItem($"Remove this lock file ({lockFile.Name})...", null, async (_, _) => await RemoveLocksAsync(user, new[] { lockFile.FullPath })) { Enabled = !removingLocks });
                }
            }

            if (here.Count == 0)
            {
                usersMenu.Items.Add(new ToolStripMenuItem($"No {location.Name} folder for this user") { Enabled = false });
            }

            usersMenu.Items.Add(new ToolStripSeparator());
        }

        var openAll = new ToolStripMenuItem("Open folder");
        foreach (var c in user.Containers)
        {
            var label = $"{c.Location.Name}{(c.IsLocked ? "  [LOCKED: " + string.Join(", ", c.LockServers) + "]" : string.Empty)}  -  {c.FolderPath}";
            openAll.DropDownItems.Add(new ToolStripMenuItem(label, null, (_, _) => Shell.OpenFolder(c.FolderPath)));
        }

        openAll.Enabled = openAll.DropDownItems.Count > 0;
        usersMenu.Items.Add(openAll);

        var locks = user.Containers.SelectMany(c => c.Locks.Select(l => (c, l))).ToList();
        if (locks.Count > 0)
        {
            var lockMenu = new ToolStripMenuItem($"Lock files ({locks.Count})");
            foreach (var (c, l) in locks)
            {
                lockMenu.DropDownItems.Add(new ToolStripMenuItem(
                    $"{c.Location.Name}: {l.Name}  -  {l.Lock?.Server ?? "unknown"}  ({Format.Age(l.LastWriteTime)})",
                    null,
                    (_, _) => Shell.SelectInExplorer(l.FullPath)));
            }

            usersMenu.Items.Add(lockMenu);
        }

        var copy = new ToolStripMenuItem("Copy");
        copy.DropDownItems.Add(new ToolStripMenuItem("User name", null, (_, _) => Shell.Copy(user.UserName)));
        if (user.Sid is not null)
        {
            copy.DropDownItems.Add(new ToolStripMenuItem("SID", null, (_, _) => Shell.Copy(user.Sid)));
        }

        if (user.Directory.AccountName is not null)
        {
            copy.DropDownItems.Add(new ToolStripMenuItem("AD account", null, (_, _) => Shell.Copy(user.Directory.AccountName)));
        }

        if (user.Directory.DisplayName is not null)
        {
            copy.DropDownItems.Add(new ToolStripMenuItem("Display name", null, (_, _) => Shell.Copy(user.Directory.DisplayName)));
        }

        copy.DropDownItems.Add(new ToolStripMenuItem("All folder paths", null, (_, _) => Shell.Copy(string.Join(Environment.NewLine, user.Containers.Select(c => c.FolderPath)))));
        if (user.IsLocked)
        {
            copy.DropDownItems.Add(new ToolStripMenuItem("Lock servers", null, (_, _) => Shell.Copy(string.Join(Environment.NewLine, user.LockServers))));
        }

        usersMenu.Items.Add(copy);
        usersMenu.Items.Add(new ToolStripSeparator());
        usersMenu.Items.Add(new ToolStripMenuItem("Rescan this user", null, async (_, _) => await RescanUserAsync(user)));
        if (user.IsLocked)
        {
            var count = user.Containers.Sum(c => c.Locks.Count());
            usersMenu.Items.Add(new ToolStripMenuItem($"Remove all lock files for {user.UserName} ({count})...", null, async (_, _) => await RemoveLocksAsync(user, null)) { Enabled = !removingLocks });
        }

        if (File.Exists(LockRemovalLogPath))
        {
            usersMenu.Items.Add(new ToolStripMenuItem("Open lock removal log", null, (_, _) => Shell.OpenInNotepad(LockRemovalLogPath)));
        }
    }

    // ------------------------------------------------------------------ files grid

    private sealed record FileRow(ProfileContainer Container, ProfileFile? File);

    private void PopulateFiles()
    {
        filesGrid.Rows.Clear();
        if (usersGrid.CurrentRow?.Tag is not UserRecord user)
        {
            return;
        }

        var mismatched = user.UnlockedWhileOthersLocked.ToHashSet();
        var locksOnly = chkLockFilesOnly.Checked;
        foreach (var c in user.Containers)
        {
            var isMismatch = mismatched.Contains(c);
            var isOld = user.IsOld(c);
            if (c.Files.Count == 0)
            {
                AddFileRow(c, null, null, false, isOld);
            }
            else if (locksOnly && !c.IsLocked && !isOld)
            {
                // Keep a row per container so a missing lock is obvious.
                AddFileRow(c, null, isMismatch ? "(NO LOCK FILE - but other containers are locked)" : "(no lock file)", isMismatch, false);
            }

            foreach (var f in c.Files.Where(f => !locksOnly || f.Kind == ProfileFileKind.Lock))
            {
                AddFileRow(c, f, null, isMismatch && f.Kind == ProfileFileKind.Disk, isOld || f.Kind == ProfileFileKind.OldDisk);
            }
        }
    }

    private void AddFileRow(ProfileContainer c, ProfileFile? f, string? note, bool mismatch, bool old)
    {
        var index = filesGrid.Rows.Add(
            c.Location.Name + (old && f?.Kind != ProfileFileKind.OldDisk ? " (old)" : string.Empty),
            f is null ? string.Empty : f.Kind == ProfileFileKind.OldDisk ? "Old disk" : f.Kind.ToString(),
            f?.Name ?? note ?? (c.ScanError is not null ? "(could not read folder: " + c.ScanError + ")" : "(folder is empty)"),
            f?.Size ?? 0L,
            f is null ? string.Empty : Format.Time(f.LastWriteTime),
            f is null ? string.Empty : Format.Age(f.LastWriteTime),
            f?.Lock is { } l ? (l.ReadError is not null ? "(unreadable: " + l.ReadError + ")" : l.Server ?? "(not found in file)") : string.Empty,
            c.FolderPath);

        var row = filesGrid.Rows[index];
        row.Tag = new FileRow(c, f);
        if (mismatch)
        {
            foreach (DataGridViewCell cell in row.Cells)
            {
                StyleCell(cell, MismatchBack, MismatchSelectedBack, MismatchFore, bold: false);
                cell.ToolTipText = "Lock mismatch: this container is NOT locked, but the user's other containers are.";
            }

            row.Cells["File"].Style.Font = BoldFont;
        }
        else if (f?.Kind == ProfileFileKind.Lock)
        {
            foreach (DataGridViewCell cell in row.Cells)
            {
                StyleCell(cell, LockedBack, LockedSelectedBack, LockedFore, bold: false);
            }

            var lockedBy = row.Cells["LockedBy"];
            lockedBy.Style.Font = BoldFont;
            lockedBy.ToolTipText = f.Lock is { Candidates.Count: > 1 } ld
                ? "Other names found in file:\n" + string.Join("\n", ld.Candidates.Skip(1).Take(10))
                : "Right-click to open the lock file in Notepad.";
        }
        else if (old)
        {
            foreach (DataGridViewCell cell in row.Cells)
            {
                StyleCell(cell, OldBack, OldSelectedBack, OldFore, bold: false);
                cell.ToolTipText = "Old (renamed) profile data - ignored by the lock mismatch check.";
            }
        }
        else if (f is null)
        {
            row.DefaultCellStyle.ForeColor = MutedFore;
        }
    }

    private void FilesGridCellFormatting(object? sender, DataGridViewCellFormattingEventArgs e)
    {
        if (e.ColumnIndex >= 0 && filesGrid.Columns[e.ColumnIndex].Name == "Size" && e.Value is long bytes)
        {
            e.Value = e.RowIndex >= 0 && filesGrid.Rows[e.RowIndex].Tag is FileRow { File: null } ? string.Empty : Format.Size(bytes);
            e.FormattingApplied = true;
        }
    }

    private void BuildFilesMenu(System.ComponentModel.CancelEventArgs e)
    {
        filesMenu.Items.Clear();
        if (filesGrid.CurrentRow?.Tag is not FileRow row)
        {
            e.Cancel = true;
            return;
        }

        filesMenu.Items.Add(new ToolStripMenuItem("Open folder", null, (_, _) => Shell.OpenFolder(row.Container.FolderPath)));
        if (row.File is { } f)
        {
            filesMenu.Items.Add(new ToolStripMenuItem("Show file in Explorer", null, (_, _) => Shell.SelectInExplorer(f.FullPath)));
            if (f.Kind is ProfileFileKind.Lock or ProfileFileKind.Meta or ProfileFileKind.Other)
            {
                filesMenu.Items.Add(new ToolStripMenuItem("Open in Notepad", null, (_, _) => Shell.OpenInNotepad(f.FullPath)));
            }

            filesMenu.Items.Add(new ToolStripSeparator());
            filesMenu.Items.Add(new ToolStripMenuItem("Copy file path", null, (_, _) => Shell.Copy(f.FullPath)));
            if (f.Lock?.Server is { } server)
            {
                filesMenu.Items.Add(new ToolStripMenuItem($"Copy server name ({server})", null, (_, _) => Shell.Copy(server)));
            }

            if (f.Kind == ProfileFileKind.Lock && usersGrid.CurrentRow?.Tag is UserRecord owner)
            {
                filesMenu.Items.Add(new ToolStripSeparator());
                filesMenu.Items.Add(new ToolStripMenuItem("Remove this lock file...", null, async (_, _) => await RemoveLocksAsync(owner, new[] { f.FullPath })) { Enabled = !removingLocks });
                filesMenu.Items.Add(new ToolStripMenuItem($"Remove all lock files for {owner.UserName}...", null, async (_, _) => await RemoveLocksAsync(owner, null)) { Enabled = !removingLocks });
            }
        }

        filesMenu.Items.Add(new ToolStripMenuItem("Copy folder path", null, (_, _) => Shell.Copy(row.Container.FolderPath)));
    }

    // ------------------------------------------------------------------ misc

    private void ShowWarnings()
    {
        if (result is null || result.Errors.Count == 0)
        {
            return;
        }

        MessageBox.Show(this, string.Join(Environment.NewLine, result.Errors.Take(40)) + (result.Errors.Count > 40 ? $"{Environment.NewLine}... and {result.Errors.Count - 40} more" : string.Empty), "Scan warnings", MessageBoxButtons.OK, MessageBoxIcon.Warning);
    }

    private void ExportCsv()
    {
        if (result is null)
        {
            return;
        }

        using var dialog = new SaveFileDialog
        {
            Filter = "CSV files (*.csv)|*.csv",
            FileName = $"AdvancedSID-{DateTime.Now:yyyyMMdd-HHmm}.csv",
        };
        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        try
        {
            CsvExporter.Export(result, FilteredUsers(), dialog.FileName);
            statusLabel.Text = "Exported to " + dialog.FileName;
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Export failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}
