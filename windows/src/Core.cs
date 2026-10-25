// Windows port of DSH Whale. Original project copyright (c) 2026 Alphainfix, MIT.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Management;
using System.Net;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace DshWhale {
    public class SourceSetting {
        public string Id { get; set; }
        public string Title { get; set; }
        public bool Visible { get; set; }
    }
    public class Settings {
        public List<SourceSetting> Sources { get; set; }
        public bool ShowClawbot { get; set; }
        public bool FullAccess { get; set; }
        public bool AppWindow { get; set; }
        public string NodePath { get; set; }
        public string DshPath { get; set; }
        public string WorkingDirectory { get; set; }
        public Settings() { ShowClawbot = true; NodePath = ""; DshPath = ""; WorkingDirectory = ""; }
        public static Settings Load(string path) {
            if (!File.Exists(path)) return new Settings();
            return new JavaScriptSerializer().Deserialize<Settings>(File.ReadAllText(path, Encoding.UTF8)) ?? new Settings();
        }
        public void Save(string path) {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            string temp = path + ".tmp";
            File.WriteAllText(temp, new JavaScriptSerializer().Serialize(this), Encoding.UTF8);
            if (File.Exists(path)) File.Replace(temp, path, path + ".bak"); else File.Move(temp, path);
        }
    }
    public class Reading {
        public string Id, Label, Kind, Currency, Error;
        public double? Remaining, Limit;
        public string Text {
            get {
                if (!Remaining.HasValue) return "—";
                if (Currency == "%") return Remaining.Value.ToString("F0", CultureInfo.InvariantCulture) + "%";
                return (Currency == "CNY" ? "¥" : Currency == "USD" ? "$" : String.IsNullOrEmpty(Currency) ? "" : Currency + " ") + Remaining.Value.ToString("F2", CultureInfo.InvariantCulture);
            }
        }
        public string Health {
            get {
                if (!Remaining.HasValue) return "red";
                double v = Remaining.Value;
                if (Limit.HasValue && Limit.Value > 0) { v /= Limit.Value; return v < .1 ? "red" : v < .25 ? "yellow" : "green"; }
                v *= Currency == "USD" ? 7 : 1;
                return v < 5 ? "red" : v < 20 ? "yellow" : "green";
            }
        }
        public string DefaultTitle {
            get { return new [] { "余额", "余量", "额度" }.Any(x => Label.Contains(x)) ? Label : Label + (Kind == "quota" ? " 额度" : " 余额"); }
        }
    }
    public static class Balances {
        static string Str(Dictionary<string, object> o, string k, string fallback) { object v; return o.TryGetValue(k, out v) && v is string ? (string)v : fallback; }
        static double? Number(Dictionary<string, object> o, string k) {
            object v; if (!o.TryGetValue(k, out v) || v == null || v is bool || v is string) return null;
            try { double d = Convert.ToDouble(v, CultureInfo.InvariantCulture); return Double.IsNaN(d) || Double.IsInfinity(d) ? (double?)null : d; } catch { return null; }
        }
        public static Dictionary<string, Reading> Parse(string json) {
            var root = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(json);
            object ok, raw;
            if (root == null || !root.TryGetValue("ok", out ok) || !Object.Equals(ok, true) || !root.TryGetValue("providers", out raw) || !(raw is Dictionary<string, object>)) throw new FormatException("余额接口不可用");
            var result = new Dictionary<string, Reading>();
            foreach (var item in (Dictionary<string, object>)raw) {
                var o = item.Value as Dictionary<string, object> ?? new Dictionary<string, object>();
                result[item.Key] = new Reading { Id = item.Key, Label = Str(o, "label", item.Key), Kind = Str(o, "kind", ""), Currency = Str(o, "currency", ""), Error = Str(o,"error",""), Remaining = o.TryGetValue("ok", out ok) && Object.Equals(ok, true) ? Number(o, "remaining") : null, Limit = Number(o, "limit") };
            }
            return result;
        }
        public static List<SourceSetting> Layout(List<SourceSetting> saved, Dictionary<string, Reading> available) {
            var result = saved == null ? new List<SourceSetting>() : saved.Select(x => new SourceSetting { Id = x.Id, Title = x.Title, Visible = x.Visible }).ToList();
            string[] preferred = { "deepseek", "openrouter", "codex" };
            foreach (string id in available.Keys.Where(x => !result.Any(s => s.Id == x)).OrderBy(x => Array.IndexOf(preferred, x) < 0 ? 99 : Array.IndexOf(preferred, x)).ThenBy(x => x, StringComparer.Ordinal)) result.Add(new SourceSetting { Id = id, Title = "", Visible = saved == null });
            return result;
        }
    }
    public class LaunchSpec { public string Node, Entry; }
    public static class Discovery {
        public static string FindExe(string name) {
            foreach (string path in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(';')) {
                try { string p = Path.Combine(path.Trim().Trim('"'), name); if (File.Exists(p)) return Path.GetFullPath(p); } catch { }
            }
            return null;
        }
        static string PackageEntry(string root) {
            string file = Path.Combine(root, "node_modules", "@deepseek-ai", "dsh", "package.json");
            if (!File.Exists(file)) return null;
            try {
                var obj = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(File.ReadAllText(file));
                object bin; if (!obj.TryGetValue("bin", out bin)) return null;
                string entry = bin as string;
                var bins = bin as Dictionary<string, object>;
                object value; if (bins != null && bins.TryGetValue("dsh", out value)) entry = value as string;
                if (entry == null) return null;
                string resolved = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(file), entry));
                return File.Exists(resolved) ? resolved : null;
            } catch { return null; }
        }
        public static LaunchSpec Find(Settings settings) {
            string node = settings.NodePath;
            if (String.IsNullOrWhiteSpace(node)) node = FindExe("node.exe");
            if (String.IsNullOrEmpty(node)) {
                var candidates = new [] { Path.Combine(Environment.GetEnvironmentVariable("NVM_SYMLINK") ?? "", "node.exe"), Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "nodejs", "node.exe"), Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Volta", "bin", "node.exe") };
                node = candidates.FirstOrDefault(File.Exists);
            }
            if (String.IsNullOrWhiteSpace(node) || !File.Exists(node)) throw new FileNotFoundException("找不到 node.exe，请在设置中指定 Node.js 路径。");
            string entry = settings.DshPath;
            if (!String.IsNullOrWhiteSpace(entry)) {
                if (!File.Exists(entry)) throw new FileNotFoundException("指定的 DSH JavaScript 入口不存在。");
                if (new [] { ".cmd", ".bat", ".ps1", ".exe" }.Contains(Path.GetExtension(entry).ToLowerInvariant())) throw new InvalidOperationException("DSH 路径请选择包内 JavaScript 入口，不能选择 dsh.cmd 或脚本包装器。");
            } else {
                var roots = new List<string> { Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "npm"), Path.GetDirectoryName(node) };
                var shim = FindExe("dsh.cmd"); if (shim != null) roots.Insert(0, Path.GetDirectoryName(shim));
                foreach (string root in roots) { entry = PackageEntry(root); if (entry != null) break; }
                if (entry == null) {
                    var caches = new List<string> { Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "npm-cache", "_npx"), Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".npm", "_npx") };
                    string custom = Environment.GetEnvironmentVariable("npm_config_cache"); if (!String.IsNullOrEmpty(custom)) caches.Insert(0, Path.Combine(custom, "_npx"));
                    var entries = new List<string>();
                    foreach (string cache in caches) if (Directory.Exists(cache)) foreach (string bucket in Directory.GetDirectories(cache)) { string p = PackageEntry(bucket); if (p != null) entries.Add(p); }
                    entry = entries.OrderByDescending(File.GetLastWriteTimeUtc).FirstOrDefault();
                }
            }
            if (String.IsNullOrEmpty(entry)) throw new FileNotFoundException("未找到已安装的 DSH。请先安装并配置 DeepSeek Harness，或在设置中指定其 JavaScript 入口。");
            return new LaunchSpec { Node = Path.GetFullPath(node), Entry = Path.GetFullPath(entry) };
        }
    }
    public static class Native {
        [StructLayout(LayoutKind.Sequential)] struct TcpRow { public uint State, LocalAddress, LocalPort, RemoteAddress, RemotePort, Pid; }
        [StructLayout(LayoutKind.Sequential)] struct Tcp6Row {
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 16)] public byte[] LocalAddress;
            public uint LocalScope, LocalPort;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 16)] public byte[] RemoteAddress;
            public uint RemoteScope, RemotePort, State, Pid;
        }
        [DllImport("iphlpapi.dll")] static extern uint GetExtendedTcpTable(IntPtr table, ref int size, bool order, int family, int tableClass, uint reserved);
        public static List<int> Listeners(int port) {
            return ListenersFamily(port, 2).Concat(ListenersFamily(port, 23)).Distinct().ToList();
        }
        static List<int> ListenersFamily(int port, int family) {
            int size = 0; GetExtendedTcpTable(IntPtr.Zero, ref size, false, family, 3, 0);
            if (size == 0) return new List<int>();
            IntPtr memory = Marshal.AllocHGlobal(size);
            try {
                uint error = GetExtendedTcpTable(memory, ref size, false, family, 3, 0);
                if (error != 0) throw new System.ComponentModel.Win32Exception((int)error);
                int count = Marshal.ReadInt32(memory); var result = new List<int>();
                int stride = Marshal.SizeOf(family == 2 ? typeof(TcpRow) : typeof(Tcp6Row));
                for (int i = 0; i < count; i++) {
                    uint rawPort, pid;
                    if (family == 2) { var row = (TcpRow)Marshal.PtrToStructure(IntPtr.Add(memory, 4 + i * stride), typeof(TcpRow)); rawPort = row.LocalPort; pid = row.Pid; }
                    else { var row = (Tcp6Row)Marshal.PtrToStructure(IntPtr.Add(memory, 4 + i * stride), typeof(Tcp6Row)); rawPort = row.LocalPort; pid = row.Pid; }
                    int p = (int)(((rawPort & 255) << 8) | ((rawPort >> 8) & 255));
                    if (p == port) result.Add((int)pid);
                }
                return result.Distinct().ToList();
            } finally { Marshal.FreeHGlobal(memory); }
        }
        [StructLayout(LayoutKind.Sequential)] struct Security { public int Length; public IntPtr Descriptor; [MarshalAs(UnmanagedType.Bool)] public bool Inherit; }
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] struct Startup {
            public int Size; public string Reserved, Desktop, Title; public int X, Y, XSize, YSize, XChars, YChars, Fill, Flags; public short Show, Reserved2; public IntPtr ReservedPtr, StdIn, StdOut, StdErr;
        }
        [StructLayout(LayoutKind.Sequential)] struct ProcessInfo { public IntPtr Process, Thread; public int Pid, Tid; }
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] static extern IntPtr CreateFile(string path, uint access, uint share, ref Security security, uint creation, uint flags, IntPtr template);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] static extern bool CreateProcess(string app, StringBuilder command, IntPtr processAttributes, IntPtr threadAttributes, bool inherit, uint flags, IntPtr environment, string cwd, ref Startup startup, out ProcessInfo info);
        [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr handle);
        [DllImport("kernel32.dll", SetLastError = true)] static extern bool AttachConsole(uint pid);
        [DllImport("kernel32.dll")] static extern bool FreeConsole();
        [DllImport("kernel32.dll")] static extern bool SetConsoleCtrlHandler(IntPtr handler, bool add);
        [DllImport("kernel32.dll")] static extern bool GenerateConsoleCtrlEvent(uint control, uint group);
        [DllImport("kernel32.dll")] static extern uint GetConsoleProcessList([Out] uint[] processes, uint size);
        public static int SendControlC(int pid) {
            // Called only in a short-lived helper. Never signal a shared terminal console.
            FreeConsole();
            if (!AttachConsole((uint)pid)) return 3;
            try {
                uint[] members = new uint[128]; uint count = GetConsoleProcessList(members, (uint)members.Length);
                if (count == 0 || count > members.Length) return 3;
                uint self = (uint)Process.GetCurrentProcess().Id;
                for (int i = 0; i < count; i++) if (members[i] != (uint)pid && members[i] != self) return 3;
                SetConsoleCtrlHandler(IntPtr.Zero, true);
                if (!GenerateConsoleCtrlEvent(0, 0)) return 3;
                Thread.Sleep(250); return 0;
            } finally { FreeConsole(); }
        }
        [DllImport("kernel32.dll", SetLastError = true)] static extern bool GetProcessTimes(IntPtr process, out long creation, out long exit, out long kernel, out long user);
        public static DateTime ExitUtc(Process process) {
            long creation, exit, kernel, user;
            if (!GetProcessTimes(process.Handle, out creation, out exit, out kernel, out user)) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
            return DateTime.FromFileTimeUtc(exit);
        }
        public static string Quote(string s) { return "\"" + Regex.Replace(s, "(\\\\*)\"", "$1$1\\\"").TrimEnd('\\') + new string('\\', s.Reverse().TakeWhile(c => c == '\\').Count() * 2) + "\""; }
        public static Process Launch(LaunchSpec spec, bool fullAccess, string cwd, string logs, string extraArguments) {
            Directory.CreateDirectory(logs);
            var security = new Security { Length = Marshal.SizeOf(typeof(Security)), Inherit = true };
            IntPtr stdout = CreateFile(Path.Combine(logs, "dsh-web.out.log"), 4, 3, ref security, 4, 128, IntPtr.Zero);
            IntPtr stderr = CreateFile(Path.Combine(logs, "dsh-web.err.log"), 4, 3, ref security, 4, 128, IntPtr.Zero);
            IntPtr stdin = CreateFile("NUL", 0x80000000, 3, ref security, 3, 128, IntPtr.Zero);
            IntPtr env = IntPtr.Zero;
            try {
                if (stdout == new IntPtr(-1) || stderr == new IntPtr(-1) || stdin == new IntPtr(-1)) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
                var vars = new SortedDictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (System.Collections.DictionaryEntry item in Environment.GetEnvironmentVariables()) vars[(string)item.Key] = (string)item.Value;
                vars.Remove("DSH_PERMISSION_MODE"); if (fullAccess) vars["DSH_PERMISSION_MODE"] = "danger-full-access";
                vars["PATH"] = Path.GetDirectoryName(spec.Node) + ";" + (vars.ContainsKey("PATH") ? vars["PATH"] : "");
                env = Marshal.StringToHGlobalUni(String.Join("\0", vars.Select(x => x.Key + "=" + x.Value)) + "\0\0");
                var startup = new Startup { Size = Marshal.SizeOf(typeof(Startup)), Flags = 0x101, Show = 0, StdIn = stdin, StdOut = stdout, StdErr = stderr };
                ProcessInfo info;
                if (!CreateProcess(spec.Node, new StringBuilder(Quote(spec.Node) + " " + Quote(spec.Entry) + " " + extraArguments), IntPtr.Zero, IntPtr.Zero, true, 0x00000410, env, cwd, ref startup, out info)) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
                try { var result = Process.GetProcessById(info.Pid); IntPtr retained = result.Handle; return result; } finally { CloseHandle(info.Process); CloseHandle(info.Thread); }
            } finally {
                if (stdout != new IntPtr(-1)) CloseHandle(stdout); if (stderr != new IntPtr(-1)) CloseHandle(stderr); if (stdin != new IntPtr(-1)) CloseHandle(stdin);
                if (env != IntPtr.Zero) Marshal.FreeHGlobal(env);
            }
        }
        public static string CommandLine(int pid) {
            using (var search = new ManagementObjectSearcher("SELECT CommandLine FROM Win32_Process WHERE ProcessId=" + pid))
            using (var results = search.Get()) foreach (ManagementObject p in results) return Convert.ToString(p["CommandLine"]);
            return "";
        }
    }
    public class Service : IDisposable {
        public readonly string Home, Logs;
        public readonly int Port;
        public Settings Settings;
        public bool Running, Stopped;
        public int Failures;
        public string Detail = "正在检查…";
        public Dictionary<string, Reading> Readings = new Dictionary<string, Reading>();
        public bool BalancesReachable;
        public string BalanceProblem = "余额尚未刷新。";
        public Action<string> Notify = delegate { };
        public Action Open = delegate { };
        readonly SemaphoreSlim gate = new SemaphoreSlim(1, 1);
        Process child;
        DateTime launched;
        bool disposed;
        string launchedEntry;
        readonly CookieContainer cookies = new CookieContainer();
        string authenticatedUrl;
        public string BaseUrl { get { return "http://127.0.0.1:" + Port + "/"; } }
        public Service(Settings settings, string home, int port) { Settings = settings; Home = home; Port = port; Logs = Path.Combine(home, ".dsh", "logs"); }
        public static string ReadTail(string file) {
            if (!File.Exists(file)) return "";
            using (var f = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete)) {
                f.Seek(Math.Max(0, f.Length - 65536), SeekOrigin.Begin);
                using (var r = new StreamReader(f, Encoding.UTF8)) return r.ReadToEnd();
            }
        }
        public string WebUrl() {
            try {
                var matches = Regex.Matches(ReadTail(Path.Combine(Logs, "dsh-web.out.log")), Regex.Escape(BaseUrl) + "\\?token=[A-Za-z0-9._~%+/-]+(?:={0,2})");
                if (matches.Count > 0) return matches[matches.Count - 1].Value;
            } catch { }
            return BaseUrl;
        }
        public bool Clawbot {
            get { string dir = Path.Combine(Home, ".dsh", "clawbot", "accounts"); return Directory.Exists(dir) && Directory.GetFiles(dir, "*.json").Any(x => !Path.GetFileName(x).Contains("context-tokens")); }
        }
        public static string Redact(string value) { return Regex.Replace(value ?? "", @"(?i)(token=|sk-)[^\s""']+", "$1[已隐藏]"); }
        async Task<bool> Probe() {
            var r = (HttpWebRequest)WebRequest.Create(BaseUrl); r.Proxy = null; r.Timeout = 3000; r.ReadWriteTimeout = 3000; r.AllowAutoRedirect = false;
            using (var timeout = new System.Threading.Timer(delegate { r.Abort(); }, null, 3000, Timeout.Infinite)) {
                try { using (var response = await r.GetResponseAsync()) return true; }
                catch (WebException e) { if (e.Response != null) { e.Response.Dispose(); return true; } return false; }
            }
        }
        async Task Refresh() {
            Running = await Probe();
            Detail = Running ? "运行中" : Stopped ? "已手动停止" : Failures >= 3 ? "启动失败，已暂停重试" : "未运行";
            if (!Running) { Readings=new Dictionary<string,Reading>();BalancesReachable=false;BalanceProblem="DSH 尚未运行，开启服务后会自动读取余额。";return; }
            try {
                string loginUrl = WebUrl();
                if (loginUrl != BaseUrl && loginUrl != authenticatedUrl) {
                    var login = (HttpWebRequest)WebRequest.Create(loginUrl); login.Proxy = null; login.CookieContainer = cookies;
                    using (var timeout = new System.Threading.Timer(delegate { login.Abort(); }, null, 3000, Timeout.Infinite))
                    using (var response = await login.GetResponseAsync()) { authenticatedUrl = loginUrl; }
                }
                var request = (HttpWebRequest)WebRequest.Create(BaseUrl + "api/model-balance"); request.Proxy = null; request.Timeout = 5000; request.ReadWriteTimeout = 5000;
                request.CookieContainer = cookies;
                using (var timeout = new System.Threading.Timer(delegate { request.Abort(); }, null, 5000, Timeout.Infinite))
                using (var response = await request.GetResponseAsync()) using (var reader = new StreamReader(response.GetResponseStream())) Readings = Balances.Parse(await reader.ReadToEndAsync());
                BalancesReachable = true; BalanceProblem=Readings.Count==0?"余额接口已连接，但尚未配置任何来源。":"";
            } catch(WebException e) {
                var response=e.Response as HttpWebResponse;int code=response==null?0:(int)response.StatusCode;
                BalanceProblem=code==401||code==403?"DSH 要求认证，尚未取得当前服务的有效登录会话。":code==404?"DSH 未提供 /api/model-balance，请配置余额插件。":"余额请求失败（"+(code==0?e.Status.ToString():"HTTP "+code)+"），稍后自动重试。";
                if(code==401||code==403)authenticatedUrl=null;
                if(response!=null)response.Dispose(); Readings=new Dictionary<string,Reading>();BalancesReachable=false;
            } catch(FormatException) {Readings=new Dictionary<string,Reading>();BalancesReachable=false;BalanceProblem="接口响应不是余额数据，请检查余额插件及路由。";}
            catch { Readings = new Dictionary<string, Reading>(); BalancesReachable = false;BalanceProblem="余额数据格式不匹配，请检查插件版本。"; }
        }
        async Task Failure(string reason) {
            Failures++; Detail = reason;
            string hint = await PluginHint();
            Notify(Redact(reason + hint + (Failures >= 3 ? "\n连续失败 3 次，已暂停自动重试。修复后点击开启服务。" : "")));
        }
        async Task<string> PluginHint() {
            string path = Path.Combine(Home, ".dsh", "bin", "dsh-plugin-check.exe");
            string js = Path.Combine(Home, ".dsh", "bin", "dsh-plugin-check.js");
            if (!File.Exists(path) && !File.Exists(js)) return "";
            try {
                var info = new ProcessStartInfo { FileName = File.Exists(path) ? path : Discovery.Find(Settings).Node, Arguments = (File.Exists(path) ? "" : Native.Quote(js) + " ") + "web", UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true, WorkingDirectory = Home };
                using (var p = Process.Start(info)) {
                    var output = p.StandardOutput.ReadToEndAsync(); var error = p.StandardError.ReadToEndAsync();
                    if (!await Task.Run(() => p.WaitForExit(10000))) { p.Kill(); return "\n插件自检超时。"; }
                    string text = await output; await error;
                    if (p.ExitCode == 0) return "";
                    var hits = Regex.Replace(text, "\u001b\\[[0-9;]*m", "").Split('\n').Where(x => x.Contains("会起不来")).Take(2).ToArray();
                    return hits.Length > 0 ? "\n插件自检：" + String.Join("；", hits) : "\n插件自检失败，请检查插件配置。";
                }
            } catch (Exception e) { return "\n插件自检未完成：" + e.Message; }
        }
        async Task StartInner(bool open) {
            if (Native.Listeners(Port).Count > 0) { await Refresh(); if (open && Running) Open(); return; }
            if (child != null && !child.HasExited) return;
            string error = null;
            try {
                var spec = Discovery.Find(Settings); launchedEntry = spec.Entry;
                string cwd = String.IsNullOrWhiteSpace(Settings.WorkingDirectory) ? Home : Settings.WorkingDirectory;
                if (!Directory.Exists(cwd)) throw new DirectoryNotFoundException("工作目录不存在：" + cwd);
                launched = DateTime.UtcNow; child = Native.Launch(spec, Settings.FullAccess, cwd, Logs, "web --no-open");
                DateTime deadline = DateTime.UtcNow.AddSeconds(30);
                while (DateTime.UtcNow < deadline) {
                    if (child.HasExited) { await HandleExit(); return; }
                    if (await Probe()) { Running = true; Detail = "运行中"; Notify("DSH 服务已启动"); if (open) Open(); return; }
                    await Task.Delay(500);
                }
                Detail = "启动超过 30 秒，进程仍在运行"; Notify(Detail + "，请查看日志。");
            } catch (Exception e) { error = "启动失败：" + e.Message; }
            if (error != null) await Failure(error);
        }
        async Task HandleExit() {
            double lifetime = (Native.ExitUtc(child) - launched).TotalSeconds;
            child.Dispose(); child = null; Running = false;
            if (Stopped || disposed) return;
            if (lifetime >= 20) { Failures = 0; Notify("DSH 异常退出，将自动重启。"); return; }
            string reason = ReadTail(Path.Combine(Logs, "dsh-web.err.log")).Split('\n').Where(x => x.Contains("Error:") && !x.Contains("    at ")).LastOrDefault() ?? "进程提前退出，请查看错误日志。";
            if (reason.Contains("EADDRINUSE") && Native.Listeners(Port).Count > 0) return;
            await Failure("DSH 启动失败：" + reason.Trim().Substring(0, Math.Min(240, reason.Trim().Length)));
        }
        public async Task Tick(bool supervise) {
            if (!await gate.WaitAsync(0)) return;
            try {
                if (disposed) return;
                if (child != null && child.HasExited) await HandleExit();
                await Refresh();
                if (supervise && !Stopped && !Running && Failures < 3) await StartInner(false);
            } finally { gate.Release(); }
        }
        public async Task Start(bool open) {
            await gate.WaitAsync(); try { if (disposed) return; Stopped = false; Failures = 0; await StartInner(open); await Refresh(); } finally { gate.Release(); }
        }
        bool KnownProcess(int pid) {
            if (child != null && !child.HasExited && child.Id == pid) return true;
            string command = Native.CommandLine(pid).Replace('/', '\\');
            string entry = launchedEntry;
            if (String.IsNullOrEmpty(entry)) try { entry = Discovery.Find(Settings).Entry; } catch { }
            bool matchesEntry = !String.IsNullOrEmpty(entry) && command.IndexOf(entry.Replace('/', '\\'), StringComparison.OrdinalIgnoreCase) >= 0;
            bool matchesPackage = command.IndexOf("\\@deepseek-ai\\dsh\\", StringComparison.OrdinalIgnoreCase) >= 0;
            return (matchesEntry || matchesPackage) && Regex.IsMatch(command, @"\bweb\b", RegexOptions.IgnoreCase);
        }
        async Task StopInner() {
            var pids = Native.Listeners(Port);
            if (child != null && !child.HasExited && !pids.Contains(child.Id)) pids.Add(child.Id);
            foreach (int pid in pids) if (!await Task.Run(() => KnownProcess(pid))) throw new InvalidOperationException("端口 " + Port + " 被非 DSH 或无法核实的进程占用（PID " + pid + "），未结束该进程。");
            string helper = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "DSHWhale-Vibe.exe");
            foreach (int pid in pids) {
                if (!File.Exists(helper)) continue;
                using (var signal = Process.Start(new ProcessStartInfo(helper, "--signal " + pid) { UseShellExecute = false, CreateNoWindow = true })) await Task.Run(() => signal.WaitForExit(2000));
            }
            DateTime deadline = DateTime.UtcNow.AddSeconds(6);
            while (DateTime.UtcNow < deadline && pids.Any(IsAlive)) await Task.Delay(200);
            foreach (int pid in pids) {
                if (!IsAlive(pid)) continue;
                if (!await Task.Run(() => KnownProcess(pid))) throw new InvalidOperationException("进程身份已变化，已取消强制终止。");
                var info = new ProcessStartInfo { FileName = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "taskkill.exe"), Arguments = "/PID " + pid + " /T /F", UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
                using (var p = Process.Start(info)) { var a = p.StandardOutput.ReadToEndAsync(); var b = p.StandardError.ReadToEndAsync(); await Task.Run(() => p.WaitForExit()); await a; await b; }
            }
            for (int i = 0; i < 40 && Native.Listeners(Port).Count > 0; i++) await Task.Delay(200);
            if (Native.Listeners(Port).Count > 0) throw new InvalidOperationException("服务停止超时，端口仍被占用。");
            if (child != null) { child.Dispose(); child = null; } Running = false;
        }
        static bool IsAlive(int pid) { try { using (var p = Process.GetProcessById(pid)) return !p.HasExited; } catch (ArgumentException) { return false; } }
        public async Task Stop() {
            await gate.WaitAsync(); try { Stopped = true; await StopInner(); Detail = "已手动停止"; Notify("DSH 服务已停止"); } finally { gate.Release(); }
        }
        public async Task Restart() {
            await gate.WaitAsync(); try { Stopped = true; await StopInner(); Stopped = false; Failures = 0; await StartInner(false); await Refresh(); } finally { gate.Release(); }
        }
        public void Dispose() { disposed = true; if (child != null) child.Dispose(); }
    }
}
