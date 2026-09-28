import AppKit
import Foundation
import SwiftUI
import UserNotifications

// MARK: - 服务状态

struct ServiceStatus {
    let running: Bool
    let detail: String
}

// MARK: - 状态检测(全部异步,不阻塞主线程)

enum StatusChecker {
    /// DSH Web 是否在跑:探测 127.0.0.1:3080(后台执行)
    static func dshWeb(completion: @escaping (ServiceStatus) -> Void) {
        var request = URLRequest(url: URL(string: "http://127.0.0.1:3080")!)
        request.timeoutInterval = 3
        URLSession.shared.dataTask(with: request) { _, resp, _ in
            // **任何 HTTP 回应都说明它活着**,不能只认 2xx/3xx。DSH 0.1.5 起
            // 没有 cookie 的 `/` 一律回 401(「dsh web authentication required」)
            // —— 那恰恰证明服务在监听、在处理请求。原来只认 200..<400,于是升级
            // 之后小鲸鱼一直显示「未运行」,而服务好得很。
            // 真正的"没在跑"是**连不上**:resp 为 nil。
            let running = resp is HTTPURLResponse
            completion(ServiceStatus(running: running, detail: running ? "运行中" : "未运行"))
        }.resume()
    }

    /// 微信 Clawbot 是否在跑:检查 clawbot 账号绑定(快,同步可接受)
    static func clawbot() -> ServiceStatus {
        let home = FileManager.default.homeDirectoryForCurrentUser.path
        let accountsDir = "\(home)/.dsh/clawbot/accounts"
        let hasAccount = (try? FileManager.default.contentsOfDirectory(atPath: accountsDir))?
            .contains { $0.hasSuffix(".json") && !$0.contains("context-tokens") } ?? false
        return ServiceStatus(running: hasAccount, detail: hasAccount ? "已登录" : "未登录")
    }

    /// 余额:调 DSH 的 GET /api/model-balance(后台执行)。
    ///
    /// 接口把每个源统一成 {ok, label, kind, currency, remaining, limit},并且**总是**
    /// 200 —— 单个源失败只体现在它自己的 ok 上。它报什么源,这里就有什么源;
    /// 显示哪些、叫什么、什么顺序,交给设置(见 Settings.swift)。
    static func balances(completion: @escaping (BalanceSnapshot) -> Void) {
        var request = URLRequest(url: URL(string: "http://127.0.0.1:3080/api/model-balance")!)
        request.timeoutInterval = 5
        URLSession.shared.dataTask(with: request) { data, _, _ in
            guard let data,
                  let obj = try? JSONSerialization.jsonObject(with: data) as? [String: Any],
                  let ok = obj["ok"] as? Bool, ok,
                  let providers = obj["providers"] as? [String: Any]
            else {
                completion(.unreachable)
                return
            }
            var sources: [String: BalanceSourceInfo] = [:]
            for (id, raw) in providers {
                let entry = raw as? [String: Any]
                sources[id] = BalanceSourceInfo(id: id,
                                                label: entry?["label"] as? String ?? id,
                                                kind: entry?["kind"] as? String,
                                                reading: read(raw))
            }
            completion(BalanceSnapshot(reachable: true, sources: sources))
        }.resume()
    }

    /// 把一个 provider 条目读成 BalanceReading。金额一律两位小数 —— 是钱,不是测量值。
    private static func read(_ raw: Any?) -> BalanceReading {
        guard let entry = raw as? [String: Any],
              let ok = entry["ok"] as? Bool, ok,
              let remaining = entry["remaining"] as? Double
        else { return .unavailable }
        // 百分比配额:符号在后,而且 "85%" 比 "85.00%" 好读。
        if (entry["currency"] as? String) == "%" {
            return BalanceReading(text: String(format: "%.0f%%", remaining),
                                  value: remaining,
                                  limit: entry["limit"] as? Double)
        }
        let symbol: String
        switch entry["currency"] as? String {
        case "CNY": symbol = "¥"
        case "USD": symbol = "$"
        case let code?: symbol = "\(code) "
        default: symbol = ""
        }
        return BalanceReading(text: String(format: "%@%.2f", symbol, remaining),
                              value: remaining,
                              limit: entry["limit"] as? Double,
                              // 美元判色前折成人民币(1:7),和网页插件同一条规则。
                              cnyScale: (entry["currency"] as? String) == "USD" ? 7 : 1)
    }
}

// MARK: - DSH 进程管理器(小鲸鱼全权管理)

/// 打开 DSH 的 webapp —— Safari「添加到程序坞」生成的 ~/Applications/DSH.app。
///
/// 为什么不让 dsh 自己开:`dsh web` 默认会在默认浏览器里开一个标签页,那个用起来
/// 不如独立窗口的 webapp。所以启动 dsh 时一律带 --no-open,界面由这里统一打开。
/// 当前该打开的网页地址。
///
/// DSH 0.1.5 起网页要认证:没有 cookie 的 `/` 一律 401(「dsh web
/// authentication required」)。cookie 要拿启动时打印在 stdout 的那个
/// **进程 token** 去换(`/?token=…` → 303 并 Set-Cookie),换到之后能用 30 天
/// —— 签名密钥在 ~/.dsh/.credentials.yaml 里,跨重启不变。token 本身每次启动
/// 都不一样,但在一个进程的生命周期里可以反复用。
///
/// 小鲸鱼自己就是 dsh 的 stdout 的接收方(写进 dsh-web.out.log),所以最后一行
/// 带 token 的地址就是当前这次启动的。取不到就退回裸地址:行为不比以前差。
func currentWebURL() -> URL {
    let fallback = URL(string: "http://127.0.0.1:3080/")!
    let log = FileManager.default.homeDirectoryForCurrentUser
        .appendingPathComponent(".dsh/logs/dsh-web.out.log")
    guard let text = try? String(contentsOf: log, encoding: .utf8) else { return fallback }
    // 只认最后一行:前面那些是历次启动留下的旧 token,用旧的照样 401。
    let hit = text.split(whereSeparator: \.isNewline)
        .compactMap { line -> URL? in
            guard let r = line.range(of: "http://127.0.0.1:3080/?token=") else { return nil }
            let raw = line[r.lowerBound...].trimmingCharacters(in: .whitespaces)
            return URL(string: raw)
        }
        .last
    return hit ?? fallback
}

/// 打开 webapp(菜单里的那一项)。
///
/// DSH.app 是个 Safari Web App,它的 start_url 是**裸** `127.0.0.1:3080/` ——
/// 0.1.5 之后点开只会看到一行 401 文本,表现就是「打不开」。所以这里显式把带
/// token 的地址交给它;万一它拒绝接 URL(只是把窗口拉到前面),退回默认浏览器,
/// 至少能进去,而且 cookie 一换就是 30 天,后面裸地址也能用。
func openDSHWebApp() {
    let app = FileManager.default.homeDirectoryForCurrentUser
        .appendingPathComponent("Applications/DSH.app")
    let url = currentWebURL()
    NSWorkspace.shared.open([url], withApplicationAt: app, configuration: NSWorkspace.OpenConfiguration()) { _, err in
        guard err != nil else { return }
        NSLog("DSHWhale: DSH.app 打不开带 token 的地址(\(err!.localizedDescription)),改用默认浏览器")
        NSWorkspace.shared.open(url)
    }
}

final class DSHProcessManager {
    private(set) var process: Process?
    private var intentionallyStopped = false

    /// 给界面用的快速判断(可以稍微陈旧)。**动作路径不要用它** —— 见 livePid()。
    var isRunning: Bool {
        if let p = process, p.isRunning { return true }
        return cachedDshRunning
    }
    /// 上次探测结果缓存(避免每次判断都阻塞);由 20 秒的定时器刷新
    var cachedDshRunning = false

    /// 当前正在监听 3080 的进程号 —— 实时查,不看缓存。
    ///
    /// 小鲸鱼只在自己拉起服务时才有 `process` 句柄。你在终端里 `dsh web`、
    /// 或者小鲸鱼重启过,句柄就是 nil,但服务还在跑。这时只能按端口找它。
    private func livePid() -> Int32? {
        let p = Process()
        p.executableURL = URL(fileURLWithPath: "/usr/sbin/lsof")
        p.arguments = ["-nP", "-iTCP:3080", "-sTCP:LISTEN", "-t"]
        let pipe = Pipe()
        p.standardOutput = pipe
        p.standardError = FileHandle.nullDevice
        do { try p.run() } catch { return nil }
        let data = pipe.fileHandleForReading.readDataToEndOfFile()
        p.waitUntilExit()
        let text = String(data: data, encoding: .utf8) ?? ""
        return text.split(whereSeparator: \.isNewline).first.flatMap { Int32($0) }
    }

    /// 停掉服务并**等它真的放开端口**。返回是否成功停下。
    ///
    /// 先 SIGINT(自己的进程用 interrupt,别人的按端口 kill),轮询端口直到释放;
    /// 超时就升级到 SIGTERM 再等一小会儿。
    @discardableResult
    private func stopAndWait(timeout: TimeInterval = 12) -> Bool {
        if let p = process, p.isRunning {
            p.interrupt()
        } else if let pid = livePid() {
            kill(pid, SIGINT)
        }
        let deadline = Date().addingTimeInterval(timeout)
        while Date() < deadline {
            if livePid() == nil { break }
            Thread.sleep(forTimeInterval: 0.25)
        }
        if let pid = livePid() {
            if let p = process, p.isRunning { p.terminate() } else { kill(pid, SIGTERM) }
            let hard = Date().addingTimeInterval(4)
            while Date() < hard, livePid() != nil { Thread.sleep(forTimeInterval: 0.25) }
        }
        process = nil
        cachedDshRunning = false
        return livePid() == nil
    }

    /// 找 node。**nvm 优先**,系统位置只作兜底 —— 顺序反了会出事:
    /// DSH 装在某个 node 的 npx 缓存里,里面有按 node ABI 编译的原生模块
    /// (dsh-attachment-local 依赖 sharp)。nvm 和 homebrew 装的 node 大版本经常
    /// 不一样,挑错了就等于拿另一个 ABI 的 node 去跑原生模块,附图整条路径会挂。
    /// 所以先用 nvm 里最新的那个 —— 通常也就是装 DSH 时用的那个。
    ///
    /// 不能用 `which` —— 登录项/launchd 起来的进程拿不到用户 shell 的 PATH。
    static func findNode(home: String) -> String? {
        let fm = FileManager.default
        let nvm = "\(home)/.nvm/versions/node"
        if let versions = try? fm.contentsOfDirectory(atPath: nvm) {
            // 字典序排会把 v9 排在 v20 后面,所以按版本号数值比。
            let sorted = versions.sorted { lhs, rhs in
                let a = lhs.dropFirst().split(separator: ".").compactMap { Int($0) }
                let b = rhs.dropFirst().split(separator: ".").compactMap { Int($0) }
                for (x, y) in zip(a, b) where x != y { return x > y }
                return a.count > b.count
            }
            for v in sorted {
                let candidate = "\(nvm)/\(v)/bin/node"
                if fm.isExecutableFile(atPath: candidate) { return candidate }
            }
        }
        for path in ["/opt/homebrew/bin/node", "/usr/local/bin/node", "/usr/bin/node"]
        where fm.isExecutableFile(atPath: path) { return path }
        return nil
    }

    /// 找 dsh:全局安装优先,否则扫 npx 缓存(目录名是内容哈希,不能写死)。
    static func findDsh(home: String) -> String? {
        let fm = FileManager.default
        let fixed = [
            "/opt/homebrew/bin/dsh",
            "/usr/local/bin/dsh",
            "\(home)/.npm-global/bin/dsh",
        ]
        for path in fixed where fm.isExecutableFile(atPath: path) { return path }

        let npx = "\(home)/.npm/_npx"
        guard let buckets = try? fm.contentsOfDirectory(atPath: npx) else { return nil }
        // 多个缓存桶时取最近改动的那个:它才是当前在用的那份安装。
        let candidates = buckets
            .map { "\(npx)/\($0)/node_modules/.bin/dsh" }
            .filter { fm.isExecutableFile(atPath: $0) }
        return candidates.max { lhs, rhs in
            let l = (try? fm.attributesOfItem(atPath: lhs)[.modificationDate] as? Date) ?? nil
            let r = (try? fm.attributesOfItem(atPath: rhs)[.modificationDate] as? Date) ?? nil
            return (l ?? .distantPast) < (r ?? .distantPast)
        }
    }

    func start(openUI: Bool = false) {
        // 这里过去用的是 `isRunning`,而它会回落到 20 秒才刷新一次的
        // cachedDshRunning。重启时端口刚放开、缓存还是陈旧的 true,于是
        // start() 被自己挡下,服务再也起不来。动作路径一律实时查端口。
        guard livePid() == nil else { return }
        intentionallyStopped = false
        let home = FileManager.default.homeDirectoryForCurrentUser.path
        // 两条路径都要发现,不能写死。node 写死成某个 nvm 版本,升级之后那条路径就
        // 不存在了,服务再也起不来;dsh 写死成 npx 缓存的那串哈希,换机器或重装就变。
        guard let node = Self.findNode(home: home) else {
            NSLog("DSHWhale: 找不到 node,无法启动 dsh")
            return
        }
        guard let dshBin = Self.findDsh(home: home) else {
            NSLog("DSHWhale: 找不到 dsh 可执行文件,无法启动")
            return
        }

        let p = Process()
        p.executableURL = URL(fileURLWithPath: node)
        // --no-open:dsh 自己会开默认浏览器的标签页,我们要的是 webapp(见 openDSHWebApp)
        p.arguments = [dshBin, "web", "--no-open"]
        p.currentDirectoryURL = URL(fileURLWithPath: home)

        // 权限模式来自设置。默认不注入,由 DSH 自己决定(workspace-write + 审批);
        // 选了「完全访问」就注入 danger-full-access —— 全盘读写、不弹审批。
        // DSH_PERMISSION_MODE 同时决定 sandbox-policy.mode 和 approval.policy
        // (见 dsh-base 的 cordis 树)。
        //
        // 必须在**继承的**环境上叠加:整体替换 p.environment 会丢掉 PATH 等,
        // node 直接起不来。这里显式注入而不只依赖 ~/.env,是为了即使以后
        // currentDirectoryURL 变了也仍然生效。
        var env = ProcessInfo.processInfo.environment
        if let mode = WhaleSettings.shared.permissionMode.environmentValue {
            env["DSH_PERMISSION_MODE"] = mode
        } else {
            env.removeValue(forKey: "DSH_PERMISSION_MODE")
        }
        p.environment = env

        let logDir = "\(home)/.dsh/logs"
        try? FileManager.default.createDirectory(atPath: logDir, withIntermediateDirectories: true)
        p.standardOutput = Self.appendHandle("\(logDir)/dsh-web.out.log")
        p.standardError = Self.appendHandle(Self.errLogPath)

        let launchedAt = Date()
        p.terminationHandler = { [weak self] _ in
            guard let self else { return }
            guard !self.intentionallyStopped else { return }
            let alive = Date().timeIntervalSince(launchedAt)
            DispatchQueue.main.async { [weak self] in
                guard let self, !self.intentionallyStopped else { return }
                self.cachedDshRunning = false
                self.process = nil

                // 起来不到 20 秒就死 = 启动失败(配置错误、插件加载不了…),
                // 不是"跑着跑着崩了"。这种情况重试多少次都是一样的结果,
                // 所以把 dsh 自己报的错捞出来告诉用户,而不是无声地空转。
                if alive < 20 {
                    let reason = Self.lastStartupError() ?? "原因见 ~/.dsh/logs/dsh-web.err.log"
                    // EADDRINUSE 不是故障,是"已经有一个在跑了" —— 多半是本进程
                    // 和看门/terminationHandler 抢着起。这种情况别计数也别弹通知,
                    // 否则用户看到的是「DSH 启动失败」而服务其实好着。
                    if reason.contains("EADDRINUSE") {
                        NSLog("DSHWhale: 重复启动撞上 EADDRINUSE,已有实例在跑,忽略")
                        return
                    }
                    self.failedStarts += 1
                    // dsh 抛的报错常常只说「插件树加载失败」,不说是哪个插件、该怎么改。
                    // 装完插件起不来是最常见的情形,所以顺手跑一遍自检把答案带上。
                    // 子进程会阻塞,放后台。
                    DispatchQueue.global(qos: .utility).async {
                        let hint = Self.pluginCheckHint()
                        self.sendNotification(
                            hint == nil ? "DSH 启动失败:\(reason)" : "DSH 启动失败:\(reason)\n\(hint!)",
                            "小鲸鱼")
                    }
                    return
                }

                self.failedStarts = 0
                guard self.livePid() == nil else { return }
                self.sendNotification("DSH 服务异常退出,正在自动重启", "小鲸鱼")
                self.start()
            }
        }

        do {
            try p.run()
            process = p
        } catch {
            sendNotification("启动失败: \(error.localizedDescription)", "小鲸鱼")
            return
        }
        // 不在这里报"已启动":此刻只是 node 起来了,dsh 可能几百毫秒后就因为
        // 插件加载失败退出。等端口真的在监听再说话。
        DispatchQueue.global(qos: .utility).async { [weak self] in
            let deadline = Date().addingTimeInterval(30)
            while Date() < deadline {
                if self?.livePid() != nil {
                    self?.sendNotification("DSH 服务已启动 (由小鲸鱼托管)", "小鲸鱼")
                    DispatchQueue.main.async {
                        self?.cachedDshRunning = true
                        // 等端口真的在监听再开界面,否则 webapp 会先加载出一个连不上的页面。
                        // 只有用户主动启动/开机首次启动才开;崩溃自愈的重启不弹窗。
                        if openUI { openDSHWebApp() }
                    }
                    return
                }
                if self?.process == nil { return }   // terminationHandler 已经报过错了
                Thread.sleep(forTimeInterval: 0.5)
            }
            self?.sendNotification("DSH 30 秒内没有监听 3080,可能启动失败", "小鲸鱼")
        }
    }

    /// 自检脚本路径(没装就跳过)。
    private static let pluginCheckPath =
        "\(FileManager.default.homeDirectoryForCurrentUser.path)/.dsh/bin/dsh-plugin-check"

    /// 跑一遍 dsh-plugin-check,把「会起不来」那几行压成一句提示。
    ///
    /// 它查的正是那几类「装个插件就让整个 harness 开不了机」的问题:宿主包放错
    /// dependencies、@deepseek-ai/* 解析不到、声明了 dsh.client 却没有
    /// exports["./client"]。退出码 0 表示没查出致命问题,那就不多嘴。
    private static func pluginCheckHint() -> String? {
        guard FileManager.default.isExecutableFile(atPath: pluginCheckPath) else { return nil }
        let p = Process()
        p.executableURL = URL(fileURLWithPath: pluginCheckPath)
        p.arguments = ["web"]
        let pipe = Pipe()
        p.standardOutput = pipe
        p.standardError = FileHandle.nullDevice
        do { try p.run() } catch { return nil }
        let data = pipe.fileHandleForReading.readDataToEndOfFile()
        p.waitUntilExit()
        guard p.terminationStatus != 0 else { return nil }   // 0 = 没有致命问题
        let text = String(data: data, encoding: .utf8) ?? ""
        // 去掉 ANSI 颜色,只留报致命问题的那几行
        let plain = text.replacingOccurrences(
            of: "\u{1B}\\[[0-9;]*m", with: "", options: .regularExpression)
        let hits = plain.split(separator: "\n")
            .filter { $0.contains("会起不来") }
            .map { $0.replacingOccurrences(of: "会起不来", with: "").trimmingCharacters(in: .whitespaces) }
        guard !hits.isEmpty else { return nil }
        let head = hits.prefix(2).joined(separator: ";")
        return hits.count > 2 ? "插件自检:\(head)(还有 \(hits.count - 2) 条)" : "插件自检:\(head)"
    }

    /// 连续启动失败次数(用于界面提示,不做无限重试)。
    private(set) var failedStarts = 0

    private static let errLogPath =
        "\(FileManager.default.homeDirectoryForCurrentUser.path)/.dsh/logs/dsh-web.err.log"

    /// 追加写入的句柄。`FileHandle(forWritingAtPath:)` 是从偏移 0 开始写的,
    /// 会把旧日志盖掉 —— 正是它让这次排查多花了不少功夫。
    private static func appendHandle(_ path: String) -> FileHandle {
        if !FileManager.default.fileExists(atPath: path) {
            FileManager.default.createFile(atPath: path, contents: nil)
        }
        guard let handle = FileHandle(forWritingAtPath: path) else { return .nullDevice }
        handle.seekToEndOfFile()
        return handle
    }

    /// 从错误日志尾部捞出 dsh 自己抛的那行 Error,用作通知里的原因。
    private static func lastStartupError() -> String? {
        guard let data = FileManager.default.contents(atPath: errLogPath),
              let text = String(data: data.suffix(20_000), encoding: .utf8) else { return nil }
        let line = text
            .split(separator: "\n")
            .last { $0.contains("Error:") && !$0.contains("    at ") }
        guard let line else { return nil }
        return String(line.trimmingCharacters(in: .whitespaces).prefix(180))
    }

    /// 停止服务。退出小鲸鱼时是唯一的停止路径,所以即使服务不是小鲸鱼拉起来的
    /// 也必须能停 —— 按端口找到它。同步等待,超时短一些(退出流程里不能久等)。
    func stop() {
        intentionallyStopped = true
        guard livePid() != nil || process != nil else { return }
        let stopped = stopAndWait(timeout: 6)
        sendNotification(stopped ? "DSH 服务已停止" : "DSH 服务停止超时,可能仍在运行", "小鲸鱼")
    }

    /// 兜底看门(定时器每 20 秒调一次)。
    ///
    /// 为什么不能只靠 terminationHandler:它有一个致命的空窗 —— 处理器执行的
    /// 那一刻 dsh 可能还在拆 web server、端口仍然 LISTEN,于是里面那句
    /// `guard livePid() == nil` 直接 return,小鲸鱼从此再也不试。
    /// 实际出过这样的事:改了一次设置触发插件热重载,dsh 自己退了,小鲸鱼一声不响,
    /// 表现就是「网页打不开」。
    /// 守护进程应该按状态收敛,而不是只在一个事件里赌一次。
    ///
    /// 连续启动失败 3 次就停手:crash loop 里每 20 秒重试毫无意义,只会刷通知。
    /// 计数在"活过 20 秒"的那次退出里清零(见 terminationHandler)。
    func superviseIfNeeded() {
        guard !intentionallyStopped else { return }
        guard livePid() == nil else { return }
        // 这里**故意不去 pgrep「还有没有 dsh 进程」**:试过,是个陷阱 ——
        // `pgrep -f "dsh web"` 会匹配到任何命令行里带这串字的进程(比如一条
        // 正在跑的 shell 命令),于是看门以为服务还活着,真的挂了也不拉 ——
        // 实测被这样骗过,服务停了将近一分钟都没恢复。
        // 正确做法是让它去撞:旧实例还占着 socket 时,新进程会以
        // `listen EADDRINUSE` 立刻退出,而那一条在 terminationHandler 里被当成
        // 「已经有一个在跑」忽略掉(不计数、不弹通知),20 秒后再试一次就成了。
        guard failedStarts < 3 else { return }
        NSLog("DSHWhale: 看门发现 dsh 不在跑,自动拉起(failedStarts=\(failedStarts))")
        start()
    }

    /// 重启服务:先停到端口真的释放,再拉起来。
    ///
    /// 旧实现有两处坏掉:只 interrupt 自己 spawn 的进程(别处起的 dsh 完全动不了),
    /// 而且固定等 4 秒后调 start() —— 那时 cachedDshRunning 还是陈旧的 true,
    /// start() 的 guard 直接返回。结果是通知说"重启中",实际什么都没发生。
    func restart() {
        intentionallyStopped = false
        sendNotification("DSH 服务重启中…", "小鲸鱼")
        // lsof 轮询会阻塞,放后台;拉起进程回主线程。
        DispatchQueue.global(qos: .userInitiated).async { [weak self] in
            guard let self else { return }
            let stopped = self.stopAndWait()
            DispatchQueue.main.async {
                guard !self.intentionallyStopped else { return }
                if !stopped {
                    self.sendNotification("旧进程没能停下,已放弃重启", "小鲸鱼")
                    return
                }
                self.start()
            }
        }
    }

    func sendNotification(_ message: String, _ title: String) {
        let center = UNUserNotificationCenter.current()
        center.requestAuthorization(options: [.alert, .sound]) { _, _ in }
        let content = UNMutableNotificationContent()
        content.title = title
        content.body = message
        content.sound = .default
        let request = UNNotificationRequest(identifier: UUID().uuidString, content: content, trigger: nil)
        center.add(request)
    }
}

// MARK: - UI 组件

/// 状态点:实心圆 + 同色柔光晕环(比裸色点更有质感,弱视觉噪音)
final class StatusDot: NSView {
    private let halo = NSView()
    private let core = NSView()

    init() {
        super.init(frame: .zero)
        translatesAutoresizingMaskIntoConstraints = false
        wantsLayer = true
        for v in [halo, core] {
            v.translatesAutoresizingMaskIntoConstraints = false
            v.wantsLayer = true
            addSubview(v)
        }
        halo.layer?.cornerRadius = 7
        core.layer?.cornerRadius = 3

        NSLayoutConstraint.activate([
            widthAnchor.constraint(equalToConstant: 14),
            heightAnchor.constraint(equalToConstant: 14),
            halo.leadingAnchor.constraint(equalTo: leadingAnchor),
            halo.trailingAnchor.constraint(equalTo: trailingAnchor),
            halo.topAnchor.constraint(equalTo: topAnchor),
            halo.bottomAnchor.constraint(equalTo: bottomAnchor),
            core.centerXAnchor.constraint(equalTo: centerXAnchor),
            core.centerYAnchor.constraint(equalTo: centerYAnchor),
            core.widthAnchor.constraint(equalToConstant: 6),
            core.heightAnchor.constraint(equalToConstant: 6),
        ])
    }

    required init?(coder: NSCoder) { fatalError("init(coder:) has not been implemented") }

    func apply(_ color: NSColor) {
        halo.layer?.backgroundColor = color.withAlphaComponent(0.20).cgColor
        core.layer?.backgroundColor = color.cgColor
    }
}

/// 一行状态:● 标题 …………… 值
/// 一整行都可点的容器:鼠标移上去出圆角浅底 + 手型光标,点击触发动作。
///
/// 用 draw(_:) 画底色而不是 layer.backgroundColor —— 后者不会跟随浅色/深色
/// 外观自动切换,还得自己监听 viewDidChangeEffectiveAppearance。
final class ClickableRow: NSView {
    private let onClick: () -> Void
    private var hovering = false
    private var tracking: NSTrackingArea?

    init(accessibilityLabel: String, onClick: @escaping () -> Void) {
        self.onClick = onClick
        super.init(frame: .zero)
        translatesAutoresizingMaskIntoConstraints = false
        setAccessibilityRole(.button)
        setAccessibilityLabel(accessibilityLabel)
    }

    required init?(coder: NSCoder) { fatalError("init(coder:) has not been implemented") }

    override var isOpaque: Bool { false }

    override func updateTrackingAreas() {
        super.updateTrackingAreas()
        if let tracking { removeTrackingArea(tracking) }
        let area = NSTrackingArea(rect: bounds,
                                  options: [.mouseEnteredAndExited, .activeInActiveApp],
                                  owner: self)
        addTrackingArea(area)
        tracking = area
    }

    override func mouseEntered(with event: NSEvent) {
        hovering = true
        needsDisplay = true
    }

    override func mouseExited(with event: NSEvent) {
        hovering = false
        needsDisplay = true
    }

    override func mouseUp(with event: NSEvent) {
        // 只有松手时仍在行内才算点击 —— 和系统按钮的行为一致。
        if bounds.contains(convert(event.locationInWindow, from: nil)) { onClick() }
    }

    override func draw(_ dirtyRect: NSRect) {
        guard hovering else { return }
        NSColor.labelColor.withAlphaComponent(0.08).setFill()
        NSBezierPath(roundedRect: bounds, xRadius: 6, yRadius: 6).fill()
    }
}

final class StatusRow: NSView {
    private let dot = StatusDot()
    private let titleLabel = NSTextField(labelWithString: "")
    private let valueLabel = NSTextField(labelWithString: "—")

    init(_ title: String, emphasizedValue: Bool = false) {
        super.init(frame: .zero)
        translatesAutoresizingMaskIntoConstraints = false

        titleLabel.stringValue = title
        titleLabel.font = .systemFont(ofSize: 13, weight: .medium)
        titleLabel.textColor = .labelColor

        // 余额用等宽数字,刷新时数字不会左右跳动
        valueLabel.font = emphasizedValue
            ? .monospacedDigitSystemFont(ofSize: 13, weight: .semibold)
            : .systemFont(ofSize: 12, weight: .regular)
        valueLabel.textColor = emphasizedValue ? .labelColor : .secondaryLabelColor
        valueLabel.alignment = .right

        for v in [dot, titleLabel, valueLabel] {
            v.translatesAutoresizingMaskIntoConstraints = false
            addSubview(v)
        }
        valueLabel.setContentHuggingPriority(.defaultHigh, for: .horizontal)
        valueLabel.setContentCompressionResistancePriority(.required, for: .horizontal)

        NSLayoutConstraint.activate([
            heightAnchor.constraint(equalToConstant: 22),
            dot.leadingAnchor.constraint(equalTo: leadingAnchor),
            dot.centerYAnchor.constraint(equalTo: centerYAnchor),
            titleLabel.leadingAnchor.constraint(equalTo: dot.trailingAnchor, constant: 9),
            titleLabel.centerYAnchor.constraint(equalTo: centerYAnchor),
            valueLabel.trailingAnchor.constraint(equalTo: trailingAnchor),
            valueLabel.centerYAnchor.constraint(equalTo: centerYAnchor),
            valueLabel.leadingAnchor.constraint(greaterThanOrEqualTo: titleLabel.trailingAnchor, constant: 8),
        ])
    }

    required init?(coder: NSCoder) { fatalError("init(coder:) has not been implemented") }

    func update(color: NSColor, text: String) {
        dot.apply(color)
        valueLabel.stringValue = text
    }
}

/// 细分隔线
private func makeSeparator() -> NSBox {
    let box = NSBox()
    box.boxType = .separator
    box.translatesAutoresizingMaskIntoConstraints = false
    return box
}

// MARK: - 弹出面板

final class StatusPanelViewController: NSViewController {
    static let panelWidth: CGFloat = 288

    private let manager: DSHProcessManager
    private let onOpenWeb: () -> Void

    private let dshRow = StatusRow("DSH 后台服务")
    private let clawRow = StatusRow("微信 Clawbot")
    /// 状态区:两行服务状态 + 按设置排出来的余额行。
    private let statusStack = NSStackView()
    /// 余额行按设置动态生成(显示哪些、叫什么、什么顺序,见 Settings.swift)。
    private var balanceRows: [(row: PanelBalanceRow, view: StatusRow)] = []

    /// 开关服务:跑着时是「关闭服务」,停着时是「开启服务」。
    private let toggleButton = NSButton()
    /// 重启服务:只有服务在跑时才有意义。
    private let restartButton = NSButton()
    private let updatedLabel = NSTextField(labelWithString: "")

    /// 缓存值(由 WhaleApp 后台刷新)
    var cachedBalances = BalanceSnapshot.unreachable
    var lastUpdated: Date? = nil

    /// 行数变了(改了设置、接口多报了一个源),面板要重新量尺寸。
    var onLayoutChange: (() -> Void)?
    var onOpenSettings: (() -> Void)?

    init(manager: DSHProcessManager, onOpenWeb: @escaping () -> Void) {
        self.manager = manager
        self.onOpenWeb = onOpenWeb
        super.init(nibName: nil, bundle: nil)
        // 两行服务状态在这里就放进去,余额行永远排在它们后面 ——
        // 不依赖 refresh() 和 loadView() 谁先跑。
        for row in [dshRow, clawRow] { statusStack.addArrangedSubview(row) }
    }

    required init?(coder: NSCoder) { fatalError("init(coder:) has not been implemented") }

    override func loadView() {
        let root = NSView()
        root.translatesAutoresizingMaskIntoConstraints = false

        // ── 头部:鲸鱼 + 标题 + 刷新 ──
        // 菜单栏那张图是「实心圆角方块 + 抠掉鲸鱼」,当 logo 放大后是一坨深色块;
        // whale-glyph 是同一份美术资源反相出来的纯鲸鱼剪影,更适合做标题图标。
        let icon = NSImageView()
        icon.translatesAutoresizingMaskIntoConstraints = false
        if let image = NSImage(named: "whale-glyph") ?? NSImage(named: "menubar") {
            image.isTemplate = true
            icon.image = image
        }
        icon.contentTintColor = .labelColor
        icon.imageScaling = .scaleProportionallyUpOrDown

        let title = NSTextField(labelWithString: "DSH 小鲸鱼")
        title.font = .systemFont(ofSize: 13, weight: .semibold)
        title.textColor = .labelColor
        title.translatesAutoresizingMaskIntoConstraints = false

        // 原来这里有个「刷新状态」按钮,删了:面板本来就每 20 秒 + 每次弹出前
        // 自动刷新一次,开关服务后 toggleService() 还会自己再排一次,那个按钮
        // 能做的事系统已经全做了。onRefresh 本身保留 —— 那两处仍在用。

        // 整行可点 = 打开 DSH 界面,和下面那颗按钮同一个动作。
        let header = ClickableRow(accessibilityLabel: "打开 DSH 界面") { [weak self] in
            self?.openWeb()
        }
        header.toolTip = "打开 DSH 界面"
        for v in [icon, title] { header.addSubview(v) }

        // ── 状态区 ──
        statusStack.orientation = .vertical
        statusStack.alignment = .leading
        statusStack.spacing = 4
        statusStack.translatesAutoresizingMaskIntoConstraints = false
        statusStack.setHuggingPriority(.defaultLow, for: .horizontal)

        // ── 操作区 ──
        toggleButton.bezelStyle = .rounded
        toggleButton.controlSize = .regular
        toggleButton.target = self
        toggleButton.action = #selector(toggleService)
        toggleButton.translatesAutoresizingMaskIntoConstraints = false

        restartButton.title = "重启服务"
        restartButton.bezelStyle = .rounded
        restartButton.controlSize = .regular
        restartButton.target = self
        restartButton.action = #selector(restartService)
        restartButton.translatesAutoresizingMaskIntoConstraints = false

        let buttonRow = NSStackView(views: [toggleButton, restartButton])
        buttonRow.orientation = .horizontal
        buttonRow.distribution = .fillEqually
        buttonRow.spacing = 8
        buttonRow.translatesAutoresizingMaskIntoConstraints = false

        // 没有「停止服务」按钮:要停服务就退出小鲸鱼,退出时会问停不停。
        // 少一个按钮,也少一条「服务停了但小鲸鱼还亮着」的怪状态。
        let openButton = NSButton(title: "打开 DSH 界面", target: self, action: #selector(openWeb))
        openButton.bezelStyle = .rounded
        openButton.controlSize = .regular
        openButton.translatesAutoresizingMaskIntoConstraints = false

        // ── footer:更新时间 + 退出 ──
        updatedLabel.font = .systemFont(ofSize: 10)
        updatedLabel.textColor = .tertiaryLabelColor
        updatedLabel.translatesAutoresizingMaskIntoConstraints = false

        let quitButton = NSButton(title: "退出小鲸鱼", target: self, action: #selector(quitApp))
        quitButton.isBordered = false
        quitButton.font = .systemFont(ofSize: 10)
        quitButton.contentTintColor = .secondaryLabelColor
        quitButton.translatesAutoresizingMaskIntoConstraints = false

        let settingsButton = NSButton(title: "设置…", target: self, action: #selector(openSettings))
        settingsButton.isBordered = false
        settingsButton.font = .systemFont(ofSize: 10)
        settingsButton.contentTintColor = .secondaryLabelColor
        settingsButton.keyEquivalent = ","
        settingsButton.keyEquivalentModifierMask = .command
        settingsButton.toolTip = "选择要显示的余额 (⌘,)"
        settingsButton.translatesAutoresizingMaskIntoConstraints = false

        let footer = NSView()
        footer.translatesAutoresizingMaskIntoConstraints = false
        for v in [updatedLabel, settingsButton, quitButton] { footer.addSubview(v) }

        let topSeparator = makeSeparator()
        let bottomSeparator = makeSeparator()
        for v in [header, topSeparator, statusStack, bottomSeparator, buttonRow, openButton, footer] {
            root.addSubview(v)
        }

        let pad: CGFloat = 14
        NSLayoutConstraint.activate([
            root.widthAnchor.constraint(equalToConstant: Self.panelWidth),

            // header
            header.topAnchor.constraint(equalTo: root.topAnchor, constant: 8),
            // 左右各外扩 6pt,高度 30:hover 的圆角底需要落脚空间,否则紧贴
            // 文字很局促。图标再往里缩 6pt,视觉位置和原来一致。
            //
            // 上边距 12→8、高度 28→30:整块往上提 4pt,下界只上移 2pt,视觉上
            // 比原来更居中(原来底下留白偏多)。
            header.leadingAnchor.constraint(equalTo: root.leadingAnchor, constant: pad - 6),
            header.trailingAnchor.constraint(equalTo: root.trailingAnchor, constant: -(pad - 6)),
            header.heightAnchor.constraint(equalToConstant: 30),
            icon.leadingAnchor.constraint(equalTo: header.leadingAnchor, constant: 6),
            icon.centerYAnchor.constraint(equalTo: header.centerYAnchor),
            icon.widthAnchor.constraint(equalToConstant: 19),
            icon.heightAnchor.constraint(equalToConstant: 19),
            title.leadingAnchor.constraint(equalTo: icon.trailingAnchor, constant: 7),
            title.centerYAnchor.constraint(equalTo: header.centerYAnchor),
            // 横线往下挪:贴太近会让 hover 框显得被压住,留 8pt 更均衡。
            topSeparator.topAnchor.constraint(equalTo: header.bottomAnchor, constant: 8),
            topSeparator.leadingAnchor.constraint(equalTo: root.leadingAnchor, constant: pad),
            topSeparator.trailingAnchor.constraint(equalTo: root.trailingAnchor, constant: -pad),

            statusStack.topAnchor.constraint(equalTo: topSeparator.bottomAnchor, constant: 10),
            statusStack.leadingAnchor.constraint(equalTo: root.leadingAnchor, constant: pad),
            statusStack.trailingAnchor.constraint(equalTo: root.trailingAnchor, constant: -pad),

            bottomSeparator.topAnchor.constraint(equalTo: statusStack.bottomAnchor, constant: 11),
            bottomSeparator.leadingAnchor.constraint(equalTo: root.leadingAnchor, constant: pad),
            bottomSeparator.trailingAnchor.constraint(equalTo: root.trailingAnchor, constant: -pad),

            buttonRow.topAnchor.constraint(equalTo: bottomSeparator.bottomAnchor, constant: 11),
            buttonRow.leadingAnchor.constraint(equalTo: root.leadingAnchor, constant: pad),
            buttonRow.trailingAnchor.constraint(equalTo: root.trailingAnchor, constant: -pad),

            openButton.topAnchor.constraint(equalTo: buttonRow.bottomAnchor, constant: 7),
            openButton.leadingAnchor.constraint(equalTo: root.leadingAnchor, constant: pad),
            openButton.trailingAnchor.constraint(equalTo: root.trailingAnchor, constant: -pad),

            footer.topAnchor.constraint(equalTo: openButton.bottomAnchor, constant: 9),
            footer.leadingAnchor.constraint(equalTo: root.leadingAnchor, constant: pad),
            footer.trailingAnchor.constraint(equalTo: root.trailingAnchor, constant: -pad),
            footer.heightAnchor.constraint(equalToConstant: 16),
            footer.bottomAnchor.constraint(equalTo: root.bottomAnchor, constant: -11),
            updatedLabel.leadingAnchor.constraint(equalTo: footer.leadingAnchor),
            updatedLabel.centerYAnchor.constraint(equalTo: footer.centerYAnchor),
            quitButton.trailingAnchor.constraint(equalTo: footer.trailingAnchor),
            quitButton.centerYAnchor.constraint(equalTo: footer.centerYAnchor),
            settingsButton.trailingAnchor.constraint(equalTo: quitButton.leadingAnchor, constant: -10),
            settingsButton.centerYAnchor.constraint(equalTo: footer.centerYAnchor),
        ])

        // 各状态行撑满面板宽度
        for row in [dshRow, clawRow] {
            row.widthAnchor.constraint(equalTo: statusStack.widthAnchor).isActive = true
        }

        self.view = root
        rebuildBalanceRowsIfNeeded()
    }

    /// 按设置和最近一次余额数据排出余额行。行没变就什么都不做;变了返回 true。
    @discardableResult
    private func rebuildBalanceRowsIfNeeded() -> Bool {
        let settings = WhaleSettings.shared
        var changed = false
        let wantClaw = settings.showClawbot
        if clawRow.isHidden == wantClaw {
            clawRow.isHidden = !wantClaw
            changed = true
        }
        let wanted = BalanceLayout.visibleRows(saved: settings.balanceSources, available: cachedBalances.sources)
        guard wanted != balanceRows.map(\.row) else { return changed }
        for (_, view) in balanceRows {
            statusStack.removeArrangedSubview(view)
            view.removeFromSuperview()
        }
        balanceRows = wanted.map { row in
            let view = StatusRow(row.title, emphasizedValue: true)
            statusStack.addArrangedSubview(view)
            view.widthAnchor.constraint(equalTo: statusStack.widthAnchor).isActive = true
            return (row, view)
        }
        return true
    }

    /// 用缓存/异步结果刷新(不阻塞)
    func refresh() {
        let dshRunning = manager.cachedDshRunning
        dshRow.update(color: dshRunning ? .systemGreen : .systemRed,
                      text: dshRunning ? "运行中" : "未运行")
        // 开关按钮随状态换标题。停着的时候「开启服务」是主操作,给它默认键
        // (强调色填充);跑着的时候标题变成「关闭服务」,就把默认键摘掉 ——
        // 免得一个回车把服务给关了,也不该让偏破坏性的动作顶着强调色。
        toggleButton.title = dshRunning ? "关闭服务" : "开启服务"
        toggleButton.keyEquivalent = dshRunning ? "" : "\r"
        restartButton.isEnabled = dshRunning

        let claw = StatusChecker.clawbot()
        clawRow.update(color: claw.running ? .systemGreen : .systemRed, text: claw.detail)

        if rebuildBalanceRowsIfNeeded() { onLayoutChange?() }
        // 健康度判断在 BalanceReading 里:额度型按剩余比例算,预付型按绝对档位。
        for (row, view) in balanceRows {
            let reading = cachedBalances.sources[row.id]?.reading ?? .unavailable
            view.update(color: reading.dotColor, text: reading.text)
        }

        if let lastUpdated {
            let seconds = Int(Date().timeIntervalSince(lastUpdated))
            updatedLabel.stringValue = seconds < 5 ? "刚刚更新" : "\(seconds) 秒前更新"
        } else {
            updatedLabel.stringValue = ""
        }
    }

    /// 状态数据的拉取入口(开关服务、重启后由本类主动调用)。
    var onRefresh: (() -> Void)?

    @objc private func toggleService() {
        guard manager.isRunning else {
            manager.start(openUI: true)
            scheduleRefresh(after: 2.5)
            return
        }
        // stop() 要轮询到端口真的放开,最多阻塞 6 秒 —— 绝不能压在主线程上,
        // 否则点一下面板就卡住不动。
        toggleButton.isEnabled = false
        toggleButton.title = "正在关闭…"
        DispatchQueue.global(qos: .userInitiated).async { [weak self] in
            self?.manager.stop()
            DispatchQueue.main.async {
                self?.toggleButton.isEnabled = true
                self?.onRefresh?()
            }
        }
    }

    @objc private func restartService() {
        manager.restart()          // 自己就是异步的
        scheduleRefresh(after: 3)
    }

    /// 动作之后多刷几次:服务起停要好几秒,一次刷新往往还看不到结果。
    private func scheduleRefresh(after seconds: TimeInterval) {
        for delay in [seconds, seconds + 4, seconds + 9] {
            DispatchQueue.main.asyncAfter(deadline: .now() + delay) { [weak self] in
                self?.onRefresh?()
            }
        }
    }


    @objc private func openWeb() { onOpenWeb() }

    @objc private func openSettings() { onOpenSettings?() }

    @objc private func quitApp() {
        let alert = NSAlert()
        alert.messageText = "退出小鲸鱼"
        alert.informativeText = "DSH 服务由小鲸鱼托管,退出时可以选择是否一并停止。"
        alert.addButton(withTitle: "退出并停止服务")
        alert.addButton(withTitle: "退出但留下服务")
        alert.addButton(withTitle: "取消")
        let resp = alert.runModal()
        if resp == .alertFirstButtonReturn {
            manager.stop()
            NSApp.terminate(nil)
        } else if resp == .alertSecondButtonReturn {
            NSApp.terminate(nil)
        }
    }
}

// MARK: - 可获得键盘焦点的无边框面板
//
// .borderless 窗口默认 canBecomeKey == false。让它能成为 key window 后,
// 「点到别处」就由系统的 windowDidResignKey 负责收回面板 —— 不必再自己
// 装全局/本地鼠标监视器去猜哪一次点击算「外部」。

final class WhalePanel: NSPanel {
    override var canBecomeKey: Bool { true }
}

// MARK: - 菜单栏 App

final class WhaleApp: NSObject, NSApplicationDelegate, UNUserNotificationCenterDelegate, NSWindowDelegate {
    private var statusItem: NSStatusItem!
    private var timer: Timer?
    private let manager = DSHProcessManager()
    private var panel: WhalePanel!
    private var panelVC: StatusPanelViewController!
    private let settingsWindow = SettingsWindowController(settings: WhaleSettings.shared)

    /// 面板最后一次关闭的时刻。
    ///
    /// 面板开着时点状态栏图标,会先触发 windowDidResignKey 把面板收掉,紧接着
    /// 按钮的 action 才跑 —— 那时面板已经不可见,天真的实现会把它当成「关着,
    /// 那就打开吧」,于是面板永远关不上。用一个极短的时间窗把这次点击认成
    /// 「刚刚已经关过了」,两种事件顺序就都正确了。
    private var lastCloseAt = Date.distantPast

    /// 可拉伸的圆角矩形遮罩:中间一格拉伸,四角保持原样,所以一张小图能罩住任意尺寸。
    private static func roundedMask(radius: CGFloat) -> NSImage {
        let side = radius * 2 + 1
        let image = NSImage(size: NSSize(width: side, height: side), flipped: false) { rect in
            NSColor.black.setFill()
            NSBezierPath(roundedRect: rect, xRadius: radius, yRadius: radius).fill()
            return true
        }
        image.capInsets = NSEdgeInsets(top: radius, left: radius, bottom: radius, right: radius)
        image.resizingMode = .stretch
        return image
    }

    func applicationDidFinishLaunching(_ notification: Notification) {
        NSApp.setActivationPolicy(.accessory)
        if let i = CommandLine.arguments.firstIndex(of: "--snapshot"), i + 1 < CommandLine.arguments.count {
            renderSnapshots(to: URL(fileURLWithPath: CommandLine.arguments[i + 1]))
            return
        }
        UNUserNotificationCenter.current().delegate = self

        statusItem = NSStatusBar.system.statusItem(withLength: 22)
        if let button = statusItem.button {
            if let icon = NSImage(named: "menubar") {
                icon.isTemplate = true
                button.image = icon
                button.image?.size = NSSize(width: 16, height: 16)
                button.imagePosition = .imageOnly
            } else {
                button.title = "🐋"
                button.font = NSFont.systemFont(ofSize: 14)
            }
            button.action = #selector(togglePanel)
            button.target = self
        }

        panelVC = StatusPanelViewController(manager: manager, onOpenWeb: { [weak self] in self?.openWeb() })
        panelVC.onRefresh = { [weak self] in self?.refreshCache() }
        panelVC.onLayoutChange = { [weak self] in self?.fitPanel() }
        panelVC.onOpenSettings = { [weak self] in self?.openSettings() }
        NotificationCenter.default.addObserver(forName: .whaleSettingsChanged, object: nil, queue: .main) { [weak self] _ in
            self?.panelVC.refresh()
        }

        // 毛玻璃圆角容器。
        //
        // 圆角用 maskImage 而不是 layer.cornerRadius:模糊是窗口服务器画的,拿
        // CALayer 去裁它,边缘会透出一圈没被裁干净的底,在浅色模式下就是那道很淡
        // 的白包边。maskImage 直接把模糊本身裁成圆角,边缘外是真正的透明。
        let effect = NSVisualEffectView()
        effect.material = .popover
        effect.state = .active
        effect.blendingMode = .behindWindow
        effect.maskImage = Self.roundedMask(radius: 12)

        let content = panelVC.view
        content.translatesAutoresizingMaskIntoConstraints = false
        effect.addSubview(content)
        NSLayoutConstraint.activate([
            content.leadingAnchor.constraint(equalTo: effect.leadingAnchor),
            content.trailingAnchor.constraint(equalTo: effect.trailingAnchor),
            content.topAnchor.constraint(equalTo: effect.topAnchor),
            content.bottomAnchor.constraint(equalTo: effect.bottomAnchor),
        ])

        let size = content.fittingSize
        let panel = WhalePanel(contentRect: NSRect(origin: .zero, size: size),
                               styleMask: [.borderless],
                               backing: .buffered,
                               defer: false)
        panel.contentView = effect
        panel.isFloatingPanel = true
        panel.level = .statusBar
        panel.collectionBehavior = [.canJoinAllSpaces, .fullScreenAuxiliary]
        panel.backgroundColor = .clear
        panel.isOpaque = false
        panel.hasShadow = true
        panel.isMovable = false
        panel.hidesOnDeactivate = false
        panel.animationBehavior = .utilityWindow
        panel.delegate = self
        panel.setContentSize(size)
        self.panel = panel

        // 后台异步刷新缓存(每 20 秒 + 弹出前),绝不阻塞主线程
        refreshCache()
        timer = Timer.scheduledTimer(withTimeInterval: 20, repeats: true) { [weak self] _ in
            self?.refreshCache()
            // 顺手兜底:端口空着且不是用户自己停的,就拉起来(见 superviseIfNeeded)
            self?.manager.superviseIfNeeded()
        }

        // 启动即检查:服务没在跑就**直接起**,不问。
        //
        // 这里原来弹一个「是否现在启动?」的 NSAlert —— 那等于把开机自启废掉了:
        // 登录时没人在键盘前面,模态框就一直挂着,dsh 永远没起来,微信 bot 收不到
        // 消息。表现是:LaunchAgent 明明把小鲸鱼拉起来了,
        // 服务却没有。小鲸鱼存在的意义就是守着 dsh,没什么要问的。
        // openUI: false —— 登录时不要弹出 webapp 窗口。
        refreshCache {
            guard !self.manager.isRunning else { return }
            NSLog("DSHWhale: dsh 未运行,自动拉起")
            self.manager.start(openUI: false)
        }

        // `open -a DSHWhale --args --settings`:启动后直接打开设置窗口。
        if CommandLine.arguments.contains("--settings") {
            DispatchQueue.main.asyncAfter(deadline: .now() + 1.5) { [weak self] in self?.openSettings() }
        }
    }

    private func openSettings() {
        closePanel()
        settingsWindow.show(panelVC.cachedBalances)
    }

    /// `DSHWhale --snapshot <目录> [--demo]`:把面板和设置页渲染成 PNG 后退出。
    /// 不建状态栏图标、不碰 dsh 进程 —— 用来检查排版、给 README 截图。
    /// `--demo` 用虚构的余额,不读本机接口。
    private func renderSnapshots(to dir: URL) {
        try? FileManager.default.createDirectory(at: dir, withIntermediateDirectories: true)
        let render = { (running: Bool, snapshot: BalanceSnapshot) in
            DispatchQueue.main.async {
                self.manager.cachedDshRunning = running
                let vc = StatusPanelViewController(manager: self.manager, onOpenWeb: {})
                vc.cachedBalances = snapshot
                vc.lastUpdated = Date()
                vc.refresh()
                Self.writeCard(vc.view, to: dir.appendingPathComponent("panel.png"))

                let model = SettingsModel(settings: WhaleSettings.shared)
                model.reload(snapshot)
                let hosting = NSHostingView(rootView: SettingsView(model: model, settings: WhaleSettings.shared))
                hosting.frame.size = hosting.fittingSize
                // 离屏渲染要先放进一个窗口里,SwiftUI 才会真的布局。
                let window = NSWindow(contentRect: hosting.frame, styleMask: [.titled], backing: .buffered, defer: false)
                window.appearance = NSAppearance(named: .aqua)
                window.contentView = hosting
                DispatchQueue.main.asyncAfter(deadline: .now() + 0.5) {
                    Self.writeCard(hosting, to: dir.appendingPathComponent("settings.png"))
                    NSApp.terminate(nil)
                }
            }
        }
        if CommandLine.arguments.contains("--demo") {
            render(true, Self.demoSnapshot)
            return
        }
        StatusChecker.dshWeb { status in
            StatusChecker.balances { snapshot in render(status.running, snapshot) }
        }
    }

    private static let demoSnapshot: BalanceSnapshot = {
        func source(_ id: String, _ label: String, _ kind: String, _ reading: BalanceReading) -> (String, BalanceSourceInfo) {
            (id, BalanceSourceInfo(id: id, label: label, kind: kind, reading: reading))
        }
        return BalanceSnapshot(reachable: true, sources: Dictionary(uniqueKeysWithValues: [
            source("deepseek", "DeepSeek", "prepaid", BalanceReading(text: "¥86.40", value: 86.4, limit: nil)),
            source("openrouter", "OpenRouter", "prepaid", BalanceReading(text: "$12.50", value: 12.5, limit: nil, cnyScale: 7)),
            source("codex", "ChatGPT 5h 余量", "quota", BalanceReading(text: "72%", value: 72, limit: 100)),
        ]))
    }()

    /// 浅色外观、白底圆角,放进 README 在深色模式下也看得清。
    private static func writeCard(_ content: NSView, to url: URL) {
        content.appearance = NSAppearance(named: .aqua)
        content.layoutSubtreeIfNeeded()
        let size = content.fittingSize
        let card = NSView(frame: NSRect(origin: .zero, size: size))
        card.appearance = NSAppearance(named: .aqua)
        card.wantsLayer = true
        card.layer?.backgroundColor = NSColor.white.cgColor
        card.layer?.cornerRadius = 12
        content.removeFromSuperview()
        content.translatesAutoresizingMaskIntoConstraints = true
        content.frame = card.bounds
        card.addSubview(content)
        card.layoutSubtreeIfNeeded()
        guard let rep = card.bitmapImageRepForCachingDisplay(in: card.bounds) else { return }
        card.cacheDisplay(in: card.bounds, to: rep)
        try? rep.representation(using: .png, properties: [:])?.write(to: url)
    }

    /// 行数变了之后重新量面板尺寸;开着的话贴回状态栏图标下面。
    private func fitPanel() {
        guard let panel else { return }
        panel.setContentSize(panelVC.view.fittingSize)
        if panel.isVisible { positionPanel() }
    }

    /// 后台刷新所有状态缓存(异步,主线程只更新 UI)
    private func refreshCache(completion: (() -> Void)? = nil) {
        StatusChecker.dshWeb { [weak self] status in
            DispatchQueue.main.async {
                guard let self else { return }
                self.manager.cachedDshRunning = status.running
                self.panelVC.lastUpdated = Date()
                self.panelVC.refresh()
                completion?()
            }
        }
        StatusChecker.balances { [weak self] reading in
            DispatchQueue.main.async {
                guard let self else { return }
                self.panelVC.cachedBalances = reading
                self.panelVC.lastUpdated = Date()
                self.panelVC.refresh()
                self.settingsWindow.update(reading)
            }
        }
    }

    // MARK: 面板开关

    @objc private func togglePanel() {
        if panel.isVisible {
            closePanel()
            return
        }
        // resignKey 刚刚因为这次点击收起了面板 —— 这一下是「收」,不是「开」
        if Date().timeIntervalSince(lastCloseAt) < 0.25 { return }
        showPanel()
    }

    private func showPanel() {
        panelVC.refresh()
        panel.setContentSize(panelVC.view.fittingSize)
        positionPanel()

        NSApp.activate(ignoringOtherApps: true)
        panel.makeKeyAndOrderFront(nil)
        refreshCache()
    }

    /// 把面板挂在状态栏图标正下方。
    private func positionPanel() {
        guard let button = statusItem.button, let buttonWindow = button.window else { return }
        let anchor = buttonWindow.convertToScreen(button.convert(button.bounds, to: nil))
        let size = panel.frame.size
        var x = anchor.midX - size.width / 2
        // 图标靠屏幕右缘时,面板不要跑出屏幕
        if let screen = buttonWindow.screen ?? NSScreen.main {
            let visible = screen.visibleFrame
            x = min(max(x, visible.minX + 8), visible.maxX - size.width - 8)
        }
        panel.setFrameOrigin(NSPoint(x: x, y: anchor.minY - size.height - 6))
    }

    private func closePanel() {
        guard panel.isVisible else { return }
        panel.orderOut(nil)
        lastCloseAt = Date()
    }

    /// 点到面板以外的任何地方(别的 App、桌面、状态栏图标)都会让面板失去 key
    func windowDidResignKey(_ notification: Notification) {
        guard (notification.object as? NSWindow) === panel else { return }
        closePanel()
    }

    private func openWeb() { openDSHWebApp() }

    func userNotificationCenter(_ center: UNUserNotificationCenter, willPresent notification: UNNotification, withCompletionHandler completionHandler: @escaping (UNNotificationPresentationOptions) -> Void) {
        completionHandler([.banner, .sound])
    }
}

// MARK: - 入口

let app = NSApplication.shared
let delegate = WhaleApp()
app.delegate = delegate
app.run()
