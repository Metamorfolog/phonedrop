using System.Drawing.Drawing2D;

namespace PhoneDrop;

/// <summary>Malé okénko s drop zónou, průběhem a ikonou v oznamovací oblasti.</summary>
public sealed class MainForm : Form
{
    public static readonly Icon AppIcon = new(typeof(MainForm).Assembly.GetManifestResourceStream("PhoneDrop.ico")!);

    static readonly Color Back = Color.FromArgb(24, 24, 24);
    static readonly Color Fore = Color.FromArgb(230, 230, 230);
    static readonly Color Dim = Color.FromArgb(140, 140, 140);
    static readonly Color Accent = Color.FromArgb(127, 209, 139);

    readonly Config config = Config.Load();
    readonly Sender sender;
    readonly Queue<string> queue = new();
    readonly NotifyIcon tray;
    readonly Label phone;
    readonly DropZone zone;
    readonly ProgressBar bar;
    readonly Label status;
    readonly ToolStripMenuItem sendToItem;
    readonly ToolStripMenuItem topItem;
    bool busy;
    bool exiting;
    bool hintShown;

    public MainForm(string[] initialFiles)
    {
        sender = new Sender(config);
        sender.Status += (text, progress) => BeginInvoke(() => ShowStatus(text, progress));

        Text = "PhoneDrop";
        Icon = AppIcon;
        BackColor = Back;
        ForeColor = Fore;
        Font = new Font("Segoe UI", 9.5f);
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(380, 300);
        StartPosition = FormStartPosition.Manual;
        var wa = Screen.PrimaryScreen!.WorkingArea;
        Location = new Point(wa.Right - Width - 24, wa.Bottom - Height - 24);
        TopMost = config.TopMost;

        phone = new Label { Dock = DockStyle.Top, Height = 34, Padding = new Padding(12, 10, 12, 0), ForeColor = Dim };
        zone = new DropZone { Dock = DockStyle.Fill, Margin = new Padding(12) };
        zone.Click += (_, _) => PickFiles();
        var zoneHost = new Panel { Dock = DockStyle.Fill, Padding = new Padding(12, 4, 12, 8) };
        zoneHost.Controls.Add(zone);

        bar = new ProgressBar { Dock = DockStyle.Bottom, Height = 6, Style = ProgressBarStyle.Continuous, Maximum = 1000, Visible = false };
        status = new Label { Dock = DockStyle.Bottom, Height = 46, Padding = new Padding(12, 4, 12, 4), ForeColor = Fore, AutoEllipsis = true };

        var menu = new ContextMenuStrip();
        menu.Items.Add("Spárovat telefon…", null, (_, _) => Pair());
        sendToItem = new ToolStripMenuItem("V Průzkumníku: Odeslat do › Telefon", null, (_, _) => ToggleSendTo());
        topItem = new ToolStripMenuItem("Vždy navrchu", null, (_, _) => ToggleTopMost());
        menu.Items.Add(sendToItem);
        menu.Items.Add(topItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Ukončit", null, (_, _) => Quit());
        menu.Opening += (_, _) =>
        {
            sendToItem.Checked = SendTo.Installed;
            topItem.Checked = TopMost;
        };

        var more = new Button
        {
            Text = "⋯", Width = 36, Height = 28, FlatStyle = FlatStyle.Flat, ForeColor = Fore,
            Anchor = AnchorStyles.Top | AnchorStyles.Right, Location = new Point(ClientSize.Width - 46, 4),
        };
        more.FlatAppearance.BorderSize = 0;
        more.Click += (_, _) => menu.Show(more, new Point(0, more.Height));

        Controls.Add(zoneHost);
        Controls.Add(bar);
        Controls.Add(status);
        Controls.Add(more);
        Controls.Add(phone);
        more.BringToFront();

        tray = new NotifyIcon { Icon = AppIcon, Text = "PhoneDrop", Visible = true, ContextMenuStrip = menu };
        tray.MouseClick += (_, e) => { if (e.Button == MouseButtons.Left) ShowWindow(); };

        // Drop funguje kamkoli do okna, ne jen do rámečku.
        foreach (Control c in new Control[] { this, zoneHost, zone, phone, status })
        {
            c.AllowDrop = true;
            c.DragEnter += (_, e) => OnDrag(e);
            c.DragOver += (_, e) => OnDrag(e);
            c.DragLeave += (_, _) => zone.Hot = false;
            c.DragDrop += (_, e) =>
            {
                zone.Hot = false;
                if (e.Data?.GetData(DataFormats.FileDrop) is string[] files) Enqueue(files);
            };
        }

        RefreshPhone();
        ShowStatus(config.IsPaired ? "Připraveno." : "Nejdřív spáruj telefon (⋯ → Spárovat telefon).", null);
        Shown += (_, _) =>
        {
            if (!config.IsPaired) Pair();
            Enqueue(initialFiles);
        };
    }

    void OnDrag(DragEventArgs e)
    {
        bool files = e.Data?.GetDataPresent(DataFormats.FileDrop) == true;
        e.Effect = files ? DragDropEffects.Copy : DragDropEffects.None;
        zone.Hot = files;
    }

    void RefreshPhone()
    {
        phone.Text = config.IsPaired ? $"📱 {config.DeviceName}  ·  {config.Address}" : "📱 Telefon není spárovaný";
    }

    void ShowStatus(string text, double? progress)
    {
        status.Text = text;
        bar.Visible = progress.HasValue;
        if (progress.HasValue) bar.Value = (int)Math.Clamp(progress.Value * 1000, 0, 1000);
    }

    void PickFiles()
    {
        using var dlg = new OpenFileDialog { Multiselect = true, Title = "Poslat do telefonu" };
        if (dlg.ShowDialog(this) == DialogResult.OK) Enqueue(dlg.FileNames);
    }

    public void Enqueue(string[] paths, bool bringToFront = false)
    {
        if (bringToFront) ShowWindow();
        var files = new List<string>();
        foreach (var p in paths)
        {
            if (File.Exists(p)) files.Add(p);
            else if (Directory.Exists(p)) ShowStatus($"Složky zatím neposílám ({Path.GetFileName(p)}) – přetáhni soubory.", null);
        }
        foreach (var f in files) queue.Enqueue(f);
        if (files.Count > 0 && !busy) _ = ProcessQueueAsync();
    }

    async Task ProcessQueueAsync()
    {
        busy = true;
        var results = new List<string>();
        try
        {
            if (!config.IsPaired) { Pair(); if (!config.IsPaired) { queue.Clear(); return; } }
            var link = await sender.ConnectAsync(CancellationToken.None);
            RefreshPhone();
            while (queue.Count > 0)
            {
                var file = queue.Dequeue();
                try
                {
                    var r = await sender.SendFileAsync(link, file, CancellationToken.None);
                    results.Add(r);
                    ShowStatus(r, null);
                }
                catch (Exception e) when (e is PhoneException or IOException or HttpRequestException or UnauthorizedAccessException)
                {
                    results.Add($"{Path.GetFileName(file)}: {e.Message}");
                    ShowStatus(results[^1], null);
                }
            }
        }
        catch (Exception e) when (e is PhoneException or HttpRequestException or OperationCanceledException)
        {
            queue.Clear();
            results.Add(e is PhoneException ? e.Message : "Telefon neodpovídá: " + e.Message);
            ShowStatus(results[^1], null);
        }
        finally
        {
            busy = false;
        }
        if (results.Count > 1) ShowStatus(string.Join("\n", results.TakeLast(2)), null);
        if (!Visible && results.Count > 0) tray.ShowBalloonTip(4000, "PhoneDrop", string.Join("\n", results.TakeLast(3)), ToolTipIcon.None);
    }

    void Pair()
    {
        ShowWindow();
        using var dlg = new PairForm();
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        config.Address = dlg.Address;
        config.Token = dlg.Token;
        config.DeviceId = dlg.DeviceId;
        config.DeviceName = dlg.DeviceName;
        config.Save();
        RefreshPhone();
        ShowStatus($"Spárováno s {config.DeviceName}. Přetáhni sem soubor.", null);
    }

    void ToggleSendTo()
    {
        try
        {
            if (SendTo.Installed) SendTo.Uninstall(); else SendTo.Install();
            ShowStatus(SendTo.Installed
                ? "Přidáno: pravé tlačítko na soubor → Odeslat do › Telefon (PhoneDrop)."
                : "Odebráno z nabídky Odeslat do.", null);
        }
        catch (Exception e)
        {
            ShowStatus("Nepodařilo se: " + e.Message, null);
        }
    }

    void ToggleTopMost()
    {
        TopMost = !TopMost;
        config.TopMost = TopMost;
        config.Save();
    }

    void ShowWindow()
    {
        Show();
        if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
        Activate();
    }

    void Quit()
    {
        exiting = true;
        tray.Visible = false;
        Close();
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        // Křížek jen schová okno do oznamovací oblasti; konec přes menu.
        if (!exiting && e.CloseReason == CloseReason.UserClosing)
        {
            e.Cancel = true;
            Hide();
            if (!hintShown)
            {
                hintShown = true;
                tray.ShowBalloonTip(3000, "PhoneDrop běží dál", "Okno otevřeš kliknutím na ikonu. Ukončení přes pravé tlačítko.", ToolTipIcon.None);
            }
            return;
        }
        tray.Dispose();
        base.OnFormClosing(e);
    }

    /// <summary>Čárkovaný rámeček „Přetáhni sem“, při přetahování zezelená.</summary>
    sealed class DropZone : Control
    {
        bool hot;
        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        public bool Hot { get => hot; set { if (hot != value) { hot = value; Invalidate(); } } }

        public DropZone()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);
            Cursor = Cursors.Hand;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Back);
            var r = new Rectangle(1, 1, Width - 3, Height - 3);
            using var path = Rounded(r, 14);
            if (hot) { using var fill = new SolidBrush(Color.FromArgb(28, Accent)); g.FillPath(fill, path); }
            using var pen = new Pen(hot ? Accent : Color.FromArgb(80, 80, 80), 2) { DashStyle = DashStyle.Dash };
            g.DrawPath(pen, path);
            using var big = new Font("Segoe UI", 13f, FontStyle.Bold);
            using var small = new Font("Segoe UI", 9f);
            var c = hot ? Accent : Fore;
            var mid = Height / 2;
            TextRenderer.DrawText(g, "Přetáhni sem soubor nebo APK", big, new Rectangle(0, mid - 26, Width, 28), c,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            TextRenderer.DrawText(g, "APK se v telefonu nabídne k instalaci, ostatní jde do Stažené/Drop", small,
                new Rectangle(8, mid + 4, Width - 16, 36), Dim, TextFormatFlags.HorizontalCenter | TextFormatFlags.WordBreak);
        }

        static GraphicsPath Rounded(Rectangle r, int radius)
        {
            var p = new GraphicsPath();
            int d = radius * 2;
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }
    }
}
