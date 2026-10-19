namespace PhoneDrop;

/// <summary>Párování: vyber telefon nalezený v síti (nebo zadej adresu) a opiš PIN z appky v telefonu.</summary>
public sealed class PairForm : Form
{
    readonly ListBox list;
    readonly InputBox address;
    readonly InputBox pin;
    readonly Label info;
    readonly Label empty;
    readonly RoundButton pair;
    readonly RoundButton search;

    public string Address { get; private set; } = "";
    public string Token { get; private set; } = "";
    public string DeviceId { get; private set; } = "";
    public string DeviceName { get; private set; } = "";

    public PairForm()
    {
        SuspendLayout();
        Text = L.PairTitle;
        Icon = MainForm.AppIcon;
        Font = Theme.UiFont(9.5f);
        BackColor = Theme.Back;
        ForeColor = Theme.Fore;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        AutoScaleDimensions = new SizeF(96F, 96F);
        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(380, 446);

        var title = new Label { Text = L.PairTitle, Font = Theme.Semibold(14f), Location = new Point(20, 16), AutoSize = true };
        var intro = new Label
        {
            Text = L.PairIntro,
            ForeColor = Theme.Dim, Location = new Point(20, 48), AutoSize = true, MaximumSize = new Size(340, 0),
        };

        var found = new Label { Text = L.PairFound, Font = Theme.Semibold(8f), ForeColor = Theme.Dim, Location = new Point(20, 108), AutoSize = true };
        search = new RoundButton { Text = L.PairSearchAgain, Location = new Point(258, 102), Size = new Size(102, 28), Font = Theme.Semibold(8.5f) };
        search.Click += async (_, _) => await DiscoverAsync();
        list = new ListBox
        {
            Location = new Point(20, 136), Size = new Size(340, 104), IntegralHeight = false, BorderStyle = BorderStyle.None,
            BackColor = Theme.Back, ForeColor = Theme.Fore, DrawMode = DrawMode.OwnerDrawFixed, ItemHeight = 52,
        };
        list.DrawItem += DrawPhone;
        list.SelectedIndexChanged += (_, _) =>
        {
            if (list.SelectedItem is FoundItem f) address!.Box.Text = f.Phone.Address;
            list.Invalidate();
        };

        var la = new Label { Text = L.PairAddress, Font = Theme.Semibold(8f), ForeColor = Theme.Dim, Location = new Point(20, 254), AutoSize = true };
        address = new InputBox { Location = new Point(20, 274), Width = 200 };
        address.Box.PlaceholderText = "192.168.1.20:8765";
        var lp = new Label { Text = "PIN", Font = Theme.Semibold(8f), ForeColor = Theme.Dim, Location = new Point(232, 254), AutoSize = true };
        pin = new InputBox(10.5f) { Location = new Point(232, 274), Width = 128 };
        pin.Box.Font = new Font("Consolas", 12f, FontStyle.Bold);
        pin.Box.MaxLength = 7;
        pin.Box.PlaceholderText = "000 000";
        pin.Box.TextAlign = HorizontalAlignment.Center;

        info = new Label { Location = new Point(20, 330), AutoSize = true, MaximumSize = new Size(340, 0), ForeColor = Theme.Dim };
        empty = new Label
        {
            Location = list.Location, Size = list.Size, TextAlign = ContentAlignment.MiddleCenter,
            ForeColor = Theme.Dim, BackColor = Theme.Surface, Text = L.PairSearching,
        };
        pair = new RoundButton { Text = L.PairButton, Primary = true, Location = new Point(250, 392), Size = new Size(110, 36) };
        pair.Click += async (_, _) => await PairAsync();
        var cancel = new RoundButton { Text = L.Cancel, Location = new Point(132, 392), Size = new Size(110, 36), DialogResult = DialogResult.Cancel };
        AcceptButton = pair;
        CancelButton = cancel;

        Controls.AddRange([title, intro, found, search, empty, list, la, address, lp, pin, info, pair, cancel]);
        Shown += async (_, _) =>
        {
            list.ItemHeight = (int)(52 * DeviceDpi / 96f);
            if (MainForm.SelfTest) return;
            await DiscoverAsync();
        };
        ResumeLayout(false);
        PerformLayout();
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        Theme.TitleBar(this);
    }

    sealed record FoundItem(FoundPhone Phone)
    {
        public override string ToString() => Phone.Instance;
    }

    /// <summary>Položka seznamu jako karta: ikona telefonu, název a adresa; vybraná má zelený rámeček.</summary>
    void DrawPhone(object? sender, DrawItemEventArgs e)
    {
        var g = e.Graphics.Smooth();
        using (var bg = new SolidBrush(Theme.Back)) g.FillRectangle(bg, e.Bounds);
        if (e.Index < 0 || list.Items[e.Index] is not FoundItem item) return;
        bool selected = (e.State & DrawItemState.Selected) != 0;
        float k = list.DeviceDpi / 96f;
        var r = new RectangleF(e.Bounds.X + 0.5f, e.Bounds.Y + 2.5f * k, e.Bounds.Width - 1.5f, e.Bounds.Height - 6 * k);
        using (var path = Theme.Rounded(r, 12 * k))
        {
            using var fill = new SolidBrush(selected ? Theme.Mix(Theme.Surface, Theme.Accent, 0.10f) : Theme.Surface);
            g.FillPath(fill, path);
            if (selected) { using var pen = new Pen(Theme.Accent, 1.4f); g.DrawPath(pen, path); }
        }
        PaintedControl.DrawPhone(g, new RectangleF(r.X + 16 * k, r.Y + 11 * k, 13 * k, 22 * k), selected ? Theme.Accent : Theme.Dim, 1.6f * k);
        var name = item.Phone.Instance.StartsWith("PhoneDrop ") ? item.Phone.Instance[10..] : item.Phone.Instance;
        using var tf = Theme.PxFont(9.75f, k, true);
        using var df = Theme.PxFont(8.5f, k);
        int x = (int)(r.X + 44 * k), w = (int)(r.Width - 54 * k);
        TextRenderer.DrawText(g, name, tf, new Rectangle(x, (int)(r.Y + 6 * k), w, (int)(20 * k)), Theme.Fore, TextFormatFlags.EndEllipsis);
        TextRenderer.DrawText(g, item.Phone.Address, df, new Rectangle(x, (int)(r.Y + 24 * k), w, (int)(18 * k)), Theme.Dim, TextFormatFlags.EndEllipsis);
    }

    async Task DiscoverAsync()
    {
        search.Enabled = false;
        if (list.Items.Count == 0) { list.Visible = false; empty.Text = L.PairSearching; }
        info.ForeColor = Theme.Dim;
        info.Text = L.PairSearchingNetwork;
        try
        {
            var found = await Mdns.DiscoverAsync();
            list.Items.Clear();
            foreach (var f in found) list.Items.Add(new FoundItem(f));
            list.Visible = found.Count > 0;
            empty.Text = L.PairNoneInList;
            if (found.Count > 0)
            {
                list.SelectedIndex = 0;
                info.Text = found.Count == 1 ? L.PairFoundOne : L.PairFoundMany(found.Count);
                pin.Box.Focus();
            }
            else info.Text = L.PairNothing;
        }
        catch (Exception e)
        {
            info.Text = L.PairSearchFailed(e.Message);
        }
        finally
        {
            search.Enabled = true;
        }
    }

    async Task PairAsync()
    {
        var addr = address.Box.Text.Trim();
        info.ForeColor = Theme.Bad;
        if (addr.Length == 0) { info.Text = L.PairNeedAddress; return; }
        if (!addr.Contains(':')) addr += ":8765";
        var code = new string(pin.Box.Text.Where(char.IsDigit).ToArray());
        if (code.Length != 6) { info.Text = L.PairPinDigits; return; }
        pair.Enabled = false;
        info.ForeColor = Theme.Dim;
        info.Text = L.Pairing;
        try
        {
            var (token, id, name) = await LanLink.PairAsync(addr, code, CancellationToken.None);
            (Address, Token, DeviceId, DeviceName) = (addr, token, id, name);
            DialogResult = DialogResult.OK;
            Close();
        }
        catch (Exception e)
        {
            info.ForeColor = Theme.Bad;
            info.Text = e is PhoneException ? e.Message : L.PhoneNotResponding(e.Message);
        }
        finally
        {
            pair.Enabled = true;
        }
    }
}
