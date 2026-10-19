using System.Net.Http.Json;
using System.Runtime.InteropServices;

namespace PhoneDrop;

/// <summary>Odeslání zpětné vazby autorovi (stejná služba jako formulář na webu, tickety na /stats/tickets/).</summary>
public static class Feedback
{
    const string DefaultUrl = "https://phonedrop.apexpiksel.cz/api/feedback";
    static string Url => Environment.GetEnvironmentVariable("PHONEDROP_FEEDBACK_URL") is { Length: > 0 } u ? u : DefaultUrl;
    static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(20) };

    public static string AppVersion => Updater.Current.ToString(3) + (Updater.IsSelfContained ? " (Windows)" : " (Windows, .NET)");

    /// <summary>Vrátí číslo ticketu; výjimka PhoneException s hláškou pro uživatele, když to nevyjde.</summary>
    public static async Task<int> SendAsync(string kind, string message, string email, CancellationToken ct)
    {
        var payload = new Dictionary<string, string>
        {
            ["kind"] = kind,
            ["message"] = message,
            ["email"] = email,
            ["source"] = "app",
            ["version"] = AppVersion,
            ["os"] = RuntimeInformation.OSDescription,
            ["lang"] = L.Current,
        };
        HttpResponseMessage res;
        try
        {
            res = await Http.PostAsync(Url, new StringContent(System.Text.Json.JsonSerializer.Serialize(payload, FeedbackJson.Default.DictionaryStringString),
                System.Text.Encoding.UTF8, "application/json"), ct);
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException)
        {
            throw new PhoneException(L.FeedbackOffline);
        }
        using (res)
        {
            if ((int)res.StatusCode == 429) throw new PhoneException(L.FeedbackTooMany);
            if (!res.IsSuccessStatusCode) throw new PhoneException(L.FeedbackFailed((int)res.StatusCode));
            var json = await res.Content.ReadAsStringAsync(ct);
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            return doc.RootElement.TryGetProperty("id", out var id) ? id.GetInt32() : 0;
        }
    }
}

[System.Text.Json.Serialization.JsonSerializable(typeof(Dictionary<string, string>))]
internal sealed partial class FeedbackJson : System.Text.Json.Serialization.JsonSerializerContext;

/// <summary>Okénko „Napsat zpětnou vazbu“: druh, zpráva, nepovinný e-mail.</summary>
public sealed class FeedbackForm : Form
{
    readonly RoundButton[] kinds;
    readonly string[] kindCodes = ["bug", "idea", "question", "other"];
    string kind = "bug";
    readonly TextBox message;
    readonly InputBox email;
    readonly Label info;
    readonly RoundButton send;

    public FeedbackForm()
    {
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
        ClientSize = new Size(400, 476);

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

        var le = new Label { Text = L.FeedbackEmail, Font = Theme.Semibold(8f), ForeColor = Theme.Dim, Location = new Point(20, 308), AutoSize = true };
        email = new InputBox { Location = new Point(20, 328), Width = 360 };
        email.Box.PlaceholderText = L.FeedbackEmailPlaceholder;
        email.Box.MaxLength = 200;

        info = new Label { Text = L.FeedbackPrivacy, ForeColor = Theme.Dim, Location = new Point(20, 374), AutoSize = true, MaximumSize = new Size(360, 0), Font = Theme.UiFont(8.5f) };
        send = new RoundButton { Text = L.FeedbackSend, Primary = true, Location = new Point(270, 428), Size = new Size(110, 34) };
        send.Click += async (_, _) => await SendAsync();
        var cancel = new RoundButton { Text = L.Cancel, Location = new Point(152, 428), Size = new Size(110, 34), DialogResult = DialogResult.Cancel };
        CancelButton = cancel;

        Controls.AddRange([title, intro, .. kinds, lm, box, le, email, info, send, cancel]);
        SelectKind(0);
        ResumeLayout(false);
        PerformLayout();
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        Theme.TitleBar(this);
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
        info.ForeColor = Theme.Bad;
        if (text.Length < 3) { info.Text = L.FeedbackTooShort; return; }
        if (mail.Length > 0 && !System.Text.RegularExpressions.Regex.IsMatch(mail, @"^[^@\s]+@[^@\s]+\.[^@\s]+$")) { info.Text = L.FeedbackBadEmail; return; }
        send.Enabled = false;
        info.ForeColor = Theme.Dim;
        info.Text = L.FeedbackSending;
        try
        {
            await Feedback.SendAsync(kind, text, mail, CancellationToken.None);
            DialogResult = DialogResult.OK;
            Close();
        }
        catch (PhoneException e)
        {
            info.ForeColor = Theme.Bad;
            info.Text = e.Message;
        }
        finally
        {
            send.Enabled = true;
        }
    }
}
