using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Diagnostics;
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
    public class WhaleForm : Form {
        readonly bool demo;
        readonly string settingsPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DSHWhale-Vibe", "settings.json");
        Settings settings;
        Service service;
        NotifyIcon tray;
        System.Windows.Forms.Timer timer;
        FlowLayoutPanel rows;
        Label updated;
        Button toggle, restart, open, configure, quit;
        Panel bottomLine;
        DateTime updatedAt;
        bool quitting, busy;
        DateTime lastClose;
        SettingsForm settingsForm;
        Icon whaleIcon;
        public WhaleForm(bool demo) {
            this.demo = demo;
            try { settings = demo ? new Settings() : Settings.Load(settingsPath); }
            catch (Exception e) { settings = new Settings(); MessageBox.Show("设置无法读取，将使用默认值。原文件保留，保存设置时会生成备份。\n" + e.Message, "DSH 小鲸鱼"); }
            string userWorkspace = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "deepseek-harness", "default-workspace");
            if (!File.Exists(settingsPath) && Directory.Exists(userWorkspace)) settings.WorkingDirectory = userWorkspace;
            service = new Service(settings, Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), 3080);
            service.Notify = Notify; service.Open = OpenWeb;
            Text = "DSH 小鲸鱼 · Vibe"; Font = Style.Font(13,false); BackColor = Color.White;
            AutoScaleMode=AutoScaleMode.None; FormBorderStyle=FormBorderStyle.None; ShowInTaskbar=false; TopMost=true;
            ClientSize=new Size(Style.P(288),Style.P(289));
            var header=new Panel { Bounds=new Rectangle(Style.P(8),Style.P(8),Style.P(272),Style.P(30)),Cursor=Cursors.Hand };
            header.Click+=delegate{OpenWeb();}; Controls.Add(header);
            string glyph=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"whale.png");
            if(File.Exists(glyph)) {var picture=new PictureBox{Image=Image.FromFile(glyph),SizeMode=PictureBoxSizeMode.Zoom,Bounds=new Rectangle(Style.P(6),Style.P(5),Style.P(19),Style.P(19))};picture.Click+=delegate{OpenWeb();};header.Controls.Add(picture);}
            var title=Style.Label("DSH 小鲸鱼",32,4,210,24,13,true,Color.Black);title.Click+=delegate{OpenWeb();};header.Controls.Add(title);
            Controls.Add(new Panel { Bounds=new Rectangle(Style.P(14),Style.P(46),Style.P(260),1),BackColor=Color.FromArgb(225,225,225) });
            rows=new FlowLayoutPanel { Bounds=new Rectangle(Style.P(14),Style.P(56),Style.P(260),Style.P(126)),FlowDirection=FlowDirection.TopDown,WrapContents=false,AutoScroll=true };Controls.Add(rows);
            bottomLine=new Panel { Height=1,Left=Style.P(14),Width=Style.P(260),BackColor=Color.FromArgb(225,225,225) };Controls.Add(bottomLine);
            toggle=ButtonAt("开启服务",14,205,126,async delegate{await Act(async()=>{if(service.Running)await service.Stop();else await service.Start(true);});});
            restart=ButtonAt("重启服务",148,205,126,async delegate{await Act(()=>service.Restart());});
            open=ButtonAt("启动并打开 DSH",14,233,260,async delegate{await OpenDsh();});
            configure=ButtonAt("设置…",174,264,37,delegate{ShowSettings();});((SoftButton)configure).Link=true;configure.Font=Style.Font(10,false);
            quit=ButtonAt("退出小鲸鱼",215,264,59,async delegate{await Quit();});((SoftButton)quit).Link=true;quit.Font=Style.Font(10,false);
            updated=Style.Label("",14,264,150,16,10,false,Color.FromArgb(160,160,160));Controls.Add(updated);
            whaleIcon = MakeIcon(); Icon = whaleIcon;
            Deactivate += delegate { if (!demo && !busy) { Hide(); lastClose = DateTime.UtcNow; } };
            FormClosing += delegate(object sender, FormClosingEventArgs e) { if (!quitting && e.CloseReason == CloseReason.UserClosing) { e.Cancel = true; Hide(); } };
            if (demo) { service.Readings = DemoReadings(); service.Running = true; service.BalancesReachable = true; }
            KeyPreview = true; KeyDown += delegate(object sender, KeyEventArgs e) { if (e.Control && e.KeyCode == Keys.Oemcomma) { ShowSettings(); e.Handled = true; } };
            Render();
        }
        public static Dictionary<string, Reading> DemoReadings() {
            return Balances.Parse("{\"ok\":true,\"providers\":{\"deepseek\":{\"ok\":true,\"label\":\"DeepSeek\",\"kind\":\"prepaid\",\"currency\":\"CNY\",\"remaining\":86.4},\"openrouter\":{\"ok\":true,\"label\":\"OpenRouter\",\"kind\":\"prepaid\",\"currency\":\"USD\",\"remaining\":12.5},\"codex\":{\"ok\":true,\"label\":\"ChatGPT 5h 余量\",\"kind\":\"quota\",\"currency\":\"%\",\"remaining\":72,\"limit\":100}}}");
        }
        Icon MakeIcon() {
            string appIcon = Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"whale-windows.ico");
            if(File.Exists(appIcon))return new Icon(appIcon,32,32);
            string resource = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "whale.png");
            if (!File.Exists(resource)) return (Icon)SystemIcons.Application.Clone();
            using (var original = new Bitmap(resource)) using (var bitmap = new Bitmap(32, 32)) {
                using (var g = Graphics.FromImage(bitmap)) { g.Clear(Color.FromArgb(61, 132, 246)); g.DrawImage(original, 3, 3, 26, 26); }
                IntPtr h = bitmap.GetHicon(); try { using (var borrowed = Icon.FromHandle(h)) return (Icon)borrowed.Clone(); } finally { DestroyIcon(h); }
            }
        }
        [System.Runtime.InteropServices.DllImport("user32.dll")] static extern bool DestroyIcon(IntPtr handle);
        Button ButtonAt(string text, int x, int y, int width, EventHandler action) {
            var b=new SoftButton { Text=text,Bounds=new Rectangle(Style.P(x),Style.P(y),Style.P(width),Style.P(22)) };
            b.Click+=action;Controls.Add(b);return b;
        }
        protected override CreateParams CreateParams { get { var p=base.CreateParams;p.ClassStyle|=0x20000;return p;} }
        protected override void OnSizeChanged(EventArgs e) { base.OnSizeChanged(e); if(Width>24&&Height>24)using(var path=Style.Round(new RectangleF(0,0,Width,Height),Style.P(12))){var previous=Region;Region=new Region(path);if(previous!=null)previous.Dispose();} }
        public async Task Initialize(bool showSettings, bool noStart, bool showPanel = false) {
            if (noStart) service.Stopped = true;
            tray = new NotifyIcon { Icon = whaleIcon, Text = "DSH 小鲸鱼 · Vibe", Visible = true };
            tray.MouseClick += async delegate(object sender, MouseEventArgs e) {
                if (e.Button != MouseButtons.Left) return;
                if (Visible) { Hide(); return; } if ((DateTime.UtcNow - lastClose).TotalMilliseconds < 250) return;
                ShowPanel(); if (!demo) await Act(() => service.Tick(false));
            };
            var menu = new ContextMenuStrip();
            menu.Items.Add("启动并打开 DSH", null, async delegate { await OpenDsh(); });
            menu.Items.Add("显示面板", null, delegate { ShowPanel(); });
            menu.Items.Add("设置…", null, delegate { ShowSettings(); });
            menu.Items.Add("登录 ChatGPT…", null, delegate { OpenChatGPT(); });
            menu.Items.Add("退出…", null, async delegate { await Quit(); }); tray.ContextMenuStrip = menu;
            if (demo || showPanel) ShowPanel(); else Hide();
            timer = new System.Windows.Forms.Timer { Interval = 20000 };
            timer.Tick += async delegate { if (!demo) await Act(() => service.Tick(true)); };
            timer.Start();
            if (showSettings) ShowSettings();
            if (!demo) await Act(() => service.Tick(true));
        }
        internal void ShowPanel() {
            Rectangle area = Screen.FromPoint(Cursor.Position).WorkingArea;
            Location = new Point(Math.Max(area.Left, area.Right - Width - 12), Math.Max(area.Top, area.Bottom - Height - 12));
            Render(); Show(); Activate();
        }
        void AddRow(string title,string value,string health,bool emphasis) { rows.Controls.Add(new StatusLine(title,value,health,emphasis)); }
        void Render() {
            rows.SuspendLayout();foreach(Control c in rows.Controls.Cast<Control>().ToArray())c.Dispose();rows.Controls.Clear();
            AddRow("DSH 后台服务",service.Running?"运行中":"未运行",service.Running?"green":"red",false);
            if(settings.ShowClawbot){bool logged=demo||service.Clawbot;AddRow("微信 Clawbot",logged?"已登录":"未登录",logged?"green":"red",false);}
            foreach(var s in Balances.Layout(settings.Sources,service.Readings).Where(x=>x.Visible)) {Reading r;service.Readings.TryGetValue(s.Id,out r);AddRow(String.IsNullOrWhiteSpace(s.Title)?r==null?s.Id:r.DefaultTitle:s.Title.Trim(),r==null?"—":r.Text,r==null?"red":r.Health,true);}
            if(rows.Controls.Count>0) rows.Controls[rows.Controls.Count-1].Margin=Padding.Empty;
            int rowHeight=rows.Controls.Cast<Control>().Sum(c=>c.Height+c.Margin.Vertical);
            rows.AutoScroll=rowHeight>Style.P(330);rows.Height=Math.Min(Style.P(330),rowHeight);
            foreach(Control row in rows.Controls)row.Width=rows.Width-(rows.AutoScroll?SystemInformation.VerticalScrollBarWidth:0);
            rows.ResumeLayout();
            bottomLine.Top=rows.Bottom+Style.P(11);toggle.Top=restart.Top=bottomLine.Bottom+Style.P(10);open.Top=toggle.Bottom+Style.P(7);
            updated.Top=configure.Top=quit.Top=open.Bottom+Style.P(9);configure.Height=quit.Height=Style.P(16);ClientSize=new Size(Style.P(288),quit.Bottom+Style.P(11));
            toggle.Text=service.Running?"关闭服务":busy?"正在启动…":"开启服务";restart.Enabled=!busy&&service.Running;
            toggle.Enabled=open.Enabled=!busy;configure.Enabled=true;open.Text=busy?"正在连接 DSH…":service.Running?"打开 DSH 界面":"启动并打开 DSH";((SoftButton)toggle).Primary=!service.Running&&!busy;toggle.Invalidate();AcceptButton=service.Running?null:toggle;
            int seconds=updatedAt==default(DateTime)?0:(int)(DateTime.Now-updatedAt).TotalSeconds;updated.Text=demo||seconds<5?"刚刚更新":seconds+" 秒前更新";
            if(tray!=null)tray.Text="DSH 小鲸鱼 · Vibe · "+(service.Running?"运行中":"未运行");
            if(settingsForm!=null&&!settingsForm.IsDisposed)settingsForm.UpdateReadings(service.Readings,service.BalancesReachable,service.BalanceProblem);
        }
        async Task Act(Func<Task> action) {
            if (busy) return;
            busy = true; Render();
            try { await action(); updatedAt=DateTime.Now; }
            catch (Exception e) { Notify(Service.Redact(e.Message)); }
            finally { busy = false; if (!IsDisposed) Render(); }
        }
        void Notify(string text) {
            Diagnostics.Log(text);
            if (tray != null) tray.ShowBalloonTip(8000, "DSH 小鲸鱼", text.Length > 250 ? text.Substring(0, 250) : text, ToolTipIcon.Info);
        }
        void OpenWeb() {
            if (demo) return;
            string url = service.WebUrl();
            if (settings.AppWindow) {
                string edge = new [] { Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Microsoft", "Edge", "Application", "msedge.exe"), Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Microsoft", "Edge", "Application", "msedge.exe") }.FirstOrDefault(File.Exists);
                if (edge != null) { Process.Start(new ProcessStartInfo(edge, "--app=" + Native.Quote(url)) { UseShellExecute = false }); return; }
            }
            try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); } catch (Exception e) { Notify("无法打开浏览器：" + e.Message); }
        }
        internal void ShowSettings() {
            if (settingsForm != null && !settingsForm.IsDisposed) { settingsForm.Show(); settingsForm.Activate(); return; }
            Hide(); settingsForm = new SettingsForm(settings, service.Readings, demo);
            settingsForm.LoginRequested += OpenChatGPT;
            settingsForm.UpdateReadings(service.Readings, service.BalancesReachable, service.BalanceProblem);
            settingsForm.Saved += delegate(Settings s) {
                if (!demo) s.Save(settingsPath);
                settings = s; service.Settings = s; Render();
            };
            settingsForm.Show();
        }
        internal async Task OpenDsh() {
            if(demo)return;
            while(busy&&!IsDisposed)await Task.Delay(100);
            if(!IsDisposed)await Act(()=>service.Start(true));
        }
        void OpenChatGPT() {
            if(demo)return;
            if(!service.Running){Notify("请先开启 DSH 服务。");return;}
            try {Process.Start(new ProcessStartInfo("http://127.0.0.1:3080/api/whale/chatgpt"){UseShellExecute=true});}
            catch(Exception e){Notify("无法打开登录页面："+e.Message);}
        }
        async Task Quit() {
            if (busy) { Notify("服务操作正在进行，请稍后退出。"); return; }
            var result = MessageBox.Show("是否同时停止 DSH 服务？\n\n是：退出并停止服务\n否：退出但保留服务\n取消：继续运行小鲸鱼", "退出小鲸鱼", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
            if (result == DialogResult.Cancel) return;
            if (result == DialogResult.Yes && !demo) {
                busy = true; try { await service.Stop(); } catch (Exception e) { MessageBox.Show(e.Message, "停止失败，未退出"); return; } finally { busy = false; }
            }
            quitting = true; if (timer != null) timer.Stop(); if (tray != null) tray.Visible = false; Application.Exit();
        }
        public void Snapshot(string directory) {
            Directory.CreateDirectory(directory);
            StartPosition=FormStartPosition.Manual;Location=new Point(-20000,-20000);Show(); PerformLayout(); Application.DoEvents();
            using (var bitmap = new Bitmap(Width, Height)) { DrawToBitmap(bitmap, new Rectangle(0, 0, Width, Height)); bitmap.Save(Path.Combine(directory, "panel.png")); }
            using (var f = new SettingsForm(settings, service.Readings, true)) {
                f.StartPosition=FormStartPosition.Manual;f.Location=new Point(-20000,-20000);f.Show(); f.PerformLayout(); Application.DoEvents();
                using (var bitmap = new Bitmap(f.Width, f.Height)) { f.DrawToBitmap(bitmap, new Rectangle(0, 0, f.Width, f.Height)); bitmap.Save(Path.Combine(directory, "settings.png")); }
            }
            Hide();
        }
        protected override void Dispose(bool disposing) {
            if (disposing) { if (timer != null) timer.Dispose(); if (tray != null) tray.Dispose(); if (settingsForm != null) settingsForm.Dispose(); service.Dispose(); if (whaleIcon != null) whaleIcon.Dispose(); }
            base.Dispose(disposing);
        }
    }
}
