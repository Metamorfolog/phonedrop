import Foundation

struct FoundPhone: Hashable {
    let instance: String
    let address: String
    let id: String?
}

/// Hledání telefonů v síti přes Bonjour (`_phonedrop._tcp`, TXT `id=<UUID telefonu>`).
@MainActor
final class Bonjour: NSObject, NetServiceBrowserDelegate, NetServiceDelegate {
    static let serviceType = "_phonedrop._tcp."

    private let browser = NetServiceBrowser()
    private var services: [NetService] = []
    private var found: [String: FoundPhone] = [:]

    /// Projde síť a vrátí telefony, které se stihly ozvat a přeložit na IPv4 adresu.
    static func discover(timeout: TimeInterval = 2.5) async -> [FoundPhone] {
        let b = Bonjour()
        return await b.run(timeout: timeout)
    }

    private func run(timeout: TimeInterval) async -> [FoundPhone] {
        browser.delegate = self
        browser.searchForServices(ofType: Bonjour.serviceType, inDomain: "local.")
        try? await Task.sleep(nanoseconds: UInt64(timeout * 1_000_000_000))
        browser.stop()
        services.forEach { $0.stop() }
        return Array(found.values).sorted { $0.instance < $1.instance }
    }

    nonisolated func netServiceBrowser(_ browser: NetServiceBrowser, didFind service: NetService, moreComing: Bool) {
        MainActor.assumeIsolated {
            services.append(service)
            service.delegate = self
            service.resolve(withTimeout: 5)
        }
    }

    nonisolated func netServiceDidResolveAddress(_ sender: NetService) {
        MainActor.assumeIsolated {
            guard let ip = sender.addresses?.compactMap(Bonjour.ipv4).first, sender.port > 0 else { return }
            var id: String?
            if let txt = sender.txtRecordData() {
                let dict = NetService.dictionary(fromTXTRecord: txt)
                if let d = dict["id"] { id = String(data: d, encoding: .utf8) }
            }
            found[sender.name] = FoundPhone(instance: sender.name, address: "\(ip):\(sender.port)", id: id)
        }
    }

    /// IPv4 adresa ze sockaddr (telefon poslouchá na IPv4; link-local IPv6 by v URL potřebovala zónu).
    nonisolated static func ipv4(_ data: Data) -> String? {
        data.withUnsafeBytes { raw -> String? in
            guard let base = raw.baseAddress, raw.count >= MemoryLayout<sockaddr_in>.size else { return nil }
            let sa = base.assumingMemoryBound(to: sockaddr.self)
            guard sa.pointee.sa_family == sa_family_t(AF_INET) else { return nil }
            var addr = base.assumingMemoryBound(to: sockaddr_in.self).pointee.sin_addr
            var buf = [CChar](repeating: 0, count: Int(INET_ADDRSTRLEN))
            guard inet_ntop(AF_INET, &addr, &buf, socklen_t(INET_ADDRSTRLEN)) != nil else { return nil }
            return String(cString: buf)
        }
    }
}
