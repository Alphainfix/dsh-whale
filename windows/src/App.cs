using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Win32;

namespace DshWhale {
    public static class Program {
        [STAThread] public static int Main(string[] args) {
            if (args.Length == 2 && args[0] == "--signal") { int pid; return Int32.TryParse(args[1], out pid) ? Native.SendControlC(pid) : 2; }
            string stopName = "Local\\DSHWhale.Shutdown." + Environment.UserName;
            if (args.Contains("--shutdown")) { try { using (var signal = EventWaitHandle.OpenExisting(stopName)) signal.Set(); } catch (WaitHandleCannotBeOpenedException) { } return 0; }
            bool special = args.Contains("--worker") || args.Contains("--demo") || args.Contains("--snapshot");
            if (!special) return Lifecycle.Supervise(args);
            Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.ThreadException += delegate(object sender, System.Threading.ThreadExceptionEventArgs e) { Diagnostics.Log("UI exception: " + e.Exception); };
            AppDomain.CurrentDomain.UnhandledException += delegate(object sender, UnhandledExceptionEventArgs e) { Diagnostics.Log("Fatal exception: " + e.ExceptionObject); };
            bool demo = args.Contains("--demo");
            int snapshotIndex = Array.IndexOf(args, "--snapshot");
            if (snapshotIndex >= 0) {
                if (snapshotIndex + 1 >= args.Length) return 2;
                var window = new WhaleForm(true); window.Snapshot(args[snapshotIndex + 1]); window.Dispose(); return 0;
            }
            bool created;
            using (var mutex = new Mutex(true, "Local\\DSHWhale.Windows." + Environment.UserName + (demo ? ".Demo" : ""), out created)) {
                if (!created) { MessageBox.Show("小鲸鱼已在系统托盘运行。请点击任务栏右下角的鲸鱼图标。", "DSH 小鲸鱼"); return 0; }
                using (var stop = new EventWaitHandle(false, EventResetMode.AutoReset, stopName + (demo ? ".Demo" : "")))
                using (var settingsSignal = new EventWaitHandle(false, EventResetMode.AutoReset, Lifecycle.SettingsEvent + (demo ? ".Demo" : "")))
                using (var openSignal = new EventWaitHandle(false, EventResetMode.AutoReset, Lifecycle.OpenEvent + (demo ? ".Demo" : "")))
                using (var panelSignal = new EventWaitHandle(false, EventResetMode.AutoReset, Lifecycle.PanelEvent + (demo ? ".Demo" : "")))
                using (var form = new WhaleForm(demo)) {
                    IntPtr handle = form.Handle;
                    using (var context = new ApplicationContext()) {
                        using (var stopTimer = new System.Windows.Forms.Timer { Interval = 500 }) {
                        stopTimer.Tick += delegate {
                            if (settingsSignal.WaitOne(0)) form.ShowSettings();
                            if (panelSignal.WaitOne(0)) form.ShowPanel();
                            if (openSignal.WaitOne(0)) form.BeginInvoke(new Action(async delegate { await form.OpenDsh(); }));
                            if (stop.WaitOne(0)) { Diagnostics.Log("Clean shutdown requested; DSH left running."); Application.Exit(); }
                        };
                        stopTimer.Start();
                        form.FormClosed += delegate { context.ExitThread(); };
                        form.BeginInvoke(new Action(async delegate { await form.Initialize(args.Contains("--settings"), args.Contains("--no-start"),args.Contains("--panel")); if(args.Contains("--open"))await form.OpenDsh(); }));
                        Diagnostics.Log("Tray worker running: " + Application.ExecutablePath);
                        Application.Run(context);
                        }
                    }
                }
            }
            return 0;
        }
    }
    // The tray panel. It opens against the taskbar next to the tray icon, closes on a second click, a click
    // elsewhere or Esc, and keeps the button layout: service toggle and restart, the open button, then the footer.
    public class WhaleForm : Form {
        class RowData { public string Title, Value, Health; public bool Emphasis; }
        readonly bool demo;
        readonly string settingsPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DSHWhale", "settings.json");
        const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        Settings settings;
        Service service;
        NotifyIcon tray;
        MenuItem openItem;
        System.Windows.Forms.Timer timer, clock;
        readonly List<StatusLine> rows = new List<StatusLine>();
        readonly SoftButton toggle, restart, open, configure, quit;
        Rectangle header, card, anchor;
        int separator = -1;
        bool headerHot, quitting, refreshing;
        // The service action under way ("toggle", "restart", "open", "quit"). The 20-second refresh never sets it,
        // so the buttons stay usable while it runs.
        string busy;
        string startupProblem;
        DateTime updatedAt, lastClose;
        SettingsForm settingsForm;
        readonly Icon trayIcon, appIcon;
        readonly Bitmap glyph;
        [DllImport("user32.dll")] static extern short GetAsyncKeyState(int key);
        [DllImport("shell32.dll")] static extern int Shell_NotifyIconGetRect(ref NotifyIconIdentifier identifier, out NativeRect rect);
        [StructLayout(LayoutKind.Sequential)] struct NotifyIconIdentifier { public int Size; public IntPtr Window; public uint Id; public Guid Item; }
        [StructLayout(LayoutKind.Sequential)] struct NativeRect { public int Left, Top, Right, Bottom; }
        public WhaleForm(bool demo) {
            this.demo = demo;
            if (!demo) MigrateSettings();
            // No modal dialog here: at login it would hold up the tray and DSH autostart until clicked.
            try { settings = demo ? new Settings() : Settings.Load(settingsPath); }
            catch (Exception e) { settings = new Settings(); startupProblem = "设置无法读取，将使用默认值。原文件保留，保存设置时会生成备份。\n" + e.Message; }
            // An empty WorkingDirectory starts DSH in the user folder, as the Mac app does.
            service = new Service(settings, Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), 3080);
            service.Notify = Notify; service.Open = OpenWeb;
            Text = "DSH 小鲸鱼"; Font = Style.Font(13, false); BackColor = Style.Back;
            AutoScaleMode = AutoScaleMode.None; FormBorderStyle = FormBorderStyle.None; ShowInTaskbar = false; TopMost = true; StartPosition = FormStartPosition.Manual;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            // A whale-tray.custom.ico / whale-app.custom.ico beside the exe wins over the packaged icons and survives updates.
            trayIcon = LoadIcon(SystemInformation.SmallIconSize, "whale-tray.custom.ico", "whale-tray.ico", "whale-windows.ico");
            appIcon = LoadIcon(SystemInformation.IconSize, "whale-app.custom.ico", "whale-windows.ico");
            Icon = appIcon;
            glyph = LoadGlyph(Style.P(20));
            toggle = AddButton(async delegate { await ToggleService(); });
            restart = AddButton(async delegate { await Act("restart", () => service.Restart()); });
            open = AddButton(async delegate { await OpenDsh(); });
            configure = AddButton(delegate { ShowSettings(); }); configure.Text = "设置…";
            quit = AddButton(async delegate { await Quit(); }); quit.Text = "退出小鲸鱼";
            foreach (var link in new [] { configure, quit }) { link.Link = true; link.Align = TextFormatFlags.Right; link.Font = Style.Font(12, false); }
            Deactivate += delegate { if (!demo && !TrayIconPressed()) HidePanel(); };
            FormClosing += delegate(object sender, FormClosingEventArgs e) { if (!quitting && e.CloseReason == CloseReason.UserClosing) { e.Cancel = true; HidePanel(); } };
            KeyPreview = true;
            KeyDown += delegate(object sender, KeyEventArgs e) {
                if (e.KeyCode == Keys.Escape) { HidePanel(); e.Handled = true; }
                else if (e.Control && e.KeyCode == Keys.Oemcomma) { ShowSettings(); e.Handled = true; }
            };
            if (demo) { service.Readings = DemoReadings(); service.Running = true; service.BalancesReachable = true; updatedAt = DateTime.Now; }
            Render();
        }
        // Builds before the rename kept settings under %LOCALAPPDATA%\DSHWhale-Vibe and used a
        // "DSHWhale-Vibe" Run value; carry both over once.
        void MigrateSettings() {
            string old = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DSHWhale-Vibe", "settings.json");
            try { if (!File.Exists(settingsPath) && File.Exists(old)) { Directory.CreateDirectory(Path.GetDirectoryName(settingsPath)); File.Copy(old, settingsPath); } }
            catch (Exception e) { Diagnostics.Log("Settings migration failed: " + e.Message); }
        }
        static void MigrateAutostart() {
            try {
                using (var key = Registry.CurrentUser.OpenSubKey(RunKey, true)) {
                    if (key == null) return;
                    bool old = key.GetValue("DSHWhale-Vibe") != null; object current = key.GetValue("DSHWhale");
                    if (!old && (current == null || Convert.ToString(current).Contains("--autostart"))) return;
                    key.SetValue("DSHWhale", Native.Quote(Application.ExecutablePath) + " --autostart");
                    if (old) key.DeleteValue("DSHWhale-Vibe", false);
                }
            } catch (Exception e) { Diagnostics.Log("Autostart migration failed: " + e.Message); }
        }
        public static Dictionary<string, Reading> DemoReadings() {
            return Balances.Parse("{\"ok\":true,\"providers\":{\"deepseek\":{\"ok\":true,\"label\":\"DeepSeek\",\"kind\":\"prepaid\",\"currency\":\"CNY\",\"remaining\":86.4},\"openrouter\":{\"ok\":true,\"label\":\"OpenRouter\",\"kind\":\"prepaid\",\"currency\":\"USD\",\"remaining\":12.5},\"codex\":{\"ok\":true,\"label\":\"ChatGPT 5h 余量\",\"kind\":\"quota\",\"currency\":\"%\",\"remaining\":72,\"limit\":100}}}");
        }
        static Icon LoadIcon(Size size, params string[] names) {
            foreach (string name in names) {
                string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, name);
                if (File.Exists(path)) try { return new Icon(path, size); } catch (Exception e) { Diagnostics.Log("Icon " + name + " unreadable: " + e.Message); }
            }
            try { return Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { return (Icon)SystemIcons.Application.Clone(); }
        }
        // The header whale, shrunk once to this screen's exact pixel size (see Style.Scaled).
        static Bitmap LoadGlyph(int size) {
            string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "whale.png");
            if (!File.Exists(path)) return null;
            try { using (var source = Image.FromFile(path)) return Style.Scaled(source, size); } catch { return null; }
        }
        SoftButton AddButton(EventHandler action) { var b = new SoftButton(); b.Click += action; Controls.Add(b); return b; }
        protected override CreateParams CreateParams { get { var p=base.CreateParams;p.ClassStyle|=0x20000;return p;} }
        protected override void OnHandleCreated(EventArgs e) { base.OnHandleCreated(e); Style.RoundCorners(Handle); }
        public async Task Initialize(bool showSettings, bool noStart, bool showPanel = false) {
            if (noStart) service.Stopped = true;
            if (!demo) MigrateAutostart();
            tray = new NotifyIcon { Icon = trayIcon, Text = "DSH 小鲸鱼", Visible = true, ContextMenu = BuildMenu() };
            tray.MouseClick += delegate(object sender, MouseEventArgs e) {
                if (e.Button != MouseButtons.Left) return;
                if (Visible) { HidePanel(); return; }
                // Deactivate already closed it for this very click: the taskbar took the focus on mouse down.
                if ((DateTime.UtcNow - lastClose).TotalMilliseconds < 500) return;
                ShowPanel(new Rectangle(Cursor.Position, new Size(1, 1)));
            };
            if (startupProblem != null) Notify(startupProblem);
            timer = new System.Windows.Forms.Timer { Interval = 20000 };
            timer.Tick += delegate { BeginRefresh(true); };
            timer.Start();
            // Keeps "N 秒前更新" current while the panel is open.
            clock = new System.Windows.Forms.Timer { Interval = 1000 };
            clock.Tick += delegate { if (Visible) Invalidate(FooterBounds()); };
            clock.Start();
            if (showSettings) ShowSettings();
            if (demo || showPanel) ShowPanel();
            if (!demo) await RefreshAll(true);
        }
        // Native menu: Windows 11 draws it with its own rounded style.
        ContextMenu BuildMenu() {
            var menu = new ContextMenu();
            openItem = new MenuItem("打开 DSH 界面", async delegate { await OpenDsh(); });
            menu.MenuItems.Add(openItem); menu.MenuItems.Add("-");
            menu.MenuItems.Add(new MenuItem("显示面板", delegate { ShowPanel(); }));
            menu.MenuItems.Add(new MenuItem("设置…", delegate { ShowSettings(); }));
            menu.MenuItems.Add("-");
            menu.MenuItems.Add(new MenuItem("退出小鲸鱼", async delegate { await Quit(); }));
            menu.Popup += delegate {
                HidePanel();
                openItem.Text = service.Running ? "打开 DSH 界面" : "启动并打开 DSH";
            };
            return menu;
        }
        internal void ShowPanel() { ShowPanel(Rectangle.Empty); }
        void ShowPanel(Rectangle click) {
            Render();
            Rectangle icon = TrayBounds();
            // A click in the hidden-icons flyout is reported where it happened; the icon's own rect is elsewhere then.
            anchor = !click.IsEmpty && (icon.IsEmpty || !Rectangle.Inflate(icon, Style.P(8), Style.P(8)).Contains(click.Location)) ? click : icon;
            Place();
            Show(); Activate();
            BeginRefresh(true);
        }
        void Place() {
            Screen screen = anchor.IsEmpty ? Screen.PrimaryScreen : Screen.FromRectangle(anchor);
            Rectangle work = screen.WorkingArea;
            // Without an icon position: the old spot, bottom right above the taskbar.
            Rectangle at = anchor.IsEmpty ? new Rectangle(work.Right - 1, work.Bottom, 1, 1) : anchor;
            Location = Placement.Compute(at, screen.Bounds, work, Size, Style.P(12));
        }
        void HidePanel() { if (!Visible) return; Hide(); lastClose = DateTime.UtcNow; }
        // The tray icon's rect on screen. NotifyIcon keeps its window and id private; Shell_NotifyIconGetRect needs both.
        Rectangle TrayBounds() {
            if (tray == null) return Rectangle.Empty;
            try {
                var flags = BindingFlags.NonPublic | BindingFlags.Instance;
                var window = typeof(NotifyIcon).GetField("window", flags).GetValue(tray) as NativeWindow;
                object id = typeof(NotifyIcon).GetField("id", flags).GetValue(tray);
                if (window == null || !(id is int)) return Rectangle.Empty;
                var identifier = new NotifyIconIdentifier { Size = Marshal.SizeOf(typeof(NotifyIconIdentifier)), Window = window.Handle, Id = (uint)(int)id };
                NativeRect r;
                if (Shell_NotifyIconGetRect(ref identifier, out r) != 0) return Rectangle.Empty;
                var bounds = Rectangle.FromLTRB(r.Left, r.Top, r.Right, r.Bottom);
                return bounds.Width > 0 && Screen.AllScreens.Any(s => s.Bounds.IntersectsWith(bounds)) ? bounds : Rectangle.Empty;
            } catch (Exception) { return Rectangle.Empty; }
        }
        // The tray icon is being clicked while the panel is open: MouseClick closes it, and closing it here
        // as well would make that same click open it again.
        bool TrayIconPressed() {
            if ((GetAsyncKeyState(0x01) & 0x8000) == 0) return false;
            Rectangle icon = TrayBounds();
            return !icon.IsEmpty && icon.Contains(Cursor.Position);
        }
        string LocalStatus(bool manage) {
            if (service.Running) return "运行中";
            if (!manage) return service.DesktopOnly ? "桌面版管理" : "未运行";
            return service.Stopped ? "已停止" : service.Failures >= 3 ? "启动失败" : "未运行";
        }
        void Render() {
            bool manage = demo || service.Manages;
            var data = new List<RowData>();
            data.Add(new RowData { Title = "DSH 后台服务", Value = LocalStatus(manage), Health = service.Running ? "green" : manage ? "red" : "yellow" });
            if (settings.ShowClawbot) { bool logged = demo || service.Clawbot; data.Add(new RowData { Title = "微信 Clawbot", Value = logged ? "已登录" : "未登录", Health = logged ? "green" : "red" }); }
            int hosts = data.Count;
            var readings = service.Readings;
            foreach (var s in Balances.Layout(settings.Sources, readings).Where(x => x.Visible)) {
                Reading r; readings.TryGetValue(s.Id, out r);
                data.Add(new RowData { Title = String.IsNullOrWhiteSpace(s.Title) ? (r == null ? s.Id : r.DefaultTitle) : s.Title.Trim(), Value = r == null ? "—" : r.Text, Health = r == null ? "red" : r.Health, Emphasis = true });
            }
            while (rows.Count < data.Count) { var line = new StatusLine(); rows.Add(line); Controls.Add(line); }
            while (rows.Count > data.Count) { var line = rows[rows.Count - 1]; rows.RemoveAt(rows.Count - 1); Controls.Remove(line); line.Dispose(); }
            // Same grid as before: 14 margins, two 126-wide columns 8 apart.
            int width = Style.P(288), margin = Style.P(14), column = Style.P(126), buttonHeight = Style.P(28), rowHeight = Style.P(30);
            header = new Rectangle(Style.P(8), Style.P(8), width - Style.P(16), Style.P(36));
            // Rows sit inside the card's rounded corners, clear of its outline.
            int top = header.Bottom + Style.P(6), y = top + Style.P(6);
            separator = -1;
            for (int i = 0; i < data.Count; i++) {
                if (i == hosts && i > 0) { separator = y + Style.P(4); y += Style.P(9); }
                rows[i].Set(data[i].Title, data[i].Value, data[i].Health, data[i].Emphasis);
                rows[i].Bounds = new Rectangle(margin + Style.P(3), y, width - 2 * margin - Style.P(6), rowHeight);
                y += rowHeight;
            }
            card = new Rectangle(margin, top, width - 2 * margin, y + Style.P(6) - top);
            y = card.Bottom + Style.P(12);
            toggle.Bounds = new Rectangle(margin, y, column, buttonHeight);
            restart.Bounds = new Rectangle(width - margin - column, y, column, buttonHeight);
            y += buttonHeight + Style.P(8);
            open.Bounds = new Rectangle(margin, y, width - 2 * margin, buttonHeight);
            y += buttonHeight + Style.P(10);
            int linkHeight = Style.P(20), quitWidth = TextRenderer.MeasureText(quit.Text, quit.Font).Width + Style.P(6), configureWidth = TextRenderer.MeasureText(configure.Text, configure.Font).Width + Style.P(6);
            quit.Bounds = new Rectangle(width - margin - quitWidth, y, quitWidth, linkHeight);
            configure.Bounds = new Rectangle(quit.Left - Style.P(10) - configureWidth, y, configureWidth, linkHeight);
            toggle.Text = busy == "toggle" ? (service.Running ? "正在停止…" : "正在启动…") : service.Running ? "关闭服务" : "开启服务";
            toggle.Enabled = busy == null && manage;
            toggle.Primary = busy == null && manage && !service.Running; toggle.Invalidate();
            restart.Text = busy == "restart" ? "正在重启…" : "重启服务";
            restart.Enabled = busy == null && manage && service.Running;
            open.Text = busy == "open" ? "正在连接 DSH…" : service.Running ? "打开 DSH 界面" : manage ? "启动并打开 DSH" : service.DesktopOnly ? "请打开 DSH 桌面版" : "DSH 未运行";
            open.Enabled = busy == null && (manage || service.Running);
            var size = new Size(width, y + linkHeight + Style.P(10));
            if (ClientSize != size) { ClientSize = size; if (Visible) Place(); }
            if (tray != null) tray.Text = TrayText();
            if (settingsForm != null && !settingsForm.IsDisposed) settingsForm.UpdateReadings(readings, service.BalancesReachable, service.BalanceProblem);
            Invalidate();
        }
        string TrayText() { return "DSH 小鲸鱼 · " + (service.Running ? "运行中" : !(demo || service.Manages) && service.DesktopOnly ? "桌面版管理" : "未运行"); }
        Rectangle FooterBounds() { return new Rectangle(Style.P(16), configure.Top, Math.Max(0, configure.Left - Style.P(24)), configure.Height); }
        string FooterText() {
            if (!demo && updatedAt == default(DateTime)) return "正在检查…";
            int seconds = (int)(DateTime.Now - updatedAt).TotalSeconds;
            return demo || seconds < 5 ? "刚刚更新" : seconds < 60 ? seconds + " 秒前更新" : seconds / 60 + " 分钟前更新";
        }
        protected override void OnPaint(PaintEventArgs e) {
            var g = e.Graphics;
            g.Clear(Style.Back); g.SmoothingMode = SmoothingMode.AntiAlias;
            if (headerHot) using (var path = Style.Round(header, Style.P(6))) using (var b = new SolidBrush(Style.Hover)) g.FillPath(b, path);
            int textLeft = header.Left + Style.P(8);
            if (glyph != null) { g.DrawImageUnscaled(glyph, textLeft, header.Top + (header.Height - glyph.Height) / 2); textLeft += glyph.Width + Style.P(8); }
            using (var f = Style.Font(14, true)) TextRenderer.DrawText(g, "DSH 小鲸鱼", f, new Rectangle(textLeft, header.Top, header.Right - textLeft, header.Height), Style.Text, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            using (var path = Style.Round(new RectangleF(card.X + .5f, card.Y + .5f, card.Width - 1, card.Height - 1), Style.P(8))) {
                using (var b = new SolidBrush(Style.Card)) g.FillPath(b, path);
                using (var p = new Pen(Style.CardEdge)) g.DrawPath(p, path);
            }
            if (separator >= 0) using (var p = new Pen(Style.Line)) g.DrawLine(p, card.Left + Style.P(12), separator, card.Right - Style.P(12), separator);
            using (var f = Style.Font(12, false)) TextRenderer.DrawText(g, FooterText(), f, FooterBounds(), Style.Muted, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);
            // Outline for Windows 10 and screenshots; on Windows 11 it lies under the rounded border DWM draws.
            using (var p = new Pen(Style.Edge)) {
                if (Style.Win11) using (var path = Style.Round(new RectangleF(.5f, .5f, Width - 1, Height - 1), Style.P(8))) g.DrawPath(p, path);
                else g.DrawRectangle(p, 0, 0, Width - 1, Height - 1);
            }
        }
        // The header opens this computer's DSH, like the Mac panel's header.
        protected override void OnMouseMove(MouseEventArgs e) { base.OnMouseMove(e); SetHeaderHot(header.Contains(e.Location)); }
        protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); SetHeaderHot(false); }
        protected override void OnMouseClick(MouseEventArgs e) { base.OnMouseClick(e); if (e.Button == MouseButtons.Left && header.Contains(e.Location)) { var ignored = OpenDsh(); } }
        void SetHeaderHot(bool hot) { if (hot == headerHot) return; headerHot = hot; Invalidate(header); }
        void BeginRefresh(bool supervise) { var ignored = RefreshAll(supervise); }
        async Task RefreshAll(bool supervise) {
            if (refreshing || demo) return;
            refreshing = true;
            try { await service.Tick(supervise); updatedAt = DateTime.Now; }
            catch (Exception e) { Diagnostics.Log("Refresh failed: " + e.Message); }
            finally { refreshing = false; if (!IsDisposed) Render(); }
        }
        async Task Act(string what, Func<Task> action) {
            if (busy != null) return;
            busy = what; Render();
            try { await action(); updatedAt = DateTime.Now; }
            catch (Exception e) { Notify(Service.Redact(e.Message)); }
            finally { busy = null; if (!IsDisposed) Render(); }
        }
        // Starting only starts; the open buttons are there to open a window.
        async Task ToggleService() { await Act("toggle", async () => { if (service.Running) await service.Stop(); else await service.Start(false); }); }
        void Notify(string text) {
            Diagnostics.Log(text);
            if (tray != null) tray.ShowBalloonTip(8000, "DSH 小鲸鱼", text.Length > 250 ? text.Substring(0, 250) : text, ToolTipIcon.Info);
        }
        void OpenWeb() {
            if (demo) return;
            try { Browser.Open(service.WebUrl(), settings.AppWindow); } catch (Exception e) { Notify("无法打开浏览器：" + e.Message); }
        }
        internal void ShowSettings() {
            HidePanel();
            if (settingsForm != null && !settingsForm.IsDisposed) { settingsForm.Show(); settingsForm.Activate(); return; }
            settingsForm = new SettingsForm(settings, service.Readings, demo) { Icon = appIcon };
            settingsForm.UpdateReadings(service.Readings, service.BalancesReachable, service.BalanceProblem);
            settingsForm.Saved += delegate(Settings s) {
                if (!demo) s.Save(settingsPath);
                settings = s; service.Settings = s; Render();
            };
            settingsForm.Show();
        }
        internal async Task OpenDsh() {
            if(demo)return;
            // Already listening: open at once. Only a stopped DSH takes the start path, whose refresh waits on the balance lookups.
            bool up; try { up = Native.Listeners(service.Port).Count > 0; } catch (Exception) { up = false; }
            if (up) { OpenWeb(); HidePanel(); return; }
            while(busy!=null&&!IsDisposed)await Task.Delay(100);
            if(!IsDisposed)await Act("open", ()=>service.Start(true));
        }
        async Task Quit() {
            if (busy != null) { Notify("服务操作正在进行，请稍后退出。"); return; }
            HidePanel();
            // Nothing to ask when the tray only watches DSH or it is not running.
            QuitChoice choice = demo || (service.Manages && service.Running) ? QuitPrompt.Ask(appIcon) : QuitChoice.KeepService;
            if (choice == QuitChoice.Cancel) return;
            if (choice == QuitChoice.StopService && !demo) {
                busy = "quit"; try { await service.Stop(); } catch (Exception e) { MessageBox.Show(e.Message, "停止失败，未退出"); return; } finally { busy = null; }
            }
            quitting = true; if (timer != null) timer.Stop(); if (clock != null) clock.Stop(); if (tray != null) tray.Visible = false; Application.Exit();
        }
        public void Snapshot(string directory) {
            Directory.CreateDirectory(directory);
            Location=new Point(-20000,-20000);Show(); PerformLayout(); Application.DoEvents();
            using (var bitmap = new Bitmap(Width, Height)) { DrawToBitmap(bitmap, new Rectangle(0, 0, Width, Height)); bitmap.Save(Path.Combine(directory, "panel.png")); }
            using (var f = new SettingsForm(settings, service.Readings, true)) {
                f.StartPosition=FormStartPosition.Manual;f.Location=new Point(-20000,-20000);f.Show(); f.PerformLayout(); Application.DoEvents();
                using (var bitmap = new Bitmap(f.Width, f.Height)) { f.DrawToBitmap(bitmap, new Rectangle(0, 0, f.Width, f.Height)); bitmap.Save(Path.Combine(directory, "settings.png")); }
            }
            Hide();
        }
        protected override void Dispose(bool disposing) {
            if (disposing) { if (timer != null) timer.Dispose(); if (clock != null) clock.Dispose(); if (tray != null) tray.Dispose(); if (settingsForm != null) settingsForm.Dispose(); service.Dispose(); if (glyph != null) glyph.Dispose(); }
            base.Dispose(disposing);
            if (disposing) { if (trayIcon != null) trayIcon.Dispose(); if (appIcon != null) appIcon.Dispose(); }
        }
    }
}
