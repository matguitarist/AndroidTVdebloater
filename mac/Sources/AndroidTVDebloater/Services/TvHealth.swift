import Foundation

struct ProcessUse: Identifiable {
    var name: String
    var mb: Int64
    var id: String { name }
}

struct CrashLoop {
    var package: String
    var restarts: Int
}

/// A read-only snapshot of how the TV is doing. Same readings as the Windows app.
struct HealthReport {
    var memTotalMb: Int64 = 0
    var memAvailableMb: Int64 = 0
    var storageTotalMb: Int64 = 0
    var storageFreeMb: Int64 = 0
    var load = 0.0
    var cores = 1
    var uptimeHours = 0.0
    var topMemory: [ProcessUse] = []
    var crashing: [CrashLoop] = []

    /// Restarts in the current log above which an app counts as crash-looping.
    static let crashThreshold = 3

    static func read(_ adb: Adb, serial: String) async -> HealthReport {
        var h = HealthReport()
        let basics = await adb.shell(serial,
            "grep -E 'MemTotal|MemAvailable' /proc/meminfo; echo load $(cat /proc/loadavg); " +
            "echo cpus $(cat /sys/devices/system/cpu/present); echo up $(cat /proc/uptime)").output
        func first(_ pattern: String) -> [String] {
            guard let re = try? NSRegularExpression(pattern: pattern),
                  let m = re.firstMatch(in: basics, range: NSRange(basics.startIndex..., in: basics)) else { return [] }
            return (1..<m.numberOfRanges).compactMap { Range(m.range(at: $0), in: basics).map { String(basics[$0]) } }
        }
        h.memTotalMb = (Int64(first(#"MemTotal:\s+(\d+)"#).first ?? "") ?? 0) / 1024
        h.memAvailableMb = (Int64(first(#"MemAvailable:\s+(\d+)"#).first ?? "") ?? 0) / 1024
        h.load = Double(first(#"load ([\d.]+)"#).first ?? "") ?? 0
        let cpus = first(#"cpus (\d+)-(\d+)"#)
        if cpus.count == 2, let a = Int(cpus[0]), let b = Int(cpus[1]) { h.cores = b - a + 1 }
        h.uptimeHours = (Double(first(#"up ([\d.]+)"#).first ?? "") ?? 0) / 3600

        let storage = await TvDevice.readStorage(adb, serial: serial)
        h.storageTotalMb = storage.totalMb
        h.storageFreeMb = storage.freeMb

        // "193,472K: com.example.app (pid 4813 / activities)"
        let mem = await adb.shell(serial, "dumpsys meminfo | sed -n '/Total PSS by process/,/^$/p' | head -12", timeout: 30).output
        let line = try! NSRegularExpression(pattern: #"^\s*([\d,]+)K: ([^\s(]+)"#, options: .anchorsMatchLines)
        h.topMemory = line.matches(in: mem, range: NSRange(mem.startIndex..., in: mem)).prefix(8).compactMap { m in
            guard let size = Range(m.range(at: 1), in: mem), let name = Range(m.range(at: 2), in: mem) else { return nil }
            return ProcessUse(name: String(mem[name]), mb: (Int64(mem[size].replacingOccurrences(of: ",", with: "")) ?? 0) / 1024)
        }

        // Android logs every restart of a process that died: many restarts means a crash loop.
        let log = await adb.shell(serial, "logcat -d | grep -E 'Start proc [0-9]+:[^ ]+ for restart'", timeout: 30).output
        let restart = try! NSRegularExpression(pattern: #"Start proc \d+:([^/\s]+)/\S+ for restart"#)
        var counts: [String: Int] = [:]
        for m in restart.matches(in: log, range: NSRange(log.startIndex..., in: log)) {
            if let r = Range(m.range(at: 1), in: log) { counts[String(log[r]), default: 0] += 1 }
        }
        h.crashing = counts.filter { $0.value >= crashThreshold }
            .map { CrashLoop(package: $0.key, restarts: $0.value) }
            .sorted { $0.restarts > $1.restarts }
        return h
    }
}

/// What earlier runs turned off or changed that is back the way it was, usually after a system update.
struct ReturnedItems {
    var since: String
    var packages: [String]
    var settings: [SettingChange]
    var count: Int { packages.count + settings.count }

    /// Compare the TV with the backups of runs that weren't undone. Read-only.
    static func find(_ adb: Adb, device dev: TvDevice) async -> ReturnedItems? {
        let runs = Backup.list(for: dev) // newest first, undone runs excluded
        guard let newest = runs.first else { return nil }

        let packages = Set(runs.flatMap(\.disabledPackages))
            .filter { dev.enabled.contains($0) }
            .filter { p in !runs.contains { $0.enabledPackages.contains(p) } } // turned back on on purpose
            .sorted()

        // The latest value we set for each setting.
        var wanted: [String: SettingChange] = [:]
        var order: [String] = []
        for run in runs {
            for s in run.settings where wanted["\(s.namespace)|\(s.key)"] == nil {
                wanted["\(s.namespace)|\(s.key)"] = s
                order.append("\(s.namespace)|\(s.key)")
            }
        }
        if runs.contains(where: { $0.wifiWasOn == true }), wanted["global|wifi_on"] == nil {
            wanted["global|wifi_on"] = SettingChange(namespace: "global", key: "wifi_on", oldValue: nil, newValue: "0")
            order.append("global|wifi_on")
        }

        var settings: [SettingChange] = []
        let list = order.compactMap { wanted[$0] }
        if !list.isEmpty {
            // One shell call reads them all: "@@<n>" before each value.
            let script = list.enumerated().map { i, s in
                let isConfig = s.namespace.hasPrefix("device_config:")
                let ns = isConfig ? String(s.namespace.dropFirst("device_config:".count)) : s.namespace
                return "echo @@\(i); \(isConfig ? "device_config" : "settings") get \(ns) \(s.key)"
            }.joined(separator: "; ")
            let out = await adb.shell(dev.serial, script, timeout: 30).output
            var values: [Int: String] = [:]
            var current = -1
            for line in out.components(separatedBy: "\n") {
                if line.hasPrefix("@@"), let n = Int(line.dropFirst(2).trimmingCharacters(in: .whitespaces)) { current = n }
                else if current >= 0, values[current] == nil { values[current] = line.trimmingCharacters(in: .whitespaces) }
            }
            for (i, s) in list.enumerated() {
                let now = values[i] ?? ""
                // wifi_on: anything but "0" means the radio is back on.
                let changedBack = s.key == "wifi_on" ? now != "0" : now != s.newValue
                if changedBack && !now.isEmpty { settings.append(s) }
            }
        }
        let r = ReturnedItems(since: newest.created, packages: packages, settings: settings)
        return r.count > 0 ? r : nil
    }
}
