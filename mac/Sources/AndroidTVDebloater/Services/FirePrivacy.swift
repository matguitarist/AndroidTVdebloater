import Foundation

/// Amazon's tracking switches on Fire TV. They are kept in the Amazon account and synced back,
/// so the app only reads them and sends the user to the TV's own menu.
enum FirePrivacy {
    /// Secure setting (1 = on) and the string key of its name in the TV's menu.
    static let switches = [
        ("advertisingIdApp:interestBasedAds:value", "Fire_IbAds"),
        ("settingsApp:deviceUsageData:value", "Fire_DeviceUsage"),
        ("settingsApp:appUsageData:value", "Fire_AppUsage"),
    ]

    /// The labels of the switches that are on.
    static func readOn(_ adb: Adb, serial: String) async -> [String] {
        var on: [String] = []
        for (key, label) in switches {
            let r = await adb.shell(serial, "settings get secure \(key)")
            if r.output.trimmingCharacters(in: .whitespacesAndNewlines) == "1" { on.append(label) }
        }
        return on
    }

    /// Opens Settings → Preferences on the TV, where Privacy Settings is.
    static func openOnTv(_ adb: Adb, serial: String) async {
        _ = await adb.shell(serial, "am start -a com.amazon.device.settings.action.PREFERENCES")
    }
}
