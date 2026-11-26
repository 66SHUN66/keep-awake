using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Media;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

namespace KeepAwakeApp
{
    partial class MainForm
    {
        // ================= 托盘 / 显示隐藏 =================
        private void HideToTray()
        {
            ShowInTaskbar = false;
            Hide();
            if (!_trayHintShown)
            {
                _trayHintShown = true;
                try
                {
                    _tray.ShowBalloonTip(2500, "保持亮屏", "已收进托盘，保持状态继续运行。双击托盘图标可重新打开。", ToolTipIcon.Info);
                }
                catch { }
            }
        }

        private void ShowFromTray()
        {
            ShowInTaskbar = true;
            Show();
            WindowState = FormWindowState.Normal;
            Activate();
            TopMost = true;
            TopMost = false;
        }

        protected override void WndProc(ref Message m)
        {
            if (_showMsg != 0 && m.Msg == _showMsg)
            {
                ShowFromTray();
                return;
            }
            base.WndProc(ref m);
        }

        // ================= 开关逻辑 =================
        private void KeepChanged(object sender, EventArgs e)
        {
            if (_sync) return;
            if (_swKeep.Checked)
            {
                _manualBlock = false;
                StartKeep(false);
            }
            else
            {
                _manualBlock = true;
                StopKeep(StopReason.UserOff);
            }
        }

        private void StartKeep(bool auto)
        {
            Native.SetThreadExecutionState(Native.KeepOn);
            _running = true;
            _start = DateTime.Now;
            _autoRunning = auto;
            _uiTimer.Start();
            _timerRenew.Start();

            _sync = true;
            _swKeep.Sync(true);
            _sync = false;

            SetPill(auto ? "自动保持中" : "保持中", Theme.Accent);
            RefreshStats();
        }

        private void StopKeep(StopReason reason)
        {
            Native.SetThreadExecutionState(Native.ES_CONTINUOUS);
            _running = false;
            _uiTimer.Stop();
            _timerRenew.Stop();
            _autoRunning = false;

            _sync = true;
            _swKeep.Sync(false);
            _sync = false;

            _valElapsed.Text = "00:00:00";
            switch (reason)
            {
                case StopReason.Timeout: _manualBlock = true; SetPill("已到时停止", Theme.Warn); break;
                case StopReason.BatteryLow: SetPill("电量低已停止", Theme.Warn); break;
                case StopReason.AutoEnded: SetPill("自动条件结束", Theme.Sub); break;
                default: SetPill("未开启", Theme.Sub); break;
            }
            RefreshStats();
        }

        private void SetPill(string caption, Color color)
        {
            _pill.Caption = caption;
            _pill.CaptionColor = color;
            _pill.DotColor = color;
            _pill.Invalidate();
        }

        private string Format(TimeSpan t)
        {
            if (t.TotalHours >= 24)
                return string.Format("{0}天{1:00}:{2:00}:{3:00}", (int)t.TotalDays, t.Hours, t.Minutes, t.Seconds);
            return string.Format("{0:00}:{1:00}:{2:00}", (int)t.TotalHours, t.Minutes, t.Seconds);
        }

        private void UiTick(object sender, EventArgs e)
        {
            if (!_running) return;
            TimeSpan span = DateTime.Now - _start;
            _valElapsed.Text = Format(span);

            if (_swLimit.Checked)
            {
                double total = _minutes * 60;
                double remain = total - span.TotalSeconds;
                if (remain <= 0)
                {
                    StopKeep(StopReason.Timeout);
                    try { SystemSounds.Asterisk.Play(); } catch { }
                    return;
                }
                _valRemain.Text = Format(TimeSpan.FromSeconds(remain));
                _valRemain.ForeColor = remain <= 60 ? Theme.Warn : Theme.Accent;
            }
            else
            {
                _valRemain.Text = "—";
                _valRemain.ForeColor = Theme.Sub;
            }
            RefreshTrayText(span);
        }

        private void RefreshStats()
        {
            if (!_running)
            {
                _valElapsed.Text = "00:00:00";
                _valRemain.Text = _swLimit.Checked ? Format(TimeSpan.FromMinutes(_minutes)) : "—";
                _valRemain.ForeColor = Theme.Sub;
            }
            foreach (Chip c in _chips) c.Selected = (c.Value == _minutes);
            if (_fMinutes != null && !_fMinutes.Box.Focused) _fMinutes.Value = _minutes.ToString();
            RefreshTrayText(TimeSpan.Zero);
        }

        private void RefreshTrayText(TimeSpan span)
        {
            if (_miToggle == null) return;
            _miToggle.Text = _running ? "停止保持" : "开始保持";
            if (_running)
            {
                string info = "已保持 " + Format(span);
                if (_swLimit.Checked)
                {
                    double remain = Math.Max(0, _minutes * 60 - span.TotalSeconds);
                    info += " · 剩余 " + Format(TimeSpan.FromSeconds(remain));
                }
                _miInfo.Text = info;
            }
            else
            {
                _miInfo.Text = "未开启";
            }
        }

        // ================= 自动化 =================
        private void AutoSettingChanged(object sender, EventArgs e)
        {
            if (_sync) return;
            ApplyAutoUi();
            SaveSettings();
            if (sender == _swBatt) CheckBattery(true);
            EvaluateAuto();
        }

        private static readonly string[] ProcSeps = new string[] { ",", "，", ";", "；", " " };

        private bool ProcessRunning()
        {
            string raw = _fProc.Value.Trim();
            if (raw.Length == 0) return false;
            string[] parts = raw.Split(ProcSeps, StringSplitOptions.RemoveEmptyEntries);
            foreach (string part in parts)
            {
                string name = part.Trim();
                if (name.Length == 0) continue;
                if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) name = name.Substring(0, name.Length - 4);
                if (name.Length == 0) continue;
                Process[] ps = null;
                try
                {
                    ps = Process.GetProcessesByName(name);
                    if (ps != null && ps.Length > 0) return true;
                }
                catch { }
                finally
                {
                    if (ps != null) foreach (Process p in ps) { try { p.Dispose(); } catch { } }
                }
            }
            return false;
        }

        private bool InSchedule()
        {
            int s = ParseClock(_fSchedStart.Value);
            int e = ParseClock(_fSchedEnd.Value);
            if (s < 0 || e < 0 || s == e) return false;
            int now = DateTime.Now.Hour * 60 + DateTime.Now.Minute;
            if (s < e) return now >= s && now < e;
            return now >= s || now < e;
        }

        private int ParseClock(string text)
        {
            try
            {
                string t = text.Trim().Replace("：", ":").Replace(".", ":");
                string[] parts = t.Split(':');
                int h = int.Parse(parts[0]);
                int m = parts.Length > 1 ? int.Parse(parts[1]) : 0;
                if (h < 0 || h > 23 || m < 0 || m > 59) return -1;
                return h * 60 + m;
            }
            catch { return -1; }
        }

        private void EvaluateAuto()
        {
            if (_swProc == null) return;
            bool cond = (_swProc.Checked && ProcessRunning()) || (_swSched.Checked && InSchedule());

            if (cond != _autoActiveLast)
            {
                _autoActiveLast = cond;
                _manualBlock = false;
            }

            if (cond && !_running && !_manualBlock && !_batteryBlocked)
            {
                StartKeep(true);
                return;
            }
            if (!cond && _running && _autoRunning)
            {
                StopKeep(StopReason.AutoEnded);
            }

            if (_lblAutoState != null)
            {
                string state = "";
                if (cond) state = "条件已满足";
                else if (_swProc.Checked || _swSched.Checked) state = "等待条件";
                _lblAutoState.Text = state;
                _lblAutoState.ForeColor = cond ? Theme.Accent : Theme.Faint;
            }
        }

        private void CheckBattery(bool force)
        {
            if (_swBatt == null) return;
            try
            {
                PowerStatus ps = SystemInformation.PowerStatus;
                bool hasBattery = ps.BatteryChargeStatus != BatteryChargeStatus.NoSystemBattery
                    && ps.BatteryChargeStatus != BatteryChargeStatus.Unknown;
                bool onBattery = ps.PowerLineStatus == PowerLineStatus.Offline;
                int pct = (int)Math.Round(ps.BatteryLifePercent * 100);

                if (!hasBattery)
                {
                    _swBatt.Enabled = false;
                    _lblBattState.Text = "未检测到电池";
                    _lblBattState.ForeColor = Theme.Faint;
                    _batteryBlocked = false;
                    return;
                }
                if (!_swBatt.Enabled) _swBatt.Enabled = true;

                _lblBattState.Text = onBattery ? ("电池 " + pct + "%") : "已接电源";
                _lblBattState.ForeColor = Theme.Faint;

                int limitPct = FieldInt(_fBatt, _battPct);
                if (_swBatt.Checked && onBattery && pct > 0 && pct <= limitPct)
                {
                    if (!_batteryBlocked)
                    {
                        _batteryBlocked = true;
                        if (_running) StopKeep(StopReason.BatteryLow);
                    }
                }
                else
                {
                    _batteryBlocked = false;
                }
            }
            catch { }
        }

        // ================= 开机自启 =================
        private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string RunName = "KeepAwake";

        private void AutoStartChanged(object sender, EventArgs e)
        {
            if (_sync) return;
            try
            {
                RegistryKey k = Registry.CurrentUser.OpenSubKey(RunKey, true);
                if (k != null)
                {
                    if (_swAutoStart.Checked)
                        k.SetValue(RunName, "\"" + Application.ExecutablePath + "\" --minimized");
                    else
                    {
                        try { k.DeleteValue(RunName, false); }
                        catch { }
                    }
                    k.Close();
                }
            }
            catch { }
            SaveSettings();
        }

        // ================= 退出 =================
        private void CloseForm()
        {
            _reallyExit = true;
            Close();
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (!_reallyExit)
            {
                e.Cancel = true;
                HideToTray();
                return;
            }
            Native.SetThreadExecutionState(Native.ES_CONTINUOUS);
            SaveSettings();
            if (_tray != null)
            {
                _tray.Visible = false;
                _tray.Dispose();
            }
            base.OnFormClosing(e);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.KeyCode == Keys.Escape) HideToTray();
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            if (_startHidden) HideToTray();
            if (_selfTestLog != null) RunSelfTest(_selfTestLog);
        }

        // ================= 自检 =================
        private void RunSelfTest(string logPath)
        {
            List<string> log = new List<string>();
            try
            {
                _swLimit.Sync(true);
                ApplyLimitUi();
                SetMinutes(60);
                _manualBlock = false;
                StartKeep(false);
                log.Add("A1 start: running=" + _running + " pill=" + _pill.Caption);
                _start = _start.AddSeconds(-(_minutes * 60 - 3));

                System.Windows.Forms.Timer t1 = new System.Windows.Forms.Timer();
                t1.Interval = 4000;
                t1.Tick += delegate
                {
                    t1.Stop();
                    _uiTimer_ForceTick();
                    log.Add("A2 timeout: running=" + _running + " pill=" + _pill.Caption);

                    _swLimit.Sync(false);
                    ApplyLimitUi();

                    _fProc.Value = "explorer";
                    _swProc.Sync(true);
                    ApplyAutoUi();
                    EvaluateAuto();
                    log.Add("B1 watchdog(explorer): running=" + _running + " auto=" + _autoRunning + " pill=" + _pill.Caption);

                    _fProc.Value = "不存在-的-进程-xyz";
                    EvaluateAuto();
                    log.Add("B2 watchdog(no match): running=" + _running + " pill=" + _pill.Caption);

                    _swSched.Sync(true);
                    _fSchedStart.Value = "00:00";
                    _fSchedEnd.Value = "23:59";
                    log.Add("C1 schedule in-window=" + InSchedule());
                    _fSchedStart.Value = "23:58";
                    _fSchedEnd.Value = "23:59";
                    log.Add("C2 schedule out-window=" + InSchedule());

                    try
                    {
                        PowerStatus ps = SystemInformation.PowerStatus;
                        log.Add("D1 battery line=" + ps.PowerLineStatus + " pct=" + ps.BatteryLifePercent
                            + " status=" + ps.BatteryChargeStatus);
                    }
                    catch { log.Add("D1 battery read fail"); }

                    log.Add("E1 tray visible=" + (_tray != null && _tray.Visible) + " menuItems=" + _menu.Items.Count);
                    RegistryKey rk = Registry.CurrentUser.OpenSubKey(RunKey);
                    log.Add("E2 autostart=" + (rk != null && rk.GetValue(RunName) != null));
                    if (rk != null) rk.Close();

                    WriteLog(logPath, log);
                    CloseForm();
                };
                t1.Start();
            }
            catch (Exception ex)
            {
                log.Add("EX " + ex.Message);
                WriteLog(logPath, log);
                CloseForm();
            }
        }

        // 让 UI 计时器立即跑一次（自检时不必等 1 秒）
        private void _uiTimer_ForceTick()
        {
            try
            {
                TimeSpan span = DateTime.Now - _start;
                _valElapsed.Text = Format(span);
                if (_swLimit.Checked)
                {
                    double remain = _minutes * 60 - span.TotalSeconds;
                    if (remain <= 0)
                    {
                        StopKeep(StopReason.Timeout);
                        return;
                    }
                    _valRemain.Text = Format(TimeSpan.FromSeconds(remain));
                }
            }
            catch { }
        }

        private void WriteLog(string path, List<string> lines)
        {
            try { File.WriteAllLines(path, lines.ToArray()); }
            catch { }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (Pen pen = new Pen(Color.FromArgb(38, 42, 56)))
            using (GraphicsPath p = Theme.RoundRect(new Rectangle(0, 0, Width - 1, Height - 1), Theme.S(18)))
                e.Graphics.DrawPath(pen, p);
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            if (Width <= 0 || Height <= 0) return;
            using (GraphicsPath p = Theme.RoundRect(new Rectangle(0, 0, Width, Height), Theme.S(18)))
                Region = new Region(p);
        }
    }

    static class Program
    {
        private static Mutex _mutex;

        [STAThread]
        static void Main()
        {
            bool created = false;
            try { _mutex = new Mutex(true, "KeepAwake_SingleInstance_v1", out created); }
            catch { created = true; }

            if (!created)
            {
                int msg = Native.RegisterWindowMessage("KeepAwake_ShowWindow_v1");
                Native.PostMessage(Native.HWND_BROADCAST, msg, IntPtr.Zero, IntPtr.Zero);
                return;
            }

            try { Native.SetProcessDPIAware(); }
            catch { }
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            MainForm f = new MainForm();
            bool selfTest = false;
            bool hidden = false;
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 1; i < args.Length; i++)
            {
                if (args[i] == "--selftest") selfTest = true;
                if (args[i] == "--minimized") hidden = true;
            }
            if (selfTest) f.EnableSelfTest(Path.Combine(Application.StartupPath, "selftest.log"));
            else if (hidden) f.StartHidden();

            Application.Run(f);

            try { _mutex.ReleaseMutex(); }
            catch { }
        }
    }
}


