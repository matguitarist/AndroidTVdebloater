import Foundation

struct AdbResult: Sendable {
    let exitCode: Int32
    let output: String
    let error: String
    var ok: Bool { exitCode == 0 }
    var all: String { (output + "\n" + error).trimmingCharacters(in: .whitespacesAndNewlines) }
}

/// Where the app keeps adb, backups and settings. Same folder the Windows/Linux app uses on a Mac.
enum AppPaths {
    static let dataDir: URL = FileManager.default
        .urls(for: .applicationSupportDirectory, in: .userDomainMask)[0]
        .appendingPathComponent("AndroidTVDebloater", isDirectory: true)
    static var backupDir: URL { dataDir.appendingPathComponent("backups", isDirectory: true) }

    static func ensureDataDir() {
        try? FileManager.default.createDirectory(at: dataDir, withIntermediateDirectories: true)
    }
}

/// Owns a private copy of Google's platform-tools (downloaded on first run) and runs adb.
final class Adb: @unchecked Sendable {
    static let shared = Adb()

    var toolsDir: URL { AppPaths.dataDir.appendingPathComponent("platform-tools", isDirectory: true) }
    var adbURL: URL { toolsDir.appendingPathComponent("adb") }
    var isInstalled: Bool { FileManager.default.isExecutableFile(atPath: adbURL.path) }

    private let lock = NSLock()
    private var serverStarted = false

    private static let downloadURL = URL(string: "https://dl.google.com/android/repository/platform-tools-latest-darwin.zip")!

    /// Download and unpack platform-tools from Google if they are not there yet.
    func ensureInstalled(progress: @escaping @Sendable (Double) -> Void) async throws {
        if isInstalled { progress(1); return }
        AppPaths.ensureDataDir()
        let zip = AppPaths.dataDir.appendingPathComponent("platform-tools.zip")
        let tmp = AppPaths.dataDir.appendingPathComponent("platform-tools.tmp", isDirectory: true)

        try await Downloader.download(Self.downloadURL, to: zip, progress: { progress(min(0.99, $0)) })

        let fm = FileManager.default
        try? fm.removeItem(at: tmp)
        try fm.createDirectory(at: tmp, withIntermediateDirectories: true)
        let unzip = try await Shell.run("/usr/bin/ditto", ["-x", "-k", zip.path, tmp.path], timeout: 120)
        guard unzip.ok else { throw AppError.message(unzip.all) }
        try? fm.removeItem(at: toolsDir)
        try fm.moveItem(at: tmp.appendingPathComponent("platform-tools"), to: toolsDir)
        try? fm.removeItem(at: tmp)
        try? fm.removeItem(at: zip)
        progress(1)
    }

    /// Start the server with no pipes: it outlives the call and would hold them open forever.
    private func startServerIfNeeded() async {
        let needed: Bool = lock.withLock {
            if serverStarted { return false }
            serverStarted = true
            return true
        }
        guard needed else { return }
        _ = try? await Shell.run(adbURL.path, ["start-server"], timeout: 30, capture: false)
    }

    func run(_ args: [String], timeout: TimeInterval = 20) async -> AdbResult {
        await startServerIfNeeded()
        do { return try await Shell.run(adbURL.path, args, timeout: timeout) }
        catch { return AdbResult(exitCode: -1, output: "", error: error.localizedDescription) }
    }

    /// Raw bytes from stdout, e.g. "exec-out screencap -p". Nil on failure or timeout.
    func runBinary(_ args: [String], timeout: TimeInterval = 10) async -> Data? {
        await startServerIfNeeded()
        let path = adbURL
        return await withCheckedContinuation { (cont: CheckedContinuation<Data?, Never>) in
            let p = Process()
            p.executableURL = path
            p.arguments = args
            let out = Pipe()
            let buf = Shell.Buffer()
            p.standardOutput = out
            p.standardError = FileHandle.nullDevice
            out.fileHandleForReading.readabilityHandler = { h in buf.append(h.availableData) }
            let once = Once()
            p.terminationHandler = { proc in
                out.fileHandleForReading.readabilityHandler = nil
                buf.append(out.fileHandleForReading.readDataToEndOfFile())
                let data = buf.bytes
                once.run { cont.resume(returning: proc.terminationStatus == 0 && !data.isEmpty ? data : nil) }
            }
            do { try p.run() } catch { once.run { cont.resume(returning: nil) }; return }
            DispatchQueue.global().asyncAfter(deadline: .now() + timeout) {
                guard p.isRunning else { return }
                p.terminate()
                once.run { cont.resume(returning: nil) }
            }
        }
    }

    func shell(_ serial: String, _ command: String, timeout: TimeInterval = 20) async -> AdbResult {
        await run(["-s", serial, "shell", command], timeout: timeout)
    }

    /// State of one device in "adb devices": device, unauthorized, offline, or nil if absent.
    func state(_ serial: String) async -> String? {
        let r = await run(["devices"], timeout: 10)
        for line in r.output.split(separator: "\n") {
            let parts = line.split(whereSeparator: { $0 == "\t" || $0 == " " })
            if parts.count >= 2, parts[0] == serial { return String(parts[1]) }
        }
        return nil
    }

    func killServer() {
        guard isInstalled, lock.withLock({ serverStarted }) else { return }
        let p = Process()
        p.executableURL = adbURL
        p.arguments = ["kill-server"]
        p.standardOutput = FileHandle.nullDevice
        p.standardError = FileHandle.nullDevice
        try? p.run()
        p.waitUntilExit()
    }
}

enum AppError: LocalizedError {
    case message(String)
    var errorDescription: String? { if case .message(let m) = self { return m }; return nil }
}

/// Runs a program and collects its output, killing it after a timeout.
enum Shell {
    static func run(_ path: String, _ args: [String], timeout: TimeInterval, capture: Bool = true) async throws -> AdbResult {
        try await withCheckedThrowingContinuation { (cont: CheckedContinuation<AdbResult, Error>) in
            let p = Process()
            p.executableURL = URL(fileURLWithPath: path)
            p.arguments = args
            let out = Pipe(), err = Pipe()
            let outBuf = Buffer(), errBuf = Buffer()
            if capture {
                p.standardOutput = out
                p.standardError = err
                out.fileHandleForReading.readabilityHandler = { h in outBuf.append(h.availableData) }
                err.fileHandleForReading.readabilityHandler = { h in errBuf.append(h.availableData) }
            } else {
                p.standardOutput = FileHandle.nullDevice
                p.standardError = FileHandle.nullDevice
            }
            let once = Once()
            p.terminationHandler = { proc in
                out.fileHandleForReading.readabilityHandler = nil
                err.fileHandleForReading.readabilityHandler = nil
                if capture {
                    outBuf.append(out.fileHandleForReading.readDataToEndOfFile())
                    errBuf.append(err.fileHandleForReading.readDataToEndOfFile())
                }
                once.run {
                    cont.resume(returning: AdbResult(exitCode: proc.terminationStatus,
                                                     output: outBuf.string, error: errBuf.string))
                }
            }
            do { try p.run() } catch {
                once.run { cont.resume(throwing: error) }
                return
            }
            DispatchQueue.global().asyncAfter(deadline: .now() + timeout) {
                guard p.isRunning else { return }
                p.terminate()
                once.run { cont.resume(returning: AdbResult(exitCode: -1, output: "", error: "timeout")) }
            }
        }
    }

    final class Buffer: @unchecked Sendable {
        private var data = Data()
        private let lock = NSLock()
        func append(_ d: Data) { lock.withLock { data.append(d) } }
        var bytes: Data { lock.withLock { data } }
        var string: String {
            lock.withLock { String(decoding: data, as: UTF8.self).replacingOccurrences(of: "\r", with: "") }
        }
    }
}

/// Runs a closure at most once, from any thread.
final class Once: @unchecked Sendable {
    private var done = false
    private let lock = NSLock()
    func run(_ body: () -> Void) {
        let first = lock.withLock { () -> Bool in
            if done { return false }
            done = true
            return true
        }
        if first { body() }
    }
}

/// File download with progress.
enum Downloader {
    static func download(_ url: URL, to destination: URL, progress: @escaping @Sendable (Double) -> Void) async throws {
        try await withCheckedThrowingContinuation { (cont: CheckedContinuation<Void, Error>) in
            var observation: NSKeyValueObservation?
            let task = URLSession.shared.downloadTask(with: url) { tmp, response, error in
                observation?.invalidate()
                observation = nil
                if let error { cont.resume(throwing: error); return }
                guard let tmp, let http = response as? HTTPURLResponse, (200..<300).contains(http.statusCode) else {
                    cont.resume(throwing: AppError.message("HTTP \((response as? HTTPURLResponse)?.statusCode ?? 0)"))
                    return
                }
                // The temporary file is deleted when this handler returns, so move it now.
                do {
                    try? FileManager.default.removeItem(at: destination)
                    try FileManager.default.moveItem(at: tmp, to: destination)
                    cont.resume()
                } catch { cont.resume(throwing: error) }
            }
            observation = task.progress.observe(\.fractionCompleted) { p, _ in progress(p.fractionCompleted) }
            task.resume()
        }
    }
}
