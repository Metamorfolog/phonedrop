import SwiftUI
import UniformTypeIdentifiers

/// Barvy jako ve Windows appce; světlá/tmavá podle macOS.
enum Theme {
    static func dynamic(_ light: (Int, Int, Int), _ dark: (Int, Int, Int)) -> Color {
        Color(nsColor: NSColor(name: nil) { a in
            let c = a.bestMatch(from: [.darkAqua, .aqua]) == .darkAqua ? dark : light
            return NSColor(srgbRed: CGFloat(c.0) / 255, green: CGFloat(c.1) / 255, blue: CGFloat(c.2) / 255, alpha: 1)
        })
    }
    static let back = dynamic((242, 245, 241), (15, 19, 17))
    static let surface = dynamic((255, 255, 255), (26, 32, 28))
    static let surface2 = dynamic((234, 239, 234), (38, 46, 41))
    static let line = dynamic((214, 222, 215), (52, 62, 56))
    static let fore = dynamic((22, 33, 26), (236, 242, 238))
    static let dim = dynamic((102, 115, 106), (154, 166, 158))
    static let accent = dynamic((36, 166, 83), (95, 214, 138))
    static let onAccent = dynamic((255, 255, 255), (6, 33, 15))
    static let bad = dynamic((220, 74, 74), (240, 123, 123))
    static let warn = dynamic((217, 130, 43), (242, 166, 94))
}

struct ContentView: View {
    @EnvironmentObject var model: AppModel

    var body: some View {
        Group {
            if model.showPinPair {
                PinPairView()
            } else {
                VStack(spacing: 0) {
                    Header()
                    DropZone()
                        .padding(.horizontal, 14)
                        .padding(.top, 2)
                        .padding(.bottom, 10)
                    StatusCard()
                        .frame(height: 66)
                        .padding(.horizontal, 14)
                        .padding(.bottom, 14)
                }
                .frame(width: 400, height: 380)
            }
        }
        .background(Theme.back)
        .foregroundColor(Theme.fore)
        // Drop funguje kamkoli do okna, ne jen do rámečku.
        .onDrop(of: [UTType.fileURL], isTargeted: $model.hot) { providers in
            Task { @MainActor in
                var urls: [URL] = []
                for p in providers {
                    if let url = await p.fileURL() { urls.append(url) }
                }
                model.enqueue(urls)
            }
            return true
        }
    }
}

extension NSItemProvider {
    func fileURL() async -> URL? {
        await withCheckedContinuation { c in
            _ = loadObject(ofClass: URL.self) { url, _ in c.resume(returning: url) }
        }
    }
}

struct Header: View {
    @EnvironmentObject var model: AppModel

    var body: some View {
        HStack(spacing: 12) {
            ZStack(alignment: .bottomTrailing) {
                RoundedRectangle(cornerRadius: 10).fill(Theme.surface2).frame(width: 38, height: 38)
                    .overlay(Image(systemName: "iphone").font(.system(size: 18, weight: .medium)).foregroundColor(Theme.dim))
                if model.config.isPaired {
                    Circle().fill(dot).frame(width: 11, height: 11)
                        .overlay(Circle().stroke(Theme.back, lineWidth: 2))
                        .offset(x: 2, y: 2)
                }
            }
            VStack(alignment: .leading, spacing: 2) {
                Text(model.headerTitle).font(.system(size: 14, weight: .semibold)).lineLimit(1)
                Text(model.headerDetail).font(.system(size: 11.5)).foregroundColor(Theme.dim).lineLimit(1)
            }
            Spacer()
            Menu {
                AppMenuItems().environmentObject(model)
            } label: {
                Image(systemName: "ellipsis").font(.system(size: 14, weight: .bold)).foregroundColor(Theme.dim)
                    .frame(width: 30, height: 30)
            }
            .menuStyle(.borderlessButton)
            .menuIndicator(.hidden)
            .frame(width: 30, height: 30)
        }
        .padding(.horizontal, 16)
        .frame(height: 62)
    }

    var dot: Color {
        switch model.online {
        case true: return Theme.accent
        case false: return Theme.bad
        default: return Theme.dim
        }
    }
}

/// Položky menu „⋯“ (stejné jsou i v ikoně na liště).
struct AppMenuItems: View {
    @EnvironmentObject var model: AppModel

    var body: some View {
        Button(L.menuSendFiles) { model.pickFiles() }
        Divider()
        Button(L.menuOpenReceived) { model.openReceived() }
        Button(L.menuReceiveFolder) { model.pickReceiveFolder() }
        Divider()
        Button(L.menuPair) { model.startQrPairing() }
        Button(L.menuPairPin) { model.showWindow(); model.showPinPair = true }
        Toggle(L.menuLogin, isOn: Binding(get: { model.launchAtLogin }, set: { _ in model.toggleLaunchAtLogin() }))
        Picker(L.menuLanguage, selection: Binding(get: { model.config.Language }, set: { model.setLanguage($0) })) {
            Text(L.menuAuto).tag("auto")
            ForEach(L.languages, id: \.code) { Text($0.name).tag($0.code) }
        }
        Divider()
        Button(L.menuQuit) { NSApp.terminate(nil) }
    }
}

struct DropZone: View {
    @EnvironmentObject var model: AppModel
    @State private var hover = false

    var body: some View {
        ZStack {
            RoundedRectangle(cornerRadius: 14)
                .fill(model.hot ? Theme.accent.opacity(0.10) : (hover ? Theme.surface2.opacity(0.6) : Theme.surface))
            RoundedRectangle(cornerRadius: 14)
                .strokeBorder(model.hot ? Theme.accent : Theme.line, style: StrokeStyle(lineWidth: model.hot ? 2 : 1.5, dash: [6, 5]))
            if let qr = model.qrImage {
                HStack(spacing: 16) {
                    Image(nsImage: qr).interpolation(.none).resizable()
                        .frame(width: 150, height: 150)
                        .padding(8)
                        .background(RoundedRectangle(cornerRadius: 8).fill(Color.white))
                    VStack(alignment: .leading, spacing: 6) {
                        Text(L.qrTitle).font(.system(size: 14, weight: .semibold))
                        Text(L.qrApp).font(.system(size: 12)).foregroundColor(Theme.dim)
                        Text(L.qrCamera).font(.system(size: 12)).foregroundColor(Theme.dim)
                        Spacer().frame(height: 6)
                        Text(L.qrPin).font(.system(size: 11.5)).foregroundColor(Theme.accent)
                    }
                    .frame(maxWidth: .infinity, alignment: .leading)
                }
                .padding(14)
            } else {
                VStack(spacing: 8) {
                    ZStack {
                        Circle().fill(model.hot ? Theme.accent : Theme.surface2).frame(width: 64, height: 64)
                        Image(systemName: "arrow.down").font(.system(size: 26, weight: .semibold))
                            .foregroundColor(model.hot ? Theme.onAccent : Theme.accent)
                    }
                    .padding(.bottom, 6)
                    Text(model.hot ? L.dropHot : L.dropTitle).font(.system(size: 15, weight: .semibold))
                    Text(L.dropClick).font(.system(size: 12)).foregroundColor(Theme.dim)
                    Text(L.dropApk).font(.system(size: 11.5)).foregroundColor(Theme.dim).padding(.top, 6)
                }
            }
        }
        .contentShape(Rectangle())
        .onHover { hover = $0 }
        .onTapGesture {
            if model.qrImage != nil { model.showPinPair = true } else { model.pickFiles() }
        }
    }
}

struct StatusCard: View {
    @EnvironmentObject var model: AppModel

    var body: some View {
        HStack(spacing: 12) {
            icon.frame(width: 28, height: 28)
            VStack(alignment: .leading, spacing: 3) {
                HStack {
                    Text(model.cardTitle).font(.system(size: 13, weight: .semibold)).lineLimit(1).truncationMode(.middle)
                    Spacer()
                    if model.queueCount > 0 {
                        Text("+\(model.queueCount)").font(.system(size: 11, weight: .semibold)).foregroundColor(Theme.dim)
                    }
                }
                Text(model.cardDetail).font(.system(size: 11.5)).foregroundColor(Theme.dim).lineLimit(2).truncationMode(.middle)
                if let p = model.cardProgress {
                    ProgressView(value: min(max(p, 0), 1)).progressViewStyle(.linear).tint(Theme.accent)
                        .controlSize(.small)
                }
            }
        }
        .padding(.horizontal, 14)
        .frame(maxWidth: .infinity, maxHeight: .infinity, alignment: .leading)
        .background(RoundedRectangle(cornerRadius: 12).fill(Theme.surface))
        .overlay(RoundedRectangle(cornerRadius: 12).strokeBorder(Theme.line, lineWidth: 1))
    }

    @ViewBuilder var icon: some View {
        switch model.cardKind {
        case .busy:
            ProgressView().controlSize(.small)
        case .ok:
            Image(systemName: "checkmark.circle.fill").font(.system(size: 22)).foregroundColor(Theme.accent)
        case .error:
            Image(systemName: "exclamationmark.circle.fill").font(.system(size: 22)).foregroundColor(Theme.bad)
        case .info:
            Image(systemName: "info.circle").font(.system(size: 22)).foregroundColor(Theme.dim)
        }
    }
}

/// Párování PINem (záloha k QR kódu): seznam telefonů v síti, adresa a PIN.
struct PinPairView: View {
    @EnvironmentObject var model: AppModel
    @State private var phones: [FoundPhone] = []
    @State private var searching = false
    @State private var address = ""
    @State private var pin = ""
    @State private var status = ""
    @State private var statusBad = false
    @State private var pairing = false

    var body: some View {
        VStack(alignment: .leading, spacing: 12) {
            Text(L.pairTitle).font(.system(size: 16, weight: .semibold))
            Text(L.pairIntro).font(.system(size: 12)).foregroundColor(Theme.dim).fixedSize(horizontal: false, vertical: true)

            HStack {
                Text(L.pairFound).font(.system(size: 10.5, weight: .semibold)).foregroundColor(Theme.dim)
                Spacer()
                Button(searching ? L.pairSearching : L.pairSearchAgain) { Task { await search() } }
                    .buttonStyle(.link).disabled(searching)
            }
            VStack(spacing: 0) {
                if phones.isEmpty {
                    Text(searching ? L.pairSearching : L.pairNoneInList).font(.system(size: 12)).foregroundColor(Theme.dim)
                        .frame(maxWidth: .infinity, minHeight: 60)
                } else {
                    ForEach(phones, id: \.self) { p in
                        HStack {
                            Image(systemName: "iphone").foregroundColor(Theme.dim)
                            VStack(alignment: .leading) {
                                Text(p.instance).font(.system(size: 12.5, weight: .medium))
                                Text(p.address).font(.system(size: 11)).foregroundColor(Theme.dim)
                            }
                            Spacer()
                        }
                        .padding(8)
                        .background(address == p.address ? Theme.accent.opacity(0.15) : Color.clear)
                        .contentShape(Rectangle())
                        .onTapGesture { address = p.address }
                    }
                }
            }
            .background(RoundedRectangle(cornerRadius: 8).fill(Theme.surface2))

            HStack(spacing: 10) {
                VStack(alignment: .leading, spacing: 4) {
                    Text(L.pairAddress).font(.system(size: 10.5, weight: .semibold)).foregroundColor(Theme.dim)
                    TextField("192.168.1.20:8765", text: $address).textFieldStyle(.roundedBorder)
                }
                VStack(alignment: .leading, spacing: 4) {
                    Text("PIN").font(.system(size: 10.5, weight: .semibold)).foregroundColor(Theme.dim)
                    TextField("123456", text: $pin).textFieldStyle(.roundedBorder).frame(width: 100)
                        .font(.system(size: 13, design: .monospaced))
                        .onSubmit { Task { await pair() } }
                }
            }
            Text(status).font(.system(size: 11.5)).foregroundColor(statusBad ? Theme.bad : Theme.dim)
                .frame(minHeight: 16, alignment: .topLeading).fixedSize(horizontal: false, vertical: true)
            HStack {
                Spacer()
                Button(L.cancel) { model.showPinPair = false }.keyboardShortcut(.cancelAction)
                Button(pairing ? L.pairing : L.pairButton) { Task { await pair() } }
                    .keyboardShortcut(.defaultAction).disabled(pairing)
            }
        }
        .padding(20)
        .frame(width: 400)
        .task { await search() }
    }

    func search() async {
        searching = true
        phones = await Bonjour.discover()
        searching = false
        if address.isEmpty, let first = phones.first { address = first.address }
    }

    func pair() async {
        var addr = address.trimmingCharacters(in: .whitespaces)
        let p = pin.filter(\.isNumber)
        guard !addr.isEmpty else { status = L.pairNeedAddress; statusBad = true; return }
        guard p.count == 6 else { status = L.pairPinDigits; statusBad = true; return }
        if !addr.contains(":") { addr += ":8765" }
        pairing = true
        status = L.pairing
        statusBad = false
        defer { pairing = false }
        do {
            let phone = try await LanLink.pair(address: addr, pin: p)
            model.paired(phone, key: nil)
            model.showPinPair = false
        } catch {
            status = error is PhoneError ? error.localizedDescription : L.phoneNotResponding(error.localizedDescription)
            statusBad = true
        }
    }
}
