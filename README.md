# PhoneDrop

Náhrada za „adb install“ přes wireless debugging: **přetáhni soubor do okénka ve Windows a je v telefonu.**

- `.apk` → telefon rovnou otevře systémovou instalaci (jedno ťuknutí na **Instalovat**; tichá instalace bez rootu/ADB nejde).
- ostatní soubory → `Stažené/Drop` (Download/Drop), s oznámením, které soubor otevře.
- Funguje v místní síti (Wi-Fi). Telefon se najde sám přes mDNS, i když se mu změní IP.

```
android/   přijímač pro Android (Java, bez knihoven, minSdk 29)
windows/   okénko pro Windows (.NET 10, WinForms) + příkazová řádka
PROTOCOL.md  HTTP protokol a plán relay mimo domácí síť
```

## Sestavení

**Android** (Gradle 9.1 + AGP 9.0.1, JDK z Android Studia):

```bash
cd android
JAVA_HOME="/c/Program Files/Android/Android Studio/jbr" ./gradlew assembleDebug
```

APK: `android/app/build/outputs/apk/debug/app-debug.apk`. `android/local.properties` (není v gitu) musí obsahovat
`sdk.dir=C:/Users/piklr/dev/android-sdk` (s lomítky).

**Windows** (.NET 10 SDK):

```bash
cd windows/PhoneDrop
dotnet publish -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -o ../publish
```

Výsledek: `windows/publish/PhoneDrop.exe` (~260 kB, potřebuje .NET 10 Desktop Runtime). Bez runtime:
`--self-contained true` (větší exe). Ikonu přegeneruje `windows/make-icon.ps1`.

## První spuštění

1. **Telefon:** nainstaluj APK (jednou přes kabel/adb nebo stažením), otevři PhoneDrop, povol oznámení.
   Ukáže adresu a **PIN**. Klepni na *Povolit instalaci aplikací* a *Vypnout optimalizaci baterie*
   (jinak telefon se zhasnutým displejem v Doze přestane přijímat).
2. **Windows:** spusť `PhoneDrop.exe`. Otevře se párování – vyber telefon ze seznamu (nebo opiš adresu) a zadej PIN.
3. Přetáhni soubor do okénka. Křížek okno jen schová do oznamovací oblasti; konec přes ⋯ → Ukončit.
4. Volitelně ⋯ → *V Průzkumníku: Odeslat do › Telefon* – pak pravé tlačítko na soubor → **Odeslat do › Telefon (PhoneDrop)**
   (ve Windows 11 pod „Zobrazit další možnosti“). Když už okno běží, soubory se předají jemu.

### Jak probíhá instalace APK

- Je-li PhoneDrop v telefonu otevřený, systémový dialog *Instalovat / Aktualizovat* se ukáže hned.
- Je-li na pozadí, Android aplikacím nedovolí otevřít okno samo (Background Activity Launch) – objeví se oznámení
  **Instalovat … – ťukni a potvrď**, ťuknutí otevře dialog.
- Při úplně první instalaci se systém zeptá na povolení „Instalovat neznámé aplikace“ pro PhoneDrop (jednou).
- Google Play Protect může u nového/neznámého APK nabídnout sken – *Další podrobnosti → Instalovat bez skenování*.
- Okénko ve Windows čeká na výsledek a ukáže *nainstalováno ✓ / zrušeno / selhalo (důvod)*.

## Jazyky

Čeština a angličtina, výchozí podle jazyka systému.

- **Windows:** ⋯ → *Jazyk* (Automaticky / English / Čeština), okno se hned přestaví. Nebo `PhoneDrop.exe --lang auto|en|cs`.
  Texty jsou v `windows/PhoneDrop/Strings.cs` – další jazyk = další parametr v `Pick` a řádek v `Languages`.
- **Android:** podle jazyka telefonu, případně Nastavení → Aplikace → PhoneDrop → Jazyk (Android 13+).
  Texty v `res/values/strings.xml` (angličtina) a `res/values-cs/strings.xml`; další jazyk = další složka `values-xx`
  a řádek v `res/xml/locales_config.xml`.
- Chyby z telefonu chodí do počítače jako kódy (`no_space`, `bad_pin`, …) a překládá je až Windows – hláška je
  v jazyce počítače, i když má telefon jiný.

## Příkazová řádka

```
PhoneDrop.exe --discover               telefony v síti (mDNS)
PhoneDrop.exe --pair 192.168.1.20:8765 123456
PhoneDrop.exe --send app.apk foto.jpg  pošle soubory (APK počká na instalaci)
PhoneDrop.exe --status                 ověří spojení
PhoneDrop.exe --sendto on|off          položka „Odeslat do“
```

## Test na emulátoru

Emulátor je za NAT, mDNS z Windows nevidí – použij přesměrování portu:

```bash
adb forward tcp:8765 tcp:8765
PhoneDrop.exe --pair 127.0.0.1:8765 <PIN z emulátoru>
PhoneDrop.exe --send app-debug.apk
```

## Omezení

- Jen LAN; spojení je nešifrované HTTP s tokenem (viz PROTOCOL.md). Relay mimo domácí síť je připravený v rozhraní, ne hotový.
- Složky se neposílají (jen soubory). Split APK (`.apks`, `.xapk`) zatím ne.
