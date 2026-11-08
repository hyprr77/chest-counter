// Chest Counter - small always-on-top overlay for counting Fortnite chests.
//   Default keys: F8 = +1, F7 = -1, F9 = reset to 0 (works while the game is focused).
//   Click the gear at the top (or right-click > Settings) to change keys, type in a number, or turn effects off.
//   Every time the count reaches a multiple of 100 there's a gold celebration.
//   Drag with the left mouse button to move it. Drag its right/bottom edge to resize it, or right-click > Size.
//   Everything is saved in %AppData%\ChestCounter (count.txt, settings.ini).
//   Use Fortnite in "Windowed Fullscreen" so the overlay can show on top of the game.
//
// What it does: checks whether your chosen keys are down (GetAsyncKeyState) and reads/writes two small
// text files next to the exe. No network code, nothing is read from or written into the game.
using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

[assembly: AssemblyTitle("Chest Counter")]
[assembly: AssemblyDescription("On-screen chest counter overlay")]
[assembly: AssemblyCompany("hypr.7")]
[assembly: AssemblyProduct("Chest Counter")]
[assembly: AssemblyCopyright("Copyright (c) hypr.7")]
[assembly: AssemblyVersion("1.0.0.0")]
[assembly: AssemblyFileVersion("1.0.0.0")]

static class Input
{
    [DllImport("user32.dll")] static extern short GetAsyncKeyState(int vKey);

    public static bool IsDown(int vk) { return GetAsyncKeyState(vk) < 0; }

    public static bool IsModifier(int vk)
    {
        return (vk >= 0x10 && vk <= 0x12) || (vk >= 0xA0 && vk <= 0xA5) || vk == 0x5B || vk == 0x5C;
    }

    public static string Describe(int vk)
    {
        if (vk == 0) return "Not set";
        switch ((Keys)vk)
        {
            case Keys.XButton1: return "Mouse back button";
            case Keys.XButton2: return "Mouse forward button";
            case Keys.MButton: return "Middle mouse button";
            case Keys.RButton: return "Right mouse button";
            case Keys.Prior: return "Page Up";
            case Keys.Next: return "Page Down";
            case Keys.Capital: return "Caps Lock";
            default:
                if (vk >= 0x30 && vk <= 0x39) return ((char)vk).ToString();
                if (vk >= 0x60 && vk <= 0x69) return "Numpad " + (vk - 0x60);
                return ((Keys)vk).ToString();
        }
    }
}

class ChestCounter : Form
{
    [DllImport("user32.dll")] static extern bool SetProcessDPIAware();
    [DllImport("user32.dll")] static extern bool ReleaseCapture();
    [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr hWnd, IntPtr after, int x, int y, int cx, int cy, uint flags);

    const uint SWP_NOSIZE = 0x1, SWP_NOMOVE = 0x2, SWP_NOACTIVATE = 0x10;
    static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);

    // Everything is drawn in a 320x130 design space and scaled to the window size
    const float BaseW = 320f, BaseH = 130f;
    const int MinWidth = 200, MaxWidth = 2000;
    static readonly RectangleF GearDesign = new RectangleF(BaseW - 112, 3, 104, 22);

    public static readonly string[] ActionNames = { "Add a chest", "Remove a chest", "Reset to 0" };
    public static readonly int[] DefaultBinds = { 0x77, 0x76, 0x78 };   // F8, F7, F9

    static readonly string Dir = AppDomain.CurrentDomain.BaseDirectory;
    static readonly string DataDir = GetDataDir();
    static readonly string CountFile = Path.Combine(DataDir, "count.txt");
    static readonly string SettingsFile = Path.Combine(DataDir, "settings.ini");

    // Saved data lives in %AppData%\ChestCounter so the exe can sit anywhere (Downloads, a zip, Program Files)
    static string GetDataDir()
    {
        string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ChestCounter");
        try
        {
            Directory.CreateDirectory(dir);
            // Bring over files from older versions that kept them next to the exe
            foreach (string name in new[] { "count.txt", "settings.ini" })
            {
                string old = Path.Combine(Dir, name), now = Path.Combine(dir, name);
                if (File.Exists(old) && !File.Exists(now)) File.Copy(old, now);
            }
        }
        catch (Exception) { }
        return dir;
    }

    public static readonly Color Gold = Color.FromArgb(246, 195, 67);
    const string Watermark = "@hypr.7";

    // Settings (saved in settings.ini)
    public int[] Binds = (int[])DefaultBinds.Clone();
    public bool NoPop;
    public bool NoCelebrate;
    int savedWidth = 320;
    Point? savedPos;

    // Celebration every time the count reaches a new multiple of 100
    class Spark { public float Vx, Vy, Rot, RotSpeed, Size; public Color Col; }
    const double CelebrateMs = 2400;
    readonly Random rng = new Random();
    Spark[] sparks;
    DateTime celebrateAt = DateTime.MinValue;
    int celebrateValue;

    readonly Image chest;
    readonly bool[] wasDown = new bool[256];
    readonly System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer();
    bool hoverGear;
    bool paused;
    int count;
    int topTicks;
    DateTime popAt = DateTime.MinValue;

    ChestCounter()
    {
        Text = "Chest Counter";
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = true;   // a normal taskbar button, so it can be pinned
        TopMost = true;
        DoubleBuffered = true;
        SetStyle(ControlStyles.ResizeRedraw, true);
        try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch (Exception) { }
        BackColor = Color.FromArgb(14, 10, 28);
        ClientSize = new Size(320, 130);
        StartPosition = FormStartPosition.CenterScreen;

        // A chest.png next to the exe replaces the built-in picture
        try
        {
            string file = Path.Combine(Dir, "chest.png");
            if (File.Exists(file)) chest = Image.FromFile(file);
            else
            {
                Stream res = typeof(ChestCounter).Assembly.GetManifestResourceStream("chest.png");
                if (res != null) chest = Image.FromStream(res);
            }
        }
        catch (Exception) { }

        int saved;
        if (File.Exists(CountFile) && int.TryParse(File.ReadAllText(CountFile).Trim(), out saved))
            count = Math.Max(0, saved);

        LoadSettings();
        ClientSize = new Size(savedWidth, (int)(savedWidth * BaseH / BaseW));
        if (savedPos.HasValue && OnScreen(new Rectangle(savedPos.Value, Size)))
        {
            StartPosition = FormStartPosition.Manual;
            Location = savedPos.Value;
        }

        var menu = new ContextMenuStrip();
        menu.Items.Add("Add a chest", null, delegate { Set(count + 1); });
        menu.Items.Add("Remove a chest", null, delegate { Set(count - 1); });
        menu.Items.Add("Reset to 0", null, delegate
        {
            if (MessageBox.Show("Reset the chest count to 0?", "Chest Counter", MessageBoxButtons.YesNo) == DialogResult.Yes)
                Set(0);
        });
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Settings...", null, delegate { OpenSettings(); });
        menu.Items.Add("Preview 100 celebration", null, delegate { StartCelebration(100); });
        var sizes = new ToolStripMenuItem("Size  (or drag the edge)");
        foreach (int w in new[] { 240, 320, 480, 640 })
        {
            int wide = w;
            string name = wide == 240 ? "Small" : wide == 320 ? "Medium" : wide == 480 ? "Large" : "Extra large";
            sizes.DropDownItems.Add(name, null, delegate { SetWidth(wide); });
        }
        menu.Items.Add(sizes);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Exit", null, delegate { Close(); });
        ContextMenuStrip = menu;

        timer.Interval = 15;
        timer.Tick += Tick;
        timer.Start();
    }

    // Clicking the overlay shouldn't pull focus away from the game
    protected override CreateParams CreateParams
    {
        get
        {
            CreateParams cp = base.CreateParams;
            cp.ExStyle |= 0x08000000;   // WS_EX_NOACTIVATE
            return cp;
        }
    }

    protected override bool ShowWithoutActivation { get { return true; } }

    static bool OnScreen(Rectangle r)
    {
        foreach (Screen s in Screen.AllScreens)
        {
            Rectangle seen = Rectangle.Intersect(s.Bounds, r);
            if (seen.Width >= 60 && seen.Height >= 40) return true;
        }
        return false;
    }

    // ---------- settings ----------

    void LoadSettings()
    {
        if (!File.Exists(SettingsFile)) return;
        int left = int.MinValue, top = int.MinValue;
        try
        {
            foreach (string line in File.ReadAllLines(SettingsFile))
            {
                int eq = line.IndexOf('=');
                if (eq < 0) continue;
                string name = line.Substring(0, eq).Trim();
                string value = line.Substring(eq + 1).Trim();
                int n;
                bool isNumber = int.TryParse(value, out n);
                switch (name)
                {
                    case "Add": if (isNumber && n >= 0 && n < 256) Binds[0] = n; break;
                    case "Subtract": if (isNumber && n >= 0 && n < 256) Binds[1] = n; break;
                    case "Reset": if (isNumber && n >= 0 && n < 256) Binds[2] = n; break;
                    case "NoPop": NoPop = value == "1"; break;
                    case "NoCelebrate": NoCelebrate = value == "1"; break;
                    case "Width": if (isNumber && n >= MinWidth && n <= MaxWidth) savedWidth = n; break;
                    case "Left": if (isNumber) left = n; break;
                    case "Top": if (isNumber) top = n; break;
                }
            }
        }
        catch (Exception) { }
        if (left != int.MinValue && top != int.MinValue) savedPos = new Point(left, top);
    }

    public void SaveSettings()
    {
        try
        {
            File.WriteAllLines(SettingsFile, new[]
            {
                "Add=" + Binds[0],
                "Subtract=" + Binds[1],
                "Reset=" + Binds[2],
                "NoPop=" + (NoPop ? 1 : 0),
                "NoCelebrate=" + (NoCelebrate ? 1 : 0),
                "Width=" + ClientSize.Width,
                "Left=" + Location.X,
                "Top=" + Location.Y,
            });
        }
        catch (Exception) { }
    }

    void OpenSettings()
    {
        paused = true;
        using (var w = new SettingsWindow(this)) w.ShowDialog(this);
        paused = false;
        SaveSettings();
        Invalidate();
    }

    // ---------- counting ----------

    public int Count { get { return count; } }

    // Typed-in number from Settings: no pop and no celebration
    public void SetNumber(int n)
    {
        count = Math.Max(0, n);
        try { File.WriteAllText(CountFile, count.ToString()); } catch (Exception) { }
        Invalidate();
    }

    void Set(int n)
    {
        n = Math.Max(0, n);
        if (n > count)
        {
            popAt = DateTime.Now;
            // Reaching 100, 200, 300... (or jumping past one) sets off the celebration
            if (!NoCelebrate && n / 100 > count / 100) StartCelebration(n / 100 * 100);
        }
        count = n;
        try { File.WriteAllText(CountFile, count.ToString()); } catch (Exception) { }
        Invalidate();
    }

    void Tick(object sender, EventArgs e)
    {
        // Fire once per press (on the down edge), not while the key is held
        for (int i = 0; i < Binds.Length; i++)
        {
            int vk = Binds[i];
            if (vk == 0) continue;
            bool down = Input.IsDown(vk);
            if (down && !wasDown[vk] && !paused)
            {
                if (i == 0) Set(count + 1);
                else if (i == 1) Set(count - 1);
                else Set(0);
            }
            wasDown[vk] = down;
        }

        if ((!NoPop && (DateTime.Now - popAt).TotalMilliseconds < 500) ||
            (DateTime.Now - celebrateAt).TotalMilliseconds < CelebrateMs) Invalidate();

        // A game going fullscreen can push in front; step back on top every couple of seconds
        if (++topTicks >= 130)
        {
            topTicks = 0;
            if (!paused)
                SetWindowPos(Handle, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
        }
    }

    // ---------- celebration ----------

    float CenterX { get { return chest != null ? 216f : BaseW / 2f; } }
    const float CenterY = 72f;

    void StartCelebration(int value)
    {
        celebrateValue = value;
        celebrateAt = DateTime.Now;
        Color[] palette = { Gold, Color.White, Color.FromArgb(255, 105, 180), Color.FromArgb(80, 220, 255), Color.FromArgb(255, 140, 40) };
        sparks = new Spark[70];
        for (int i = 0; i < sparks.Length; i++)
        {
            double a = rng.NextDouble() * Math.PI * 2;
            double speed = 110 + rng.NextDouble() * 230;
            sparks[i] = new Spark
            {
                Vx = (float)(Math.Cos(a) * speed),
                Vy = (float)(Math.Sin(a) * speed) - 90f,
                Rot = (float)(rng.NextDouble() * 360),
                RotSpeed = (float)(rng.NextDouble() * 720 - 360),
                Size = 4f + (float)rng.NextDouble() * 5f,
                Col = palette[rng.Next(palette.Length)],
            };
        }
        Invalidate();
    }

    // Glow and shock rings, drawn behind the number
    void DrawCelebrationBack(Graphics g, double ms)
    {
        if (ms >= CelebrateMs) return;
        float cx = CenterX, cy = CenterY;

        float glow = (float)Math.Max(0, 1 - ms / 1700);
        if (glow > 0)
        {
            using (var path = new GraphicsPath())
            {
                path.AddEllipse(cx - 150, cy - 62, 300, 124);
                using (var br = new PathGradientBrush(path))
                {
                    br.CenterColor = Color.FromArgb((int)(170 * glow), 255, 200, 80);
                    br.SurroundColors = new[] { Color.FromArgb(0, 255, 200, 80) };
                    g.FillPath(br, path);
                }
            }
        }

        for (int k = 0; k < 2; k++)
        {
            float rp = (float)((ms - k * 260) / 950);
            if (rp <= 0 || rp >= 1) continue;
            float r = 20 + rp * 210;
            using (var pen = new Pen(Color.FromArgb((int)(230 * (1 - rp)), 255, 215, 90), 1.5f + 4f * (1 - rp)))
                g.DrawEllipse(pen, cx - r, cy - r, r * 2, r * 2);
        }
    }

    // Confetti, drawn on top of everything
    void DrawCelebrationFront(Graphics g, double ms)
    {
        if (ms >= CelebrateMs || sparks == null) return;
        float cx = CenterX, cy = CenterY;
        float ts = (float)(ms / 1000);
        float p = (float)(ms / CelebrateMs);
        int alpha = p < 0.6f ? 255 : (int)(255 * (1 - (p - 0.6f) / 0.4f));
        foreach (Spark s in sparks)
        {
            float x = cx + s.Vx * ts;
            float y = cy + s.Vy * ts + 190f * ts * ts;   // gravity pulls them down
            GraphicsState state = g.Save();
            g.TranslateTransform(x, y);
            g.RotateTransform(s.Rot + s.RotSpeed * ts);
            using (var b = new SolidBrush(Color.FromArgb(Math.Max(0, alpha), s.Col)))
                g.FillRectangle(b, -s.Size / 2, -s.Size * 0.3f, s.Size, s.Size * 0.6f);
            g.Restore(state);
        }
    }

    // ---------- window behaviour ----------

    float Scale { get { return Math.Min(ClientSize.Width / BaseW, ClientSize.Height / BaseH); } }

    bool GearHit(Point p)
    {
        float k = Scale;
        return GearDesign.Contains(p.X / k, p.Y / k);
    }

    void SetWidth(int w)
    {
        ClientSize = new Size(w, (int)(w * BaseH / BaseW));
        SaveSettings();
    }

    [StructLayout(LayoutKind.Sequential)] struct RECT { public int Left, Top, Right, Bottom; }
    const int WM_NCHITTEST = 0x84, WM_SIZING = 0x214, WMSZ_BOTTOM = 6;
    const int HTCLIENT = 1, HTRIGHT = 11, HTBOTTOM = 15, HTBOTTOMRIGHT = 17;

    // Drag the right edge, bottom edge or bottom-right corner to resize; the shape stays the same
    protected override void WndProc(ref Message m)
    {
        if (m.Msg == WM_NCHITTEST)
        {
            base.WndProc(ref m);
            if ((int)m.Result != HTCLIENT) return;
            int lp = unchecked((int)(long)m.LParam);
            Point p = PointToClient(new Point((short)(lp & 0xFFFF), (short)((lp >> 16) & 0xFFFF)));
            int grip = Math.Max(10, ClientSize.Width / 40);
            if (p.X >= ClientSize.Width - grip * 2 && p.Y >= ClientSize.Height - grip * 2) m.Result = (IntPtr)HTBOTTOMRIGHT;
            else if (p.X >= ClientSize.Width - grip) m.Result = (IntPtr)HTRIGHT;
            else if (p.Y >= ClientSize.Height - grip) m.Result = (IntPtr)HTBOTTOM;
            return;
        }
        if (m.Msg == WM_SIZING)
        {
            var r = (RECT)Marshal.PtrToStructure(m.LParam, typeof(RECT));
            int w = m.WParam.ToInt32() == WMSZ_BOTTOM
                ? (int)Math.Round((r.Bottom - r.Top) * BaseW / BaseH)
                : r.Right - r.Left;
            w = Math.Max(MinWidth, Math.Min(MaxWidth, w));
            r.Right = r.Left + w;
            r.Bottom = r.Top + (int)(w * BaseH / BaseW);
            Marshal.StructureToPtr(r, m.LParam, false);
            m.Result = (IntPtr)1;
            return;
        }
        base.WndProc(ref m);
    }

    // Fires when a resize drag finishes
    protected override void OnResizeEnd(EventArgs e)
    {
        base.OnResizeEnd(e);
        SaveSettings();
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        SaveSettings();
        base.OnFormClosing(e);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button != MouseButtons.Left) return;
        if (GearHit(e.Location)) { OpenSettings(); return; }
        ReleaseCapture();
        SendMessage(Handle, 0xA1, (IntPtr)2, IntPtr.Zero);   // drag like a title bar
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        bool over = GearHit(e.Location);
        if (over != hoverGear)
        {
            hoverGear = over;
            Cursor = over ? Cursors.Hand : Cursors.Default;
            Invalidate();
        }
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        if (hoverGear) { hoverGear = false; Cursor = Cursors.Default; Invalidate(); }
    }

    // ---------- painting ----------

    protected override void OnPaint(PaintEventArgs e)
    {
        Graphics g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAlias;

        double t = NoPop ? 99999 : (DateTime.Now - popAt).TotalMilliseconds;
        double cms = (DateTime.Now - celebrateAt).TotalMilliseconds;
        bool celebrating = cms < CelebrateMs;

        g.Clear(Color.FromArgb(13, 9, 26));
        float winScale = Scale;
        g.ScaleTransform(winScale, winScale);

        // Flat dark card
        using (var bg = new LinearGradientBrush(new RectangleF(0, 0, BaseW, BaseH), Color.FromArgb(30, 22, 54), Color.FromArgb(13, 9, 26), 90f))
            g.FillRectangle(bg, 0, 0, BaseW + 1, BaseH + 1);

        // Top bar: small title on the left, gear on the right
        string title = celebrating ? celebrateValue.ToString("N0") + " CHESTS!" : "CHESTS";
        Color titleColor = celebrating && ((int)(cms / 140) % 2 == 0) ? Color.White : Gold;
        using (var tf = new Font("Segoe UI", celebrating ? 10.5f : 9f, FontStyle.Bold))
        using (var tb = new SolidBrush(titleColor))
            g.DrawString(title, tf, tb, 14, celebrating ? 5 : 6);

        using (var line = new Pen(Color.FromArgb(40, 255, 255, 255), 1f))
            g.DrawLine(line, 12, 29, BaseW - 12, 29);

        using (var gf = new Font("Segoe UI Symbol", hoverGear ? 9.5f : 12f, FontStyle.Regular))
        using (var gb = new SolidBrush(hoverGear ? Color.White : Color.FromArgb(150, 255, 255, 255)))
        {
            var gfmt = new StringFormat { Alignment = StringAlignment.Far, LineAlignment = StringAlignment.Center };
            g.DrawString(hoverGear ? "\u2699  Settings" : "\u2699", gf, gb, GearDesign, gfmt);
        }

        // Chest with a soft glow behind it, and a small bounce when the count goes up
        if (chest != null)
        {
            float flash = t < 600 ? (float)(t < 120 ? t / 120 : 1 - (t - 120) / 480) : 0f;
            float gr = 58 + 14 * flash;
            using (var path = new GraphicsPath())
            {
                path.AddEllipse(64 - gr, 74 - gr, gr * 2, gr * 2);
                using (var glow = new PathGradientBrush(path))
                {
                    glow.CenterColor = Color.FromArgb((int)(38 + 130 * flash), 255, 200, 80);
                    glow.SurroundColors = new[] { Color.FromArgb(0, 255, 200, 80) };
                    g.FillPath(glow, path);
                }
            }

            float k = Math.Min(100f / chest.Width, 80f / chest.Height);
            float w = chest.Width * k, h = chest.Height * k;
            float scale = 1, angle = 0;
            if (t < 350)
            {
                float p = (float)(t / 350);
                float wave = (float)Math.Sin(Math.PI * 2 * p) * (1 - p);
                scale = 1 + 0.2f * wave;
                angle = -8 * wave;
            }
            GraphicsState state = g.Save();
            g.TranslateTransform(64, 74);
            g.RotateTransform(angle);
            g.ScaleTransform(scale, scale);
            g.DrawImage(chest, -w / 2, -h / 2, w, h);
            g.Restore(state);
        }

        DrawCelebrationBack(g, cms);

        // The number, shrunk if it gets too wide
        float bump = t < 250 ? 1f + 0.12f * (float)Math.Sin(Math.PI * t / 250) : 1f;
        if (celebrating) bump = Math.Max(bump, 1f + 0.3f * (float)Math.Sin(Math.PI * Math.Min(cms, 800) / 800));
        var area = chest != null ? new RectangleF(124, 34, BaseW - 124 - 12, 80) : new RectangleF(12, 34, BaseW - 24, 80);
        string text = count.ToString("N0");
        float size = (chest != null ? 42f : 46f) * bump;
        while (size > 12f)
        {
            using (var f = new Font("Segoe UI", size, FontStyle.Bold))
                if (g.MeasureString(text, f).Width <= area.Width) break;
            size -= 2f;
        }
        using (var f = new Font("Segoe UI", size, FontStyle.Bold))
        using (var shadow = new SolidBrush(Color.FromArgb(90, 0, 0, 0)))
        using (var numBrush = new SolidBrush(celebrating && cms < 1800 ? Gold : Color.White))
        {
            var fmt = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
            var below = new RectangleF(area.X, area.Y + 2, area.Width, area.Height);
            g.DrawString(text, f, shadow, below, fmt);
            g.DrawString(text, f, numBrush, area, fmt);
        }

        // Watermark, bottom-right
        using (var wf = new Font("Segoe UI", 8.5f, FontStyle.Bold))
        using (var wb = new SolidBrush(Color.FromArgb(150, 255, 255, 255)))
        {
            var wfmt = new StringFormat { Alignment = StringAlignment.Far, LineAlignment = StringAlignment.Far };
            g.DrawString(Watermark, wf, wb, new RectangleF(0, 0, BaseW - 14, BaseH - 8), wfmt);
        }

        // Thin gold frame
        Color frameColor = celebrating && cms < 900 && ((int)(cms / 90) % 2 == 0) ? Color.White : Color.FromArgb(215, Gold);
        using (var frame = new Pen(frameColor, 2f))
            g.DrawRectangle(frame, 1f, 1f, BaseW - 2, BaseH - 2);

        DrawCelebrationFront(g, cms);
    }

    [STAThread]
    static void Main()
    {
        bool first;
        using (new Mutex(true, "ChestCounterOverlay.SingleInstance", out first))
        {
            if (!first) return;
            try { SetProcessDPIAware(); } catch (Exception) { }   // crisp on high-DPI screens
            Application.EnableVisualStyles();
            Application.Run(new ChestCounter());
        }
    }
}

// A small on/off switch
class Toggle : Control
{
    bool on;
    public event EventHandler CheckedChanged;

    public bool Checked
    {
        get { return on; }
        set
        {
            if (on == value) return;
            on = value;
            Invalidate();
            if (CheckedChanged != null) CheckedChanged(this, EventArgs.Empty);
        }
    }

    public Toggle()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        Size = new Size(44, 24);
        Cursor = Cursors.Hand;
    }

    protected override void OnClick(EventArgs e)
    {
        base.OnClick(e);
        Checked = !Checked;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        Graphics g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(Parent != null ? Parent.BackColor : BackColor);
        var rect = new RectangleF(0.5f, 0.5f, Width - 1, Height - 1);
        using (var path = new GraphicsPath())
        {
            float r = rect.Height / 2;
            path.AddArc(rect.X, rect.Y, rect.Height, rect.Height, 90, 180);
            path.AddArc(rect.Right - rect.Height, rect.Y, rect.Height, rect.Height, 270, 180);
            path.CloseFigure();
            using (var fill = new SolidBrush(on ? ChestCounter.Gold : Color.FromArgb(58, 50, 90)))
                g.FillPath(fill, path);
        }
        float d = Height - 7;
        float x = on ? Width - d - 4 : 4;
        using (var knob = new SolidBrush(on ? Color.FromArgb(30, 20, 5) : Color.FromArgb(200, 195, 220)))
            g.FillEllipse(knob, x, 3.5f, d, d);
    }
}

// Settings window: dark, one page - keys, set the count, effects
class SettingsWindow : Form
{
    static readonly Color Bg = Color.FromArgb(22, 17, 40);
    static readonly Color Panel = Color.FromArgb(36, 29, 62);
    static readonly Color Line = Color.FromArgb(62, 53, 96);
    static readonly Color Text1 = Color.FromArgb(238, 233, 248);
    static readonly Color Muted = Color.FromArgb(152, 142, 178);
    static readonly Color Gold = ChestCounter.Gold;

    readonly ChestCounter app;
    readonly Label[] pills = new Label[3];
    readonly Button[] changeButtons = new Button[3];
    readonly bool[] heldAtStart = new bool[256];
    readonly System.Windows.Forms.Timer poll = new System.Windows.Forms.Timer();
    int capturing = -1;
    int lastCaptured;

    public SettingsWindow(ChestCounter app)
    {
        this.app = app;
        AutoScaleDimensions = new SizeF(96F, 96F);
        AutoScaleMode = AutoScaleMode.Dpi;
        Text = "Chest Counter - Settings";
        Font = new Font("Segoe UI", 10f);
        BackColor = Bg;
        ForeColor = Text1;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(420, 520);

        AddLabel("Settings", 20, 14, 300, 17f, Text1, true);

        // Keys
        AddSection("KEYS", 62);
        for (int i = 0; i < 3; i++)
        {
            int index = i;
            int y = 92 + i * 40;
            AddLabel(ChestCounter.ActionNames[i], 20, y + 4, 140, 10f, Text1, false);

            var pill = new Label
            {
                Location = new Point(166, y),
                Size = new Size(170, 30),
                BackColor = Panel,
                ForeColor = Text1,
                TextAlign = ContentAlignment.MiddleCenter,
            };
            pills[i] = pill;
            Controls.Add(pill);

            var change = MakeButton("Change", 346, y, 58, 30, false);
            change.Click += delegate { StartCapture(index); };
            changeButtons[i] = change;
            Controls.Add(change);
        }

        // Set the count
        AddSection("COUNT", 226);
        var box = new TextBox
        {
            Location = new Point(20, 256),
            Width = 316,
            Font = new Font("Segoe UI", 11f),
            BorderStyle = BorderStyle.FixedSingle,
            BackColor = Panel,
            ForeColor = Text1,
            Text = app.Count.ToString(),
        };
        box.TextChanged += delegate { box.BackColor = Panel; };
        Controls.Add(box);
        var set = MakeButton("Set", 346, 255, 58, 30, true);
        set.Click += delegate
        {
            int n;
            if (TryParseCount(box.Text, out n))
            {
                app.SetNumber(n);
                box.Text = n.ToString();
                box.SelectAll();
            }
            else box.BackColor = Color.FromArgb(96, 30, 52);
        };
        box.KeyDown += delegate(object s, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter) { set.PerformClick(); e.SuppressKeyPress = true; }
        };
        Controls.Add(set);
        AddLabel("Type a number like 1000 or 2.5k, then Set (or press Enter).", 20, 292, 380, 8.5f, Muted, false);

        // Effects
        AddSection("EFFECTS", 332);
        AddToggleRow("Pop animation", "The chest bounces and the number pulses on every chest.", 362, !app.NoPop,
            delegate(bool v) { app.NoPop = !v; });
        AddToggleRow("100-chest celebration", "Gold burst each time you reach 100, 200, 300...", 416, !app.NoCelebrate,
            delegate(bool v) { app.NoCelebrate = !v; });

        // Footer
        var defaults = MakeButton("Restore default keys", 20, 474, 160, 32, false);
        defaults.Click += delegate
        {
            capturing = -1;
            for (int i = 0; i < 3; i++) app.Binds[i] = ChestCounter.DefaultBinds[i];
            ShowKeys();
            app.SaveSettings();
        };
        Controls.Add(defaults);
        var done = MakeButton("Done", 316, 474, 88, 32, true);
        done.Click += delegate { Close(); };
        Controls.Add(done);

        ShowKeys();
        poll.Interval = 15;
        poll.Tick += Poll;
        poll.Start();
    }

    // ---------- small building blocks ----------

    Label AddLabel(string text, int x, int y, int w, float size, Color color, bool bold)
    {
        var l = new Label
        {
            Text = text,
            Location = new Point(x, y),
            Size = new Size(w, (int)(size * 2.1f) + 6),
            Font = new Font("Segoe UI", size, bold ? FontStyle.Bold : FontStyle.Regular),
            ForeColor = color,
            BackColor = Color.Transparent,
        };
        Controls.Add(l);
        return l;
    }

    // Small gold heading with a thin line after it
    void AddSection(string text, int y)
    {
        var l = new Label
        {
            Text = text,
            Location = new Point(20, y),
            AutoSize = true,
            Font = new Font("Segoe UI", 8.5f, FontStyle.Bold),
            ForeColor = Gold,
            BackColor = Color.Transparent,
        };
        Controls.Add(l);
        int w = TextRenderer.MeasureText(text, l.Font).Width;
        Controls.Add(new Panel { Location = new Point(20 + w + 8, y + 9), Size = new Size(380 - w - 8, 1), BackColor = Line });
    }

    void AddToggleRow(string title, string detail, int y, bool on, Action<bool> apply)
    {
        AddLabel(title, 20, y, 320, 10f, Text1, false);
        AddLabel(detail, 20, y + 22, 330, 8.5f, Muted, false);
        var toggle = new Toggle { Location = new Point(360, y + 2), Checked = on };
        toggle.CheckedChanged += delegate
        {
            apply(toggle.Checked);
            app.SaveSettings();
        };
        Controls.Add(toggle);
    }

    static Button MakeButton(string text, int x, int y, int w, int h, bool primary)
    {
        var b = new Button
        {
            Text = text,
            Location = new Point(x, y),
            Size = new Size(w, h),
            FlatStyle = FlatStyle.Flat,
            Cursor = Cursors.Hand,
            UseVisualStyleBackColor = false,
            Font = new Font("Segoe UI", 9.5f, primary ? FontStyle.Bold : FontStyle.Regular),
        };
        b.FlatAppearance.BorderSize = primary ? 0 : 1;
        if (primary)
        {
            b.BackColor = Gold;
            b.ForeColor = Color.FromArgb(30, 20, 5);
            b.FlatAppearance.MouseOverBackColor = Color.FromArgb(255, 214, 110);
            b.FlatAppearance.MouseDownBackColor = Color.FromArgb(220, 170, 50);
        }
        else
        {
            b.BackColor = Panel;
            b.ForeColor = Text1;
            b.FlatAppearance.BorderColor = Line;
            b.FlatAppearance.MouseOverBackColor = Color.FromArgb(52, 43, 88);
            b.FlatAppearance.MouseDownBackColor = Color.FromArgb(70, 58, 112);
        }
        return b;
    }

    // Accepts whole numbers with optional commas/spaces and a "k" for thousands ("2.5k" = 2500)
    static bool TryParseCount(string text, out int n)
    {
        n = 0;
        string t = text.Replace(",", "").Replace(" ", "").Trim().ToLowerInvariant();
        decimal multiplier = 1;
        if (t.EndsWith("k"))
        {
            multiplier = 1000;
            t = t.Substring(0, t.Length - 1);
        }
        decimal v;
        if (t.Length == 0 || !decimal.TryParse(t, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out v)) return false;
        v *= multiplier;
        if (v != decimal.Truncate(v) || v > 999999999) return false;
        n = (int)v;
        return true;
    }

    // ---------- key capture ----------

    void ShowKeys()
    {
        for (int i = 0; i < 3; i++)
        {
            pills[i].Text = Input.Describe(app.Binds[i]);
            pills[i].ForeColor = app.Binds[i] == 0 ? Muted : Text1;
            pills[i].BackColor = Panel;
            changeButtons[i].Text = "Change";
        }
    }

    void StartCapture(int i)
    {
        capturing = -1;
        ShowKeys();
        capturing = i;
        for (int vk = 1; vk < 256; vk++) heldAtStart[vk] = Input.IsDown(vk);
        pills[i].Text = "Press a key...";
        pills[i].ForeColor = Gold;
        changeButtons[i].Text = "...";
    }

    void Assign(int i, int vk)
    {
        // One key does one thing: take it off any other action first
        for (int j = 0; j < 3; j++)
            if (j != i && app.Binds[j] == vk) app.Binds[j] = 0;
        app.Binds[i] = vk;
        capturing = -1;
        ShowKeys();
        app.SaveSettings();
    }

    void Poll(object sender, EventArgs e)
    {
        if (capturing < 0) return;
        for (int vk = 2; vk < 256; vk++)   // 1 = left mouse button, needed to click this window
        {
            if (Input.IsModifier(vk)) continue;
            bool down = Input.IsDown(vk);
            if (!down) { heldAtStart[vk] = false; continue; }
            if (heldAtStart[vk]) continue;
            if (vk == 0x1B) { capturing = -1; ShowKeys(); return; }
            lastCaptured = vk;
            Assign(capturing, vk);
            return;
        }
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        // The key being captured mustn't also press a button (e.g. Space or Enter on "Change")
        if (capturing >= 0 || (lastCaptured != 0 && Input.IsDown(lastCaptured))) return true;
        return base.ProcessCmdKey(ref msg, keyData);
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        poll.Stop();
        base.OnFormClosed(e);
    }
}
