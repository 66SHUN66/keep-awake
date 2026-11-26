using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Media;
using System.Windows.Forms;
using Microsoft.Win32;

namespace KeepAwakeApp
{
    partial class MainForm : Form
    {
        private ToggleSwitch _swKeep;
        private ToggleSwitch _swLimit;
        private ToggleSwitch _swProc;
        private ToggleSwitch _swSched;
        private ToggleSwitch _swBatt;
        private ToggleSwitch _swAutoStart;
        private StatusPill _pill;
        private Label _valElapsed;
        private Label _valRemain;
        private Label _unitLabel;
        private RoundButton _btnMinus;
        private RoundButton _btnPlus;
        private RoundButton _btnClose;
        private RoundButton _btnMin;
        private Card _card1;
        private Card _card2;
        private Card _card3;
        private Field _fMinutes;
        private Field _fProc;
        private Field _fSchedStart;
        private Field _fSchedEnd;
        private Field _fBatt;
        private Label _lblBattState;
        private Label _lblAutoState;

        private Timer _uiTimer;
        private Timer _timerRenew;
        private Timer _timerAuto;
        private NotifyIcon _tray;
        private ContextMenuStrip _menu;
        private ToolStripMenuItem _miShow;
        private ToolStripMenuItem _miToggle;
        private ToolStripMenuItem _miInfo;
        private ToolStripMenuItem _miExit;
        private static int _showMsg;

        private bool _running;
        private bool _sync;
        private bool _reallyExit;
        private bool _trayHintShown;
        private bool _startHidden;
        private bool _autoRunning;
        private bool _manualBlock;
        private bool _autoActiveLast;
        private bool _batteryBlocked;
        private DateTime _start;
        private int _minutes = 60;
        private List<Chip> _chips = new List<Chip>();
        private string _selfTestLog;

        private bool _cfgLimit;
        private bool _cfgProcOn;
        private bool _cfgSchedOn;
        private bool _cfgBattOn;
        private bool _cfgAutoStart;
        private string _procText = "python.exe";
        private string _schedStart = "20:00";
        private string _schedEnd = "24:00";
        private int _battPct = 20;
        private Dictionary<string, string> _cfg = new Dictionary<string, string>();

        public MainForm()
        {
            try
            {
                using (Graphics g = CreateGraphics()) Theme.K = g.DpiX / 96f;
            }
            catch { Theme.K = 1f; }
            if (Theme.K < 1f || Theme.K > 4f) Theme.K = 1f;

            _showMsg = Native.RegisterWindowMessage("KeepAwake_ShowWindow_v1");

            AutoScaleDimensions = new SizeF(96F, 96F);
            AutoScaleMode = AutoScaleMode.None;
            Text = "保持亮屏";
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            BackColor = Theme.Bg;
            DoubleBuffered = true;
            MaximizeBox = false;
            ShowInTaskbar = true;
            KeyPreview = true;

            try { Icon = LoadResIcon("AppIco", Theme.S(32)); } catch { }

            LoadSettings();
            BuildUi();
            BuildTray();
            ClientSize = new Size(Theme.S(660), Theme.S(786));
            try
            {
                int sw = Native.GetSystemMetrics(0);
                int sh = Native.GetSystemMetrics(1);
                if (sw > 0 && sh > 0)
                    Location = new Point(Math.Max(0, (sw - Width) / 2), Math.Max(0, (sh - Height) / 2 - Theme.S(20)));
            }
            catch { }

            _uiTimer = new Timer();
            _uiTimer.Interval = 1000;
            _uiTimer.Tick += UiTick;

            _timerRenew = new Timer();
            _timerRenew.Interval = 30000;
            _timerRenew.Tick += delegate
            {
                if (_running) Native.SetThreadExecutionState(Native.KeepOn);
            };

            _timerAuto = new Timer();
            _timerAuto.Interval = 5000;
            _timerAuto.Tick += delegate { EvaluateAuto(); CheckBattery(false); };
            _timerAuto.Start();

            ApplyLimitUi();
            ApplyAutoUi();
            RefreshStats();
            CheckBattery(true);
        }

        public void StartHidden() { _startHidden = true; }
        public void EnableSelfTest(string logPath) { _selfTestLog = logPath; }

        // ================= 设置持久化 =================
        private string SettingsPath()
        {
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "KeepAwake", "settings.ini");
        }

        private void LoadSettings()
        {
            try
            {
                string path = SettingsPath();
                if (File.Exists(path))
                {
                    foreach (string line in File.ReadAllLines(path))
                    {
                        int idx = line.IndexOf('=');
                        if (idx <= 0) continue;
                        _cfg[line.Substring(0, idx).Trim()] = line.Substring(idx + 1).Trim();
                    }
                }
            }
            catch { }

            _minutes = Math.Min(1440, Math.Max(5, GetInt("minutes", 60)));
            _cfgLimit = GetInt("limit", 0) == 1;
            _cfgProcOn = GetInt("procOn", 0) == 1;
            _procText = GetStr("proc", "python.exe");
            _cfgSchedOn = GetInt("sched", 0) == 1;
            _schedStart = GetStr("schedStart", "20:00");
            _schedEnd = GetStr("schedEnd", "24:00");
            _cfgBattOn = GetInt("batt", 1) == 1;
            _battPct = Math.Min(90, Math.Max(5, GetInt("battPct", 20)));
            _cfgAutoStart = GetInt("autostart", 0) == 1;
        }

        private int GetInt(string key, int def)
        {
            try
            {
                if (_cfg.ContainsKey(key)) return int.Parse(_cfg[key]);
            }
            catch { }
            return def;
        }

        private string GetStr(string key, string def)
        {
            if (_cfg.ContainsKey(key) && _cfg[key].Length > 0) return _cfg[key];
            return def;
        }

        private int FieldInt(Field f, int def)
        {
            try
            {
                int v = int.Parse(f.Value.Trim());
                if (v < 0) return def;
                return v;
            }
            catch { return def; }
        }

        private void SaveSettings()
        {
            try
            {
                string path = SettingsPath();
                string dir = Path.GetDirectoryName(path);
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                List<string> lines = new List<string>();
                lines.Add("minutes=" + _minutes);
                lines.Add("limit=" + (_swLimit.Checked ? 1 : 0));
                lines.Add("procOn=" + (_swProc.Checked ? 1 : 0));
                lines.Add("proc=" + _fProc.Value.Trim());
                lines.Add("sched=" + (_swSched.Checked ? 1 : 0));
                lines.Add("schedStart=" + _fSchedStart.Value.Trim());
                lines.Add("schedEnd=" + _fSchedEnd.Value.Trim());
                lines.Add("batt=" + (_swBatt.Checked ? 1 : 0));
                lines.Add("battPct=" + FieldInt(_fBatt, _battPct));
                lines.Add("autostart=" + (_swAutoStart.Checked ? 1 : 0));
                File.WriteAllLines(path, lines.ToArray());
            }
            catch { }
        }

        // ================= 界面构件 =================
        private static List<Stream> _iconStreams = new List<Stream>();

        private static byte[] LoadResBytes(string name)
        {
            try
            {
                Stream st = typeof(MainForm).Assembly.GetManifestResourceStream(name);
                if (st == null) return null;
                byte[] buf = new byte[st.Length];
                int read = 0;
                while (read < buf.Length)
                {
                    int n = st.Read(buf, read, buf.Length - read);
                    if (n <= 0) break;
                    read += n;
                }
                st.Dispose();
                return buf;
            }
            catch { return null; }
        }

        private static Icon LoadResIcon(string name, int size)
        {
            try
            {
                byte[] buf = LoadResBytes(name);
                if (buf == null) return null;
                MemoryStream ms = new MemoryStream(buf);
                _iconStreams.Add(ms);
                return new Icon(ms, Math.Max(16, size), Math.Max(16, size));
            }
            catch { return null; }
        }

        private static Image LoadResImage(string name)
        {
            try
            {
                byte[] buf = LoadResBytes(name);
                if (buf == null) return null;
                return Image.FromStream(new MemoryStream(buf));
            }
            catch { return null; }
        }

        private Label MakeLabel(Control parent, string text, float size, Color color, int x, int y, FontStyle style)
        {
            Label l = new Label();
            l.Text = text;
            l.Font = Theme.F(size, style);
            l.ForeColor = color;
            l.AutoSize = true;
            l.BackColor = Color.Transparent;
            l.Location = new Point(Theme.S(x), Theme.S(y));
            parent.Controls.Add(l);
            return l;
        }

        private Panel MakeDivider(Control parent, int y, int width)
        {
            Panel p = new Panel();
            p.BackColor = Theme.CardBorder;
            p.Bounds = new Rectangle(Theme.S(22), Theme.S(y), width, Math.Max(1, Theme.S(1)));
            parent.Controls.Add(p);
            return p;
        }

        private void BuildUi()
        {
            Panel title = new Panel();
            title.Bounds = new Rectangle(0, 0, Theme.S(660), Theme.S(52));
            title.BackColor = Theme.Bg;
            title.MouseDown += DragWindow;

            // 标题栏品牌图标（透明底 PNG，深色背景上清晰）
            try
            {
                Image mark = LoadResImage("MarkPng");
                if (mark != null)
                {
                    PictureBox pb = new PictureBox();
                    pb.BackColor = Theme.Bg;
                    pb.SizeMode = PictureBoxSizeMode.Zoom;
                    pb.Bounds = new Rectangle(Theme.S(24), Theme.S(14), Theme.S(20), Theme.S(20));
                    pb.Image = mark;
                    pb.MouseDown += DragWindow;
                    title.Controls.Add(pb);
                }
            }
            catch { }

            MakeLabel(title, "保持亮屏", 11f, Theme.Text, 52, 15, FontStyle.Bold);
            MakeLabel(title, "KEEP AWAKE", 8f, Theme.Faint, 121, 20, FontStyle.Regular);

            _btnClose = new RoundButton();
            _btnClose.Glyph = "✕";
            _btnClose.Radius = 7;
            _btnClose.FillColor = Theme.Bg;
            _btnClose.HoverColor = Color.FromArgb(68, 40, 44);
            _btnClose.GlyphColor = Theme.Sub;
            _btnClose.Bounds = new Rectangle(Theme.S(608), Theme.S(12), Theme.S(30), Theme.S(28));
            _btnClose.Click += delegate { HideToTray(); };
            title.Controls.Add(_btnClose);

            _btnMin = new RoundButton();
            _btnMin.Glyph = "—";
            _btnMin.Radius = 7;
            _btnMin.FillColor = Theme.Bg;
            _btnMin.HoverColor = Color.FromArgb(44, 47, 60);
            _btnMin.GlyphColor = Theme.Sub;
            _btnMin.Bounds = new Rectangle(Theme.S(572), Theme.S(12), Theme.S(30), Theme.S(28));
            _btnMin.Click += delegate { HideToTray(); };
            title.Controls.Add(_btnMin);

            Controls.Add(title);

            // 卡片 1
            _card1 = new Card();
            _card1.Bounds = new Rectangle(Theme.S(24), Theme.S(60), Theme.S(612), Theme.S(168));
            int cw = _card1.Width;

            MakeLabel(_card1, "保持亮屏", 14f, Theme.Text, 22, 17, FontStyle.Bold);
            MakeLabel(_card1, "阻止屏幕熄灭与系统休眠", 9f, Theme.Sub, 22, 47, FontStyle.Regular);

            _swKeep = new ToggleSwitch();
            _swKeep.Location = new Point(cw - Theme.S(22) - _swKeep.Width, Theme.S(24));
            _swKeep.CheckedChanged += KeepChanged;
            _card1.Controls.Add(_swKeep);

            MakeDivider(_card1, 82, cw - Theme.S(44));

            _pill = new StatusPill();
            _pill.Bounds = new Rectangle(Theme.S(22), Theme.S(102), _pill.Width, _pill.Height);
            _card1.Controls.Add(_pill);

            MakeLabel(_card1, "已保持", 8.8f, Theme.Sub, 158, 98, FontStyle.Regular);
            _valElapsed = MakeLabel(_card1, "00:00:00", 13f, Theme.Text, 158, 116, FontStyle.Regular);

            MakeLabel(_card1, "剩余", 8.8f, Theme.Sub, 340, 98, FontStyle.Regular);
            _valRemain = MakeLabel(_card1, "—", 13f, Theme.Sub, 340, 116, FontStyle.Regular);

            MakeLabel(_card1, "关闭/最小化会收进托盘继续运行", 8.2f, Theme.Faint, 448, 104, FontStyle.Regular);

            Controls.Add(_card1);

            // 卡片 2
            _card2 = new Card();
            _card2.Bounds = new Rectangle(Theme.S(24), Theme.S(236), Theme.S(612), Theme.S(170));
            cw = _card2.Width;

            MakeLabel(_card2, "设置亮屏时长", 14f, Theme.Text, 22, 17, FontStyle.Bold);
            MakeLabel(_card2, "开启后到达设定时长自动停止", 9f, Theme.Sub, 22, 47, FontStyle.Regular);

            _swLimit = new ToggleSwitch();
            _swLimit.Location = new Point(cw - Theme.S(22) - _swLimit.Width, Theme.S(24));
            _swLimit.CheckedChanged += LimitChanged;
            _card2.Controls.Add(_swLimit);

            MakeDivider(_card2, 82, cw - Theme.S(44));

            _fMinutes = new Field(_minutes.ToString(), 84, 32, true);
            _fMinutes.Bounds = new Rectangle(Theme.S(22), Theme.S(102), _fMinutes.Width, _fMinutes.Height);
            _fMinutes.Box.Leave += delegate { CommitMinutes(); };
            _fMinutes.Box.KeyDown += delegate(object s, KeyEventArgs e)
            {
                if (e.KeyCode == Keys.Enter) { CommitMinutes(); e.SuppressKeyPress = true; }
            };
            _card2.Controls.Add(_fMinutes);

            _unitLabel = MakeLabel(_card2, "分钟", 8.8f, Theme.Sub, 112, 112, FontStyle.Regular);

            _btnMinus = new RoundButton();
            _btnMinus.Glyph = "−";
            _btnMinus.Radius = 8;
            _btnMinus.Bounds = new Rectangle(Theme.S(148), Theme.S(100), Theme.S(36), Theme.S(36));
            _btnMinus.Click += delegate { SetMinutes(CurrentMinutes() - 5); };
            _card2.Controls.Add(_btnMinus);

            _btnPlus = new RoundButton();
            _btnPlus.Glyph = "+";
            _btnPlus.Radius = 8;
            _btnPlus.Bounds = new Rectangle(Theme.S(190), Theme.S(100), Theme.S(36), Theme.S(36));
            _btnPlus.Click += delegate { SetMinutes(CurrentMinutes() + 5); };
            _card2.Controls.Add(_btnPlus);

            AddChip(_card2, 15, "15 分", 240);
            AddChip(_card2, 30, "30 分", 312);
            AddChip(_card2, 60, "1 小时", 384);
            AddChip(_card2, 120, "2 小时", 456);

            MakeLabel(_card2, "可直接输入分钟数（5–1440），也可点预设", 8.4f, Theme.Faint, 22, 148, FontStyle.Regular);

            Controls.Add(_card2);

            // 卡片 3：自动化
            _card3 = new Card();
            _card3.Bounds = new Rectangle(Theme.S(24), Theme.S(414), Theme.S(612), Theme.S(326));
            cw = _card3.Width;

            MakeLabel(_card3, "自动化", 14f, Theme.Text, 22, 17, FontStyle.Bold);
            MakeLabel(_card3, "满足条件时自动开启，条件结束自动停止", 9f, Theme.Sub, 22, 47, FontStyle.Regular);

            MakeDivider(_card3, 82, cw - Theme.S(44));

            int rowY = 96;
            MakeLabel(_card3, "进程联动", 9.5f, Theme.Text, 22, rowY + 9, FontStyle.Regular);
            _fProc = new Field(_procText, 196, 32, false);
            _fProc.Bounds = new Rectangle(Theme.S(112), Theme.S(rowY), _fProc.Width, _fProc.Height);
            _fProc.Box.Leave += delegate { SaveSettings(); EvaluateAuto(); };
            _card3.Controls.Add(_fProc);

            _swProc = new ToggleSwitch();
            _swProc.Location = new Point(cw - Theme.S(22) - _swProc.Width, Theme.S(rowY) + Theme.S(2));
            _swProc.CheckedChanged += AutoSettingChanged;
            _card3.Controls.Add(_swProc);
            MakeLabel(_card3, "在跑就保持", 8.2f, Theme.Faint, 322, rowY + 11, FontStyle.Regular);

            rowY = 142;
            MakeLabel(_card3, "定时时段", 9.5f, Theme.Text, 22, rowY + 9, FontStyle.Regular);
            _fSchedStart = new Field(_schedStart, 66, 32, true);
            _fSchedStart.Bounds = new Rectangle(Theme.S(112), Theme.S(rowY), _fSchedStart.Width, _fSchedStart.Height);
            _fSchedStart.Box.Leave += delegate { SaveSettings(); EvaluateAuto(); };
            _card3.Controls.Add(_fSchedStart);

            MakeLabel(_card3, "到", 9f, Theme.Sub, 186, rowY + 9, FontStyle.Regular);

            _fSchedEnd = new Field(_schedEnd, 66, 32, true);
            _fSchedEnd.Bounds = new Rectangle(Theme.S(204), Theme.S(rowY), _fSchedEnd.Width, _fSchedEnd.Height);
            _fSchedEnd.Box.Leave += delegate { SaveSettings(); EvaluateAuto(); };
            _card3.Controls.Add(_fSchedEnd);

            _swSched = new ToggleSwitch();
            _swSched.Location = new Point(cw - Theme.S(22) - _swSched.Width, Theme.S(rowY) + Theme.S(2));
            _swSched.CheckedChanged += AutoSettingChanged;
            _card3.Controls.Add(_swSched);
            _lblAutoState = MakeLabel(_card3, "", 8.2f, Theme.Faint, 282, rowY + 11, FontStyle.Regular);

            rowY = 188;
            MakeLabel(_card3, "低电量保护", 9.5f, Theme.Text, 22, rowY + 9, FontStyle.Regular);
            _fBatt = new Field(_battPct.ToString(), 58, 32, true);
            _fBatt.Bounds = new Rectangle(Theme.S(112), Theme.S(rowY), _fBatt.Width, _fBatt.Height);
            _fBatt.Box.Leave += delegate { SaveSettings(); CheckBattery(true); };
            _card3.Controls.Add(_fBatt);

            MakeLabel(_card3, "% 以下停止", 8.2f, Theme.Sub, 176, rowY + 11, FontStyle.Regular);

            _swBatt = new ToggleSwitch();
            _swBatt.Location = new Point(cw - Theme.S(22) - _swBatt.Width, Theme.S(rowY) + Theme.S(2));
            _swBatt.CheckedChanged += AutoSettingChanged;
            _card3.Controls.Add(_swBatt);
            _lblBattState = MakeLabel(_card3, "", 8.2f, Theme.Faint, 252, rowY + 11, FontStyle.Regular);

            rowY = 234;
            MakeLabel(_card3, "开机自启", 9.5f, Theme.Text, 22, rowY + 9, FontStyle.Regular);
            MakeLabel(_card3, "开机后静默进托盘，不自动开始保持", 8.2f, Theme.Faint, 112, rowY + 11, FontStyle.Regular);

            _swAutoStart = new ToggleSwitch();
            _swAutoStart.Location = new Point(cw - Theme.S(22) - _swAutoStart.Width, Theme.S(rowY) + Theme.S(2));
            _swAutoStart.CheckedChanged += AutoStartChanged;
            _card3.Controls.Add(_swAutoStart);

            MakeDivider(_card3, 284, cw - Theme.S(44));
            MakeLabel(_card3, "进程名多个用逗号分隔（如 python.exe, aria2c）；时段跨零点自动顺延", 8.4f, Theme.Faint, 22, 294, FontStyle.Regular);

            Controls.Add(_card3);

            MakeLabel(this, "原理：调用系统 SetThreadExecutionState 接口，运行时才生效，不改动电源计划", 8.6f,
                Color.FromArgb(96, 101, 120), 26, 756, FontStyle.Regular);

            _sync = true;
            _swLimit.Sync(_cfgLimit);
            _swProc.Sync(_cfgProcOn);
            _swSched.Sync(_cfgSchedOn);
            _swBatt.Sync(_cfgBattOn);
            _swAutoStart.Sync(_cfgAutoStart);
            _sync = false;
        }

        private void AddChip(Card parent, int value, string caption, int x)
        {
            Chip chip = new Chip(value, caption);
            chip.Bounds = new Rectangle(Theme.S(x), Theme.S(100), chip.Width, chip.Height);
            chip.Click += delegate { SetMinutes(value); };
            parent.Controls.Add(chip);
            _chips.Add(chip);
        }

        private void BuildTray()
        {
            _menu = new ContextMenuStrip();
            _menu.BackColor = Color.FromArgb(28, 31, 43);
            _menu.ForeColor = Theme.Text;
            _menu.Renderer = new DarkRenderer();
            _menu.ShowImageMargin = false;
            _menu.Font = Theme.F(9.5f, FontStyle.Regular);

            _miShow = new ToolStripMenuItem("显示主窗口");
            _miShow.Click += delegate { ShowFromTray(); };

            _miToggle = new ToolStripMenuItem("开始保持");
            _miToggle.Click += delegate
            {
                if (_running) { _manualBlock = true; StopKeep(StopReason.UserOff); }
                else { _manualBlock = false; StartKeep(false); }
            };

            _miInfo = new ToolStripMenuItem("未开启");
            _miInfo.Enabled = false;

            _miExit = new ToolStripMenuItem("退出");
            _miExit.Click += delegate { CloseForm(); };

            _menu.Items.Add(_miShow);
            _menu.Items.Add(_miToggle);
            _menu.Items.Add(_miInfo);
            _menu.Items.Add(new ToolStripSeparator());
            _menu.Items.Add(_miExit);

            _tray = new NotifyIcon();
            Icon trayIcon = null;
            try { trayIcon = LoadResIcon("TrayIco", SystemInformation.SmallIconSize.Width); } catch { }
            if (trayIcon != null) { _tray.Icon = trayIcon; }
            else
            {
                try { _tray.Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); }
                catch { _tray.Icon = SystemIcons.Application; }
            }
            _tray.Text = "保持亮屏";
            _tray.Visible = true;
            _tray.ContextMenuStrip = _menu;
            _tray.DoubleClick += delegate { ShowFromTray(); };
        }

        private void DragWindow(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left) return;
            Native.ReleaseCapture();
            Native.SendMessage(Handle, 0xA1, (IntPtr)0x2, IntPtr.Zero);
        }

        // ================= 时长 =================
        private int CurrentMinutes()
        {
            return FieldInt(_fMinutes, _minutes);
        }

        private void CommitMinutes()
        {
            SetMinutes(FieldInt(_fMinutes, _minutes));
        }

        private void SetMinutes(int m)
        {
            if (m < 5) m = 5;
            if (m > 1440) m = 1440;
            _minutes = m;
            if (!_fMinutes.Box.Focused) _fMinutes.Value = m.ToString();
            foreach (Chip c in _chips) c.Selected = (c.Value == m);
            SaveSettings();
            if (!_running) RefreshStats();
        }

        private void ApplyLimitUi()
        {
            bool on = _swLimit.Checked;
            _btnMinus.Enabled = on;
            _btnPlus.Enabled = on;
            foreach (Chip c in _chips) c.Enabled = on;
            _fMinutes.Box.ReadOnly = !on;
            _fMinutes.Box.ForeColor = on ? Theme.Text : Color.FromArgb(150, 155, 172);
            _unitLabel.ForeColor = on ? Theme.Sub : Color.FromArgb(96, 101, 120);
        }

        private void LimitChanged(object sender, EventArgs e)
        {
            if (_sync) return;
            ApplyLimitUi();
            SaveSettings();
            RefreshStats();
        }

        private void ApplyAutoUi()
        {
            _fProc.Box.ReadOnly = !_swProc.Checked;
            _fProc.Box.ForeColor = _swProc.Checked ? Theme.Text : Color.FromArgb(150, 155, 172);
            _fSchedStart.Box.ReadOnly = !_swSched.Checked;
            _fSchedEnd.Box.ReadOnly = !_swSched.Checked;
            Color schedColor = _swSched.Checked ? Theme.Text : Color.FromArgb(150, 155, 172);
            _fSchedStart.Box.ForeColor = schedColor;
            _fSchedEnd.Box.ForeColor = schedColor;
        }
    }
}


