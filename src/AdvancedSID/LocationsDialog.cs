namespace AdvancedSID;

/// <summary>
/// "Configure locations" window: which container types are in use, their paths, and AD lookup.
/// Edits a copy of the values and only writes them to <see cref="AppSettings"/> on OK.
/// </summary>
internal sealed class LocationsDialog : Form
{
    private static readonly string[] RowLabels =
    {
        "User Profiles:",
        "Office Profiles:",
        "User Profiles Cloud Cache:",
        "Office Profiles Cloud Cache:",
    };

    private readonly AppSettings settings;
    private readonly CheckBox chkDirectory = new() { Text = "Look up users in Active Directory", AutoSize = true };
    private readonly CheckBox chkOffice = new() { Text = "Office (ODFC) containers enabled", AutoSize = true };
    private readonly CheckBox chkCloudCache = new() { Text = "Cloud Cache enabled", AutoSize = true };
    private readonly FlowLayoutPanel options = new() { AutoSize = true, WrapContents = false, Margin = Padding.Empty };
    private readonly Label[] labels = new Label[4];
    private readonly TextBox[] boxes = new TextBox[4];
    private readonly Button[] browse = new Button[4];
    private readonly Button btnOk = new() { Text = "OK", DialogResult = DialogResult.None };
    private readonly Button btnCancel = new() { Text = "Cancel", DialogResult = DialogResult.Cancel };

    public LocationsDialog(AppSettings settings)
    {
        this.settings = settings;
        Text = "Configure locations";
        FormBorderStyle = FormBorderStyle.Sizable;
        MinimizeBox = false;
        MaximizeBox = false;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterParent;
        AcceptButton = btnOk;
        CancelButton = btnCancel;

        options.Controls.AddRange(new Control[] { chkDirectory, chkOffice, chkCloudCache });
        Controls.Add(options);
        for (var i = 0; i < 4; i++)
        {
            var index = i;
            labels[i] = new Label { Text = RowLabels[i], AutoSize = true };
            boxes[i] = new TextBox { PlaceholderText = @"e.g. N:\AVDProfiles  or  \\server\share\Profiles" };
            browse[i] = new Button { Text = "Browse...", AutoSize = true };
            browse[i].Click += (_, _) => Browse(index);
            Controls.AddRange(new Control[] { labels[i], boxes[i], browse[i] });
        }

        btnOk.Size = btnCancel.Size = new Size(LogicalToDeviceUnits(88), LogicalToDeviceUnits(28));
        Controls.AddRange(new Control[] { btnOk, btnCancel });

        chkDirectory.Checked = settings.DirectoryLookupEnabled;
        chkOffice.Checked = settings.OfficeEnabled;
        chkCloudCache.Checked = settings.CloudCacheEnabled;
        boxes[0].Text = settings.ProfilePath1;
        boxes[1].Text = settings.OfficePath1;
        boxes[2].Text = settings.ProfilePath2;
        boxes[3].Text = settings.OfficePath2;

        chkOffice.CheckedChanged += (_, _) => UpdateRows();
        chkCloudCache.CheckedChanged += (_, _) => UpdateRows();
        btnOk.Click += (_, _) => Save();
        Resize += (_, _) => DoLayout();

        Width = LogicalToDeviceUnits(820);
        UpdateRows();
    }

    private bool[] ActiveRows() => AppSettings.ActiveRows(chkOffice.Checked, chkCloudCache.Checked);

    private void UpdateRows()
    {
        var active = ActiveRows();
        for (var i = 0; i < 4; i++)
        {
            labels[i].Visible = boxes[i].Visible = browse[i].Visible = active[i];
        }

        DoLayout();
    }

    private void DoLayout()
    {
        var margin = LogicalToDeviceUnits(12);
        var gap = LogicalToDeviceUnits(8);
        var width = ClientSize.Width;

        options.Location = new Point(margin - 3, margin);
        options.Size = options.PreferredSize;
        var y = options.Bottom + gap;

        var labelWidth = labels.Max(l => l.PreferredSize.Width);
        var active = ActiveRows();
        for (var i = 0; i < 4; i++)
        {
            if (!active[i])
            {
                continue;
            }

            var button = browse[i];
            var box = boxes[i];
            button.Size = button.PreferredSize;
            var rowHeight = Math.Max(box.PreferredHeight, button.Height);
            labels[i].Location = new Point(margin, y + ((rowHeight - labels[i].PreferredSize.Height) / 2));
            button.Location = new Point(Math.Max(0, width - margin - button.Width), y + ((rowHeight - button.Height) / 2));
            box.Location = new Point(margin + labelWidth + gap, y + ((rowHeight - box.Height) / 2));
            box.Width = Math.Max(LogicalToDeviceUnits(120), button.Left - gap - box.Left);
            y += rowHeight + gap;
        }

        y += gap;
        btnCancel.Location = new Point(width - margin - btnCancel.Width, y);
        btnOk.Location = new Point(btnCancel.Left - gap - btnOk.Width, y);

        // Fit the window height to the visible rows; only the width is user-resizable.
        var height = btnOk.Bottom + margin;
        var minWidth = margin + labelWidth + gap + LogicalToDeviceUnits(260);
        MinimumSize = new Size(0, 0);
        MaximumSize = new Size(0, 0);
        if (ClientSize.Height != height)
        {
            ClientSize = new Size(width, height);
        }

        MinimumSize = new Size(SizeFromClientSize(new Size(minWidth, height)).Width, Height);
        MaximumSize = new Size(int.MaxValue, Height);
    }

    private void Browse(int index)
    {
        using var dialog = new FolderBrowserDialog
        {
            Description = "Select the share / folder that contains the per-user folders",
            UseDescriptionForTitle = true,
            ShowNewFolderButton = false,
        };
        if (Directory.Exists(boxes[index].Text))
        {
            dialog.InitialDirectory = boxes[index].Text;
        }

        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            boxes[index].Text = dialog.SelectedPath;
        }
    }

    private void Save()
    {
        var active = ActiveRows();
        var empty = Enumerable.Range(0, 4).Where(i => active[i] && string.IsNullOrWhiteSpace(boxes[i].Text)).ToList();
        if (empty.Count > 0)
        {
            MessageBox.Show(this, "Please enter a path for: " + string.Join(", ", empty.Select(i => RowLabels[i].TrimEnd(':'))), Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
            boxes[empty[0]].Focus();
            return;
        }

        var unreachable = Enumerable.Range(0, 4)
            .Where(i => active[i] && !Directory.Exists(boxes[i].Text.Trim()))
            .Select(i => $"{RowLabels[i]}  {boxes[i].Text.Trim()}")
            .ToList();
        if (unreachable.Count > 0
            && MessageBox.Show(this, "These paths can't be reached right now:\n\n" + string.Join("\n", unreachable) + "\n\nSave anyway?", Text, MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
        {
            return;
        }

        settings.DirectoryLookupEnabled = chkDirectory.Checked;
        settings.OfficeEnabled = chkOffice.Checked;
        settings.CloudCacheEnabled = chkCloudCache.Checked;
        settings.ProfilePath1 = boxes[0].Text.Trim();
        settings.OfficePath1 = boxes[1].Text.Trim();
        settings.ProfilePath2 = boxes[2].Text.Trim();
        settings.OfficePath2 = boxes[3].Text.Trim();
        settings.Save();
        DialogResult = DialogResult.OK;
    }
}
