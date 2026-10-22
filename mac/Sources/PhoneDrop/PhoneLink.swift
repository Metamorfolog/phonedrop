import Foundation

/// Výsledek nahrání: soubor uložený v telefonu, nebo APK čekající na potvrzení instalace.
struct SendResult {
    let kind: String
    let path: String?
    let session: Int
    let canInstall: Bool
}

/// Chyba, kterou hlásí telefon (už přeložená do jazyka počítače).
struct PhoneError: LocalizedError {
    let message: String
    var errorDescription: String? { message }
}

/// Telefon neodpovídá (síť, timeout) – na rozdíl od `PhoneError`, kdy odpověděl, ale odmítl.
struct UnreachableError: LocalizedError {
    let message: String
    var errorDescription: String? { message }
}

/// Starší telefon bez posílání do počítače (`/outbox` vrací 404).
struct OutboxUnsupported: Error {}

/// Soubor, který telefon posílá do počítače (Sdílet › PhoneDrop).
struct OutboxItem {
    let id: String
    let name: String
    let size: Int64
}

struct PairedPhone {
    let token: String
    let id: String
    let name: String
    let address: String
}

/// Přímé HTTP k telefonu v místní síti (viz PROTOCOL.md).
struct LanLink {
    let address: String
    let token: String

    static let session: URLSession = {
        let c = URLSessionConfiguration.ephemeral
        c.httpMaximumConnectionsPerHost = 4
        c.waitsForConnectivity = false
        c.timeoutIntervalForResource = 7 * 24 * 3600
        return URLSession(configuration: c)
    }()

    static var clientName: String {
        Host.current().localizedName ?? ProcessInfo.processInfo.hostName
    }

    static func url(_ address: String, _ pathAndQuery: String) -> URL {
        URL(string: "http://\(address)\(pathAndQuery)")!
    }

    static func q(_ s: String) -> String {
        var allowed = CharacterSet.urlQueryAllowed
        allowed.remove(charactersIn: "&=+?#/")
        return s.addingPercentEncoding(withAllowedCharacters: allowed) ?? s
    }

    // MARK: Párování

    /// Spáruje Mac s telefonem pomocí PINu z telefonu.
    static func pair(address: String, pin: String) async throws -> PairedPhone {
        var req = URLRequest(url: url(address, "/pair?pin=\(q(pin))&client=\(q(clientName))"), timeoutInterval: 10)
        req.httpMethod = "POST"
        let (json, res) = try await call(req)
        try check(res, json)
        return PairedPhone(token: json["token"] as? String ?? "", id: json["id"] as? String ?? "",
                           name: json["name"] as? String ?? "", address: address)
    }

    /// Párování QR kódem: vyzvedne žádost, kterou telefon naskenoval. Nil = tenhle telefon o ní neví.
    static func claim(address: String, claim: String, proof: String) async throws -> PairedPhone? {
        var req = URLRequest(url: url(address, "/pair/claim?c=\(q(claim))&proof=\(proof)&client=\(q(clientName))"), timeoutInterval: 3)
        req.httpMethod = "POST"
        let (json, res) = try await call(req)
        if res.statusCode == 404 { return nil }
        try check(res, json)
        return PairedPhone(token: json["token"] as? String ?? "", id: json["id"] as? String ?? "",
                           name: json["name"] as? String ?? "", address: address)
    }

    /// Ověří, že na adrese běží PhoneDrop; vrátí jeho ID a název.
    static func info(address: String) async throws -> (id: String, name: String) {
        let (json, res) = try await call(URLRequest(url: url(address, "/info"), timeoutInterval: 3))
        try check(res, json)
        return (json["id"] as? String ?? "", json["name"] as? String ?? "")
    }

    // MARK: Spárovaný telefon

    func ping() async throws {
        let (json, res) = try await LanLink.call(request("GET", "/ping", timeout: 4))
        try LanLink.check(res, json)
    }

    func send(file: URL, dir: String?, progress: @escaping @Sendable (Int64) -> Void) async throws -> SendResult {
        var query = "/upload?name=" + LanLink.q(file.lastPathComponent)
        if let dir, !dir.isEmpty { query += "&dir=" + LanLink.q(dir) }
        var req = request("PUT", query, timeout: 60)
        let size = (try? FileManager.default.attributesOfItem(atPath: file.path)[.size] as? NSNumber)?.int64Value ?? 0
        req.setValue(String(size), forHTTPHeaderField: "Content-Length")
        req.setValue("application/octet-stream", forHTTPHeaderField: "Content-Type")
        let delegate = UploadProgress(progress)
        let data: Data, response: URLResponse
        do {
            (data, response) = try await LanLink.session.upload(for: req, fromFile: file, delegate: delegate)
        } catch {
            throw LanLink.wrap(error)
        }
        let res = response as! HTTPURLResponse
        let json = try LanLink.parse(data, res)
        try LanLink.check(res, json)
        return SendResult(kind: json["kind"] as? String ?? "file",
                          path: json["path"] as? String,
                          session: (json["session"] as? NSNumber)?.intValue ?? -1,
                          canInstall: (json["canInstall"] as? Bool) ?? true)
    }

    func installState(session: Int) async throws -> String {
        let (json, res) = try await LanLink.call(request("GET", "/install?session=\(session)", timeout: 4))
        try LanLink.check(res, json)
        return json["state"] as? String ?? "unknown"
    }

    /// Soubory, které telefon chce poslat do počítače (dlouhý dotaz).
    func outbox(wait: Int) async throws -> [OutboxItem] {
        let (json, res) = try await LanLink.call(request("GET", "/outbox?wait=\(wait)", timeout: TimeInterval(wait + 15)))
        if res.statusCode == 404 { throw OutboxUnsupported() }
        try LanLink.check(res, json)
        return (json["items"] as? [[String: Any]] ?? []).map {
            OutboxItem(id: $0["id"] as? String ?? "", name: $0["name"] as? String ?? "file",
                       size: ($0["size"] as? NSNumber)?.int64Value ?? 0)
        }
    }

    /// Stáhne soubor z telefonu do `target` (bez dat 30 s = chyba).
    func download(_ item: OutboxItem, to target: URL, progress: @escaping @Sendable (Int64) -> Void) async throws {
        let req = request("GET", "/outbox/file?id=" + LanLink.q(item.id), timeout: 30)
        let done = try await FileDownload(request: req, target: target, progress: progress).run()
        if done != item.size { throw PhoneError(message: L.receiveIncomplete) }
    }

    /// Potvrdí telefonu, že je soubor uložený (smaže ho z fronty).
    func done(_ item: OutboxItem) async throws {
        let (json, res) = try await LanLink.call(request("POST", "/outbox/done?id=" + LanLink.q(item.id), timeout: 10))
        try LanLink.check(res, json)
    }

    // MARK: Pomocné

    func request(_ method: String, _ path: String, timeout: TimeInterval) -> URLRequest {
        var req = URLRequest(url: LanLink.url(address, path), timeoutInterval: timeout)
        req.httpMethod = method
        req.setValue("Bearer " + token, forHTTPHeaderField: "Authorization")
        return req
    }

    static func call(_ req: URLRequest) async throws -> ([String: Any], HTTPURLResponse) {
        let data: Data, response: URLResponse
        do {
            (data, response) = try await session.data(for: req)
        } catch {
            throw wrap(error)
        }
        let res = response as! HTTPURLResponse
        if res.statusCode == 404 { return ((try? JSONSerialization.jsonObject(with: data)) as? [String: Any] ?? [:], res) }
        return (try parse(data, res), res)
    }

    static func wrap(_ error: Error) -> Error {
        if error is CancellationError { return error }
        if let u = error as? URLError, u.code == .cancelled, Task.isCancelled { return CancellationError() }
        return UnreachableError(message: error.localizedDescription)
    }

    static func parse(_ data: Data, _ res: HTTPURLResponse) throws -> [String: Any] {
        guard let json = (try? JSONSerialization.jsonObject(with: data)) as? [String: Any] else {
            throw PhoneError(message: L.invalidResponse(res.statusCode))
        }
        return json
    }

    static func check(_ res: HTTPURLResponse, _ json: [String: Any]) throws {
        if res.statusCode == 401 { throw PhoneError(message: L.unknownComputer) }
        if (200..<300).contains(res.statusCode), json["ok"] as? Bool == true { return }
        // Telefon posílá strojový kód (překládá se tady) a anglický text jako zálohu.
        func int(_ k: String) -> Int { (json[k] as? NSNumber)?.intValue ?? 0 }
        switch json["code"] as? String {
        case "no_space": throw PhoneError(message: L.noSpace(int("needMb"), int("freeMb")))
        case "bad_pin": throw PhoneError(message: L.wrongPin)
        case "pairing_closed": throw PhoneError(message: L.pairingClosed)
        case "pairing_locked": throw PhoneError(message: L.pairingLocked(json["retryAfter"] == nil ? 60 : int("retryAfter")))
        case "unpaired": throw PhoneError(message: L.unknownComputer)
        default: throw PhoneError(message: json["error"] as? String ?? L.phoneError(res.statusCode))
        }
    }
}

/// Průběh odesílání těla PUT.
final class UploadProgress: NSObject, URLSessionTaskDelegate, @unchecked Sendable {
    let report: @Sendable (Int64) -> Void
    init(_ report: @escaping @Sendable (Int64) -> Void) { self.report = report }

    func urlSession(_ session: URLSession, task: URLSessionTask, didSendBodyData bytesSent: Int64,
                    totalBytesSent: Int64, totalBytesExpectedToSend: Int64) {
        report(totalBytesSent)
    }
}

/// Stahování proudem rovnou do souboru s hlášením průběhu.
final class FileDownload: NSObject, URLSessionDataDelegate, @unchecked Sendable {
    let request: URLRequest
    let target: URL
    let progress: @Sendable (Int64) -> Void
    private var handle: FileHandle?
    private var done: Int64 = 0
    private var status = 0
    private var errorBody = Data()
    private var continuation: CheckedContinuation<Int64, Error>?
    private var session: URLSession?

    init(request: URLRequest, target: URL, progress: @escaping @Sendable (Int64) -> Void) {
        self.request = request
        self.target = target
        self.progress = progress
    }

    func run() async throws -> Int64 {
        guard FileManager.default.createFile(atPath: target.path, contents: nil) else {
            throw PhoneError(message: "Cannot write \(target.path)")
        }
        handle = try FileHandle(forWritingTo: target)
        return try await withTaskCancellationHandler {
            try await withCheckedThrowingContinuation { c in
                continuation = c
                let s = URLSession(configuration: .ephemeral, delegate: self, delegateQueue: nil)
                session = s
                s.dataTask(with: request).resume()
            }
        } onCancel: {
            self.session?.invalidateAndCancel()
        }
    }

    func urlSession(_ session: URLSession, dataTask: URLSessionDataTask, didReceive response: URLResponse,
                    completionHandler: @escaping (URLSession.ResponseDisposition) -> Void) {
        status = (response as? HTTPURLResponse)?.statusCode ?? 0
        completionHandler(.allow)
    }

    func urlSession(_ session: URLSession, dataTask: URLSessionDataTask, didReceive data: Data) {
        if !(200..<300).contains(status) { errorBody.append(data); return }
        do {
            try handle?.write(contentsOf: data)
            done += Int64(data.count)
            progress(done)
        } catch {
            finish(.failure(error))
            dataTask.cancel()
        }
    }

    func urlSession(_ session: URLSession, task: URLSessionTask, didCompleteWithError error: Error?) {
        if let error {
            finish(.failure(LanLink.wrap(error)))
        } else if !(200..<300).contains(status) {
            let res = HTTPURLResponse(url: request.url!, statusCode: status, httpVersion: nil, headerFields: nil)!
            let json = (try? JSONSerialization.jsonObject(with: errorBody)) as? [String: Any] ?? [:]
            do { try LanLink.check(res, json); finish(.failure(PhoneError(message: L.phoneError(status)))) }
            catch { finish(.failure(error)) }
        } else {
            finish(.success(done))
        }
        session.finishTasksAndInvalidate()
    }

    private func finish(_ result: Result<Int64, Error>) {
        try? handle?.close()
        handle = nil
        continuation?.resume(with: result)
        continuation = nil
    }
}
