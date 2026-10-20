import Foundation

/// The app's language: whatever macOS picked from the en/fr localizations
/// (System Settings › General › Language & Region › Applications can override it).
enum Lang {
    static let code: String = Bundle.main.preferredLocalizations.first?.hasPrefix("fr") == true ? "fr" : "en"
    static var locale: Locale { Locale(identifier: code) }
}

/// Localized UI text. Keys are shared with the Windows app; Localizable.strings is generated from
/// its Strings.cs by tools/gen-mac-strings.py.
func L(_ key: String, _ args: Any...) -> String {
    let format = Bundle.main.localizedString(forKey: key, value: nil, table: nil)
    guard !args.isEmpty else { return format }
    return String(format: format, locale: Lang.locale, arguments: args.map { "\($0)" as NSString })
}

/// Text in both languages, from the catalog.
struct LocText: Codable, Hashable {
    var en: String
    var fr: String

    var text: String { Lang.code == "fr" && !fr.isEmpty ? fr : en }

    func replacing(_ token: String, en enValue: String, fr frValue: String) -> LocText {
        LocText(en: en.replacingOccurrences(of: token, with: enValue),
                fr: fr.replacingOccurrences(of: token, with: frValue))
    }
}
