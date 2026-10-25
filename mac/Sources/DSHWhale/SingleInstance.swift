import AppKit
import Darwin

/// 同一时刻只留一只小鲸鱼。
///
/// 两只同时在跑,会各自看门、各自拉起 dsh,状态栏上也是两个图标。2026-10-17 就发现了一次:
/// LaunchAgent 起的那只之外,10-15 又有一只是从访达或启动台打开的。launchd 直接执行二进制
/// 起来的实例,在 LaunchServices 那里没有启动记录(`launchDate` 是 nil),所以再「打开」
/// 小鲸鱼时,系统不会去激活它,而是另起一份。
///
/// 规则:
/// - LaunchAgent 起的那只说了算(崩了 launchd 会重启它,日志也只接它的)。它启动时把别的实例都关掉。
/// - 从别的入口打开的实例,发现已经有一只在跑,就把这次打开转交给它,然后自己退出。
///   转交的意思是让在跑的那只弹出面板;带了 `--settings` 就打开设置页。
/// - 两只都不是 LaunchAgent 起的,就留先起的那只。
enum SingleInstance {
    /// 在跑的那只收到这条通知就弹出面板;object 是 "settings" 时改为打开设置页。
    static var reopenNotification: Notification.Name {
        Notification.Name("\(Bundle.main.bundleIdentifier ?? "DSHWhale").reopen")
    }

    /// 返回 false 表示这一份该退出:已经有一只在跑,这次打开已经转交给它了。
    static func claim() -> Bool {
        guard let bundleID = Bundle.main.bundleIdentifier else { return true }
        let me = ProcessInfo.processInfo.processIdentifier
        let others = NSRunningApplication.runningApplications(withBundleIdentifier: bundleID)
            .filter { $0.processIdentifier != me && !$0.isTerminated }
        guard !others.isEmpty else { return true }

        if launchedByAgent {
            // 它们没有 SIGTERM 处理器,收到就退。各自起的 dsh 在自己的进程组里,
            // 输出写的是日志文件,不会跟着退出,这一只按端口认回来接着看门。
            for other in others {
                NSLog("DSHWhale: 关掉另一只小鲸鱼(pid %d,不是 LaunchAgent 起的)", other.processIdentifier)
                kill(other.processIdentifier, SIGTERM)
            }
            return true
        }

        // 两只同时起来时会互相看见。比启动时刻,只让后起的那只退出,免得两只都退。
        let mine = startTime(of: me)
        let older = others.filter { other in
            guard let mine, let theirs = startTime(of: other.processIdentifier) else { return true }
            return theirs < mine || (theirs == mine && other.processIdentifier < me)
        }
        guard let running = older.first else { return true }
        NSLog("DSHWhale: 已经有一只小鲸鱼在跑(pid %d),这次打开交给它", running.processIdentifier)
        DistributedNotificationCenter.default().postNotificationName(
            reopenNotification,
            object: CommandLine.arguments.contains("--settings") ? "settings" : "panel",
            userInfo: nil,
            deliverImmediately: true)
        return false
    }

    /// 判断是不是 LaunchAgent 起的:父进程是 launchd,而且 `XPC_SERVICE_NAME` 是任务的 label。
    /// 从访达、启动台或 `open` 打开的,这个值是 `application.<bundle id>.…`。从终端或 dsh 里
    /// 运行的,会继承父进程的值(dsh 是小鲸鱼起的,继承到的正是那个 label),但它们的父进程不是 launchd。
    private static var launchedByAgent: Bool {
        guard getppid() == 1,
              let name = ProcessInfo.processInfo.environment["XPC_SERVICE_NAME"],
              !name.isEmpty, name != "0" else { return false }
        return !name.hasPrefix("application.")
    }

    /// 内核记的启动时刻。不能用 `NSRunningApplication.launchDate`:launchd 直接起的实例,这个值是 nil。
    private static func startTime(of pid: pid_t) -> Double? {
        var info = kinfo_proc()
        var size = MemoryLayout<kinfo_proc>.stride
        var mib: [Int32] = [CTL_KERN, KERN_PROC, KERN_PROC_PID, pid]
        guard sysctl(&mib, u_int(mib.count), &info, &size, nil, 0) == 0, size > 0 else { return nil }
        let t = info.kp_proc.p_un.__p_starttime
        return Double(t.tv_sec) + Double(t.tv_usec) / 1_000_000
    }
}
