using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using DshWhale;

class Tests {
    static int checks;
    static void Check(bool condition, string name) { if (!condition) throw new Exception("FAIL: " + name); checks++; Console.WriteLine("PASS: " + name); }
    static int FreePort() { var t = new TcpListener(IPAddress.Loopback, 0); t.Start(); int p = ((IPEndPoint)t.LocalEndpoint).Port; t.Stop(); return p; }
    static int Main(string[] args) { try { Run(args[0]).GetAwaiter().GetResult(); Console.WriteLine("Passed " + checks + " checks"); return 0; } catch (Exception e) { Console.Error.WriteLine(e); return 1; } }
    static async Task Run(string root) {
        var data = Balances.Parse("{\"ok\":true,\"providers\":{\"usd\":{\"ok\":true,\"label\":\"USD\",\"currency\":\"USD\",\"remaining\":3},\"quota\":{\"ok\":true,\"label\":\"Quota\",\"kind\":\"quota\",\"currency\":\"%\",\"remaining\":9.95,\"limit\":10},\"fail\":{\"ok\":false}}}");
        Check(data["usd"].Health == "green" && data["usd"].Text == "$3.00", "USD color uses CNY conversion");
        Check(data["quota"].Health == "green", "quota health uses fraction, not absolute value");
        Check(data["fail"].Text == "—", "provider error has unavailable value");
        var saved = new List<SourceSetting> { new SourceSetting { Id = "missing", Title = "自定义", Visible = true } };
        var layout = Balances.Layout(saved, data);
        Check(layout[0].Id == "missing" && layout.Skip(1).All(x => !x.Visible), "saved ordering retained and new providers hidden");
        Check(Balances.Layout(null, data).All(x => x.Visible), "first-run providers visible");
        string directory = Path.Combine(root, "build", "test-data-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(directory);
        string fixture = Path.Combine(directory, "fixture with spaces.js"); File.Copy(Path.Combine(root, "tests", "fake-dsh.js"), fixture);
        var settings = new Settings { NodePath = Discovery.FindExe("node.exe"), DshPath = fixture, Sources = saved };
        settings.Save(Path.Combine(directory, "settings.json"));
        Check(Settings.Load(Path.Combine(directory, "settings.json")).Sources[0].Title == "自定义", "settings UTF-8 roundtrip");
        int port = FreePort(); File.WriteAllText(Path.Combine(directory, "port.txt"), port.ToString());
        var service = new Service(settings, directory, port);
        try {
            await service.Start(false);
            Check(service.Running, "native launch and HTTP 401 counts as running");
            Check(service.Readings.ContainsKey("deepseek"), "token exchange enables authenticated balance API");
            Check(service.WebUrl().Contains("?token=fixture-"), "latest login token extracted");
            Check(Native.Listeners(port).Count == 1, "Windows listener PID discovery");
            int first = Native.Listeners(port).Single();
            await service.Start(false); Check(Native.Listeners(port).Single() == first, "duplicate start reuses existing process");
            await service.Restart(); Check(Native.Listeners(port).Single() != first && service.Running, "restart waits for port release and starts new PID");
            await service.Stop(); Check(Native.Listeners(port).Count == 0, "stop releases port");
            Check(File.Exists(Path.Combine(directory, "graceful-stop.txt")), "Windows console signal allows graceful SIGINT cleanup");
            await service.Tick(true); Check(Native.Listeners(port).Count == 0, "manual stop suppresses watchdog");
            settings.FullAccess = true; await service.Start(false);
            Check(File.ReadAllText(Path.Combine(directory, "environment.json")).Contains("danger-full-access"), "explicit full-access environment injected");
            await service.Stop(); settings.FullAccess = false;
            Environment.SetEnvironmentVariable("DSH_PERMISSION_MODE", "danger-full-access");
            await service.Start(false);
            Check(!File.ReadAllText(Path.Combine(directory, "environment.json")).Contains("danger-full-access"), "default removes inherited full-access override");
            Environment.SetEnvironmentVariable("DSH_PERMISSION_MODE", null);
            first = Native.Listeners(port).Single(); Process.GetProcessById(first).Kill(); await Task.Delay(300);
            await service.Tick(true); Check(service.Running && Native.Listeners(port).Single() != first, "watchdog restores crashed service");
            await service.Stop(); File.WriteAllText(Path.Combine(directory, "fail.txt"), "1");
            await service.Start(false); await service.Tick(true); await service.Tick(true);
            Check(service.Failures == 3, "three startup failures trip circuit breaker");
            await service.Tick(true); Check(service.Failures == 3 && !service.Running, "watchdog stops retrying after three failures");
            File.Delete(Path.Combine(directory, "fail.txt")); await service.Start(false); Check(service.Failures == 0 && service.Running, "manual start resets failure counter");
            service.Dispose();
            await Task.Delay(300); Check(Native.Listeners(port).Count == 1, "service survives disposing companion handles");
            service = new Service(settings, directory, port); await service.Tick(false); Check(service.Running, "adopts externally running service");
            await service.Stop(); Check(Native.Listeners(port).Count == 0, "verified external service can be stopped");
            var listener = new TcpListener(IPAddress.Loopback, port); listener.Start();
            bool blocked = false; try { await service.Stop(); } catch (InvalidOperationException) { blocked = true; } finally { listener.Stop(); }
            Check(blocked, "unrelated port owner is never terminated");
        } finally { try { service.Stop().GetAwaiter().GetResult(); } catch { } service.Dispose(); }
        string externalHome = Path.Combine(directory, "independent"); Directory.CreateDirectory(externalHome); File.WriteAllText(Path.Combine(externalHome, "port.txt"), port.ToString());
        string host = Path.Combine(root, "build", "TestLauncher.exe");
        using (var parent = Process.Start(new ProcessStartInfo(host, Native.Quote(settings.NodePath) + " " + Native.Quote(fixture) + " " + Native.Quote(externalHome)) { UseShellExecute = false, CreateNoWindow = true })) await Task.Run(() => parent.WaitForExit());
        for (int i = 0; i < 20 && Native.Listeners(port).Count == 0; i++) await Task.Delay(100);
        Check(Native.Listeners(port).Count == 1, "child keeps running after actual launcher process exits");
        var adopted = new Service(settings, externalHome, port); try { await adopted.Stop(); } finally { adopted.Dispose(); }
        try { var found = Discovery.Find(new Settings()); Check(File.Exists(found.Entry), "installed DSH discovered from npm cache"); }
        catch (FileNotFoundException) { if(Environment.GetEnvironmentVariable("WHALE_TEST_REQUIRE_DSH")=="1")throw; Console.WriteLine("SKIP: optional installed DSH discovery (no installation on this host)"); }
        var live = new Service(new Settings(), Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), 3080);
        try { await live.Tick(false); Console.WriteLine("READ-ONLY local DSH: running=" + live.Running + ", balanceProviders=" + live.Readings.Count); } finally { live.Dispose(); }
    }
}
