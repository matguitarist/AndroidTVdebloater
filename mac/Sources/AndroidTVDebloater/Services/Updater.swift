import Foundation
import Observation
import Sparkle

/// Mac updates through Sparkle: appcast.xml on the latest GitHub release (SUFeedURL in Info.plist),
/// and a notarized zip signed with the project's EdDSA key (SUPublicEDKey).
@MainActor @Observable
final class AppUpdater: NSObject {
    static let shared = AppUpdater()

    @ObservationIgnored private var controller: SPUStandardUpdaterController!
    @ObservationIgnored private var observation: NSKeyValueObservation?
    @ObservationIgnored private var pendingRelaunch: (() -> Void)?

    /// Tells Sparkle the app is in the middle of changing a TV, so a relaunch can wait.
    @ObservationIgnored var isBusy: () -> Bool = { false }

    var canCheckForUpdates = false

    static var currentVersion: String {
        Bundle.main.object(forInfoDictionaryKey: "CFBundleShortVersionString") as? String ?? "0.0.0"
    }

    private override init() {
        super.init()
        controller = SPUStandardUpdaterController(startingUpdater: true, updaterDelegate: self, userDriverDelegate: nil)
        observation = controller.updater.observe(\.canCheckForUpdates, options: [.initial, .new]) { [weak self] updater, _ in
            let can = updater.canCheckForUpdates
            Task { @MainActor in self?.canCheckForUpdates = can }
        }
    }

    func checkForUpdates() { controller.checkForUpdates(nil) }

    /// Called while a TV is being changed: install once it's done instead of cutting it off.
    private func relaunchWhenIdle() {
        guard let pending = pendingRelaunch else { return }
        if isBusy() {
            Task { @MainActor in
                try? await Task.sleep(for: .seconds(2))
                self.relaunchWhenIdle()
            }
        } else {
            pendingRelaunch = nil
            pending()
        }
    }
}

extension AppUpdater: SPUUpdaterDelegate {
    nonisolated func updater(_ updater: SPUUpdater, shouldPostponeRelaunchForUpdate item: SUAppcastItem,
                             untilInvokingBlock installHandler: @escaping () -> Void) -> Bool {
        MainActor.assumeIsolated {
            guard isBusy() else { return false }
            pendingRelaunch = installHandler
            relaunchWhenIdle()
            return true
        }
    }
}
