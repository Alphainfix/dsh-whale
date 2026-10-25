using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace DshWhale {
    public static class Diagnostics {
        static readonly object sync = new object();
        public static readonly string DirectoryPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DSHWhale", "logs");
        public static void Log(string message) {
            try {
                lock (sync) {
                    Directory.CreateDirectory(DirectoryPath);
                    string file = Path.Combine(DirectoryPath, "whale.log");
                    if (File.Exists(file) && new FileInfo(file).Length > 2 * 1024 * 1024) { string previous = file + ".previous"; if (File.Exists(previous)) File.Delete(previous); File.Move(file, previous); }
                    File.AppendAllText(file, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff") + " [" + Process.GetCurrentProcess().Id + "] " + Service.Redact(message) + Environment.NewLine, Encoding.UTF8);
                }
            } catch { }
        }
    }
    public static class Lifecycle {
        public static readonly string SettingsEvent = "Local\\DSHWhale.Settings." + Environment.UserName;
        public static readonly string OpenEvent = "Local\\DSHWhale.Open." + Environment.UserName;
        public static readonly string PanelEvent = "Local\\DSHWhale.Panel." + Environment.UserName;
        [DllImport("user32.dll")] static extern bool AllowSetForegroundWindow(int processId);
        // A plain launch (double-click) shows the panel; the login entry passes --autostart and stays in the tray.
        static bool WantsPanel(string[] args) {
            return args.Contains("--panel") || !(args.Contains("--autostart") || args.Contains("--open") || args.Contains("--settings") || args.Contains("--no-start"));
        }
        // The running tray may still be starting up, so keep trying for a few seconds.
        static void Forward(string name) {
            for(int attempt=0;attempt<40;attempt++) {
                try {using(var signal=EventWaitHandle.OpenExisting(name))signal.Set();return;}
                catch(WaitHandleCannotBeOpenedException){Thread.Sleep(250);}
            }
        }
        // A clean UI exit is final. Only unexpected process exits are retried, with a hard cap.
        // The supervisor mutex keeps its pre-rename name, so a DSHWhale-Vibe build and this one never both supervise DSH.
        public static int Supervise(string[] args) {
            bool created; bool panel = WantsPanel(args);
            using (var mutex = new Mutex(true, "Local\\DSHWhale.Supervisor." + Environment.UserName, out created)) {
                if (!created) {
                    // This launch got the foreground from the user's click; hand it on, or the running tray's panel opens behind other windows.
                    AllowSetForegroundWindow(-1);
                    if (args.Contains("--open")) Forward(OpenEvent); else if (panel) Forward(PanelEvent);
                    if (args.Contains("--settings")) Forward(SettingsEvent);
                    return 0;
                }
                int failures = 0;
                while (true) {
                    DateTime started = DateTime.UtcNow;
                    string options = "--worker" + (args.Contains("--settings") ? " --settings" : "") + (args.Contains("--no-start") ? " --no-start" : "") + (args.Contains("--open") && failures==0 ? " --open" : "");
                    if(panel && failures==0) options+=" --panel";
                    Diagnostics.Log("Starting tray worker, attempt " + (failures + 1));
                    int code;
                    using (var child = Process.Start(new ProcessStartInfo(Application.ExecutablePath, options) { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = AppDomain.CurrentDomain.BaseDirectory })) { child.WaitForExit(); code = child.ExitCode; }
                    Diagnostics.Log("Tray worker exited, code=" + code);
                    if (code == 0) return 0;
                    if ((DateTime.UtcNow - started).TotalMinutes >= 10) failures = 0;
                    failures++;
                    if (failures >= 3) {
                        Diagnostics.Log("Tray crash circuit breaker opened; no further restarts.");
                        MessageBox.Show("小鲸鱼连续异常退出 3 次，已停止自动重启。\n诊断日志：\n" + Diagnostics.DirectoryPath, "DSH 小鲸鱼", MessageBoxButtons.OK, MessageBoxIcon.Error);
                        return code;
                    }
                    Thread.Sleep(failures == 1 ? 3000 : 10000);
                }
            }
        }
    }
}
