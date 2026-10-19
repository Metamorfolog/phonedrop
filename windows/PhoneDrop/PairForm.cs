namespace PhoneDrop;

/// <summary>Párování: vyber telefon nalezený v síti (nebo zadej adresu) a opiš PIN z appky v telefonu.</summary>
public sealed class PairForm : Form
{
    readonly ListBox list;
    readonly TextBox address;
    readonly TextBox pin;
    readonly Label info;
    readonly Button pair;
    readonly Button search;

    public string Address { get; private set; } = "";
    public string Token { get; private set; } = "";
    public string DeviceId { get; private set; } = "";
    public string DeviceName { get; private set; } = "";

    public PairForm()
    {
        Text = "Spárovat telefon";
        Icon = MainForm.AppIcon;
        Font = new Font("Segoe UI", 9.5f);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(360, 330);

        var intro = new Label
        {
            Text = "V telefonu otevři PhoneDrop. Vyber ho ze seznamu (nebo opiš adresu) a zadej PIN, který appka ukazuje.",
            Location = new Point(12, 10), Size = new Size(336, 40),
        };
        list = new ListBox { Location = new Point(12, 54), Size = new Size(336, 96), IntegralHeight = false };
        list.SelectedIndexChanged += (_, _) =>
        {
            if (list.SelectedItem is FoundItem f) address!.Text = f.Phone.Address;
        };
        search = new Button { Text = "Hledat znovu", Location = new Point(12, 156), Size = new Size(110, 28) };
        search.Click += async (_, _) => await DiscoverAsync();

        var la = new Label { Text = "Adresa (IP:port):", Location = new Point(12, 198), AutoSize = true };
        address = new TextBox { Location = new Point(130, 195), Size = new Size(218, 26), PlaceholderText = "192.168.1.20:8765" };
        var lp = new Label { Text = "PIN z telefonu:", Location = new Point(12, 234), AutoSize = true };
        pin = new TextBox { Location = new Point(130, 231), Size = new Size(100, 26), MaxLength = 7 };

        info = new Label { Location = new Point(12, 264), Size = new Size(336, 24), ForeColor = Color.DimGray, AutoEllipsis = true };
        pair = new Button { Text = "Spárovat", Location = new Point(176, 292), Size = new Size(84, 30) };
        pair.Click += async (_, _) => await PairAsync();
        var cancel = new Button { Text = "Zrušit", Location = new Point(264, 292), Size = new Size(84, 30), DialogResult = DialogResult.Cancel };
        AcceptButton = pair;
        CancelButton = cancel;

        Controls.AddRange([intro, list, search, la, address, lp, pin, info, pair, cancel]);
        Shown += async (_, _) => await DiscoverAsync();
    }

    sealed record FoundItem(FoundPhone Phone)
    {
        public override string ToString() => $"{Phone.Instance}   {Phone.Address}";
    }

    async Task DiscoverAsync()
    {
        search.Enabled = false;
        info.Text = "Hledám telefony v síti…";
        try
        {
            var found = await Mdns.DiscoverAsync();
            list.Items.Clear();
            foreach (var f in found) list.Items.Add(new FoundItem(f));
            if (found.Count > 0)
            {
                list.SelectedIndex = 0;
                info.Text = "Nalezeno: " + found.Count;
                pin.Focus();
            }
            else info.Text = "Nic nenalezeno – zadej adresu z appky v telefonu.";
        }
        catch (Exception e)
        {
            info.Text = "Hledání selhalo: " + e.Message;
        }
        finally
        {
            search.Enabled = true;
        }
    }

    async Task PairAsync()
    {
        var addr = address.Text.Trim();
        if (addr.Length == 0) { info.Text = "Zadej adresu telefonu."; return; }
        if (!addr.Contains(':')) addr += ":8765";
        var code = new string(pin.Text.Where(char.IsDigit).ToArray());
        pair.Enabled = false;
        info.Text = "Páruji…";
        try
        {
            var (token, id, name) = await LanLink.PairAsync(addr, code, CancellationToken.None);
            (Address, Token, DeviceId, DeviceName) = (addr, token, id, name);
            DialogResult = DialogResult.OK;
            Close();
        }
        catch (Exception e)
        {
            info.Text = e is PhoneException ? e.Message : "Telefon neodpovídá: " + e.Message;
        }
        finally
        {
            pair.Enabled = true;
        }
    }
}
