import Foundation
import Network

/// Finds devices on the local network that accept adb connections on port 5555.
enum NetworkScanner {
    static let adbPort: UInt16 = 5555

    static func scan(progress: @escaping @MainActor (Double) -> Void) async -> [String] {
        let targets = localSubnets().prefix(4).flatMap { prefix in (1...254).map { "\(prefix).\($0)" } }
        guard !targets.isEmpty else { return [] }
        var found: [String] = []
        var done = 0
        // 64 probes at a time, like the Windows app.
        await withTaskGroup(of: (String, Bool).self) { group in
            var iterator = targets.makeIterator()
            for _ in 0..<64 {
                guard let ip = iterator.next() else { break }
                group.addTask { (ip, await isPortOpen(ip, port: adbPort, timeout: 0.7)) }
            }
            for await (ip, open) in group {
                if open { found.append(ip) }
                done += 1
                let fraction = Double(done) / Double(targets.count)
                await progress(fraction)
                if let next = iterator.next() {
                    group.addTask { (next, await isPortOpen(next, port: adbPort, timeout: 0.7)) }
                }
            }
        }
        return found.sorted { a, b in
            a.split(separator: ".").compactMap { Int($0) }.lexicographicallyPrecedes(b.split(separator: ".").compactMap { Int($0) })
        }
    }

    static func isPortOpen(_ host: String, port: UInt16, timeout: TimeInterval) async -> Bool {
        await withCheckedContinuation { cont in
            let conn = NWConnection(host: NWEndpoint.Host(host), port: NWEndpoint.Port(rawValue: port)!, using: .tcp)
            let once = Once()
            let finish: (Bool) -> Void = { open in
                once.run {
                    conn.cancel()
                    cont.resume(returning: open)
                }
            }
            conn.stateUpdateHandler = { state in
                switch state {
                case .ready: finish(true)
                case .failed, .cancelled: finish(false)
                case .waiting: finish(false)
                default: break
                }
            }
            conn.start(queue: .global())
            DispatchQueue.global().asyncAfter(deadline: .now() + timeout) { finish(false) }
        }
    }

    /// The /24 prefixes ("192.168.1") of this Mac's private IPv4 addresses.
    static func localSubnets() -> [String] {
        var result: [String] = []
        var ifaddr: UnsafeMutablePointer<ifaddrs>?
        guard getifaddrs(&ifaddr) == 0, let first = ifaddr else { return [] }
        defer { freeifaddrs(ifaddr) }
        for ptr in sequence(first: first, next: { $0.pointee.ifa_next }) {
            let flags = Int32(ptr.pointee.ifa_flags)
            guard (flags & IFF_UP) != 0, (flags & IFF_LOOPBACK) == 0,
                  let addr = ptr.pointee.ifa_addr, addr.pointee.sa_family == UInt8(AF_INET) else { continue }
            let ip = addr.withMemoryRebound(to: sockaddr_in.self, capacity: 1) { $0.pointee.sin_addr.s_addr }
            let b = withUnsafeBytes(of: ip) { Array($0) }
            let isPrivate = b[0] == 10 || (b[0] == 172 && (16...31).contains(b[1])) || (b[0] == 192 && b[1] == 168)
            let prefix = "\(b[0]).\(b[1]).\(b[2])"
            if isPrivate && !result.contains(prefix) { result.append(prefix) }
        }
        return result
    }

    /// Reverse DNS name for an address, if the router knows one.
    static func hostName(_ ip: String) async -> String? {
        await Task.detached {
            var sa = sockaddr_in()
            sa.sin_len = UInt8(MemoryLayout<sockaddr_in>.size)
            sa.sin_family = sa_family_t(AF_INET)
            guard inet_pton(AF_INET, ip, &sa.sin_addr) == 1 else { return nil }
            var host = [CChar](repeating: 0, count: Int(NI_MAXHOST))
            let rc = withUnsafePointer(to: &sa) {
                $0.withMemoryRebound(to: sockaddr.self, capacity: 1) {
                    getnameinfo($0, socklen_t(MemoryLayout<sockaddr_in>.size), &host, socklen_t(host.count), nil, 0, NI_NAMEREQD)
                }
            }
            guard rc == 0 else { return nil }
            let name = String(cString: host)
            return name == ip ? nil : name
        }.value
    }
}
