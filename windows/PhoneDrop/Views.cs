using System.ComponentModel;
using System.Drawing.Drawing2D;

namespace PhoneDrop;

/// <summary>
/// Kreslené prvky hlavního okna. Kreslí se v logických jednotkách (96 DPI) přes ScaleTransform;
/// text (TextRenderer měřítko grafiky ignoruje) dostává fyzické obdélníky a písmo v pixelech podle DPI monitoru.
/// </summary>
abstract class PaintedControl : Control
{
    protected PaintedControl()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
                 | ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
        BackColor = Theme.Back;
    }

    /// <summary>Měřítko monitoru (1 = 100 %).</summary>
    protected float K => DeviceDpi / 96f;
    /// <summary>Logická šířka a výška.</summary>
    protected float W => Width / K;
    protected float H => Height / K;

    protected Graphics Begin(PaintEventArgs e)
    {
        var g = e.Graphics.Smooth();
        g.Clear(BackColor);
        g.ScaleTransform(K, K);
        return g;
    }

    protected Font Px(float pt, bool semibold = false) => Theme.PxFont(pt, K, semibold);

    protected void DrawText(Graphics g, string s, Font f, RectangleF r, Color c, TextFormatFlags flags) =>
        TextRenderer.DrawText(g, s, f, Rectangle.Round(new RectangleF(r.X * K, r.Y * K, r.Width * K, r.Height * K)), c, flags);

    protected float MeasureWidth(string s, Font f) => TextRenderer.MeasureText(s, f).Width / K;

    protected override void OnDpiChangedAfterParent(EventArgs e)
    {
        base.OnDpiChangedAfterParent(e);
        Invalidate();
    }

    /// <summary>Telefon: obrys s displejem (pro hlavičku i párovací dialog).</summary>
    public static void DrawPhone(Graphics g, RectangleF r, Color color, float stroke)
    {
        using var pen = new Pen(color, stroke);
        using var body = Theme.Rounded(r, r.Width * 0.22f);
        g.DrawPath(pen, body);
        using var b = new SolidBrush(color);
        g.FillEllipse(b, r.X + r.Width / 2 - stroke * 0.7f, r.Bottom - stroke * 3.2f, stroke * 1.4f, stroke * 1.4f);
    }
}

/// <summary>Hlavička: dlaždice s telefonem, jeho název a adresa se stavovou tečkou.</summary>
sealed class PhoneHeader : PaintedControl
{
    string name = "", detail = "";
    bool? connected;

    /// <summary>connected: true = dostupný (zelená), false = nedostupný (červená), null = neověřeno (šedá).</summary>
    public void Set(string name, string detail, bool? connected)
    {
        (this.name, this.detail, this.connected) = (name, detail, connected);
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = Begin(e);
        float s = H - 16;
        var tile = new RectangleF(14, 8, s, s);
        Theme.CardShadow(g, tile, 10);
        using (var path = Theme.Rounded(tile, 10))
        using (var fill = new SolidBrush(Theme.Surface))
            g.FillPath(fill, path);
        DrawPhone(g, new RectangleF(tile.X + s * 0.33f, tile.Y + s * 0.2f, s * 0.34f, s * 0.6f),
            connected == true ? Theme.Accent : Theme.Dim, 1.8f);

        float x = tile.Right + 12;
        using var title = Px(10.5f, true);
        using var small = Px(8.75f);
        DrawText(g, name, title, new RectangleF(x, 9, W - x - 52, 24), Theme.Fore, TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);
        using (var dot = new SolidBrush(connected switch { true => Theme.Accent, false => Theme.Bad, _ => Theme.Dim }))
            g.FillEllipse(dot, x + 1, 37, 7, 7);
        DrawText(g, detail, small, new RectangleF(x + 13, 32, W - x - 62, 18), Theme.Dim, TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);
    }
}

/// <summary>Kulaté tlačítko „⋯“ v rohu hlavičky.</summary>
sealed class MoreButton : PaintedControl
{
    bool hover;

    public MoreButton() { Cursor = Cursors.Hand; Size = new Size(34, 34); }

    protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = Begin(e);
        if (hover)
        {
            using var b = new SolidBrush(Theme.Surface2);
            g.FillEllipse(b, 0, 0, W - 1, H - 1);
        }
        using var dot = new SolidBrush(Theme.Fore);
        for (int i = -1; i <= 1; i++) g.FillEllipse(dot, W / 2f + i * 6 - 1.75f, H / 2f - 1.75f, 3.5f, 3.5f);
    }
}

/// <summary>Drop zóna: karta s ilustrací šipky do telefonu; při přetahování zezelená a šipka „poskakuje“.</summary>
sealed class DropZone : PaintedControl
{
    readonly System.Windows.Forms.Timer anim = new() { Interval = 16 };
    float t, glow;
    bool hot, hover;
    bool[,]? qr;

    /// <summary>QR kód k párování; null = běžná drop zóna.</summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool[,]? Qr
    {
        get => qr;
        set { qr = value; Invalidate(); }
    }

    public DropZone()
    {
        Cursor = Cursors.Hand;
        anim.Tick += (_, _) =>
        {
            t += 0.016f;
            glow += ((hot ? 1f : hover ? 0.35f : 0f) - glow) * 0.18f;
            if (!hot && !hover && glow < 0.01f) { glow = 0; anim.Stop(); }
            Invalidate();
        };
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool Hot
    {
        get => hot;
        set { if (hot != value) { hot = value; anim.Start(); } }
    }

    protected override void OnMouseEnter(EventArgs e) { hover = true; anim.Start(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { hover = false; anim.Start(); base.OnMouseLeave(e); }

    protected override void Dispose(bool disposing)
    {
        if (disposing) anim.Dispose();
        base.Dispose(disposing);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = Begin(e);
        var r = new RectangleF(2, 1, W - 5, H - (Theme.IsDark ? 3 : 7));
        Theme.CardShadow(g, r, 16);
        using var path = Theme.Rounded(r, 16);
        using (var fill = new SolidBrush(Theme.Mix(Theme.Surface, Theme.Accent, glow * 0.12f)))
            g.FillPath(fill, path);
        using (var pen = new Pen(Theme.Mix(Theme.Line, Theme.Accent, glow), 1.6f) { DashPattern = [5f, 4f] })
            g.DrawPath(pen, path);
        if (qr != null)
        {
            PaintQr(g, qr);
            return;
        }

        // Ilustrace: kruh, v něm šipka dolů nad „miskou“.
        float cx = W / 2f, cy = H / 2f - 34;
        float rad = 30 + glow * 3;
        using (var circle = new SolidBrush(Color.FromArgb((int)(28 + glow * 40), Theme.Accent)))
            g.FillEllipse(circle, cx - rad, cy - rad, rad * 2, rad * 2);
        float bob = hot ? (float)Math.Sin(t * 7) * 3.5f : 0;
        var c = Theme.Mix(Theme.Mix(Theme.Accent, Theme.Fore, 0.25f), Theme.Accent, glow);
        using (var pen = new Pen(c, 2.6f) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round })
        {
            g.DrawLine(pen, cx, cy - 13 + bob, cx, cy + 5 + bob);
            g.DrawLines(pen, [new PointF(cx - 7, cy - 2 + bob), new PointF(cx, cy + 5 + bob), new PointF(cx + 7, cy - 2 + bob)]);
            g.DrawLines(pen, [new PointF(cx - 13, cy + 6), new PointF(cx - 13, cy + 13), new PointF(cx + 13, cy + 13), new PointF(cx + 13, cy + 6)]);
        }

        using var big = Px(12.5f, true);
        using var small = Px(8.75f);
        float ty = cy + rad + 12;
        DrawText(g, hot ? L.DropHot : L.DropTitle, big, new RectangleF(6, ty, W - 12, 26), hot ? Theme.Accent : Theme.Fore,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.EndEllipsis);
        DrawText(g, L.DropClick, small, new RectangleF(10, ty + 28, W - 20, 18), Theme.Dim, TextFormatFlags.HorizontalCenter | TextFormatFlags.EndEllipsis);
        DrawText(g, L.DropApk, small, new RectangleF(10, ty + 46, W - 20, 18), Theme.Dim, TextFormatFlags.HorizontalCenter | TextFormatFlags.EndEllipsis);
    }

    /// <summary>QR vlevo (vždy tmavý na bílém – tak ho čtou všechny foťáky), vpravo krátký návod.</summary>
    void PaintQr(Graphics g, bool[,] m)
    {
        int n = m.GetLength(0), quiet = 3;
        float side = Math.Min(H - 28, W * 0.5f);
        // Moduly na celé fyzické pixely, ať je kód ostrý.
        int mod = Math.Max(1, (int)(side * K / (n + quiet * 2)));
        int box = mod * (n + quiet * 2);
        int x0 = (int)(18 * K), y0 = (int)((H * K - box) / 2);
        var state = g.Save();
        g.ResetTransform();
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using (var white = new SolidBrush(Color.White))
        using (var rounded = Theme.Rounded(new RectangleF(x0, y0, box, box), 10 * K))
            g.FillPath(white, rounded);
        g.SmoothingMode = SmoothingMode.None;
        using (var dark = new SolidBrush(Color.FromArgb(18, 22, 20)))
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                    if (m[y, x]) g.FillRectangle(dark, x0 + (x + quiet) * mod, y0 + (y + quiet) * mod, mod, mod);
        g.Restore(state);

        // Text vpravo: nadpis i řádky se zalamují (anglický nadpis je na úzkém okně delší než řádek)
        // a celý blok se svisle vycentruje podle skutečné výšky.
        float tx = (x0 + box) / K + 16, tw = W - tx - 10;
        using var big = Px(12f, true);
        using var small = Px(8.75f);
        var lines = new (string Text, Font Font, Color Color, float Gap)[]
        {
            (L.QrTitle, big, Theme.Fore, 8), (L.QrApp, small, Theme.Dim, 2), (L.QrCamera, small, Theme.Dim, 10), (L.QrPin, small, Theme.Accent, 0),
        };
        var flags = TextFormatFlags.WordBreak | TextFormatFlags.NoPadding;
        float Height(string t, Font f) => TextRenderer.MeasureText(t, f, new Size((int)(tw * K), int.MaxValue), flags).Height / K;
        float total = lines.Sum(l => Height(l.Text, l.Font) + l.Gap), ly = (H - total) / 2f;
        foreach (var (text, font, color, gap) in lines)
        {
            float h = Height(text, font);
            DrawText(g, text, font, new RectangleF(tx, ly, tw, h + 2), color, flags);
            ly += h + gap;
        }
    }
}

enum CardKind { Info, Busy, Ok, Error }

/// <summary>Stavová karta: ikona stavu (točící se kolečko / ✓ / ✗), nadpis, detail a tenký průběh.</summary>
sealed class StatusCard : PaintedControl
{
    readonly System.Windows.Forms.Timer spin = new() { Interval = 16 };
    float angle;
    string title = "", detail = "";
    double? progress;
    CardKind kind;
    int queued;

    public StatusCard()
    {
        spin.Tick += (_, _) => { angle = (angle + 7) % 360; Invalidate(); };
    }

    public CardKind Kind => kind;
    public string Title => title;

    public void Set(CardKind kind, string title, string detail, double? progress = null, int queued = 0)
    {
        (this.kind, this.title, this.detail, this.progress, this.queued) = (kind, title, detail, progress, queued);
        if (kind == CardKind.Busy) spin.Start(); else spin.Stop();
        Invalidate();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) spin.Dispose();
        base.Dispose(disposing);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = Begin(e);
        var r = new RectangleF(2, 0.5f, W - 5, H - (Theme.IsDark ? 1.5f : 6));
        Theme.CardShadow(g, r, 14);
        using (var path = Theme.Rounded(r, 14))
        using (var fill = new SolidBrush(Theme.Surface))
            g.FillPath(fill, path);

        // Ikona stavu.
        var color = kind switch { CardKind.Ok => Theme.Accent, CardKind.Error => Theme.Bad, CardKind.Busy => Theme.Accent, _ => Theme.Dim };
        float ix = 16, d = 26, iy = r.Y + r.Height / 2f - d / 2;
        using (var bg = new SolidBrush(Color.FromArgb(kind == CardKind.Info ? 40 : 38, color)))
            g.FillEllipse(bg, ix, iy, d, d);
        using (var pen = new Pen(color, 2.2f) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round })
        {
            float cx = ix + d / 2, cy = iy + d / 2;
            switch (kind)
            {
                case CardKind.Busy:
                    g.DrawArc(pen, ix + 6, iy + 6, d - 12, d - 12, angle, 250);
                    break;
                case CardKind.Ok:
                    g.DrawLines(pen, [new PointF(cx - 6, cy), new PointF(cx - 1.5f, cy + 4.5f), new PointF(cx + 6, cy - 4)]);
                    break;
                case CardKind.Error:
                    g.DrawLine(pen, cx - 4.5f, cy - 4.5f, cx + 4.5f, cy + 4.5f);
                    g.DrawLine(pen, cx + 4.5f, cy - 4.5f, cx - 4.5f, cy + 4.5f);
                    break;
                default:
                    g.DrawLine(pen, cx, cy - 1, cx, cy + 5);
                    using (var b = new SolidBrush(color)) g.FillEllipse(b, cx - 1.4f, cy - 6.5f, 2.8f, 2.8f);
                    break;
            }
        }

        float x = ix + d + 12;
        float right = r.Right - 12;
        using var tf = Px(9.75f, true);
        using var df = Px(8.75f);
        if (queued > 0)
        {
            var q = $"+{queued}";
            float qw = MeasureWidth(q, df);
            var pill = new RectangleF(right - qw - 8, r.Y + 11, qw + 8, 20);
            using (var pp = Theme.Rounded(pill, 10))
            using (var pb = new SolidBrush(Theme.Surface2))
                g.FillPath(pb, pp);
            DrawText(g, q, df, pill, Theme.Dim, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            right = pill.X - 6;
        }
        bool bar = progress.HasValue;
        float top = bar ? r.Y + 10 : r.Y + (r.Height - 40) / 2;
        DrawText(g, title, tf, new RectangleF(x, top - 1, right - x, 22), Theme.Fore, TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);
        DrawText(g, detail, df, new RectangleF(x, top + 20, r.Right - 12 - x, 20),
            kind == CardKind.Error ? Theme.Mix(Theme.Bad, Theme.Fore, 0.3f) : Theme.Dim, TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);

        if (bar)
        {
            var track = new RectangleF(x, r.Bottom - 14, r.Right - 12 - x, 5);
            using (var tp = Theme.Rounded(track, 2.5f))
            using (var tb = new SolidBrush(Theme.Surface2))
                g.FillPath(tb, tp);
            float w = (float)(track.Width * Math.Clamp(progress!.Value, 0, 1));
            if (w > 1)
            {
                using var fp = Theme.Rounded(new RectangleF(track.X, track.Y, Math.Max(w, 5), track.Height), 2.5f);
                using var fb = new SolidBrush(Theme.Accent);
                g.FillPath(fb, fp);
            }
        }
    }
}
