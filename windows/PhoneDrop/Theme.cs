using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace PhoneDrop;

/// <summary>Barvy a vlastní ovládací prvky tmavého vzhledu (stejné odstíny jako Android appka).</summary>
static class Theme
{
    public static readonly Color Back = Color.FromArgb(18, 18, 18);
    public static readonly Color Surface = Color.FromArgb(30, 30, 30);
    public static readonly Color Surface2 = Color.FromArgb(42, 42, 42);
    public static readonly Color Line = Color.FromArgb(58, 58, 58);
    public static readonly Color Fore = Color.FromArgb(232, 232, 232);
    public static readonly Color Dim = Color.FromArgb(150, 150, 150);
    public static readonly Color Accent = Color.FromArgb(127, 209, 139);
    public static readonly Color OnAccent = Color.FromArgb(12, 30, 16);
    public static readonly Color Bad = Color.FromArgb(240, 113, 113);
    public static readonly Color Warn = Color.FromArgb(240, 163, 94);

    public static Font UiFont(float size, FontStyle style = FontStyle.Regular) => new("Segoe UI", size, style);
    public static Font Semibold(float size) => new("Segoe UI Semibold", size);

    public static GraphicsPath Rounded(RectangleF r, float radius)
    {
        var p = new GraphicsPath();
        float d = Math.Min(radius * 2, Math.Min(r.Width, r.Height));
        p.AddArc(r.X, r.Y, d, d, 180, 90);
        p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        p.CloseFigure();
        return p;
    }

    public static Color Mix(Color a, Color b, float t) => Color.FromArgb(
        (int)(a.R + (b.R - a.R) * t), (int)(a.G + (b.G - a.G) * t), (int)(a.B + (b.B - a.B) * t));

    /// <summary>Tmavý titulek okna (Windows 10 20H1+ / Windows 11) v barvě pozadí.</summary>
    public static void DarkTitleBar(Form form)
    {
        int on = 1;
        DwmSetWindowAttribute(form.Handle, 20, ref on, 4); // DWMWA_USE_IMMERSIVE_DARK_MODE
        int color = Back.R | Back.G << 8 | Back.B << 16;
        DwmSetWindowAttribute(form.Handle, 35, ref color, 4); // DWMWA_CAPTION_COLOR (Win11)
        DwmSetWindowAttribute(form.Handle, 34, ref color, 4); // DWMWA_BORDER_COLOR (Win11)
    }

    [DllImport("dwmapi.dll")]
    static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

    /// <summary>Kreslení s vyhlazením a čitelným textem.</summary>
    public static Graphics Smooth(this Graphics g)
    {
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
        return g;
    }
}

/// <summary>Zaoblené tlačítko – primární (zelené) nebo vedlejší (šedé).</summary>
sealed class RoundButton : Button
{
    bool hover, down;
    public bool Primary { get; init; }

    public RoundButton()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
        FlatStyle = FlatStyle.Flat;
        FlatAppearance.BorderSize = 0;
        Cursor = Cursors.Hand;
        Font = Theme.Semibold(9.5f);
        Height = 34;
    }

    protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { hover = down = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnMouseDown(MouseEventArgs e) { down = true; Invalidate(); base.OnMouseDown(e); }
    protected override void OnMouseUp(MouseEventArgs e) { down = false; Invalidate(); base.OnMouseUp(e); }
    protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics.Smooth();
        g.Clear(Parent?.BackColor ?? Theme.Back);
        var fill = Primary ? Theme.Accent : Theme.Surface2;
        if (hover) fill = Theme.Mix(fill, Color.White, 0.10f);
        if (down) fill = Theme.Mix(fill, Color.Black, 0.15f);
        if (!Enabled) fill = Theme.Mix(fill, Theme.Back, 0.55f);
        using var path = Theme.Rounded(new RectangleF(0.5f, 0.5f, Width - 1, Height - 1), Height / 2f);
        using var brush = new SolidBrush(fill);
        g.FillPath(brush, path);
        var text = Primary ? Theme.OnAccent : Theme.Fore;
        if (!Enabled) text = Theme.Dim;
        TextRenderer.DrawText(g, Text, Font, ClientRectangle, text,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
    }
}

/// <summary>Zaoblené pole pro text (TextBox bez rámečku uvnitř karty).</summary>
sealed class InputBox : Panel
{
    public TextBox Box { get; } = new() { BorderStyle = BorderStyle.None, BackColor = Theme.Surface2, ForeColor = Theme.Fore };
    bool focused;

    public InputBox(float fontSize = 10.5f)
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        Box.Font = Theme.UiFont(fontSize);
        Height = Box.PreferredHeight + 18;
        Padding = new Padding(12, 9, 12, 0);
        Box.Dock = DockStyle.Fill;
        Controls.Add(Box);
        Box.GotFocus += (_, _) => { focused = true; Invalidate(); };
        Box.LostFocus += (_, _) => { focused = false; Invalidate(); };
        Cursor = Cursors.IBeam;
        Click += (_, _) => Box.Focus();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics.Smooth();
        g.Clear(Parent?.BackColor ?? Theme.Back);
        using var path = Theme.Rounded(new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f), 10);
        using var fill = new SolidBrush(Theme.Surface2);
        g.FillPath(fill, path);
        using var pen = new Pen(focused ? Theme.Accent : Theme.Line, focused ? 1.5f : 1f);
        g.DrawPath(pen, path);
    }
}

/// <summary>Tmavé kontextové menu (⋯ a ikona v oznamovací oblasti).</summary>
sealed class DarkMenuRenderer() : ToolStripProfessionalRenderer(new Colors())
{
    protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
    {
        e.TextColor = e.Item.Enabled ? Theme.Fore : Theme.Dim;
        base.OnRenderItemText(e);
    }

    protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e)
    {
        var g = e.Graphics.Smooth();
        var r = e.ImageRectangle;
        using var pen = new Pen(Theme.Accent, 2f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        g.DrawLines(pen, [
            new PointF(r.X + r.Width * 0.2f, r.Y + r.Height * 0.55f),
            new PointF(r.X + r.Width * 0.42f, r.Y + r.Height * 0.75f),
            new PointF(r.X + r.Width * 0.82f, r.Y + r.Height * 0.3f),
        ]);
    }

    sealed class Colors : ProfessionalColorTable
    {
        public override Color ToolStripDropDownBackground => Theme.Surface;
        public override Color ImageMarginGradientBegin => Theme.Surface;
        public override Color ImageMarginGradientMiddle => Theme.Surface;
        public override Color ImageMarginGradientEnd => Theme.Surface;
        public override Color MenuBorder => Theme.Line;
        public override Color MenuItemBorder => Theme.Surface2;
        public override Color MenuItemSelected => Theme.Surface2;
        public override Color SeparatorDark => Theme.Line;
        public override Color SeparatorLight => Theme.Surface;
        public override Color CheckBackground => Theme.Surface;
        public override Color CheckSelectedBackground => Theme.Surface2;
        public override Color CheckPressedBackground => Theme.Surface2;
    }
}
