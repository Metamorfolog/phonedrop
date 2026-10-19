using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace PhoneDrop;

/// <summary>Obrázek k zpětné vazbě – už zmenšený a překódovaný (bez metadat), připravený k odeslání.</summary>
public sealed class FeedbackImage(string name, string type, byte[] data, Image thumb) : IDisposable
{
    public string Name { get; } = name;
    public string Type { get; } = type;
    public byte[] Data { get; } = data;
    public Image Thumb { get; } = thumb;

    const int MaxBytes = 2_400_000;   // server bere 2,5 MB
    const int MaxSide = 2560;

    /// <summary>
    /// Překreslí obrázek do nové bitmapy (tím zmizí EXIF a jiná metadata), zmenší a zakóduje:
    /// snímky obrazovky jako PNG, když se vejdou, fotky a velké snímky jako JPEG.
    /// </summary>
    public static FeedbackImage From(Image source, string name, bool photo = false)
    {
        double k = Math.Min(1, (double)MaxSide / Math.Max(source.Width, source.Height));
        int w = Math.Max(1, (int)(source.Width * k)), h = Math.Max(1, (int)(source.Height * k));
        using var bmp = new Bitmap(w, h, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bmp))
        {
            g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
            g.DrawImage(source, 0, 0, w, h);
        }
        var stem = Path.GetFileNameWithoutExtension(name);
        if (string.IsNullOrWhiteSpace(stem)) stem = "image";
        if (!photo)
        {
            var png = Encode(bmp, ImageFormat.Png, null);
            if (png.Length <= MaxBytes) return new(stem + ".png", "image/png", png, Thumbnail(bmp));
        }
        // Fotka nebo velký snímek: JPEG (na bílém podkladu, JPEG nemá průhlednost).
        using var flat = new Bitmap(w, h, PixelFormat.Format24bppRgb);
        using (var g = Graphics.FromImage(flat))
        {
            g.Clear(Color.White);
            g.DrawImage(bmp, 0, 0, w, h);
        }
        foreach (long quality in new long[] { 85, 72, 60 })
        {
            var jpg = Encode(flat, ImageFormat.Jpeg, quality);
            if (jpg.Length <= MaxBytes) return new(stem + ".jpg", "image/jpeg", jpg, Thumbnail(flat));
        }
        throw new PhoneException(L.FeedbackShotTooBig);
    }

    public static FeedbackImage FromFile(string path)
    {
        try
        {
            using var ms = new MemoryStream(File.ReadAllBytes(path));
            using var img = Image.FromStream(ms);
            var ext = Path.GetExtension(path).ToLowerInvariant();
            return From(img, Path.GetFileName(path), photo: ext is ".jpg" or ".jpeg");
        }
        catch (Exception e) when (e is ArgumentException or OutOfMemoryException or ExternalException)
        {
            throw new PhoneException(L.FeedbackShotBad);
        }
    }

    /// <summary>Snímek klientské části okna (bez rámečku Windows).</summary>
    public static FeedbackImage FromWindow(Form form)
    {
        using var full = new Bitmap(form.Width, form.Height);
        form.DrawToBitmap(full, new Rectangle(Point.Empty, form.Size));
        var origin = form.PointToScreen(Point.Empty);
        var client = new Rectangle(origin.X - form.Left, origin.Y - form.Top, form.ClientSize.Width, form.ClientSize.Height);
        client.Intersect(new Rectangle(Point.Empty, full.Size));
        using var crop = full.Clone(client, full.PixelFormat);
        return From(crop, "phonedrop-window");
    }

    static byte[] Encode(Image img, ImageFormat format, long? quality)
    {
        using var ms = new MemoryStream();
        if (quality is long q)
        {
            var codec = ImageCodecInfo.GetImageEncoders().First(c => c.FormatID == ImageFormat.Jpeg.Guid);
            using var p = new EncoderParameters(1);
            p.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, q);
            img.Save(ms, codec, p);
        }
        else img.Save(ms, format);
        return ms.ToArray();
    }

    static Image Thumbnail(Image img)
    {
        const int h = 112;
        int w = Math.Max(1, Math.Min(240, img.Width * h / Math.Max(1, img.Height)));
        var t = new Bitmap(w, h);
        using var g = Graphics.FromImage(t);
        g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
        g.DrawImage(img, 0, 0, w, h);
        return t;
    }

    public void Dispose() => Thumb.Dispose();
}

/// <summary>Odeslání zpětné vazby autorovi (stejná služba jako formulář na webu, tickety na /stats/tickets/).</summary>
public static class Feedback
{
    const string DefaultUrl = "https://phonedrop.apexpiksel.cz/api/feedback";
    static string Url => Environment.GetEnvironmentVariable("PHONEDROP_FEEDBACK_URL") is { Length: > 0 } u ? u : DefaultUrl;
    // Se snímky jde i pár MB – na pomalém připojení to chvíli trvá.
    static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(120) };

    public static string AppVersion => Updater.Current.ToString(3) + (Updater.IsSelfContained ? " (Windows)" : " (Windows, .NET)");

    /// <summary>Vrátí číslo ticketu; výjimka PhoneException s hláškou pro uživatele, když to nevyjde.</summary>
    public static async Task<int> SendAsync(string kind, string message, string email, IReadOnlyList<FeedbackImage> images, CancellationToken ct)
    {
        // Utf8JsonWriter místo serializace objektů: funguje i v ořezaném exe bez reflexe.
        using var body = new MemoryStream();
        using (var w = new Utf8JsonWriter(body))
        {
            w.WriteStartObject();
            w.WriteString("kind", kind);
            w.WriteString("message", message);
            w.WriteString("email", email);
            w.WriteString("source", "app");
            w.WriteString("version", AppVersion);
            w.WriteString("os", RuntimeInformation.OSDescription);
            w.WriteString("lang", L.Current);
            w.WriteStartArray("attachments");
            foreach (var img in images)
            {
                w.WriteStartObject();
                w.WriteString("name", img.Name);
                w.WriteString("type", img.Type);
                w.WriteBase64String("data", img.Data);
                w.WriteEndObject();
            }
            w.WriteEndArray();
            w.WriteEndObject();
        }
        HttpResponseMessage res;
        try
        {
            var content = new ByteArrayContent(body.ToArray());
            content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json") { CharSet = "utf-8" };
            res = await Http.PostAsync(Url, content, ct);
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException)
        {
            throw new PhoneException(L.FeedbackOffline);
        }
        using (res)
        {
            var json = await res.Content.ReadAsStringAsync(ct);
            if ((int)res.StatusCode == 429) throw new PhoneException(L.FeedbackTooMany);
            if ((int)res.StatusCode == 413 || json.Contains("\"attachment")) throw new PhoneException(L.FeedbackShotRejected);
            if (!res.IsSuccessStatusCode) throw new PhoneException(L.FeedbackFailed((int)res.StatusCode));
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.TryGetProperty("id", out var id) ? id.GetInt32() : 0;
        }
    }
}

/// <summary>Okénko „Napsat zpětnou vazbu“: druh, zpráva, až 3 obrázky, nepovinný e-mail.</summary>
public sealed class FeedbackForm : Form
{
    const int MaxImages = 3;
    readonly RoundButton[] kinds;
    readonly string[] kindCodes = ["bug", "idea", "question", "other"];
    string kind = "bug";
    readonly TextBox message;
    readonly InputBox email;
    readonly Label info;
    readonly RoundButton send;
    readonly FlowLayoutPanel thumbs;
    readonly List<FeedbackImage> images = [];
    readonly Form? window;

    /// <param name="window">Okno, které jde jedním klikem vyfotit (hlavní okno PhoneDropu).</param>
    public FeedbackForm(Form? window = null)
    {
        this.window = window;
        SuspendLayout();
        AutoScaleDimensions = new SizeF(96F, 96F);
        AutoScaleMode = AutoScaleMode.Dpi;
        Text = L.FeedbackTitle;
        Icon = MainForm.AppIcon;
        Font = Theme.UiFont(9.5f);
        BackColor = Theme.Back;
        ForeColor = Theme.Fore;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(400, 598);
        KeyPreview = true;

        var title = new Label { Text = L.FeedbackTitle, Font = Theme.Semibold(14f), Location = new Point(20, 16), AutoSize = true };
        var intro = new Label { Text = L.FeedbackIntro, ForeColor = Theme.Dim, Location = new Point(20, 50), AutoSize = true, MaximumSize = new Size(360, 0) };

        string[] labels = [L.KindBug, L.KindIdea, L.KindQuestion, L.KindOther];
        kinds = new RoundButton[4];
        int x = 20;
        for (int i = 0; i < 4; i++)
        {
            int n = i;
            var b = new RoundButton { Text = labels[i], Location = new Point(x, 98), Height = 30, Font = Theme.Semibold(9f) };
            b.Width = TextRenderer.MeasureText(labels[i], b.Font).Width + 28;
            b.Click += (_, _) => SelectKind(n);
            kinds[i] = b;
            x += b.Width + 8;
        }

        var lm = new Label { Text = L.FeedbackMessage, Font = Theme.Semibold(8f), ForeColor = Theme.Dim, Location = new Point(20, 144), AutoSize = true };
        var box = new Panel { Location = new Point(20, 164), Size = new Size(360, 132), BackColor = Theme.Surface2, Padding = new Padding(10, 8, 6, 8) };
        message = new TextBox
        {
            Multiline = true, ScrollBars = ScrollBars.None, BorderStyle = BorderStyle.None, Dock = DockStyle.Fill,
            BackColor = Theme.Surface2, ForeColor = Theme.Fore, Font = Theme.UiFont(10f), MaxLength = 5000,
            PlaceholderText = L.FeedbackPlaceholder,
        };
        box.Controls.Add(message);

        // Obrázky: snímek okna, soubor, nebo Ctrl+V (třeba výřez z Win+Shift+S).
        var shotWindow = new RoundButton { Text = L.FeedbackShotWindow, Location = new Point(20, 308), Height = 30, Font = Theme.Semibold(9f) };
        shotWindow.Width = TextRenderer.MeasureText(shotWindow.Text, shotWindow.Font).Width + 28;
        shotWindow.Click += (_, _) => Add(() => FeedbackImage.FromWindow(this.window!));
        shotWindow.Visible = window != null;
        var shotFile = new RoundButton { Text = L.FeedbackShotFile, Location = new Point(window != null ? shotWindow.Right + 8 : 20, 308), Height = 30, Font = Theme.Semibold(9f) };
        shotFile.Width = TextRenderer.MeasureText(shotFile.Text, shotFile.Font).Width + 28;
        shotFile.Click += (_, _) => PickImages();
        var paste = new Label { Text = L.FeedbackShotPaste, ForeColor = Theme.Dim, Font = Theme.UiFont(8.5f), AutoSize = true, Location = new Point(shotFile.Right + 10, 314) };
        thumbs = new FlowLayoutPanel { Location = new Point(20, 346), Size = new Size(360, 60), BackColor = Theme.Back, WrapContents = false, Padding = Padding.Empty };

        var le = new Label { Text = L.FeedbackEmail, Font = Theme.Semibold(8f), ForeColor = Theme.Dim, Location = new Point(20, 416), AutoSize = true };
        email = new InputBox { Location = new Point(20, 436), Width = 360 };
        email.Box.PlaceholderText = L.FeedbackEmailPlaceholder;
        email.Box.MaxLength = 200;

        info = new Label { Text = L.FeedbackPrivacy, ForeColor = Theme.Dim, Location = new Point(20, 482), AutoSize = true, MaximumSize = new Size(360, 0), Font = Theme.UiFont(8.5f) };
        send = new RoundButton { Text = L.FeedbackSend, Primary = true, Location = new Point(270, 550), Size = new Size(110, 34) };
        send.Click += async (_, _) => await SendAsync();
        var cancel = new RoundButton { Text = L.Cancel, Location = new Point(152, 550), Size = new Size(110, 34), DialogResult = DialogResult.Cancel };
        CancelButton = cancel;

        Controls.AddRange([title, intro, .. kinds, lm, box, shotWindow, shotFile, paste, thumbs, le, email, info, send, cancel]);
        SelectKind(0);
        ResumeLayout(false);
        PerformLayout();
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        Theme.TitleBar(this);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.KeyCode != Keys.V || !e.Control) return;
        // Obrázek ze schránky (výřez obrazovky) nebo zkopírované soubory; text se vkládá normálně.
        if (Clipboard.ContainsImage())
        {
            using var img = Clipboard.GetImage();
            if (img != null) Add(() => FeedbackImage.From(img, "screenshot"));
            e.SuppressKeyPress = true;
        }
        else if (Clipboard.ContainsFileDropList())
        {
            foreach (var f in Clipboard.GetFileDropList().Cast<string>().Where(IsImageFile)) Add(() => FeedbackImage.FromFile(f));
            e.SuppressKeyPress = true;
        }
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        foreach (var img in images) img.Dispose();
        base.OnFormClosed(e);
    }

    static bool IsImageFile(string path) =>
        new[] { ".png", ".jpg", ".jpeg", ".bmp", ".gif", ".tif", ".tiff" }.Contains(Path.GetExtension(path).ToLowerInvariant());

    /// <summary>Pro --ui-selftest: přiloží snímek okna; false, když se to nepovedlo.</summary>
    internal bool AddWindowShot()
    {
        if (window == null) return false;
        int before = images.Count;
        Add(() => FeedbackImage.FromWindow(window));
        return images.Count == before + 1;
    }

    void PickImages()
    {
        using var dlg = new OpenFileDialog { Multiselect = true, Filter = $"{L.FeedbackShotPick}|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.tif;*.tiff" };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        foreach (var f in dlg.FileNames) Add(() => FeedbackImage.FromFile(f));
    }

    void Add(Func<FeedbackImage> make)
    {
        if (images.Count >= MaxImages) { Say(L.FeedbackShotMax, true); return; }
        FeedbackImage img;
        try { img = make(); }
        catch (PhoneException e) { Say(e.Message, true); return; }
        images.Add(img);
        // Přidává se až po načtení okna, takže rozměry podle DPI ručně.
        float k = DeviceDpi / 96f;
        int th = (int)(56 * k);
        var thumb = new PictureBox
        {
            Image = img.Thumb, SizeMode = PictureBoxSizeMode.Zoom, Size = new Size(Math.Min((int)(110 * k), img.Thumb.Width * th / img.Thumb.Height + 4), th),
            BackColor = Theme.Surface2, Margin = new Padding(0, 0, (int)(8 * k), 0),
        };
        // Křížek v rohu náhledu = odebrat.
        var remove = new Label { Text = "✕", AutoSize = false, Size = new Size((int)(18 * k), (int)(18 * k)), TextAlign = ContentAlignment.MiddleCenter,
            BackColor = Color.FromArgb(200, 20, 20, 20), ForeColor = Color.White, Font = Theme.UiFont(8f), Cursor = Cursors.Hand };
        remove.Location = new Point(thumb.Width - remove.Width, 0);
        thumb.Controls.Add(remove);
        new ToolTip().SetToolTip(remove, L.FeedbackShotRemove);
        void Remove()
        {
            images.Remove(img);
            thumbs.Controls.Remove(thumb);
            thumb.Dispose();
            img.Dispose();
        }
        remove.Click += (_, _) => Remove();
        thumbs.Controls.Add(thumb);
        Say(L.FeedbackPrivacy, false);
    }

    void Say(string text, bool error)
    {
        info.ForeColor = error ? Theme.Bad : Theme.Dim;
        info.Text = text;
    }

    void SelectKind(int n)
    {
        kind = kindCodes[n];
        for (int i = 0; i < kinds.Length; i++)
        {
            kinds[i].Primary = i == n;
            kinds[i].Invalidate();
        }
    }

    async Task SendAsync()
    {
        var text = message.Text.Trim();
        var mail = email.Box.Text.Trim();
        if (text.Length < 3) { Say(L.FeedbackTooShort, true); return; }
        if (mail.Length > 0 && !System.Text.RegularExpressions.Regex.IsMatch(mail, @"^[^@\s]+@[^@\s]+\.[^@\s]+$")) { Say(L.FeedbackBadEmail, true); return; }
        send.Enabled = false;
        Say(L.FeedbackSending, false);
        try
        {
            await Feedback.SendAsync(kind, text, mail, images, CancellationToken.None);
            DialogResult = DialogResult.OK;
            Close();
        }
        catch (PhoneException e)
        {
            Say(e.Message, true);
        }
        finally
        {
            send.Enabled = true;
        }
    }
}
