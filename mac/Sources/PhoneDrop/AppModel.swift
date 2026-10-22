import AppKit
import ServiceManagement
import UserNotifications

enum CardKind { case info, busy, ok, error }

/// Stav okna a veškerá logika kolem telefonu (obdoba MainForm z Windows appky).
@MainActor
final class AppModel: ObservableObject {
    @Published var config = Config.load()
    @Published var online: Bool?
    @Published var hot = false
    @Published var qrImage: NSImage?
    @Published var showPinPair = false

    @Published private(set) var cardKind = CardKind.info
    @Published private(set) var cardTitle = ""
    @Published private(set) var cardDetail = ""
    @Published private(set) var cardProgress: Double?
    @Published private(set) var queueCount = 0

    /// Okno je vidět (jinak se výsledky hlásí oznámením a telefon se hlídá méně často).
    var windowVisible = true
    /// Ukáže okno (nastaví AppDelegate).
    var showWindow: () -> Void = {}

    private var queue: [DropItem] = []
    private(set) var busy = false
    private var checking = false
    private var lastCheck = Date.distantPast
    private var lastDiscovery = Date.distantPast
    private var lastReceived: URL?
    private var qrPairing: QrPairing?
    private var qrTask: Task<Void, Never>?
    private var tasks: [Task<Void, Never>] = []
    private lazy var receiver = Receiver(model: self)

    init() {
        L.initLanguage(config.Language)
        if config.isPaired { card(.info, L.ready, L.readyDetail) }
        else { card(.info, L.notPairedTitle, L.notPairedHint) }
    }

    func start() {
        if !config.isPaired { startQrPairing() }
        tasks.append(Task { [weak self] in
            while !Task.isCancelled {
                await self?.checkPhone()
                try? await Task.sleep(nanoseconds: 4_000_000_000)
            }
        })
        tasks.append(Task { [weak self] in await self?.receiver.run() })
        UNUserNotificationCenter.current().requestAuthorization(options: [.alert, .sound]) { _, _ in }
    }

    func stop() {
        tasks.forEach { $0.cancel() }
        qrTask?.cancel()
    }

    // MARK: Karta

    func card(_ kind: CardKind, _ title: String, _ detail: String, progress: Double? = nil) {
        cardKind = kind
        cardTitle = title
        cardDetail = detail
        cardProgress = progress
        queueCount = queue.count
    }

    /// Kartu smí přepsat stav telefonu, jen když neukazuje výsledek přenosu.
    private var cardReplaceable: Bool {
        cardKind == .info || (cardKind == .error && cardTitle == (config.DeviceName ?? L.phone))
    }

    var headerTitle: String { config.isPaired ? (config.DeviceName ?? L.phone) : L.noPhone }
    var headerDetail: String {
        guard config.isPaired, let a = config.Address else { return L.unpaired }
        return online == false ? L.unreachable(a) : a
    }

    // MARK: Odesílání

    func enqueue(_ urls: [URL]) {
        let files = DropItem.expand(urls)
        queue.append(contentsOf: files)
        if !files.isEmpty && !busy { Task { await processQueue() } }
    }

    func pickFiles() {
        let panel = NSOpenPanel()
        panel.title = L.pickTitle
        panel.allowsMultipleSelection = true
        panel.canChooseDirectories = true
        panel.canChooseFiles = true
        panel.treatsFilePackagesAsDirectories = false
        NSApp.activate(ignoringOtherApps: true)
        if panel.runModal() == .OK { enqueue(panel.urls) }
    }

    private func processQueue() async {
        busy = true
        var results: [Outcome] = []
        defer { busy = false }
        guard config.isPaired else {
            queue.removeAll()
            card(.error, L.pairFirst, L.notPairedHint)
            if qrImage == nil { startQrPairing() }
            return
        }
        do {
            card(.busy, config.DeviceName ?? L.phone, L.connecting)
            let link = try await Sender.connect(self)
            setOnline(true)
            while !queue.isEmpty {
                let item = queue.removeFirst()
                let r: Outcome
                do {
                    r = try await Sender.send(self, link: link, item: item)
                } catch where isUnreachable(error) || error is CancellationError {
                    throw error
                } catch {
                    r = Outcome(ok: false, title: item.url.lastPathComponent, detail: error.localizedDescription)
                }
                results.append(r)
                card(r.ok ? .ok : .error, r.title, r.detail)
            }
        } catch {
            queue.removeAll()
            let r = Outcome(ok: false, title: config.DeviceName ?? L.phone,
                            detail: error is PhoneError ? error.localizedDescription : L.notResponding(error.localizedDescription))
            results.append(r)
            card(.error, r.title, r.detail)
            setOnline(false)
        }
        busy = false
        if results.count > 1 {
            let ok = results.filter(\.ok).count
            let bad = results.filter { !$0.ok }.map(\.title)
            card(ok == results.count ? .ok : .error, L.sentSummary(ok, results.count),
                 bad.isEmpty ? L.allGood : bad.joined(separator: "  ·  "))
        }
        if !windowVisible, !results.isEmpty {
            notify("PhoneDrop", results.suffix(3).map { "\($0.ok ? "✓" : "✗") \($0.title): \($0.detail)" }.joined(separator: "\n"))
        }
    }

    // MARK: Párování

    /// QR kód v drop zóně; Mac mezitím obchází telefony v síti, jestli ho některý naskenoval.
    func startQrPairing() {
        stopQrPairing()
        showWindow()
        if !busy { card(.info, L.qrWaiting, config.isPaired ? L.qrPin : L.notPairedHint) }
        qrTask = Task { [weak self] in await self?.qrLoop() }
    }

    func stopQrPairing() {
        qrTask?.cancel()
        qrTask = nil
        qrPairing = nil
        qrImage = nil
    }

    /// Esc zavře QR kód, když už je nějaký telefon spárovaný.
    func cancelQr() {
        guard qrImage != nil, config.isPaired else { return }
        stopQrPairing()
        card(.info, L.ready, L.readyDetail)
    }

    private func qrLoop() async {
        var addresses: [String] = config.Address.map { [$0] } ?? []
        var lastScan = Date.distantPast
        while !Task.isCancelled {
            if qrPairing == nil || qrPairing!.expired {
                qrPairing = QrPairing()
                qrImage = qrPairing!.image()
            }
            // Skryté okno nikdo neskenuje – šetřit síť.
            if windowVisible {
                if Date().timeIntervalSince(lastScan) > 4 {
                    lastScan = Date()
                    for p in await Bonjour.discover(timeout: 1.2) where !addresses.contains(p.address) {
                        addresses.append(p.address)
                    }
                }
                guard let pairing = qrPairing else { return }
                let phone = await pairing.tryClaim(addresses: addresses)
                if Task.isCancelled { return }
                if let phone {
                    paired(phone, key: pairing.keyString)
                    return
                }
            }
            try? await Task.sleep(nanoseconds: 1_500_000_000)
        }
    }

    func paired(_ phone: PairedPhone, key: String?) {
        stopQrPairing()
        config.Address = phone.address
        config.Token = phone.token
        config.DeviceId = phone.id
        config.DeviceName = phone.name
        config.Key = key
        config.save()
        online = true
        card(.ok, L.pairedWith(phone.name), L.dropHere)
        showWindow()
    }

    // MARK: Hlídání telefonu

    /// Ověří, jestli telefon přijímá (každé 4 s s otevřeným oknem, každých 30 s na pozadí).
    /// Když neodpovídá, jednou za 30 s ho zkusí dohledat přes Bonjour (mohl dostat novou IP).
    func checkPhone(force: Bool = false) async {
        guard !checking, !busy, config.isPaired, let address = config.Address, let token = config.Token else { return }
        let interval: TimeInterval = windowVisible ? 4 : 30
        if !force && Date().timeIntervalSince(lastCheck) < interval - 0.5 { return }
        checking = true
        lastCheck = Date()
        defer { checking = false }
        do {
            var ok = try await ping(address, token)
            if !ok && Date().timeIntervalSince(lastDiscovery) > 30 {
                lastDiscovery = Date()
                if let found = await Bonjour.discover(timeout: 1.5).first(where: { $0.id == config.DeviceId }),
                   found.address != config.Address, try await ping(found.address, token) {
                    config.Address = found.address
                    config.save()
                    ok = true
                }
            }
            if !busy { setOnline(ok) }
        } catch {
            // Telefon odpovídá, ale tenhle Mac nezná (zrušené párování).
            if busy { return }
            setOnline(false)
            if cardReplaceable { card(.error, config.DeviceName ?? L.phone, error.localizedDescription) }
        }
    }

    private func ping(_ address: String, _ token: String) async throws -> Bool {
        do {
            try await LanLink(address: address, token: token).ping()
            return true
        } catch where isUnreachable(error) {
            return false
        }
    }

    func setOnline(_ on: Bool) {
        let was = online
        online = on
        if busy || was == on { return }
        let name = config.DeviceName ?? L.phone
        if !on && cardReplaceable {
            card(.info, L.phoneOfflineTitle, L.phoneOfflineHint)
        } else if on, was == false, cardTitle == L.phoneOfflineTitle || (cardKind == .error && cardTitle == name) {
            card(.ok, L.phoneBack, L.readyDetail)
        }
    }

    // MARK: Z telefonu

    func receiverReachable(_ on: Bool) {
        if !busy && !checking && online != on { setOnline(on) }
    }

    func receiveStarted(_ item: OutboxItem) {
        if !busy { card(.busy, item.name, L.receivingFrom(config.DeviceName ?? L.phone), progress: 0) }
    }

    func receiveProgress(_ item: OutboxItem, _ done: Int64) {
        guard !busy else { return }
        card(.busy, item.name, L.receivingFrom(config.DeviceName ?? L.phone) + "  ·  " + sizeText(done) + " / " + sizeText(item.size),
             progress: item.size > 0 ? Double(done) / Double(item.size) : nil)
    }

    func receiveFinished(_ item: OutboxItem, _ path: URL) {
        lastReceived = path
        if !busy { card(.ok, L.receivedTitle, L.receivedIn(path.lastPathComponent, path.deletingLastPathComponent().lastPathComponent)) }
        if !windowVisible { notify(L.receivedTitle, path.lastPathComponent, file: path) }
    }

    func receiveFailed(_ item: OutboxItem, _ message: String) {
        if !busy { card(.error, L.receiveFailed(item.name), message) }
    }

    /// Finder ve složce se soubory z telefonu (s označeným posledním).
    func openReceived(_ file: URL? = nil) {
        if let f = file ?? lastReceived, FileManager.default.fileExists(atPath: f.path) {
            NSWorkspace.shared.activateFileViewerSelecting([f])
        } else {
            NSWorkspace.shared.open(config.receiveFolder)
        }
    }

    func pickReceiveFolder() {
        showWindow()
        let panel = NSOpenPanel()
        panel.message = L.receiveFolderPick
        panel.canChooseFiles = false
        panel.canChooseDirectories = true
        panel.canCreateDirectories = true
        panel.directoryURL = config.receiveFolder
        NSApp.activate(ignoringOtherApps: true)
        guard panel.runModal() == .OK, let url = panel.url else { return }
        config.ReceiveDir = url.standardizedFileURL == Config.downloads.standardizedFileURL ? nil : url.path
        config.save()
        card(.ok, L.receiveFolderSet, config.receiveFolder.path)
    }

    // MARK: Nastavení

    /// Spouštění po přihlášení (Nastavení › Obecné › Položky při přihlášení).
    var launchAtLogin: Bool { SMAppService.mainApp.status == .enabled }

    func toggleLaunchAtLogin() {
        objectWillChange.send()
        do {
            if launchAtLogin { try SMAppService.mainApp.unregister() } else { try SMAppService.mainApp.register() }
        } catch {
            card(.error, L.failed, error.localizedDescription)
        }
    }

    func setLanguage(_ code: String) {
        config.Language = code
        config.save()
        L.initLanguage(code)
        // Texty se čtou při vykreslení – stačí okno překreslit.
        if config.isPaired && qrImage == nil { card(.info, L.ready, L.readyDetail) }
        else if qrImage != nil { card(.info, L.qrWaiting, config.isPaired ? L.qrPin : L.notPairedHint) }
        else { card(.info, L.notPairedTitle, L.notPairedHint) }
    }

    private func notify(_ title: String, _ body: String, file: URL? = nil) {
        let c = UNMutableNotificationContent()
        c.title = title
        c.body = body
        if let file { c.userInfo = ["file": file.path] }
        UNUserNotificationCenter.current().add(UNNotificationRequest(identifier: UUID().uuidString, content: c, trigger: nil))
    }
}
