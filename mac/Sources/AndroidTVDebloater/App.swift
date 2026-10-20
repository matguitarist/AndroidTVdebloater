import SwiftUI

@main
struct AndroidTVDebloaterApp: App {
    @NSApplicationDelegateAdaptor(AppDelegate.self) private var delegate
    @State private var model = AppModel()
    private let updater = AppUpdater.shared

    var body: some Scene {
        Window("Android TV Debloater", id: "main") {
            ContentView()
                .environment(model)
                .frame(minWidth: 860, minHeight: 600)
                .task {
                    updater.isBusy = { [model] in model.isBusy }
                    await Catalog.refreshFromGitHub()
                }
        }
        .defaultSize(width: 1060, height: 760)
        .commands {
            CommandGroup(after: .appInfo) {
                Button(L("Mac_CheckForUpdatesMenu")) { updater.checkForUpdates() }
                    .disabled(!updater.canCheckForUpdates)
            }
            CommandGroup(replacing: .newItem) {}
        }
    }
}

final class AppDelegate: NSObject, NSApplicationDelegate {
    func applicationShouldTerminateAfterLastWindowClosed(_ sender: NSApplication) -> Bool { true }

    // Leave no adb server running in the background once the app is closed.
    func applicationWillTerminate(_ notification: Notification) { Adb.shared.killServer() }
}

struct ContentView: View {
    @Environment(AppModel.self) private var model

    var body: some View {
        NavigationSplitView {
            Sidebar()
                .navigationSplitViewColumnWidth(min: 210, ideal: 230, max: 280)
        } detail: {
            Group {
                switch model.page {
                case .welcome: WelcomeView(welcome: model.welcome)
                case .connect: ConnectView(connect: model.connect)
                case .choose: if let c = model.choose { ChooseView(choose: c) }
                case .apply: if let a = model.apply { ApplyView(apply: a) }
                case .restore: if let r = model.restore { RestoreView(restore: r) }
                case .health: if let h = model.health { HealthView(health: h) }
                case .install: if let i = model.install { InstallView(install: i) }
                case .remote: if let r = model.remote { RemoteView(remote: r) }
                }
            }
            .frame(maxWidth: .infinity, maxHeight: .infinity)
        }
    }
}

struct Sidebar: View {
    @Environment(AppModel.self) private var model

    var body: some View {
        List(selection: Binding(get: { min(model.stepIndex, 3) }, set: { if let n = $0 { model.go(toStep: n) } })) {
            Section(L("Mac_Steps")) {
                ForEach(0..<4, id: \.self) { i in
                    Label {
                        Text(L(AppModel.steps[i]))
                    } icon: {
                        Image(systemName: i < model.stepIndex ? "checkmark.circle.fill" : "\(i + 1).circle")
                            .foregroundStyle(i < model.stepIndex ? .green : .secondary)
                    }
                    .tag(i)
                    .selectionDisabled(!(i == min(model.stepIndex, 3) || model.canNavigate(toStep: i)))
                }
            }
        }
        .safeAreaInset(edge: .bottom) {
            VStack(alignment: .leading, spacing: 10) {
                if let d = model.device {
                    VStack(alignment: .leading, spacing: 2) {
                        Text(L("Side_Connected")).font(.caption2.weight(.semibold)).foregroundStyle(.secondary)
                        Text(d.displayName).font(.callout.weight(.semibold)).lineLimit(2)
                        Text(d.ipAddress).font(.caption).foregroundStyle(.secondary)
                    }
                    .padding(10)
                    .frame(maxWidth: .infinity, alignment: .leading)
                    .background(.tint.opacity(0.12), in: RoundedRectangle(cornerRadius: 8))

                    VStack(alignment: .leading, spacing: 8) {
                        toolLink("Side_Health", "waveform.path.ecg") { model.goHealth() }
                        toolLink("Side_Remote", "appletvremote.gen4") { model.goRemote() }
                        toolLink("Side_Install", "square.and.arrow.down") { model.goInstall() }
                        toolLink("Side_Restore", "arrow.uturn.backward") { model.goRestore() }
                    }
                    .disabled(model.isBusy)
                }
                Divider()
                HStack {
                    Text("v\(AppUpdater.currentVersion)").font(.caption).foregroundStyle(.secondary)
                    Spacer()
                    Button(L("Side_CheckUpdates")) { AppUpdater.shared.checkForUpdates() }
                        .buttonStyle(.link)
                        .font(.caption)
                        .disabled(!AppUpdater.shared.canCheckForUpdates)
                }
            }
            .padding(12)
        }
    }
}

extension Sidebar {
    func toolLink(_ key: String, _ icon: String, action: @escaping () -> Void) -> some View {
        Button(action: action) { Label(L(key), systemImage: icon) }.buttonStyle(.link)
    }
}

/// Page layout shared by every step: title, subtitle, content, in a scroll view.
struct PageLayout<Content: View>: View {
    var title: String
    var subtitle: String
    @ViewBuilder var content: Content

    var body: some View {
        ScrollView {
            VStack(alignment: .leading, spacing: 20) {
                VStack(alignment: .leading, spacing: 6) {
                    Text(title).font(.largeTitle.weight(.semibold))
                    Text(subtitle).font(.title3).foregroundStyle(.secondary).fixedSize(horizontal: false, vertical: true)
                }
                content
            }
            .padding(28)
            .frame(maxWidth: 820, alignment: .leading)
            .frame(maxWidth: .infinity, alignment: .leading)
        }
    }
}

/// A rounded box with a title, used for the cards on each page.
struct Card<Content: View>: View {
    var title: String? = nil
    var systemImage: String? = nil
    var tint: Color = .accentColor
    @ViewBuilder var content: Content

    var body: some View {
        GroupBox {
            VStack(alignment: .leading, spacing: 10) {
                if let title {
                    HStack(spacing: 10) {
                        if let systemImage {
                            Image(systemName: systemImage)
                                .font(.title3)
                                .foregroundStyle(tint)
                                .frame(width: 28)
                        }
                        Text(title).font(.headline)
                    }
                }
                content
            }
            .padding(8)
            .frame(maxWidth: .infinity, alignment: .leading)
        }
    }
}

/// A coloured message box (info, warning, error, success).
struct Note: View {
    enum Kind { case info, warning, error, success }
    var text: String
    var kind: Kind = .info

    private var color: Color {
        switch kind { case .info: .accentColor; case .warning: .orange; case .error: .red; case .success: .green }
    }
    private var icon: String {
        switch kind {
        case .info: "info.circle.fill"; case .warning: "exclamationmark.triangle.fill"
        case .error: "xmark.octagon.fill"; case .success: "checkmark.circle.fill"
        }
    }

    var body: some View {
        Label { Text(text).textSelection(.enabled).fixedSize(horizontal: false, vertical: true) }
            icon: { Image(systemName: icon).foregroundStyle(color) }
            .padding(10)
            .frame(maxWidth: .infinity, alignment: .leading)
            .background(color.opacity(0.12), in: RoundedRectangle(cornerRadius: 8))
    }
}
