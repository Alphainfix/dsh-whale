import AppKit
import SwiftUI

// MARK: - 设置窗口

/// 设置页的编辑状态。列表里的改动即时写回 WhaleSettings,面板跟着变。
final class SettingsModel: ObservableObject {
    @Published var rows: [BalanceSourceSetting] = []
    @Published private(set) var snapshot = BalanceSnapshot.unreachable
    let settings: WhaleSettings

    init(settings: WhaleSettings) {
        self.settings = settings
    }

    /// 打开窗口时重建列表。
    func reload(_ snapshot: BalanceSnapshot) {
        self.snapshot = snapshot
        rows = BalanceLayout.editableList(saved: settings.balanceSources, available: snapshot.sources)
    }

    /// 窗口开着时余额刷新了:更新读数,把新出现的源补进列表,不打断正在编辑的内容。
    func update(_ snapshot: BalanceSnapshot) {
        self.snapshot = snapshot
        let base = settings.balanceSources == nil ? nil : rows
        let merged = BalanceLayout.editableList(saved: base, available: snapshot.sources)
        if merged != rows { rows = merged }
    }

    /// 只有真的改了才写回。打开窗口看一眼不算配置 —— 否则「从没配置过、
    /// 新源自动显示」这条规则会因为一次误开就失效。
    func commit() {
        let untouched = BalanceLayout.editableList(saved: settings.balanceSources, available: snapshot.sources)
        guard rows != untouched else { return }
        settings.balanceSources = rows
    }

    func resetToDefault() {
        settings.balanceSources = nil
        rows = BalanceLayout.editableList(saved: nil, available: snapshot.sources)
    }

    func remove(_ id: String) {
        rows.removeAll { $0.id == id }
    }

    /// 上移(-1)或下移(+1)一格。
    func move(_ id: String, by offset: Int) {
        guard let i = rows.firstIndex(where: { $0.id == id }) else { return }
        let j = i + offset
        guard rows.indices.contains(j) else { return }
        rows.swapAt(i, j)
    }

    func placeholder(for id: String) -> String {
        BalanceLayout.defaultTitle(for: id, info: snapshot.sources[id])
    }

    func detail(for id: String) -> String {
        guard let info = snapshot.sources[id] else {
            return snapshot.reachable ? "\(id) · 余额接口里已经没有这一项" : id
        }
        let kind: String?
        switch info.kind {
        case "prepaid": kind = "预付"
        case "quota": kind = "额度"
        default: kind = nil
        }
        return [id, kind, info.reading.text].compactMap { $0 }.joined(separator: " · ")
    }

    /// 只有接口明确不再报告的源才能删。DSH 没在跑时什么都读不到,
    /// 那不代表这些源没了 —— 这时候给删除键,等于诱导用户把设置清空。
    func removable(_ id: String) -> Bool {
        snapshot.reachable && snapshot.sources[id] == nil
    }
}

struct SettingsView: View {
    @ObservedObject var model: SettingsModel
    @ObservedObject var settings: WhaleSettings

    var body: some View {
        VStack(alignment: .leading, spacing: 12) {
            Text("余额显示").font(.headline)
            Text("列表来自 DSH 的余额接口。勾选的会显示在面板上，名字可以直接改，右边的箭头调整顺序。")
                .font(.callout)
                .foregroundStyle(.secondary)
                .fixedSize(horizontal: false, vertical: true)

            if !model.snapshot.reachable {
                Label("现在读不到余额接口（DSH 没在运行，或者没装提供余额的插件），下面是上次保存的设置。",
                      systemImage: "exclamationmark.triangle")
                    .font(.callout)
                    .foregroundStyle(.orange)
                    .fixedSize(horizontal: false, vertical: true)
            }

            GroupBox {
                if model.rows.isEmpty {
                    Text("还没有可显示的余额源。")
                        .foregroundStyle(.secondary)
                        .frame(maxWidth: .infinity, minHeight: 44)
                } else {
                    VStack(spacing: 0) {
                        ForEach(Array(model.rows.enumerated()), id: \.element.id) { index, row in
                            if index > 0 { Divider() }
                            SourceRow(row: binding(for: row.id),
                                      placeholder: model.placeholder(for: row.id),
                                      detail: model.detail(for: row.id),
                                      canMoveUp: index > 0,
                                      canMoveDown: index < model.rows.count - 1,
                                      removable: model.removable(row.id),
                                      onMove: { model.move(row.id, by: $0) },
                                      onRemove: { model.remove(row.id) })
                        }
                    }
                }
            }

            HStack {
                Button("恢复默认") { model.resetToDefault() }
                    .help("显示余额接口报告的全部余额源，名字恢复成默认")
                Spacer()
            }

            Divider().padding(.vertical, 4)

            Text("其他").font(.headline)
            Toggle("显示微信 Clawbot 状态", isOn: $settings.showClawbot)
            Picker("启动 DSH 的权限模式", selection: $settings.permissionMode) {
                ForEach(PermissionMode.allCases) { mode in
                    Text(mode.title).tag(mode)
                }
            }
            Text("只影响小鲸鱼下一次拉起 DSH；想马上生效，在面板里点「重启服务」。")
                .font(.callout)
                .foregroundStyle(.secondary)
                .fixedSize(horizontal: false, vertical: true)
        }
        .padding(20)
        .frame(width: 460)
        .onChange(of: model.rows) { _ in model.commit() }
    }

    /// 按 id 取行的绑定。按下标取的话,排序一变正在编辑的输入框就指到别的行上了。
    private func binding(for id: String) -> Binding<BalanceSourceSetting> {
        Binding(
            get: { model.rows.first { $0.id == id } ?? BalanceSourceSetting(id: id, title: "", visible: false) },
            set: { value in
                guard let i = model.rows.firstIndex(where: { $0.id == id }) else { return }
                model.rows[i] = value
            })
    }
}

private struct SourceRow: View {
    @Binding var row: BalanceSourceSetting
    let placeholder: String
    let detail: String
    let canMoveUp: Bool
    let canMoveDown: Bool
    let removable: Bool
    let onMove: (Int) -> Void
    let onRemove: () -> Void

    var body: some View {
        HStack(spacing: 10) {
            Toggle("", isOn: $row.visible)
                .labelsHidden()
                .toggleStyle(.checkbox)
                .help(row.visible ? "显示在面板上" : "不显示")
            VStack(alignment: .leading, spacing: 2) {
                TextField(placeholder, text: $row.title)
                    .textFieldStyle(.plain)
                    .font(.system(size: 13, weight: .medium))
                Text(detail)
                    .font(.caption)
                    .foregroundStyle(.secondary)
            }
            Spacer(minLength: 8)
            if removable {
                Button(action: onRemove) { Image(systemName: "minus.circle") }
                    .buttonStyle(.borderless)
                    .help("从列表里移除")
            }
            Button { onMove(-1) } label: { Image(systemName: "chevron.up") }
                .buttonStyle(.borderless)
                .disabled(!canMoveUp)
                .help("上移")
            Button { onMove(1) } label: { Image(systemName: "chevron.down") }
                .buttonStyle(.borderless)
                .disabled(!canMoveDown)
                .help("下移")
        }
        .padding(.vertical, 6)
        .padding(.horizontal, 4)
    }
}

final class SettingsWindowController: NSObject, NSWindowDelegate {
    private let model: SettingsModel
    private var window: NSWindow?

    init(settings: WhaleSettings) {
        model = SettingsModel(settings: settings)
    }

    var isVisible: Bool { window?.isVisible ?? false }

    func show(_ snapshot: BalanceSnapshot) {
        model.reload(snapshot)
        let window = self.window ?? makeWindow()
        self.window = window
        // 小鲸鱼没有 Dock 图标(.accessory),不主动激活的话窗口会开在别的 App 后面。
        NSApp.activate(ignoringOtherApps: true)
        if !window.isVisible { window.center() }
        window.makeKeyAndOrderFront(nil)
    }

    func update(_ snapshot: BalanceSnapshot) {
        guard isVisible else { return }
        model.update(snapshot)
    }

    private func makeWindow() -> NSWindow {
        let hosting = NSHostingController(rootView: SettingsView(model: model, settings: model.settings))
        let window = NSWindow(contentViewController: hosting)
        window.title = "小鲸鱼设置"
        window.styleMask = [.titled, .closable]
        window.isReleasedWhenClosed = false
        window.delegate = self
        return window
    }
}
