namespace PhoneDrop;

/// <summary>Malé okénko s drop zónou, průběhem a ikonou v oznamovací oblasti.</summary>
public sealed class MainForm : Form
{
    public static readonly Icon AppIcon = new(typeof(MainForm).Assembly.GetManifestResourceStream("PhoneDrop.ico")!);

    readonly Config config = Config.Load();
    readonly Sender sender;
    readonly Queue<string> queue = new();
    readonly NotifyIcon tray;
    readonly PhoneHeader header;
    readonly DropZone zone;
    readonly StatusCard card;
    readonly ToolStripMenuItem sendToItem;
    readonly ToolStripMenuItem topItem;
    bool busy;
    bool exiting;
    bool hintShown;

    public MainForm(string[] initialFiles)
    {
        sender = new Sender(config);
        sender.Status += (title, detail, progress) =>
            BeginInvoke(() => card!.Set(CardKind.Busy, title, detail, progress, queue.Count));

        Text = "PhoneDrop";
        Icon = AppIcon;
        BackColor = Theme.Back;
        ForeColor = Theme.Fore;
        Font = Theme.UiFont(9.5f);
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        // Rozměry níž jsou pro 96 DPI; WinForms je při načtení přepočítá na DPI monitoru.
        AutoScaleDimensions = new SizeF(96F, 96F);
        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(400, 380);
        StartPosition = FormStartPosition.Manual;
        // Do pravého dolního rohu – až po přepočtu na DPI, kdy je známá skutečná velikost.
        Load += (_, _) =>
        {
            var wa = Screen.FromControl(this).WorkingArea;
            Location = new Point(Math.Max(wa.Left, wa.Right - Width - 24), Math.Max(wa.Top, wa.Bottom - Height - 24));
        };
        TopMost = config.TopMost;

        header = new PhoneHeader { Dock = DockStyle.Top, Height = 62 };
        zone = new DropZone { Dock = DockStyle.Fill };
        zone.Click += (_, _) => PickFiles();
        var zoneHost = new Panel { Dock = DockStyle.Fill, Padding = new Padding(14, 2, 14, 10), BackColor = Theme.Back };
        zoneHost.Controls.Add(zone);
        card = new StatusCard { Dock = DockStyle.Fill };
        var cardHost = new Panel { Dock = DockStyle.Bottom, Height = 80, Padding = new Padding(14, 0, 14, 14), BackColor = Theme.Back };
        cardHost.Controls.Add(card);

        var menu = new ContextMenuStrip { Renderer = new DarkMenuRenderer(), ShowImageMargin = false, ShowCheckMargin = true, Font = Theme.UiFont(9.5f) };
        menu.Items.Add(L.MenuPair, null, (_, _) => Pair());
        sendToItem = new ToolStripMenuItem(L.MenuSendTo, null, (_, _) => ToggleSendTo());
        topItem = new ToolStripMenuItem(L.MenuTopMost, null, (_, _) => ToggleTopMost());
        menu.Items.Add(sendToItem);
        menu.Items.Add(topItem);
        var langItem = new ToolStripMenuItem(L.MenuLanguage);
        foreach (var (code, name) in new[] { ("auto", L.MenuAuto) }.Concat(L.Languages))
        {
            var item = new ToolStripMenuItem(name, null, (_, _) => SetLanguage(code)) { Checked = config.Language == code };
            langItem.DropDownItems.Add(item);
        }
        langItem.DropDown.BackColor = Theme.Surface;
        menu.Items.Add(langItem);
        var themeItem = new ToolStripMenuItem(L.MenuTheme);
        foreach (var (code, name) in new[] { ("auto", L.ThemeAuto), ("light", L.ThemeLight), ("dark", L.ThemeDark) })
            themeItem.DropDownItems.Add(new ToolStripMenuItem(name, null, (_, _) => SetAppearance(code)) { Checked = config.Appearance == code });
        themeItem.DropDown.BackColor = Theme.Surface;
        menu.Items.Add(themeItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(L.MenuQuit, null, (_, _) => Quit());
        foreach (ToolStripItem item in menu.Items) item.Padding = new Padding(4, 5, 12, 5);
        menu.Opening += (_, _) =>
        {
            sendToItem.Checked = SendTo.Installed;
            topItem.Checked = TopMost;
        };

        var more = new MoreButton { Anchor = AnchorStyles.Top | AnchorStyles.Right };
        more.Location = new Point(ClientSize.Width - more.Width - 12, 14);
        more.Click += (_, _) => menu.Show(more, new Point(more.Width - menu.Width, more.Height + 2));
        header.Controls.Add(more);

        Controls.Add(zoneHost);
        Controls.Add(cardHost);
        Controls.Add(header);

        tray = new NotifyIcon { Icon = AppIcon, Text = "PhoneDrop", Visible = true, ContextMenuStrip = menu };
        tray.MouseClick += (_, e) => { if (e.Button == MouseButtons.Left) ShowWindow(); };

        // Drop funguje kamkoli do okna, ne jen do rámečku.
        foreach (Control c in new Control[] { this, header, zoneHost, zone, cardHost, card })
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
        if (config.IsPaired) card.Set(CardKind.Info, L.Ready, L.ReadyDetail);
        else card.Set(CardKind.Info, L.NotPairedTitle, L.NotPairedHint);
        Shown += (_, _) =>
        {
            if (!config.IsPaired) Pair();
            Enqueue(initialFiles);
        };
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        Microsoft.Win32.SystemEvents.UserPreferenceChanged += OnSystemPreferences;
        Theme.TitleBar(this);
    }

    void OnDrag(DragEventArgs e)
    {
        bool files = e.Data?.GetDataPresent(DataFormats.FileDrop) == true;
        e.Effect = files ? DragDropEffects.Copy : DragDropEffects.None;
        zone.Hot = files;
    }

    void RefreshPhone()
    {
        if (config.IsPaired) header.Set(config.DeviceName ?? L.Phone, config.Address!, true);
        else header.Set(L.NoPhone, L.Unpaired, false);
    }

    void PickFiles()
    {
        using var dlg = new OpenFileDialog { Multiselect = true, Title = L.PickTitle };
        if (dlg.ShowDialog(this) == DialogResult.OK) Enqueue(dlg.FileNames);
    }

    public void Enqueue(string[] paths, bool bringToFront = false)
    {
        if (bringToFront) ShowWindow();
        var files = new List<string>();
        foreach (var p in paths)
        {
            if (File.Exists(p)) files.Add(p);
            else if (Directory.Exists(p)) card.Set(CardKind.Error, Path.GetFileName(p), L.NoFolders);
        }
        foreach (var f in files) queue.Enqueue(f);
        if (files.Count > 0 && !busy) _ = ProcessQueueAsync();
    }

    async Task ProcessQueueAsync()
    {
        busy = true;
        var results = new List<Outcome>();
        try
        {
            if (!config.IsPaired) { Pair(); if (!config.IsPaired) { queue.Clear(); return; } }
            card.Set(CardKind.Busy, config.DeviceName ?? L.Phone, L.Connecting, null, queue.Count);
            var link = await sender.ConnectAsync(CancellationToken.None);
            RefreshPhone();
            while (queue.Count > 0)
            {
                var file = queue.Dequeue();
                Outcome r;
                try
                {
                    r = await sender.SendFileAsync(link, file, CancellationToken.None);
                }
                catch (Exception e) when (e is PhoneException or IOException or HttpRequestException or UnauthorizedAccessException)
                {
                    r = new Outcome(false, Path.GetFileName(file), e.Message);
                }
                results.Add(r);
                card.Set(r.Ok ? CardKind.Ok : CardKind.Error, r.Title, r.Detail, null, queue.Count);
            }
        }
        catch (Exception e) when (e is PhoneException or HttpRequestException or OperationCanceledException)
        {
            queue.Clear();
            results.Add(new Outcome(false, config.DeviceName ?? L.Phone, e is PhoneException ? e.Message : L.NotResponding(e.Message)));
            card.Set(CardKind.Error, results[^1].Title, results[^1].Detail);
            header.Set(config.DeviceName ?? L.Phone, L.Unreachable(config.Address!), false);
        }
        finally
        {
            busy = false;
        }
        if (results.Count > 1)
        {
            int ok = results.Count(r => r.Ok);
            card.Set(ok == results.Count ? CardKind.Ok : CardKind.Error, L.SentSummary(ok, results.Count),
                string.Join("  ·  ", results.Where(r => !r.Ok).Select(r => r.Title).DefaultIfEmpty(L.AllGood)));
        }
        if (!Visible && results.Count > 0)
            tray.ShowBalloonTip(4000, "PhoneDrop", string.Join("\n", results.TakeLast(3)), ToolTipIcon.None);
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
        card.Set(CardKind.Ok, L.PairedWith(config.DeviceName!), L.DropHere);
    }

    void ToggleSendTo()
    {
        try
        {
            if (SendTo.Installed) SendTo.Uninstall(); else SendTo.Install();
            card.Set(CardKind.Ok, SendTo.Installed ? L.SendToAdded : L.SendToRemoved,
                SendTo.Installed ? L.SendToAddedHint : L.SendToRemovedHint);
        }
        catch (Exception e)
        {
            card.Set(CardKind.Error, L.Failed, e.Message);
        }
    }

    /// <summary>Nový jazyk: uložit a okno postavit znovu (Program ho po zavření vytvoří s novými texty).</summary>
    void SetLanguage(string code)
    {
        config.Language = code;
        config.Save();
        L.Init(code);
        // Odkaz v „Odeslat do“ nese název v jazyce appky.
        if (SendTo.Installed) try { SendTo.Install(); } catch (Exception) { }
        Program.Restart = true;
        Quit();
    }

    void SetAppearance(string code)
    {
        config.Appearance = code;
        config.Save();
        Theme.Init(code);
        Program.Restart = true;
        Quit();
    }

    /// <summary>Při „auto“ sleduje přepnutí světlého/tmavého režimu ve Windows a okno přebarví.</summary>
    void OnSystemPreferences(object? sender, Microsoft.Win32.UserPreferenceChangedEventArgs e)
    {
        if (config.Appearance != "auto" || e.Category != Microsoft.Win32.UserPreferenceCategory.General) return;
        bool wasDark = Theme.IsDark;
        Theme.Init("auto");
        if (Theme.IsDark != wasDark) BeginInvoke(() => { Program.Restart = true; Quit(); });
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
                tray.ShowBalloonTip(3000, L.StillRunning, L.StillRunningText, ToolTipIcon.None);
            }
            return;
        }
        Microsoft.Win32.SystemEvents.UserPreferenceChanged -= OnSystemPreferences;
        tray.Dispose();
        base.OnFormClosing(e);
    }
}
