import AppKit
import CryptoKit
import Foundation

struct UpdateInfo: Sendable {
    var version: String
    var notes: String
    var downloadURL: URL
    var size: Int64
    var sha256: String
    var pageURL: URL
}

/// Sparkle-style updates from GitHub Releases, same releases as the Windows app. The zip is checked
/// against the SHA-256 GitHub publishes for it, unpacked, and swapped in place of this app bundle,
/// which is then relaunched.
enum Updater {
    private static let repo = "matguitarist/AndroidTVdebloater"
    private static let assetName = "AndroidTVDebloater-mac.zip"
    static let releasesPage = URL(string: "https://github.com/\(repo)/releases/latest")!

    static var currentVersion: String {
        Bundle.main.object(forInfoDictionaryKey: "CFBundleShortVersionString") as? String ?? "0.0.0"
    }

    /// "1.2" → [1, 2, 0], so versions compare part by part.
    static func parts(_ v: String) -> [Int] {
        let p = v.trimmingCharacters(in: CharacterSet(charactersIn: "vV")).split(separator: ".").map { Int($0) ?? 0 }
        return Array((p + [0, 0, 0]).prefix(3))
    }

    static func isNewer(_ a: String, than b: String) -> Bool { parts(b).lexicographicallyPrecedes(parts(a)) }

    /// The latest release if it is newer than this copy, otherwise nil.
    static func check() async throws -> UpdateInfo? {
        var req = URLRequest(url: URL(string: "https://api.github.com/repos/\(repo)/releases/latest")!)
        req.setValue("application/vnd.github+json", forHTTPHeaderField: "Accept")
        req.setValue("AndroidTVDebloater-mac/\(currentVersion)", forHTTPHeaderField: "User-Agent")
        req.timeoutInterval = 20
        let (data, response) = try await URLSession.shared.data(for: req)
        guard (response as? HTTPURLResponse)?.statusCode == 200,
              let root = try JSONSerialization.jsonObject(with: data) as? [String: Any],
              let tag = root["tag_name"] as? String else { throw AppError.message("GitHub") }
        let version = parts(tag).map(String.init).joined(separator: ".")
        guard isNewer(version, than: currentVersion) else { return nil }

        for asset in root["assets"] as? [[String: Any]] ?? [] where asset["name"] as? String == assetName {
            guard let digest = asset["digest"] as? String, digest.hasPrefix("sha256:"),
                  let urlText = asset["browser_download_url"] as? String, let url = URL(string: urlText) else { return nil }
            return UpdateInfo(
                version: version,
                notes: cleanNotes(root["body"] as? String),
                downloadURL: url,
                size: (asset["size"] as? NSNumber)?.int64Value ?? 0,
                sha256: String(digest.dropFirst("sha256:".count)).lowercased(),
                pageURL: URL(string: root["html_url"] as? String ?? "") ?? releasesPage)
        }
        return nil
    }

    /// Release notes are Markdown; show them as plain text.
    private static func cleanNotes(_ md: String?) -> String {
        guard var t = md?.replacingOccurrences(of: "\r", with: ""), !t.isEmpty else { return "" }
        t = t.replacingOccurrences(of: #"(?m)^#+\s*"#, with: "", options: .regularExpression)
        t = t.replacingOccurrences(of: #"(?m)^\s*[-*]\s+"#, with: "• ", options: .regularExpression)
        t = t.replacingOccurrences(of: "**", with: "").replacingOccurrences(of: "`", with: "")
        t = t.replacingOccurrences(of: #"\[([^\]]+)\]\([^)]+\)"#, with: "$1", options: .regularExpression)
        return t.trimmingCharacters(in: .whitespacesAndNewlines)
    }

    /// The bundle can be replaced only when it isn't a translocated copy (run from Downloads
    /// before being moved) and its folder is writable.
    static var canInstallInPlace: Bool {
        let bundle = Bundle.main.bundleURL
        guard bundle.pathExtension == "app", !bundle.path.contains("/AppTranslocation/") else { return false }
        return FileManager.default.isWritableFile(atPath: bundle.deletingLastPathComponent().path)
    }

    /// Download, verify, unpack and swap in the new app, then relaunch it.
    static func install(_ update: UpdateInfo, progress: @escaping @Sendable (Double) -> Void) async throws {
        let fm = FileManager.default
        let work = fm.temporaryDirectory.appendingPathComponent("AndroidTVDebloater-update-\(UUID().uuidString)", isDirectory: true)
        try fm.createDirectory(at: work, withIntermediateDirectories: true)
        defer { try? fm.removeItem(at: work) }

        let zip = work.appendingPathComponent(assetName)
        try await Downloader.download(update.downloadURL, to: zip, progress: { progress(min(0.95, $0)) })

        let hash = SHA256.hash(data: try Data(contentsOf: zip, options: .mappedIfSafe))
            .map { String(format: "%02x", $0) }.joined()
        guard hash == update.sha256 else { throw UpdateError.checksum }

        let unpacked = work.appendingPathComponent("unpacked", isDirectory: true)
        let r = try await Shell.run("/usr/bin/ditto", ["-x", "-k", zip.path, unpacked.path], timeout: 120)
        guard r.ok, let newApp = try fm.contentsOfDirectory(at: unpacked, includingPropertiesForKeys: nil)
            .first(where: { $0.pathExtension == "app" }) else { throw AppError.message(r.all) }

        let current = Bundle.main.bundleURL
        _ = try fm.replaceItemAt(current, withItemAt: newApp)
        progress(1)

        // Relaunch once this process has quit.
        let relaunch = Process()
        relaunch.executableURL = URL(fileURLWithPath: "/bin/sh")
        relaunch.arguments = ["-c", "sleep 1; /usr/bin/open \"$0\"", current.path]
        try relaunch.run()
        await MainActor.run { NSApp.terminate(nil) }
    }

    enum UpdateError: Error { case checksum }
}
