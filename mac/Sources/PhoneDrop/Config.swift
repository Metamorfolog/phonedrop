import Foundation

/// Spárovaný telefon – uložený v ~/Library/Application Support/PhoneDrop/config.json (stejné klíče jako na Windows).
struct Config: Codable {
    var DeviceId: String?
    var DeviceName: String?
    /// Poslední známá adresa v LAN (host:port). Po změně IP se telefon dohledá přes Bonjour podle DeviceId.
    var Address: String?
    var Token: String?
    /// Sdílený klíč z párování QR kódem (base64url) – pro šifrovaný přenos přes relay v dalších verzích.
    var Key: String?
    var Language = "auto"
    /// Kam se ukládají soubory z telefonu; prázdné = složka Stažené.
    var ReceiveDir: String?

    var isPaired: Bool { !(Token ?? "").isEmpty && !(Address ?? "").isEmpty }

    static var downloads: URL {
        FileManager.default.urls(for: .downloadsDirectory, in: .userDomainMask).first
            ?? FileManager.default.homeDirectoryForCurrentUser.appendingPathComponent("Downloads")
    }

    /// Složka pro soubory z telefonu (vlastní, jinak Stažené).
    var receiveFolder: URL {
        var isDir: ObjCBool = false
        if let d = ReceiveDir, !d.isEmpty, FileManager.default.fileExists(atPath: d, isDirectory: &isDir), isDir.boolValue {
            return URL(fileURLWithPath: d)
        }
        return Config.downloads
    }

    static var dir: URL {
        FileManager.default.urls(for: .applicationSupportDirectory, in: .userDomainMask)[0].appendingPathComponent("PhoneDrop")
    }
    private static var file: URL { dir.appendingPathComponent("config.json") }

    static func load() -> Config {
        guard let data = try? Data(contentsOf: file) else { return Config() }
        return (try? JSONDecoder().decode(Config.self, from: data)) ?? Config()
    }

    func save() {
        try? FileManager.default.createDirectory(at: Config.dir, withIntermediateDirectories: true)
        let enc = JSONEncoder()
        enc.outputFormatting = [.prettyPrinted, .sortedKeys]
        if let data = try? enc.encode(self) { try? data.write(to: Config.file, options: .atomic) }
    }

    enum CodingKeys: String, CodingKey {
        case DeviceId, DeviceName, Address, Token, Key, Language, ReceiveDir
    }

    init() {}

    init(from decoder: Decoder) throws {
        let c = try decoder.container(keyedBy: CodingKeys.self)
        DeviceId = try c.decodeIfPresent(String.self, forKey: .DeviceId)
        DeviceName = try c.decodeIfPresent(String.self, forKey: .DeviceName)
        Address = try c.decodeIfPresent(String.self, forKey: .Address)
        Token = try c.decodeIfPresent(String.self, forKey: .Token)
        Key = try c.decodeIfPresent(String.self, forKey: .Key)
        Language = try c.decodeIfPresent(String.self, forKey: .Language) ?? "auto"
        ReceiveDir = try c.decodeIfPresent(String.self, forKey: .ReceiveDir)
    }
}
