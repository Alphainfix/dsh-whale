using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace DshWhale {
    public static class Diagnostics {
        static readonly object sync = new object();
        public static readonly string DirectoryPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DSHWhale-Vibe", "logs");
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
        public static readonly string SettingsEvent = "Local\\DSHWhale.Vibe.Settings." + Environment.UserName;
        public static readonly string OpenEvent = "Local\\DSHWhale.Vibe.Open." + Environment.UserName;
        public static readonly string PanelEvent = "Local\\DSHWhale.Vibe.Panel." + Environment.UserName;
        // A clean UI exit is final. Only unexpected process exits are retried, with a hard cap.
        public static int Supervise(string[] args) {
            bool created;
            using (var mutex = new Mutex(true, "Local\\DSHWhale.Supervisor." + Environment.UserName, out created)) {
                if (!created) {
                    if(args.Contains("--open") || args.Contains("--panel")) {
                        for(int attempt=0;attempt<40;attempt++) {
                            try {using(var signal=EventWaitHandle.OpenExisting(args.Contains("--open")?OpenEvent:PanelEvent))signal.Set();break;}
                            catch(WaitHandleCannotBeOpenedException){Thread.Sleep(250);}
                        }
                    }
                    if (args.Contains("--settings")) try { using (var signal = EventWaitHandle.OpenExisting(SettingsEvent)) signal.Set(); } catch (WaitHandleCannotBeOpenedException) { }
                    return 0;
                }
                int failures = 0;
                while (true) {
                    DateTime started = DateTime.UtcNow;
                    string options = "--worker" + (args.Contains("--settings") ? " --settings" : "") + (args.Contains("--no-start") ? " --no-start" : "") + (args.Contains("--open") && failures==0 ? " --open" : "");
                    if(args.Contains("--panel") && failures==0) options+=" --panel";
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
