using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;

[assembly: System.Reflection.AssemblyTitle("保持亮屏")]
[assembly: System.Reflection.AssemblyProduct("KeepAwake")]
[assembly: System.Reflection.AssemblyDescription("保持屏幕常亮，支持限时、进程联动、定时时段与低电量保护")]
[assembly: System.Reflection.AssemblyVersion("1.2.0.0")]
[assembly: System.Reflection.AssemblyFileVersion("1.2.0.0")]

namespace KeepAwakeApp
{
    // ---------- 系统 API ----------
    static class Native
    {
        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern uint SetThreadExecutionState(uint esFlags);

        [DllImport("user32.dll")]
        public static extern bool ReleaseCapture();

        [DllImport("user32.dll")]
        public static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll")]
        public static extern bool PostMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll")]
        public static extern bool SetProcessDPIAware();

        [DllImport("user32.dll")]
        public static extern int GetSystemMetrics(int index);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern int RegisterWindowMessage(string name);

        public const uint ES_CONTINUOUS = 0x80000000;
        public const uint ES_SYSTEM_REQUIRED = 0x00000001;
        public const uint ES_DISPLAY_REQUIRED = 0x00000002;
        public const uint KeepOn = ES_CONTINUOUS | ES_SYSTEM_REQUIRED | ES_DISPLAY_REQUIRED;

        public static readonly IntPtr HWND_BROADCAST = (IntPtr)0xFFFF;
    }

    // ---------- 主题 ----------
    static class Theme
    {
        public static float K = 1f;
        public static int S(int v) { return (int)Math.Round(v * K); }

        public static readonly Color Bg = Color.FromArgb(16, 18, 26);
        public static readonly Color Card = Color.FromArgb(27, 30, 42);
        public static readonly Color CardBorder = Color.FromArgb(43, 47, 62);
        public static readonly Color Text = Color.FromArgb(236, 238, 245);
        public static readonly Color Sub = Color.FromArgb(136, 142, 163);
        public static readonly Color Accent = Color.FromArgb(245, 184, 69);
        public static readonly Color AccentSoft = Color.FromArgb(58, 44, 26);
        public static readonly Color TrackOff = Color.FromArgb(56, 60, 76);
        public static readonly Color ChipBg = Color.FromArgb(35, 38, 50);
        public static readonly Color ChipBorder = Color.FromArgb(52, 56, 72);
        public static readonly Color Warn = Color.FromArgb(255, 128, 96);
        public static readonly Color Faint = Color.FromArgb(104, 110, 130);
        public static readonly Color Disabled = Color.FromArgb(92, 97, 114);

        public static Font F(float size, FontStyle style)
        {
            return new Font("Microsoft YaHei UI", size, style, GraphicsUnit.Point);
        }

        public static GraphicsPath RoundRect(Rectangle r, int radius)
        {
            GraphicsPath p = new GraphicsPath();
            int d = Math.Max(1, radius * 2);
            if (d > r.Width) d = r.Width;
            if (d > r.Height) d = r.Height;
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        public static Color Lerp(Color a, Color b, double t)
        {
            if (t < 0) t = 0;
            if (t > 1) t = 1;
            return Color.FromArgb(
                (int)(a.R + (b.R - a.R) * t),
                (int)(a.G + (b.G - a.G) * t),
                (int)(a.B + (b.B - a.B) * t));
        }
    }

    // ---------- 圆角卡片 ----------
    class Card : Panel
    {
        public Card()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint
                | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            BackColor = Theme.Bg;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(Theme.Bg);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            Rectangle r = new Rectangle(0, 0, Width - 1, Height - 1);
            using (GraphicsPath p = Theme.RoundRect(r, Theme.S(16)))
            {
                using (SolidBrush b = new SolidBrush(Theme.Card)) g.FillPath(b, p);
                using (Pen pen = new Pen(Theme.CardBorder)) g.DrawPath(pen, p);
            }
        }
    }

    // ---------- 开关 ----------
    class ToggleSwitch : Control
    {
        private bool _checked;
        private double _pos;
        private bool _hover;
        private Timer _anim;
        public event EventHandler CheckedChanged;

        public ToggleSwitch()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint
                | ControlStyles.OptimizedDoubleBuffer | ControlStyles.SupportsTransparentBackColor
                | ControlStyles.ResizeRedraw, true);
            BackColor = Color.Transparent;
            Size = new Size(Theme.S(58), Theme.S(32));
            Cursor = Cursors.Hand;
            TabStop = true;
            _anim = new Timer();
            _anim.Interval = 16;
            _anim.Tick += delegate
            {
                double target = _checked ? 1 : 0;
                _pos += (target - _pos) * 0.24;
                if (Math.Abs(target - _pos) < 0.015) { _pos = target; _anim.Stop(); }
                Invalidate();
            };
        }

        public bool Checked
        {
            get { return _checked; }
            set
            {
                if (_checked == value) return;
                _checked = value;
                _anim.Start();
                if (CheckedChanged != null) CheckedChanged(this, EventArgs.Empty);
            }
        }

        public void Sync(bool value)
        {
            _checked = value;
            _pos = value ? 1 : 0;
            _anim.Stop();
            Invalidate();
        }

        protected override void OnClick(EventArgs e)
        {
            base.OnClick(e);
            if (!Enabled) return;
            Checked = !_checked;
            Focus();
        }

        protected override bool IsInputKey(Keys keyData)
        {
            if (keyData == Keys.Space) return true;
            return base.IsInputKey(keyData);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (Enabled && e.KeyCode == Keys.Space) { Checked = !_checked; e.Handled = true; }
        }

        protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); _hover = true; Invalidate(); }
        protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); _hover = false; Invalidate(); }
        protected override void OnEnabledChanged(EventArgs e) { base.OnEnabledChanged(e); Invalidate(); }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            if (Parent != null) g.Clear(Parent.BackColor);

            int h = Height - 1;
            Rectangle track = new Rectangle(0, 0, Width - 1, h);
            Color baseColor = _hover && Enabled ? Color.FromArgb(72, 77, 94) : Theme.TrackOff;
            Color onColor = _hover && Enabled ? Color.FromArgb(255, 197, 92) : Theme.Accent;
            if (!Enabled)
            {
                baseColor = Color.FromArgb(46, 49, 62);
                onColor = Color.FromArgb(150, 118, 52);
            }
            Color c = Theme.Lerp(baseColor, onColor, _pos);

            using (GraphicsPath p = Theme.RoundRect(track, h / 2))
            {
                using (SolidBrush b = new SolidBrush(c)) g.FillPath(b, p);
                using (Pen pen = new Pen(Color.FromArgb(62, 66, 82))) g.DrawPath(pen, p);
            }

            int pad = Theme.S(4);
            int knob = h - pad * 2;
            int x = (int)(pad + (Width - pad * 2 - knob) * _pos);
            Color knobColor = Enabled ? Color.FromArgb(246, 247, 251) : Color.FromArgb(176, 180, 194);
            using (GraphicsPath p = Theme.RoundRect(new Rectangle(x, pad, knob, knob), knob / 2))
            {
                using (SolidBrush b = new SolidBrush(knobColor)) g.FillPath(b, p);
            }
            if (Focused)
            {
                using (Pen pen = new Pen(Color.FromArgb(96, 245, 184, 69)))
                using (GraphicsPath p = Theme.RoundRect(track, h / 2))
                    g.DrawPath(pen, p);
            }
        }
    }

    // ---------- 小圆角按钮 ----------
    class RoundButton : Control
    {
        private bool _hover;
        private bool _pressed;
        public string Glyph = "+";
        public int Radius = 10;
        public Color GlyphColor = Color.FromArgb(226, 229, 238);
        public Color FillColor = Color.FromArgb(35, 38, 50);
        public Color HoverColor = Color.FromArgb(46, 50, 65);

        public RoundButton()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint
                | ControlStyles.OptimizedDoubleBuffer | ControlStyles.SupportsTransparentBackColor
                | ControlStyles.ResizeRedraw, true);
            BackColor = Color.Transparent;
            Cursor = Cursors.Hand;
        }

        protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); _hover = true; Invalidate(); }
        protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); _hover = false; _pressed = false; Invalidate(); }
        protected override void OnMouseDown(MouseEventArgs e) { base.OnMouseDown(e); _pressed = true; Invalidate(); }
        protected override void OnMouseUp(MouseEventArgs e) { base.OnMouseUp(e); _pressed = false; Invalidate(); }
        protected override void OnEnabledChanged(EventArgs e) { base.OnEnabledChanged(e); Invalidate(); }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            if (Parent != null) g.Clear(Parent.BackColor);
            Rectangle r = new Rectangle(0, 0, Width - 1, Height - 1);
            Color fill = _pressed ? Theme.Lerp(FillColor, Color.Black, 0.25) : (_hover ? HoverColor : FillColor);
            if (!Enabled) fill = Color.FromArgb(30, 33, 44);
            using (GraphicsPath p = Theme.RoundRect(r, Theme.S(Radius)))
            {
                using (SolidBrush b = new SolidBrush(fill)) g.FillPath(b, p);
                using (Pen pen = new Pen(Theme.ChipBorder)) g.DrawPath(pen, p);
            }
            using (Font f = Theme.F(10.5f, FontStyle.Bold))
            {
                TextRenderer.DrawText(g, Glyph, f, r, Enabled ? (_hover ? Theme.Text : GlyphColor) : Color.FromArgb(88, 93, 110),
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            }
        }
    }

    // ---------- 预设时长胶囊 ----------
    class Chip : Control
    {
        public int Value;
        public string Caption = "";
        private bool _selected;
        private bool _hover;

        public Chip(int value, string caption)
        {
            Value = value;
            Caption = caption;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint
                | ControlStyles.OptimizedDoubleBuffer | ControlStyles.SupportsTransparentBackColor
                | ControlStyles.ResizeRedraw, true);
            BackColor = Color.Transparent;
            Size = new Size(Theme.S(64), Theme.S(36));
            Cursor = Cursors.Hand;
        }

        public bool Selected
        {
            get { return _selected; }
            set { if (_selected != value) { _selected = value; Invalidate(); } }
        }

        protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); _hover = true; Invalidate(); }
        protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); _hover = false; Invalidate(); }
        protected override void OnEnabledChanged(EventArgs e) { base.OnEnabledChanged(e); Invalidate(); }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            if (Parent != null) g.Clear(Parent.BackColor);
            Rectangle r = new Rectangle(0, 0, Width - 1, Height - 1);

            Color fill = _selected ? Theme.AccentSoft : (_hover && Enabled ? Color.FromArgb(44, 48, 62) : Theme.ChipBg);
            Color line = _selected ? Color.FromArgb(150, 245, 184, 69) : Theme.ChipBorder;
            Color text = !Enabled ? Theme.Disabled : (_selected ? Theme.Accent : Theme.Sub);

            using (GraphicsPath p = Theme.RoundRect(r, Theme.S(10)))
            {
                using (SolidBrush b = new SolidBrush(fill)) g.FillPath(b, p);
                using (Pen pen = new Pen(line)) g.DrawPath(pen, p);
            }
            using (Font f = Theme.F(9.5f, FontStyle.Regular))
            {
                TextRenderer.DrawText(g, Caption, f, r, text,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            }
        }
    }

    // ---------- 状态胶囊 ----------
    class StatusPill : Control
    {
        public string Caption = "未开启";
        public Color CaptionColor = Theme.Sub;
        public Color DotColor = Theme.TrackOff;

        public StatusPill()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint
                | ControlStyles.OptimizedDoubleBuffer | ControlStyles.SupportsTransparentBackColor
                | ControlStyles.ResizeRedraw, true);
            BackColor = Color.Transparent;
            Size = new Size(Theme.S(124), Theme.S(26));
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            if (Parent != null) g.Clear(Parent.BackColor);
            Rectangle r = new Rectangle(0, 0, Width - 1, Height - 1);
            using (GraphicsPath p = Theme.RoundRect(r, Height / 2))
            {
                using (SolidBrush b = new SolidBrush(Color.FromArgb(26, CaptionColor.R, CaptionColor.G, CaptionColor.B))) g.FillPath(b, p);
                using (Pen pen = new Pen(Color.FromArgb(74, CaptionColor.R, CaptionColor.G, CaptionColor.B))) g.DrawPath(pen, p);
            }
            int dot = Theme.S(7);
            using (SolidBrush b = new SolidBrush(DotColor))
                g.FillEllipse(b, new Rectangle(Theme.S(11), (Height - dot) / 2, dot, dot));
            using (Font f = Theme.F(8.8f, FontStyle.Regular))
            {
                Rectangle tr = new Rectangle(Theme.S(24), 0, Width - Theme.S(28), Height);
                TextRenderer.DrawText(g, Caption, f, tr, CaptionColor,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            }
        }
    }

    // ---------- 输入框 ----------
    class Field : Control
    {
        public TextBox Box;
        private bool _focus;

        public Field(string text, int width, int height, bool center)
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint
                | ControlStyles.OptimizedDoubleBuffer | ControlStyles.SupportsTransparentBackColor
                | ControlStyles.ResizeRedraw, true);
            BackColor = Color.Transparent;
            Size = new Size(Theme.S(width), Theme.S(height));

            Box = new TextBox();
            Box.BorderStyle = BorderStyle.None;
            Box.BackColor = Theme.ChipBg;
            Box.ForeColor = Theme.Text;
            Box.Font = Theme.F(9.5f, FontStyle.Regular);
            Box.Text = text;
            Box.TextAlign = center ? HorizontalAlignment.Center : HorizontalAlignment.Left;
            Controls.Add(Box);

            Box.Width = Width - Theme.S(18);
            Box.Location = new Point(Theme.S(9), Math.Max(1, (Height - Box.PreferredHeight) / 2));
            Box.Enter += delegate { _focus = true; Invalidate(); };
            Box.Leave += delegate { _focus = false; Invalidate(); };
        }

        public string Value
        {
            get { return Box.Text; }
            set
            {
                if (Box.Text != value) Box.Text = value;
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            if (Parent != null) g.Clear(Parent.BackColor);
            Rectangle r = new Rectangle(0, 0, Width - 1, Height - 1);
            using (GraphicsPath p = Theme.RoundRect(r, Theme.S(9)))
            {
                using (SolidBrush b = new SolidBrush(Theme.ChipBg)) g.FillPath(b, p);
                using (Pen pen = new Pen(_focus ? Color.FromArgb(150, 245, 184, 69) : Theme.ChipBorder)) g.DrawPath(pen, p);
            }
        }
    }

    // ---------- 深色菜单 ----------
    class DarkTable : ProfessionalColorTable
    {
        public override Color ToolStripDropDownBackground { get { return Color.FromArgb(28, 31, 43); } }
        public override Color MenuItemSelected { get { return Color.FromArgb(48, 52, 68); } }
        public override Color MenuItemSelectedGradientBegin { get { return Color.FromArgb(48, 52, 68); } }
        public override Color MenuItemSelectedGradientEnd { get { return Color.FromArgb(48, 52, 68); } }
        public override Color MenuItemBorder { get { return Color.FromArgb(48, 52, 68); } }
        public override Color MenuBorder { get { return Color.FromArgb(56, 60, 76); } }
        public override Color SeparatorDark { get { return Color.FromArgb(48, 52, 66); } }
        public override Color SeparatorLight { get { return Color.FromArgb(48, 52, 66); } }
        public override Color ImageMarginGradientBegin { get { return Color.FromArgb(28, 31, 43); } }
        public override Color ImageMarginGradientMiddle { get { return Color.FromArgb(28, 31, 43); } }
        public override Color ImageMarginGradientEnd { get { return Color.FromArgb(28, 31, 43); } }
    }

    class DarkRenderer : ToolStripProfessionalRenderer
    {
        public DarkRenderer() : base(new DarkTable()) { RoundedEdges = false; }

        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            e.TextColor = e.Item.Enabled ? Theme.Text : Theme.Disabled;
            base.OnRenderItemText(e);
        }

        protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
        {
            Graphics g = e.Graphics;
            Rectangle r = new Rectangle(Theme.S(10), e.Item.Height / 2, e.Item.Width - Theme.S(20), 1);
            using (SolidBrush b = new SolidBrush(Color.FromArgb(48, 52, 66))) g.FillRectangle(b, r);
        }
    }

    enum StopReason { UserOff, Timeout, BatteryLow, AutoEnded }
}
