import AppKit
import Foundation

// MARK: - 余额读数

/// 一个余额源的读数。`limit` 只有额度型才有,
/// 有上限时按剩余比例判健康度 —— 用绝对值会把 $9.95/$10 判成快没钱了。
struct BalanceReading {
    let text: String        // 如 ¥50.70 / $9.95 / 85%
    let value: Double?      // nil = 取不到
    let limit: Double?      // nil = 预付型,没有上限
    /// 绝对档位判色前的币种折算系数。档位表(<5 红、<20 黄)是按人民币定的,
    /// 美元余额要按 1:7 折算后再比,否则同一账户在网页插件和小鲸鱼上颜色对不上。
    var cnyScale: Double = 1

    static let unavailable = BalanceReading(text: "—", value: nil, limit: nil)

    /// 额度灯颜色:有上限看比例,没上限按人民币档位(美元先折算)。
    var dotColor: NSColor {
        guard let value else { return .systemRed }
        if let limit, limit > 0 {
            let fraction = value / limit
            if fraction < 0.1 { return .systemRed }
            if fraction < 0.25 { return .systemYellow }
            return .systemGreen
        }
        let cny = value * cnyScale
        if cny < 5 { return .systemRed }
        if cny < 20 { return .systemYellow }
        return .systemGreen
    }
}

/// 一次 /api/model-balance 的结果。
struct BalanceSnapshot {
    /// 接口有没有回应。false = DSH 没在跑,或者没装提供这个接口的余额插件。
    let reachable: Bool
    let sources: [String: BalanceSourceInfo]

    static let unreachable = BalanceSnapshot(reachable: false, sources: [:])
}

/// 面板上的一行余额。
struct PanelBalanceRow: Equatable {
    let id: String
    let title: String
}

// MARK: - 设置(UserDefaults)
//
// 面板里显示哪些余额、叫什么名字、按什么顺序,全由这里决定 —— 代码里不写死任何
// 厂商。余额源本身来自 DSH 的 GET /api/model-balance:它返回什么,设置里就能列什么。

/// 用户对一个余额源的设置。`id` 是 /api/model-balance 里 `providers` 下的键。
struct BalanceSourceSetting: Codable, Equatable, Identifiable {
    var id: String
    /// 面板上显示的名字;空 = 用接口给的 label 推出来的默认名。
    var title: String
    var visible: Bool
}

/// /api/model-balance 对一个余额源的报告。
struct BalanceSourceInfo {
    let id: String
    /// 接口给的名字,如 "DeepSeek"。
    let label: String
    /// "prepaid"(预付,没有上限)或 "quota"(额度,有上限)。
    let kind: String?
    let reading: BalanceReading
}

/// 启动 dsh 时注入的 DSH_PERMISSION_MODE。
enum PermissionMode: String, CaseIterable, Identifiable {
    /// 不注入,由 DSH 自己决定(默认 workspace-write:只能写工作区,操作要审批)。
    case dshDefault = ""
    /// danger-full-access:全盘读写,并且不再弹审批。
    case fullAccess = "danger-full-access"

    var id: String { rawValue }

    var title: String {
        switch self {
        case .dshDefault: return "跟随 DSH 默认(需要审批)"
        case .fullAccess: return "完全访问(不弹审批)"
        }
    }

    /// nil = 不设这个环境变量。
    var environmentValue: String? { rawValue.isEmpty ? nil : rawValue }
}

extension Notification.Name {
    static let whaleSettingsChanged = Notification.Name("DSHWhaleSettingsChanged")
}

final class WhaleSettings: ObservableObject {
    static let shared = WhaleSettings()

    private enum Key {
        static let balanceSources = "balanceSources"
        static let showClawbot = "showClawbot"
        static let permissionMode = "permissionMode"
    }

    private let defaults: UserDefaults

    /// nil = 从没配置过:接口报告的余额源全部显示。
    @Published var balanceSources: [BalanceSourceSetting]? { didSet { save() } }
    @Published var showClawbot: Bool { didSet { save() } }
    @Published var permissionMode: PermissionMode { didSet { save() } }

    init(defaults: UserDefaults = .standard) {
        self.defaults = defaults
        if let data = defaults.data(forKey: Key.balanceSources) {
            balanceSources = try? JSONDecoder().decode([BalanceSourceSetting].self, from: data)
        } else {
            balanceSources = nil
        }
        showClawbot = defaults.object(forKey: Key.showClawbot) as? Bool ?? true
        permissionMode = PermissionMode(rawValue: defaults.string(forKey: Key.permissionMode) ?? "") ?? .dshDefault
    }

    private func save() {
        if let balanceSources, let data = try? JSONEncoder().encode(balanceSources) {
            defaults.set(data, forKey: Key.balanceSources)
        } else {
            defaults.removeObject(forKey: Key.balanceSources)
        }
        defaults.set(showClawbot, forKey: Key.showClawbot)
        defaults.set(permissionMode.rawValue, forKey: Key.permissionMode)
        NotificationCenter.default.post(name: .whaleSettingsChanged, object: self)
    }
}

// MARK: - 设置 × 接口数据 → 面板上的行

enum BalanceLayout {
    /// 从没配置过时的默认顺序。不在表里的按 id 字母序排在后面。
    static let preferredOrder = ["deepseek", "openrouter", "codex"]

    /// 接口的 label 已经说明了是余额还是额度就原样用,否则按类型补一个词。
    static func defaultTitle(label: String, kind: String?) -> String {
        if ["余额", "余量", "额度"].contains(where: label.contains) { return label }
        return kind == "quota" ? "\(label) 额度" : "\(label) 余额"
    }

    static func defaultTitle(for id: String, info: BalanceSourceInfo?) -> String {
        guard let info else { return id }
        return defaultTitle(label: info.label, kind: info.kind)
    }

    static func sortedByDefault(_ ids: [String]) -> [String] {
        ids.sorted { a, b in
            let ia = preferredOrder.firstIndex(of: a) ?? Int.max
            let ib = preferredOrder.firstIndex(of: b) ?? Int.max
            return ia != ib ? ia < ib : a < b
        }
    }

    /// 设置页要列出的全部余额源:已保存的按原顺序在前(接口暂时没报的也留着,
    /// 不然 DSH 一停设置就丢了),接口新报出来的追加在后面。
    ///
    /// 新出现的源:从没配置过就显示;配置过则默认隐藏 —— 用户挑好的面板不该
    /// 因为装了个新插件就自己多出一行,想看就去设置里勾上。
    static func editableList(saved: [BalanceSourceSetting]?,
                             available: [String: BalanceSourceInfo]) -> [BalanceSourceSetting] {
        var list = saved ?? []
        let known = Set(list.map(\.id))
        let fresh = sortedByDefault(available.keys.filter { !known.contains($0) })
        list += fresh.map { BalanceSourceSetting(id: $0, title: "", visible: saved == nil) }
        return list
    }

    /// 面板上实际显示的行(按顺序)。接口没报的源照样显示,值是「—」。
    static func visibleRows(saved: [BalanceSourceSetting]?,
                            available: [String: BalanceSourceInfo]) -> [PanelBalanceRow] {
        editableList(saved: saved, available: available)
            .filter(\.visible)
            .map { setting in
                let custom = setting.title.trimmingCharacters(in: .whitespaces)
                return PanelBalanceRow(id: setting.id,
                                       title: custom.isEmpty ? defaultTitle(for: setting.id, info: available[setting.id]) : custom)
            }
    }
}
