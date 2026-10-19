# Protokol PhoneDrop (v1)

Telefon běží jako HTTP server na portu **8765** (jen HTTP/1.1, jeden požadavek na spojení, odpovědi JSON).
V síti se ohlašuje přes mDNS/DNS-SD jako `_phonedrop._tcp` s TXT `id=<UUID telefonu>` a `v=1`.

| Požadavek | Autorizace | Odpověď |
|---|---|---|
| `GET /info` | – | `{"ok":true,"app":"PhoneDrop","v":1,"id":"…","name":"Pixel 8"}` |
| `POST /pair?pin=123456&client=PC` | – | `{"ok":true,"token":"…","id":"…","name":"…"}` nebo 403 |
| `GET /ping` | Bearer | `{"ok":true,"id":"…","name":"…"}` nebo 401 |
| `PUT /upload?name=<název>` + `Content-Length` | Bearer | soubor: `{"ok":true,"kind":"file","path":"Download/Drop/x.pdf"}`<br>APK: `{"ok":true,"kind":"apk","session":42,"canInstall":true}` |
| `GET /install?session=42` | Bearer | `{"ok":true,"state":"pending|confirm|installed|aborted|failed: …|unknown"}` |

- **Párování:** telefon ukazuje 6místný PIN (v appce i v trvalém oznámení). Po úspěšném párování a po 5 chybných pokusech se PIN změní.
  Telefon si ukládá jen SHA-256 tokenu, počítač token v `%APPDATA%\PhoneDrop\config.json`.
- **Chyby:** `{"ok":false,"code":"…","error":"anglický text"}`, kódy `bad_pin`, `unpaired`, `no_space` (+ `needMb`, `freeMb`),
  `no_length`, `not_found`, `bad_request`, `failed`. Text do jazyka uživatele překládá klient podle `code`.
- **Volné místo:** klient posílá `Expect: 100-continue`; telefon nejdřív ověří token a místo (APK počítá dvojnásobek)
  a buď odpoví `100 Continue`, nebo rovnou `507 {"code":"no_space",…}` – tělo se pak vůbec neposílá.
- **Tělo uploadu** se čte proudem přímo do MediaStore (`Download/Drop`, kolize → „jméno (1).ext“) nebo do PackageInstaller session.
  Neúplný přenos se zahodí (soubor smazán / session zrušena).
- **Bez šifrování:** provoz v LAN je čisté HTTP (token jde v hlavičce). Pro domácí Wi-Fi v pořádku, do cizích sítí ne.

## Příprava na relay (zatím neimplementováno)

Mimo domácí síť telefon nebude přímo dosažitelný. Plán:

1. Relay na vlastním serveru (např. `relay.example.com`) s HTTPS. Telefon k němu drží odchozí spojení (long-poll / WebSocket)
   identifikované svým `id`; počítač posílá stejné požadavky na `https://relay/<id>/upload?name=…` se stejným Bearer tokenem.
2. Relay jen přeposílá proud bajtů – token ověřuje dál telefon, takže relay nemusí znát tajemství.
3. Ve Windows appce přibude implementace `IPhoneLink` (`RelayLink`) vedle `LanLink`; `Sender.ConnectAsync` zkusí LAN, pak relay.
   V telefonu `Inbox.accept(name, length, stream, progress)` už nezávisí na tom, odkud data přišla.
