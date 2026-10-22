import Foundation

/// Texty v angličtině a češtině (stejné jako ve Windows appce). Jazyk: volba v menu, jinak podle macOS.
enum L {
    static let languages: [(code: String, name: String)] = [("en", "English"), ("cs", "Čeština")]
    private(set) static var current = "en"

    /// "auto" nebo kód jazyka z `languages`.
    static func initLanguage(_ setting: String?) {
        var code = setting ?? "auto"
        if code == "auto" { code = String((Locale.preferredLanguages.first ?? "en").prefix(2)) }
        current = languages.contains { $0.code == code } ? code : code == "sk" ? "cs" : "en"
    }

    private static func pick(_ en: String, _ cs: String) -> String { current == "cs" ? cs : en }

    // Okno
    static var ready: String { pick("Ready", "Připraveno") }
    static var readyDetail: String { pick("Files → Downloads › Drop, APKs → installer", "Soubory jdou do Stažené › Drop, APK k instalaci") }
    static var notPairedTitle: String { pick("No phone paired", "Telefon není spárovaný") }
    static var notPairedHint: String { pick("Scan the QR code with your phone", "Naskenuj QR kód telefonem") }
    static var qrTitle: String { pick("Scan with your phone", "Naskenuj telefonem") }
    static var qrApp: String { pick("PhoneDrop → Scan QR code", "PhoneDrop → Naskenovat QR kód") }
    static var qrCamera: String { pick("or with the phone's camera", "nebo foťákem telefonu") }
    static var qrPin: String { pick("Click to enter a PIN instead", "Kliknutím zadáš PIN") }
    static var qrWaiting: String { pick("Waiting for the phone…", "Čekám na telefon…") }
    static var pairFirst: String { pick("Pair a phone first", "Nejdřív spáruj telefon") }
    static var noPhone: String { pick("No phone", "Žádný telefon") }
    static var unpaired: String { pick("Not paired", "Nespárováno") }
    static var phone: String { pick("Phone", "Telefon") }
    static var phoneOfflineTitle: String { pick("The phone isn't receiving", "Telefon teď nepřijímá") }
    static var phoneOfflineHint: String { pick("Open PhoneDrop on it and turn on Receive files", "Otevři v něm PhoneDrop a zapni Přijímat soubory") }
    static var phoneBack: String { pick("The phone is back", "Telefon je zase připojený") }
    static var connecting: String { pick("Connecting…", "Připojuji…") }
    static func unreachable(_ addr: String) -> String { pick("Unreachable · ", "Nedostupný · ") + addr }
    static func notResponding(_ msg: String) -> String { pick("Not responding: ", "Neodpovídá: ") + msg }
    static func sentSummary(_ ok: Int, _ all: Int) -> String { pick("Sent \(ok) of \(all)", "Odesláno \(ok) z \(all)") }
    static var allGood: String { pick("all good", "vše v pořádku") }
    static func pairedWith(_ name: String) -> String { pick("Paired with \(name)", "Spárováno s \(name)") }
    static var dropHere: String { pick("Drop a file here", "Přetáhni sem soubor") }
    static var failed: String { pick("That didn't work", "Nepodařilo se") }

    // Drop zóna
    static var dropTitle: String { pick("Drop a file or APK here", "Přetáhni sem soubor nebo APK") }
    static var dropHot: String { pick("Let go – off to the phone", "Pusť a letí do telefonu") }
    static var dropClick: String { pick("or click to choose", "nebo klikni a vyber") }
    static var dropApk: String { pick("APKs open the installer right away", "APK se rovnou nabídne k instalaci") }
    static var pickTitle: String { pick("Send to phone", "Poslat do telefonu") }

    // Menu
    static var menuShow: String { pick("Show PhoneDrop", "Zobrazit PhoneDrop") }
    static var menuPair: String { pick("Pair phone…", "Spárovat telefon…") }
    static var menuPairPin: String { pick("Pair with a PIN…", "Spárovat PINem…") }
    static var menuLogin: String { pick("Open at login", "Spouštět po přihlášení") }
    static var menuLanguage: String { pick("Language", "Jazyk") }
    static var menuAuto: String { pick("Automatic (macOS)", "Automaticky (podle macOS)") }
    static var menuQuit: String { pick("Quit PhoneDrop", "Ukončit PhoneDrop") }
    static var menuAbout: String { pick("About PhoneDrop", "O aplikaci PhoneDrop") }
    static var menuHide: String { pick("Hide PhoneDrop", "Skrýt PhoneDrop") }
    static var menuEdit: String { pick("Edit", "Úpravy") }
    static var menuWindow: String { pick("Window", "Okno") }
    static var menuClose: String { pick("Close", "Zavřít") }
    static var menuSendFiles: String { pick("Send files…", "Poslat soubory…") }

    // Párování
    static var pairTitle: String { pick("Pair phone", "Spárovat telefon") }
    static var pairIntro: String { pick("Open PhoneDrop on the phone, pick it from the list here and type the PIN it shows.",
        "V telefonu otevři PhoneDrop, vyber ho tady ze seznamu a opiš PIN, který ukazuje.") }
    static var pairFound: String { pick("PHONES ON THIS NETWORK", "TELEFONY V SÍTI") }
    static var pairSearchAgain: String { pick("Search again", "Hledat znovu") }
    static var pairSearching: String { pick("Searching…", "Hledám…") }
    static var pairNoneInList: String { pick("No phone on the network. Is PhoneDrop running on it?", "Žádný telefon v síti. Běží v něm PhoneDrop?") }
    static var pairAddress: String { pick("ADDRESS", "ADRESA") }
    static var pairNeedAddress: String { pick("Enter the phone's address.", "Zadej adresu telefonu.") }
    static var pairPinDigits: String { pick("The PIN has 6 digits.", "PIN má 6 číslic.") }
    static var pairing: String { pick("Pairing…", "Páruji…") }
    static var pairButton: String { pick("Pair", "Spárovat") }
    static var cancel: String { pick("Cancel", "Zrušit") }
    static func phoneNotResponding(_ msg: String) -> String { pick("Phone not responding: ", "Telefon neodpovídá: ") + msg }

    // Přenos
    static var notPaired: String { pick("No phone is paired.", "Telefon není spárovaný.") }
    static var searchingNetwork: String { pick("Searching the network…", "Hledám v síti…") }
    static var phoneNotFound: String { pick("Not found on the network. Is receiving turned on?", "Nenalezen v síti. Je v něm zapnutý příjem?") }
    static func progress(_ done: String, _ total: String, _ speed: String) -> String { pick("\(done) of \(total)\(speed)", "\(done) z \(total)\(speed)") }
    static func savedTo(_ path: String) -> String { pick("Saved to ", "Uloženo do ") + path }
    static var confirmInstall: String { pick("Confirm the install on the phone…", "Potvrď instalaci v telefonu…") }
    static var allowInstall: String { pick("On the phone, allow PhoneDrop to install apps and confirm…", "V telefonu povol PhoneDropu instalaci aplikací a potvrď…") }
    static var installed: String { pick("Installed", "Nainstalováno") }
    static var installAborted: String { pick("Install cancelled on the phone", "Instalace zrušena v telefonu") }
    static var installUnknown: String { pick("Sent, install result unknown", "Odesláno, výsledek instalace neznámý") }
    static func installFailed(_ why: String) -> String { pick("Install failed: ", "Instalace selhala: ") + why }
    static var installPending: String { pick("Sent, the install is waiting for confirmation on the phone", "Odesláno, instalace čeká na potvrzení v telefonu") }

    // Z telefonu do počítače
    static func receivingFrom(_ phone: String) -> String { pick("From \(phone)", "Z telefonu \(phone)") }
    static var receivedTitle: String { pick("Received from the phone", "Přijato z telefonu") }
    static func receivedIn(_ name: String, _ folder: String) -> String { "\(name) → \(folder)" }
    static func receiveFailed(_ name: String) -> String { pick("Couldn't receive \(name)", "Nepodařilo se přijmout \(name)") }
    static var receiveIncomplete: String { pick("The transfer was interrupted", "Přenos se přerušil") }
    static var menuOpenReceived: String { pick("Open files from the phone", "Otevřít soubory z telefonu") }
    static var menuReceiveFolder: String { pick("Folder for files from the phone…", "Složka pro soubory z telefonu…") }
    static var receiveFolderPick: String { pick("Where should files from the phone go?", "Kam ukládat soubory z telefonu?") }
    static var receiveFolderSet: String { pick("Files from the phone go to", "Soubory z telefonu se ukládají do") }

    // Chyby od telefonu (kódy z PROTOCOL.md)
    static var wrongPin: String { pick("Wrong PIN.", "Špatný PIN.") }
    static var pairingClosed: String { pick("Open PhoneDrop on the phone – pairing only works while it's open.",
        "Otevři v telefonu PhoneDrop – párovat jde, jen když je appka otevřená.") }
    static func pairingLocked(_ seconds: Int) -> String { pick("Too many wrong PINs. The phone shows a new one; try again in \(seconds) s.",
        "Moc špatných PINů. Telefon ukazuje nový, zkus to znovu za \(seconds) s.") }
    static var unknownComputer: String { pick("The phone doesn't know this Mac – pair it again.", "Telefon tento Mac nezná – spáruj ho znovu.") }
    static func noSpace(_ need: Int, _ free: Int) -> String { pick("Not enough space on the phone: need \(need) MB, \(free) MB free",
        "V telefonu není místo: potřeba \(need) MB, volno \(free) MB") }
    static func invalidResponse(_ code: Int) -> String { pick("Invalid response (\(code)).", "Neplatná odpověď (\(code)).") }
    static func phoneError(_ code: Int) -> String { pick("Error \(code).", "Chyba \(code).") }
}
