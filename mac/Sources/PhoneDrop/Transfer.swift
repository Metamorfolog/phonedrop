import Foundation

/// Soubor k odeslání; dir je podsložka v telefonu (Stažené/Drop/dir), když přišel jako součást složky.
struct DropItem {
    let url: URL
    let dir: String?

    // Systémové smetí, které ve složkách nikdo nechce mít v telefonu.
    static let skip: Set<String> = ["thumbs.db", "desktop.ini", ".ds_store"]

    /// Soubory projde, složky rozbalí do jednotlivých souborů se zachovanou strukturou.
    static func expand(_ urls: [URL]) -> [DropItem] {
        var items: [DropItem] = []
        let fm = FileManager.default
        for u in urls {
            var isDir: ObjCBool = false
            guard fm.fileExists(atPath: u.path, isDirectory: &isDir) else { continue }
            // Balíčky (.app apod.) jsou pro macOS složky; posílají se jako složka se vším uvnitř.
            if !isDir.boolValue {
                items.append(DropItem(url: u.standardizedFileURL, dir: nil))
                continue
            }
            let root = u.standardizedFileURL
            let parent = root.deletingLastPathComponent()
            guard let e = fm.enumerator(at: root, includingPropertiesForKeys: [.isRegularFileKey, .isHiddenKey, .isSymbolicLinkKey],
                                        options: [.skipsHiddenFiles]) else { continue }
            var files: [URL] = []
            for case let f as URL in e {
                let v = try? f.resourceValues(forKeys: [.isRegularFileKey, .isSymbolicLinkKey])
                if v?.isRegularFile == true && v?.isSymbolicLink != true { files.append(f) }
            }
            files.sort { $0.path.localizedStandardCompare($1.path) == .orderedAscending }
            for f in files where !skip.contains(f.lastPathComponent.lowercased()) {
                let rel = String(f.deletingLastPathComponent().standardizedFileURL.path.dropFirst(parent.path.count))
                    .trimmingCharacters(in: CharacterSet(charactersIn: "/"))
                items.append(DropItem(url: f, dir: rel))
            }
        }
        return items
    }
}

/// Výsledek jednoho souboru pro uživatele.
struct Outcome {
    let ok: Bool
    let title: String
    let detail: String
}

func sizeText(_ bytes: Int64) -> String {
    let b = Double(bytes)
    switch bytes {
    case ..<1024: return "\(bytes) B"
    case ..<1_048_576: return String(format: "%.1f kB", b / 1024)
    case ..<1_073_741_824: return String(format: "%.1f MB", b / 1_048_576)
    default: return String(format: "%.2f GB", b / 1_073_741_824)
    }
}

/// Je chyba jen „telefon neodpovídá“ (a ne odmítnutí)?
func isUnreachable(_ e: Error) -> Bool { e is UnreachableError }

/// Odeslání souborů na spárovaný telefon.
@MainActor
enum Sender {
    /// Spojení s telefonem; když na uložené adrese neodpovídá, dohledá ho přes Bonjour podle ID.
    static func connect(_ model: AppModel) async throws -> LanLink {
        let config = model.config
        guard config.isPaired, let address = config.Address, let token = config.Token else { throw PhoneError(message: L.notPaired) }
        let link = LanLink(address: address, token: token)
        do {
            try await link.ping()
            return link
        } catch where isUnreachable(error) {}

        model.card(.busy, config.DeviceName ?? L.phone, L.searchingNetwork)
        let phones = await Bonjour.discover()
        if let match = phones.first(where: { $0.id == config.DeviceId }), match.address != address {
            model.config.Address = match.address
            model.config.save()
            let l = LanLink(address: match.address, token: token)
            try await l.ping()
            return l
        }
        throw PhoneError(message: L.phoneNotFound)
    }

    /// Pošle soubor; u APK počká, až ho uživatel v telefonu potvrdí.
    static func send(_ model: AppModel, link: LanLink, item: DropItem) async throws -> Outcome {
        let name = item.dir == nil ? item.url.lastPathComponent : item.dir! + "/" + item.url.lastPathComponent
        let size = (try? FileManager.default.attributesOfItem(atPath: item.url.path)[.size] as? NSNumber)?.int64Value ?? 0
        let total = max(1, size)
        let started = Date()
        model.card(.busy, name, L.connecting, progress: 0)
        let result = try await link.send(file: item.url, dir: item.dir) { done in
            Task { @MainActor in
                let secs = Date().timeIntervalSince(started)
                let speed = secs > 0.5 ? String(format: "  ·  %.1f MB/s", Double(done) / secs / 1_048_576) : ""
                model.card(.busy, name, L.progress(sizeText(done), sizeText(total), speed), progress: Double(done) / Double(total))
            }
        }
        if result.kind != "apk" { return Outcome(ok: true, title: name, detail: L.savedTo(result.path ?? "")) }

        model.card(.busy, name, result.canInstall ? L.confirmInstall : L.allowInstall)
        let deadline = Date().addingTimeInterval(300)
        while Date() < deadline {
            try await Task.sleep(nanoseconds: 1_000_000_000)
            let state: String
            do { state = try await link.installState(session: result.session) }
            catch where isUnreachable(error) { continue } // telefon může instalací chvíli „zamrznout“
            switch state {
            case "installed": return Outcome(ok: true, title: name, detail: L.installed)
            case "aborted": return Outcome(ok: false, title: name, detail: L.installAborted)
            case "unknown": return Outcome(ok: true, title: name, detail: L.installUnknown)
            default:
                if state.hasPrefix("failed") {
                    let why = state.dropFirst(6).trimmingCharacters(in: CharacterSet(charactersIn: ": "))
                    return Outcome(ok: false, title: name, detail: L.installFailed(why))
                }
            }
        }
        return Outcome(ok: true, title: name, detail: L.installPending)
    }
}

/// Soubory z telefonu (Sdílet › PhoneDrop). Telefon je server, takže se Mac pořád ptá dlouhým dotazem
/// (`GET /outbox?wait=25`). Co telefon nabídne, stáhne do Stažených (nebo vybrané složky) a potvrdí.
@MainActor
final class Receiver {
    unowned let model: AppModel
    private var failures: [String: Int] = [:]
    /// Uložené, ale telefon to ještě nepotvrdil – podruhé se nestahuje.
    private var saved = Set<String>()
    private(set) var receiving = false

    init(model: AppModel) { self.model = model }

    func run() async {
        while !Task.isCancelled {
            let config = model.config
            guard config.isPaired, let address = config.Address, let token = config.Token else { await delay(5); continue }
            let link = LanLink(address: address, token: token)
            do {
                let items = try await link.outbox(wait: 25)
                model.receiverReachable(true)
                for item in items where failures[item.id, default: 0] < 3 {
                    try await receive(link, item)
                }
                if !items.isEmpty && items.allSatisfy({ failures[$0.id, default: 0] >= 3 }) { await delay(30) }
            } catch is OutboxUnsupported {
                model.receiverReachable(true)
                await delay(600)
            } catch is CancellationError {
                return
            } catch where isUnreachable(error) {
                model.receiverReachable(false)
                await delay(5)
            } catch {
                await delay(60) // zrušené párování – řeší hlídání telefonu v okně
            }
        }
    }

    private func receive(_ link: LanLink, _ item: OutboxItem) async throws {
        if !saved.contains(item.id) {
            receiving = true
            model.receiveStarted(item)
            let folder = model.config.receiveFolder
            let part = folder.appendingPathComponent(".\(item.id).phonedrop-part")
            do {
                try FileManager.default.createDirectory(at: folder, withIntermediateDirectories: true)
                try? FileManager.default.removeItem(at: part)
                var last: Int64 = 0
                try await link.download(item, to: part) { done in
                    Task { @MainActor in
                        guard done - last >= 256 * 1024 || done == item.size else { return }
                        last = done
                        self.model.receiveProgress(item, done)
                    }
                }
                let target = Receiver.uniquePath(folder, item.name)
                try FileManager.default.moveItem(at: part, to: target)
                saved.insert(item.id)
                receiving = false
                model.receiveFinished(item, target)
            } catch is CancellationError {
                try? FileManager.default.removeItem(at: part)
                throw CancellationError()
            } catch {
                receiving = false
                try? FileManager.default.removeItem(at: part)
                failures[item.id, default: 0] += 1
                model.receiveFailed(item, isUnreachable(error) ? L.receiveIncomplete : error.localizedDescription)
                if isUnreachable(error) { throw error }
                return
            }
        }
        try await link.done(item)
        saved.remove(item.id)
    }

    private func delay(_ seconds: Double) async {
        try? await Task.sleep(nanoseconds: UInt64(seconds * 1_000_000_000))
    }

    /// Bezpečný název (bez cesty a zakázaných znaků) a volné jméno: „foto (1).jpg“.
    static func uniquePath(_ folder: URL, _ name: String) -> URL {
        var clean = String(name.replacingOccurrences(of: "\\", with: "/").split(separator: "/").last ?? "")
            .map { $0 == ":" || $0.asciiValue.map { $0 < 32 } == true ? "_" : String($0) }.joined()
            .trimmingCharacters(in: .whitespaces)
        while clean.hasPrefix(".") { clean.removeFirst() } // skrytý soubor by v Stažených nikdo nenašel
        if clean.isEmpty { clean = "file" }
        let ext = (clean as NSString).pathExtension
        var stem = (clean as NSString).deletingPathExtension
        if stem.count > 180 { stem = String(stem.prefix(180)) }
        let dotExt = ext.isEmpty ? "" : "." + ext
        var path = folder.appendingPathComponent(stem + dotExt)
        var i = 1
        while FileManager.default.fileExists(atPath: path.path) {
            path = folder.appendingPathComponent("\(stem) (\(i))\(dotExt)")
            i += 1
        }
        return path
    }
}
