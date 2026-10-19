using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace PhoneDrop;

/// <summary>
/// Barvy a vlastní ovládací prvky. Světlá a tmavá paleta odpovídají Android appce;
/// "auto" se řídí nastavením Windows (Přizpůsobení → Barvy → Režim aplikací).
/// </summary>
static class Theme
{
    sealed record Palette(Color Back, Color Surface, Color Surface2, Color Line, Color Fore, Color Dim,
        Color Accent, Color OnAccent, Color Bad, Color Warn, Color Shadow);

    static readonly Palette Light = new(
        Back: Color.FromArgb(242, 245, 241), Surface: Color.White, Surface2: Color.FromArgb(234, 239, 234),
        Line: Color.FromArgb(214, 222, 215), Fore: Color.FromArgb(22, 33, 26), Dim: Color.FromArgb(102, 115, 106),
        Accent: Color.FromArgb(36, 166, 83), OnAccent: Color.White, Bad: Color.FromArgb(220, 74, 74),
        Warn: Color.FromArgb(217, 130, 43), Shadow: Color.FromArgb(31, 58, 42));

    static readonly Palette Dark = new(
        Back: Color.FromArgb(15, 19, 17), Surface: Color.FromArgb(26, 32, 28), Surface2: Color.FromArgb(38, 46, 41),
        Line: Color.FromArgb(52, 62, 56), Fore: Color.FromArgb(236, 242, 238), Dim: Color.FromArgb(154, 166, 158),
        Accent: Color.FromArgb(95, 214, 138), OnAccent: Color.FromArgb(6, 33, 15), Bad: Color.FromArgb(240, 123, 123),
        Warn: Color.FromArgb(242, 166, 94), Shadow: Color.Black);

    static Palette p = Dark;

    public static bool IsDark { get; private set; } = true;
    public static Color Back => p.Back;
    public static Color Surface => p.Surface;
    public static Color Surface2 => p.Surface2;
    public static Color Line => p.Line;
    public static Color Fore => p.Fore;
    public static Color Dim => p.Dim;
    public static Color Accent => p.Accent;
    public static Color OnAccent => p.OnAccent;
    public static Color Bad => p.Bad;
    public static Color Warn => p.Warn;

    /// <summary>"auto", "light" nebo "dark".</summary>
    public static void Init(string? setting)
    {
        IsDark = setting switch { "light" => false, "dark" => true, _ => !SystemUsesLight() };
        p = IsDark ? Dark : Light;
    }

    static bool SystemUsesLight()
    {
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("AppsUseLightTheme") is int v && v != 0;
        }
        catch (Exception) { return false; }
    }

    /// <summary>Měkký stín pod kartou (jen ve světlém vzhledu – v tmavém by nebyl vidět).</summary>
    public static void CardShadow(Graphics g, RectangleF r, float radius)
    {
        if (IsDark) return;
        for (int i = 4; i >= 1; i--)
        {
            using var path = Rounded(new RectangleF(r.X - i * 0.5f, r.Y + i * 0.9f, r.Width + i, r.Height + i * 0.6f), radius + i);
            using var b = new SolidBrush(Color.FromArgb(7, p.Shadow));
            g.FillPath(b, path);
        }
    }

    public static Font UiFont(float size, FontStyle style = FontStyle.Regular) => new("Segoe UI", size, style);
    public static Font Semibold(float size) => new("Segoe UI Semibold", size);

    /// <summary>Písmo v pixelech pro vlastní kreslení: velikost v bodech při 96 DPI × měřítko monitoru.</summary>
    public static Font PxFont(float pt, float k, bool semibold = false) =>
        new(semibold ? "Segoe UI Semibold" : "Segoe UI", pt * 96f / 72f * k, GraphicsUnit.Pixel);

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

    /// <summary>Titulek okna ve stejném vzhledu jako obsah (Windows 10 20H1+ / Windows 11), v barvě pozadí.</summary>
    public static void TitleBar(Form form)
    {
        int on = IsDark ? 1 : 0;
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
        if (hover) fill = Theme.Mix(fill, Primary || Theme.IsDark ? Color.White : Color.Black, Primary && !Theme.IsDark ? 0.12f : 0.07f);
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
        Controls.Add(Box);
        Box.GotFocus += (_, _) => { focused = true; Invalidate(); };
        Box.LostFocus += (_, _) => { focused = false; Invalidate(); };
        Cursor = Cursors.IBeam;
        Click += (_, _) => Box.Focus();
    }

    /// <summary>Výška a okraje podle písma a DPI (automatické škálování by je přepočítalo dvakrát).</summary>
    protected override void OnLayout(LayoutEventArgs e)
    {
        base.OnLayout(e);
        float k = DeviceDpi / 96f;
        int h = Box.PreferredHeight + (int)(18 * k);
        if (Height != h) Height = h;
        int pad = (int)(12 * k);
        Box.SetBounds(pad, (Height - Box.PreferredHeight) / 2, Math.Max(10, Width - 2 * pad), Box.PreferredHeight);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics.Smooth();
        g.Clear(Parent?.BackColor ?? Theme.Back);
        using var path = Theme.Rounded(new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f), 10 * DeviceDpi / 96f);
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
