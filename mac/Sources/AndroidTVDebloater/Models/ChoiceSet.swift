import Foundation
import UniformTypeIdentifiers
import SwiftUI

struct SavedChoice: Codable {
    /// Tweak id: "pkg:<package>" for apps, the setting id otherwise.
    var id: String
    /// Value of the chosen option, for choices that have options.
    var option: String?
}

/// A set of ticked choices, to do the same on another TV. Same file format as the Windows app.
struct ChoiceSet: Codable {
    /// The TV the choices were made on.
    var name: String
    var created: String
    var choices: [SavedChoice]

    var createdText: String { DateText.display(created) }

    private static var lastURL: URL { AppPaths.dataDir.appendingPathComponent("last-choices.json") }

    static func from(tvName: String, selected: [Tweak]) -> ChoiceSet {
        ChoiceSet(name: tvName, created: DateText.now(),
                  choices: selected.map { SavedChoice(id: $0.id, option: $0.options.isEmpty ? nil : $0.selectedOption) })
    }

    /// Tick exactly these choices. Returns how many exist on this TV.
    @discardableResult
    func apply(to all: [Tweak]) -> Int {
        var wanted: [String: SavedChoice] = [:]
        for c in choices where wanted[c.id] == nil { wanted[c.id] = c }
        var found = 0
        for t in all {
            guard let c = wanted[t.id] else { t.isSelected = false; continue }
            t.isSelected = true
            found += 1
            if let value = c.option, t.options.contains(where: { $0.value == value }) { t.selectedOption = value }
        }
        return found
    }

    func encoded() throws -> Data {
        let e = JSONEncoder()
        e.outputFormatting = [.prettyPrinted, .sortedKeys]
        return try e.encode(self)
    }

    static func decode(_ data: Data) -> ChoiceSet? {
        guard let c = try? JSONDecoder().decode(ChoiceSet.self, from: data), !c.choices.isEmpty else { return nil }
        return c
    }

    func saveAsLast() {
        AppPaths.ensureDataDir()
        try? encoded().write(to: Self.lastURL, options: .atomic)
    }

    static func loadLast() -> ChoiceSet? {
        (try? Data(contentsOf: lastURL)).flatMap(decode)
    }
}

/// Lets SwiftUI's file exporter save a ChoiceSet.
struct ChoiceSetDocument: FileDocument {
    static var readableContentTypes: [UTType] { [.json] }
    var set: ChoiceSet

    init(set: ChoiceSet) { self.set = set }

    init(configuration: ReadConfiguration) throws {
        guard let data = configuration.file.regularFileContents, let s = ChoiceSet.decode(data) else {
            throw CocoaError(.fileReadCorruptFile)
        }
        set = s
    }

    func fileWrapper(configuration: WriteConfiguration) throws -> FileWrapper {
        FileWrapper(regularFileWithContents: try set.encoded())
    }
}

struct RecentTv: Codable, Hashable {
    var address: String
    var name: String
    var lastUsed: String
}

/// TVs this Mac connected to before, so they can be reached again in one click.
enum RecentTvs {
    private static var url: URL { AppPaths.dataDir.appendingPathComponent("recent.json") }

    // The Windows app writes PascalCase keys; accept both.
    private struct Entry: Codable {
        var address: String?, name: String?, lastUsed: String?
        var Address: String?, Name: String?, LastUsed: String?
    }

    static func load() -> [RecentTv] {
        guard let data = try? Data(contentsOf: url), let entries = try? JSONDecoder().decode([Entry].self, from: data) else { return [] }
        return entries.compactMap { e in
            guard let a = e.address ?? e.Address else { return nil }
            return RecentTv(address: a, name: e.name ?? e.Name ?? "", lastUsed: e.lastUsed ?? e.LastUsed ?? "")
        }
    }

    static func remember(address: String, name: String) {
        var list = load().filter { $0.address != address }
        list.insert(RecentTv(address: address, name: name, lastUsed: DateText.now()), at: 0)
        let entries = list.prefix(5).map { Entry(Address: $0.address, Name: $0.name, LastUsed: $0.lastUsed) }
        AppPaths.ensureDataDir()
        try? JSONEncoder().encode(Array(entries)).write(to: url, options: .atomic)
    }
}

/// A pre-filled "new issue" link listing apps the catalog doesn't know. Only model and package names.
enum IssueReport {
    private static let newIssue = "https://github.com/matguitarist/AndroidTVdebloater/issues/new"
    private static let maxURL = 7000

    static func build(_ dev: TvDevice, packages: [String], catalogVersion: Int) -> (url: URL, complete: Bool) {
        let title = "Unknown apps on \(dev.displayName) (Android \(dev.androidVersion))"
        func body(_ count: Int, _ left: Int) -> String {
            var b = "**Device:** \(dev.displayName), Android \(dev.androidVersion) (SDK \(dev.sdk))\n"
            b += "**Catalog version:** \(catalogVersion)\n\n"
            b += "Preinstalled apps that are not in the catalog yet:\n```\n"
            b += packages.prefix(count).joined(separator: "\n") + "\n```\n"
            if left > 0 { b += "…and \(left) more: the full list is on the clipboard, paste it here.\n" }
            b += "\nIf you know what some of them do, add a line about it.\n"
            return b
        }
        func make(_ body: String) -> String {
            var c = URLComponents(string: newIssue)!
            c.queryItems = [URLQueryItem(name: "title", value: title), URLQueryItem(name: "body", value: body)]
            return c.url!.absoluteString
        }
        var s = make(body(packages.count, 0))
        if s.count <= maxURL { return (URL(string: s)!, true) }
        var n = packages.count
        repeat { n -= 1; s = make(body(n, packages.count - n)) } while n > 0 && s.count > maxURL
        return (URL(string: s)!, false)
    }
}
