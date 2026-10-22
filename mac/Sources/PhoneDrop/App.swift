import AppKit
import SwiftUI
import UserNotifications

/// PhoneDrop žije ve stavovém řádku nahoře: klik na ikonu otevře panel s drop zónou, pravé tlačítko menu.
/// Soubory jde přetáhnout i rovnou na ikonu. Bez ikony v Docku; běží dál a přijímá soubory z telefonu.
@MainActor
final class AppDelegate: NSObject, NSApplicationDelegate, NSMenuDelegate, NSPopoverDelegate, UNUserNotificationCenterDelegate {
    let model = AppModel()
    var statusItem: NSStatusItem!
    let popover = NSPopover()
    let menu = NSMenu()
    private var pendingFiles: [URL] = []
    private var launched = false
    /// Kdy se panel naposled zavřel – klik na ikonu ho nejdřív zavře (transient) a hned nato by ho znovu otevřel.
    private var closedAt = Date.distantPast
    private var monitors: [Any] = []

    func applicationDidFinishLaunching(_ notification: Notification) {
        buildMainMenu()

        let host = NSHostingController(rootView: ContentView().environmentObject(model))
        host.sizingOptions = .preferredContentSize
        popover.contentViewController = host
        // Klik mimo panel ho zavře (standardní chování macOS). Soubor z Finderu jde přetáhnout na ikonu – panel se sám otevře.
        popover.behavior = .transient
        popover.animates = true
        popover.delegate = self

        statusItem = NSStatusBar.system.statusItem(withLength: NSStatusItem.squareLength)
        if let button = statusItem.button {
            button.image = NSImage(systemSymbolName: "iphone.and.arrow.forward", accessibilityDescription: "PhoneDrop")
                ?? NSImage(systemSymbolName: "iphone", accessibilityDescription: "PhoneDrop")
            button.image?.isTemplate = true
            button.target = self
            button.action = #selector(statusClicked)
            button.sendAction(on: [.leftMouseUp, .rightMouseUp])
            let drop = StatusDropView(frame: button.bounds)
            drop.autoresizingMask = [.width, .height]
            drop.onHot = { [weak self] hot in
                self?.model.hot = hot
                if hot { self?.showWindow() }
            }
            drop.onDrop = { [weak self] urls in self?.showWindow(); self?.model.enqueue(urls) }
            button.addSubview(drop)
        }
        menu.delegate = self

        model.showWindow = { [weak self] in self?.showWindow() }
        UNUserNotificationCenter.current().delegate = self

        monitors.append(NSEvent.addLocalMonitorForEvents(matching: .keyDown) { [weak self] e in
            guard let self, e.keyCode == 53, self.popover.isShown, e.window === self.popover.contentViewController?.view.window else { return e }
            if self.model.showPinPair { self.model.showPinPair = false }
            else if self.model.qrImage != nil && self.model.config.isPaired { self.model.cancelQr() }
            else { self.closeWindow() }
            return nil
        } as Any)

        model.start()
        launched = true
        // První spuštění (nespárováno) ukáže QR kód rovnou – až má ikona ve stavovém řádku své místo.
        DispatchQueue.main.asyncAfter(deadline: .now() + 0.5) { [self] in
            if !model.config.isPaired || !pendingFiles.isEmpty { showWindow() }
            if !pendingFiles.isEmpty { model.enqueue(pendingFiles); pendingFiles = [] }
        }
    }

    /// Soubory otevřené přes „Otevřít v aplikaci“ nebo `open -a PhoneDrop soubor`.
    func application(_ application: NSApplication, open urls: [URL]) {
        if launched { showWindow(); model.enqueue(urls) } else { pendingFiles += urls }
    }

    func applicationShouldTerminateAfterLastWindowClosed(_ sender: NSApplication) -> Bool { false }

    func applicationShouldHandleReopen(_ sender: NSApplication, hasVisibleWindows flag: Bool) -> Bool {
        showWindow()
        return true
    }

    func applicationWillTerminate(_ notification: Notification) { model.stop() }

    @objc func statusClicked() {
        if NSApp.currentEvent?.type == .rightMouseUp || NSApp.currentEvent?.modifierFlags.contains(.control) == true {
            closeWindow()
            statusItem.menu = menu
            statusItem.button?.performClick(nil)
            statusItem.menu = nil
        } else if popover.isShown {
            closeWindow()
        } else if Date().timeIntervalSince(closedAt) > 0.3 {
            showWindow()
        }
    }

    func showWindow() {
        guard let button = statusItem.button else { return }
        if !popover.isShown {
            popover.show(relativeTo: button.bounds, of: button, preferredEdge: .minY)
        }
        NSApp.activate(ignoringOtherApps: true)
        popover.contentViewController?.view.window?.makeKey()
        model.windowVisible = true
        statusItem.button?.highlight(true)
        Task { await model.checkPhone(force: true) }
    }

    func closeWindow() {
        guard popover.isShown else { return }
        popover.performClose(nil)
    }

    func popoverDidClose(_ notification: Notification) {
        closedAt = Date()
        model.windowVisible = false
        model.showPinPair = false
        statusItem.button?.highlight(false)
    }

    // MARK: Menu (pravé tlačítko na ikoně)

    func menuNeedsUpdate(_ menu: NSMenu) {
        menu.removeAllItems()
        let head = NSMenuItem(title: model.headerTitle + " · " + model.headerDetail, action: nil, keyEquivalent: "")
        head.isEnabled = false
        menu.addItem(head)
        menu.addItem(.separator())
        menu.addItem(item(L.menuShow) { $0.showWindow() })
        menu.addItem(item(L.menuSendFiles) { $0.showWindow(); $0.model.pickFiles() })
        menu.addItem(.separator())
        menu.addItem(item(L.menuOpenReceived) { $0.model.openReceived() })
        menu.addItem(item(L.menuReceiveFolder) { $0.model.pickReceiveFolder() })
        menu.addItem(.separator())
        menu.addItem(item(L.menuPair) { $0.model.startQrPairing() })
        menu.addItem(item(L.menuPairPin) { $0.showWindow(); $0.model.showPinPair = true })
        let login = item(L.menuLogin) { $0.model.toggleLaunchAtLogin() }
        login.state = model.launchAtLogin ? .on : .off
        menu.addItem(login)
        let lang = NSMenuItem(title: L.menuLanguage, action: nil, keyEquivalent: "")
        let sub = NSMenu()
        for (code, name) in [("auto", L.menuAuto)] + L.languages.map({ ($0.code, $0.name) }) {
            let i = item(name) { d in d.model.setLanguage(code); d.buildMainMenu() }
            i.state = model.config.Language == code ? .on : .off
            sub.addItem(i)
        }
        lang.submenu = sub
        menu.addItem(lang)
        menu.addItem(.separator())
        menu.addItem(NSMenuItem(title: L.menuQuit, action: #selector(NSApplication.terminate(_:)), keyEquivalent: "q"))
    }

    private func item(_ title: String, _ action: @escaping (AppDelegate) -> Void) -> NSMenuItem {
        let i = ClosureMenuItem(title: title, action: #selector(ClosureMenuItem.fire), keyEquivalent: "")
        i.target = i
        i.handler = { [weak self] in if let self { action(self) } }
        return i
    }

    /// Hlavní menu není vidět (appka nemá Dock), ale drží zkratky: Cmd+Q, kopírování a vkládání v polích.
    func buildMainMenu() {
        let main = NSMenu()
        let app = NSMenu()
        app.addItem(NSMenuItem(title: L.menuQuit, action: #selector(NSApplication.terminate(_:)), keyEquivalent: "q"))
        main.addItem(withSubmenu: app, title: "PhoneDrop")

        let file = NSMenu(title: "File")
        let send = NSMenuItem(title: L.menuSendFiles, action: #selector(sendFiles), keyEquivalent: "o")
        send.target = self
        file.addItem(send)
        let close = NSMenuItem(title: L.menuClose, action: #selector(closeFromMenu), keyEquivalent: "w")
        close.target = self
        file.addItem(close)
        main.addItem(withSubmenu: file, title: "File")

        let edit = NSMenu(title: L.menuEdit)
        edit.addItem(NSMenuItem(title: "Cut", action: #selector(NSText.cut(_:)), keyEquivalent: "x"))
        edit.addItem(NSMenuItem(title: "Copy", action: #selector(NSText.copy(_:)), keyEquivalent: "c"))
        edit.addItem(NSMenuItem(title: "Paste", action: #selector(NSText.paste(_:)), keyEquivalent: "v"))
        edit.addItem(NSMenuItem(title: "Select All", action: #selector(NSText.selectAll(_:)), keyEquivalent: "a"))
        main.addItem(withSubmenu: edit, title: L.menuEdit)
        NSApp.mainMenu = main
    }

    @objc func sendFiles() { showWindow(); model.pickFiles() }
    @objc func closeFromMenu() { closeWindow() }

    // MARK: Oznámení

    nonisolated func userNotificationCenter(_ center: UNUserNotificationCenter, didReceive response: UNNotificationResponse,
                                            withCompletionHandler completionHandler: @escaping () -> Void) {
        let path = response.notification.request.content.userInfo["file"] as? String
        Task { @MainActor in
            if let path { self.model.openReceived(URL(fileURLWithPath: path)) } else { self.showWindow() }
        }
        completionHandler()
    }

    nonisolated func userNotificationCenter(_ center: UNUserNotificationCenter, willPresent notification: UNNotification,
                                            withCompletionHandler completionHandler: @escaping (UNNotificationPresentationOptions) -> Void) {
        completionHandler([.banner])
    }
}

/// Průhledná vrstva přes ikonu ve stavovém řádku: přijímá přetažené soubory, kliknutí propouští dál.
final class StatusDropView: NSView {
    var onHot: (Bool) -> Void = { _ in }
    var onDrop: ([URL]) -> Void = { _ in }

    override init(frame: NSRect) {
        super.init(frame: frame)
        registerForDraggedTypes([.fileURL])
    }

    required init?(coder: NSCoder) { fatalError() }

    override func hitTest(_ point: NSPoint) -> NSView? { nil }

    override func draggingEntered(_ sender: NSDraggingInfo) -> NSDragOperation {
        onHot(true)
        return .copy
    }

    override func draggingExited(_ sender: NSDraggingInfo?) { onHot(false) }

    override func performDragOperation(_ sender: NSDraggingInfo) -> Bool {
        onHot(false)
        let urls = sender.draggingPasteboard.readObjects(forClasses: [NSURL.self], options: [.urlReadingFileURLsOnly: true]) as? [URL] ?? []
        if !urls.isEmpty { onDrop(urls) }
        return !urls.isEmpty
    }
}

final class ClosureMenuItem: NSMenuItem {
    var handler: () -> Void = {}
    @objc func fire() { handler() }
}

extension NSMenu {
    func addItem(withSubmenu submenu: NSMenu, title: String) {
        let i = NSMenuItem(title: title, action: nil, keyEquivalent: "")
        i.submenu = submenu
        addItem(i)
    }
}

@main
enum Main {
    /// NSApplication.delegate je slabý odkaz – delegát musí žít tady.
    @MainActor static var delegate: AppDelegate?

    static func main() {
        let app = NSApplication.shared
        MainActor.assumeIsolated {
            delegate = AppDelegate()
            app.delegate = delegate
        }
        app.setActivationPolicy(.accessory)
        app.run()
    }
}
