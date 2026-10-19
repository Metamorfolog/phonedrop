using System.ComponentModel;
using System.Drawing.Drawing2D;

namespace PhoneDrop;

/// <summary>Kreslené prvky hlavního okna.</summary>
abstract class PaintedControl : Control
{
    protected PaintedControl()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
                 | ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
        BackColor = Theme.Back;
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
    bool connected;

    public void Set(string name, string detail, bool connected)
    {
        (this.name, this.detail, this.connected) = (name, detail, connected);
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics.Smooth();
        g.Clear(BackColor);
        int s = Height - 16;
        var tile = new RectangleF(14, 8, s, s);
        using (var path = Theme.Rounded(tile, 10))
        using (var fill = new SolidBrush(Theme.Surface))
            g.FillPath(fill, path);
        DrawPhone(g, new RectangleF(tile.X + s * 0.33f, tile.Y + s * 0.2f, s * 0.34f, s * 0.6f),
            connected ? Theme.Accent : Theme.Dim, 1.8f);

        float x = tile.Right + 12;
        using var title = Theme.Semibold(10.5f);
        using var small = Theme.UiFont(8.75f);
        TextRenderer.DrawText(g, name, title, new Rectangle((int)x, 8, Width - (int)x - 50, 26), Theme.Fore,
            TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);
        using (var dot = new SolidBrush(connected ? Theme.Accent : Theme.Dim))
            g.FillEllipse(dot, x + 1, 37, 7, 7);
        TextRenderer.DrawText(g, detail, small, new Rectangle((int)x + 13, 31, Width - (int)x - 60, 20), Theme.Dim,
            TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);
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
        var g = e.Graphics.Smooth();
        g.Clear(BackColor);
        if (hover)
        {
            using var b = new SolidBrush(Theme.Surface2);
            g.FillEllipse(b, 0, 0, Width - 1, Height - 1);
        }
        using var dot = new SolidBrush(Theme.Fore);
        for (int i = -1; i <= 1; i++) g.FillEllipse(dot, Width / 2f + i * 6 - 1.75f, Height / 2f - 1.75f, 3.5f, 3.5f);
    }
}

/// <summary>Drop zóna: karta s ilustrací šipky do telefonu; při přetahování zezelená a šipka „poskakuje“.</summary>
sealed class DropZone : PaintedControl
{
    readonly System.Windows.Forms.Timer anim = new() { Interval = 16 };
    float t, glow;
    bool hot, hover;

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
        var g = e.Graphics.Smooth();
        g.Clear(BackColor);
        var r = new RectangleF(1, 1, Width - 3, Height - 3);
        using var path = Theme.Rounded(r, 16);
        using (var fill = new SolidBrush(Theme.Mix(Theme.Surface, Theme.Accent, glow * 0.12f)))
            g.FillPath(fill, path);
        using (var pen = new Pen(Theme.Mix(Theme.Line, Theme.Accent, glow), 1.6f) { DashPattern = [5f, 4f] })
            g.DrawPath(pen, path);

        // Ilustrace: kruh, v něm šipka dolů nad „miskou“.
        float cx = Width / 2f, cy = Height / 2f - 34;
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

        using var big = Theme.Semibold(12.5f);
        using var small = Theme.UiFont(8.75f);
        int ty = (int)(cy + rad + 12);
        TextRenderer.DrawText(g, hot ? "Pusť a letí do telefonu" : "Přetáhni sem soubor nebo APK", big,
            new Rectangle(0, ty, Width, 26), hot ? Theme.Accent : Theme.Fore, TextFormatFlags.HorizontalCenter);
        TextRenderer.DrawText(g, "nebo klikni a vyber", small,
            new Rectangle(10, ty + 28, Width - 20, 18), Theme.Dim, TextFormatFlags.HorizontalCenter | TextFormatFlags.EndEllipsis);
        TextRenderer.DrawText(g, "APK se rovnou nabídne k instalaci", small,
            new Rectangle(10, ty + 46, Width - 20, 18), Theme.Dim, TextFormatFlags.HorizontalCenter | TextFormatFlags.EndEllipsis);
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
        var g = e.Graphics.Smooth();
        g.Clear(BackColor);
        var r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
        using (var path = Theme.Rounded(r, 14))
        using (var fill = new SolidBrush(Theme.Surface))
            g.FillPath(fill, path);

        // Ikona stavu.
        var color = kind switch { CardKind.Ok => Theme.Accent, CardKind.Error => Theme.Bad, CardKind.Busy => Theme.Accent, _ => Theme.Dim };
        float ix = 16, iy = Height / 2f - 13, d = 26;
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

        int x = (int)(ix + d + 12);
        int right = Width - 14;
        using var tf = Theme.Semibold(9.75f);
        using var df = Theme.UiFont(8.75f);
        if (queued > 0)
        {
            var q = $"+{queued}";
            var qs = TextRenderer.MeasureText(q, df);
            var pill = new RectangleF(right - qs.Width - 8, 12, qs.Width + 8, 20);
            using (var pp = Theme.Rounded(pill, 10))
            using (var pb = new SolidBrush(Theme.Surface2))
                g.FillPath(pb, pp);
            TextRenderer.DrawText(g, q, df, Rectangle.Round(pill), Theme.Dim, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            right = (int)pill.X - 6;
        }
        bool bar = progress.HasValue;
        int top = bar ? 11 : (Height - 40) / 2;
        TextRenderer.DrawText(g, title, tf, new Rectangle(x, top - 1, right - x, 22), Theme.Fore, TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);
        TextRenderer.DrawText(g, detail, df, new Rectangle(x, top + 20, Width - 14 - x, 20),
            kind == CardKind.Error ? Theme.Mix(Theme.Bad, Theme.Fore, 0.3f) : Theme.Dim, TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);

        if (bar)
        {
            var track = new RectangleF(x, Height - 15, Width - 14 - x, 5);
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
