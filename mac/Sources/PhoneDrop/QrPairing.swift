import AppKit
import CoreImage
import CryptoKit

/// Párování QR kódem. Kód je odkaz `https://phonedrop.apexpiksel.cz/pair#1.<c>.<k>`:
/// `c` je jednorázové ID žádosti, `k` tajný klíč sdílený jen přes obrazovku (po síti nikdy nejde).
/// Telefon po naskenování 2 minuty čeká; Mac obchází telefony v síti a žádost „vyzvedne“ s důkazem HMAC(k, c).
struct QrPairing {
    static let baseUrl = "https://phonedrop.apexpiksel.cz/pair#"
    static let lifetime: TimeInterval = 120

    private let claim = QrPairing.random(16)
    private let key = QrPairing.random(32)
    private let created = Date()

    var url: String { QrPairing.baseUrl + "1." + QrPairing.b64(claim) + "." + QrPairing.b64(key) }
    var keyString: String { QrPairing.b64(key) }
    var expired: Bool { Date().timeIntervalSince(created) > QrPairing.lifetime - 5 }

    private var claimId: String { QrPairing.b64(claim) }
    private var proof: String {
        let mac = HMAC<SHA256>.authenticationCode(for: Data(("phonedrop-pair:" + claimId).utf8), using: SymmetricKey(data: key))
        return mac.map { String(format: "%02x", $0) }.joined()
    }

    /// Zkusí žádost vyzvednout na daných adresách; nil = zatím nikdo nenaskenoval.
    func tryClaim(addresses: [String]) async -> PairedPhone? {
        var seen = Set<String>()
        for address in addresses where seen.insert(address).inserted {
            if Task.isCancelled { return nil }
            if let r = try? await LanLink.claim(address: address, claim: claimId, proof: proof) { return r }
        }
        return nil
    }

    /// QR kód jako ostrý obrázek (každý modul = celé pixely).
    func image(scale: CGFloat = 8) -> NSImage? {
        guard let filter = CIFilter(name: "CIQRCodeGenerator") else { return nil }
        filter.setValue(Data(url.utf8), forKey: "inputMessage")
        filter.setValue("M", forKey: "inputCorrectionLevel")
        guard let out = filter.outputImage?.transformed(by: CGAffineTransform(scaleX: scale, y: scale)) else { return nil }
        let rep = NSCIImageRep(ciImage: out)
        let img = NSImage(size: rep.size)
        img.addRepresentation(rep)
        return img
    }

    private static func random(_ n: Int) -> Data {
        var b = [UInt8](repeating: 0, count: n)
        _ = SecRandomCopyBytes(kSecRandomDefault, n, &b)
        return Data(b)
    }

    static func b64(_ d: Data) -> String {
        d.base64EncodedString().replacingOccurrences(of: "=", with: "")
            .replacingOccurrences(of: "+", with: "-").replacingOccurrences(of: "/", with: "_")
    }
}
