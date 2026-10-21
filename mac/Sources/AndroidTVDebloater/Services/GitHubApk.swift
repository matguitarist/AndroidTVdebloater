import CryptoKit
import Foundation

/// Downloads the .apk of a project's latest GitHub release, checked against the SHA-256 that
/// GitHub publishes for the file. A file without a checksum is refused.
enum GitHubApk {
    private struct Release: Decodable {
        struct Asset: Decodable {
            let name: String
            let browser_download_url: URL
            let digest: String?
        }
        let assets: [Asset]
    }

    /// The verified .apk, in a temporary folder the caller deletes.
    static func downloadLatest(_ repo: String) async throws -> URL {
        var request = URLRequest(url: URL(string: "https://api.github.com/repos/\(repo)/releases/latest")!)
        request.setValue("application/vnd.github+json", forHTTPHeaderField: "Accept")
        let (data, _) = try await URLSession.shared.data(for: request)
        let release = try JSONDecoder().decode(Release.self, from: data)
        guard let asset = release.assets.first(where: { $0.name.lowercased().hasSuffix(".apk") }) else {
            throw AppError.message("no .apk in the latest release")
        }
        guard let digest = asset.digest, digest.hasPrefix("sha256:") else { throw AppError.message("no checksum") }

        let folder = FileManager.default.temporaryDirectory.appendingPathComponent("unbloatmytv-\(UUID().uuidString)")
        try FileManager.default.createDirectory(at: folder, withIntermediateDirectories: true)
        let apk = folder.appendingPathComponent(asset.name)
        do {
            try await Downloader.download(asset.browser_download_url, to: apk, progress: { _ in })
            let hash = SHA256.hash(data: try Data(contentsOf: apk)).map { String(format: "%02x", $0) }.joined()
            guard hash == digest.dropFirst("sha256:".count).lowercased() else { throw AppError.message("checksum") }
            return apk
        } catch {
            try? FileManager.default.removeItem(at: folder)
            throw error
        }
    }
}
