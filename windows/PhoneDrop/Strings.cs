using System.Globalization;

namespace PhoneDrop;

/// <summary>
/// Texty v angličtině a češtině. Jazyk: volba v menu (config "Language"), jinak podle jazyka Windows.
/// Další jazyk = další parametr v <see cref="Pick"/> a řádek v <see cref="Languages"/>.
/// </summary>
static class L
{
    public static readonly (string Code, string Name)[] Languages = [("en", "English"), ("cs", "Čeština")];

    static string lang = "en";
    public static string Current => lang;

    /// <summary>"auto" nebo kód jazyka z <see cref="Languages"/>.</summary>
    public static void Init(string? setting)
    {
        var code = string.IsNullOrEmpty(setting) || setting == "auto"
            ? CultureInfo.CurrentUICulture.TwoLetterISOLanguageName
            : setting;
        lang = Languages.Any(l => l.Code == code) ? code : code == "sk" ? "cs" : "en";
    }

    static string Pick(string en, string cs) => lang == "cs" ? cs : en;

    // Okno
    public static string Ready => Pick("Ready", "Připraveno");
    public static string ReadyDetail => Pick("Files → Downloads › Drop, APKs → installer", "Soubory jdou do Stažené › Drop, APK k instalaci");
    public static string NotPairedTitle => Pick("No phone paired", "Telefon není spárovaný");
    public static string NotPairedHint => Pick("⋯ → Pair phone", "⋯ → Spárovat telefon");
    public static string NoPhone => Pick("No phone", "Žádný telefon");
    public static string Unpaired => Pick("Not paired", "Nespárováno");
    public static string Phone => Pick("Phone", "Telefon");
    public static string PhoneOfflineTitle => Pick("The phone isn't receiving", "Telefon teď nepřijímá");
    public static string PhoneOfflineHint => Pick("Open PhoneDrop on it and turn on Receive files", "Otevři v něm PhoneDrop a zapni Přijímat soubory");
    public static string PhoneBack => Pick("The phone is back", "Telefon je zase připojený");
    public static string TrayOffline(string name) => Pick($"PhoneDrop · {name} unreachable", $"PhoneDrop · {name} nedostupný");
    public static string Connecting => Pick("Connecting…", "Připojuji…");
    public static string Unreachable(string addr) => Pick("Unreachable · ", "Nedostupný · ") + addr;
    public static string NotResponding(string msg) => Pick("Not responding: ", "Neodpovídá: ") + msg;
    public static string SentSummary(int ok, int all) => Pick($"Sent {ok} of {all}", $"Odesláno {ok} z {all}");
    public static string AllGood => Pick("all good", "vše v pořádku");
    public static string PairedWith(string name) => Pick($"Paired with {name}", $"Spárováno s {name}");
    public static string DropHere => Pick("Drop a file here", "Přetáhni sem soubor");
    public static string Failed => Pick("That didn't work", "Nepodařilo se");
    public static string StillRunning => Pick("PhoneDrop keeps running", "PhoneDrop běží dál");
    public static string StillRunningText => Pick("Click the icon to open the window. Quit via right-click.", "Okno otevřeš kliknutím na ikonu. Ukončení přes pravé tlačítko.");

    // Drop zóna
    public static string DropTitle => Pick("Drop a file or APK here", "Přetáhni sem soubor nebo APK");
    public static string DropHot => Pick("Let go – off to the phone", "Pusť a letí do telefonu");
    public static string DropClick => Pick("or click to choose", "nebo klikni a vyber");
    public static string DropApk => Pick("APKs open the installer right away", "APK se rovnou nabídne k instalaci");
    public static string PickTitle => Pick("Send to phone", "Poslat do telefonu");

    // Menu
    public static string MenuPair => Pick("Pair phone…", "Spárovat telefon…");
    public static string MenuSendTo => Pick("Explorer: Send to › Phone", "V Průzkumníku: Odeslat do › Telefon");
    public static string MenuTopMost => Pick("Always on top", "Vždy navrchu");
    public static string MenuLanguage => Pick("Language", "Jazyk");
    public static string MenuAuto => Pick("Automatic (Windows)", "Automaticky (podle Windows)");
    public static string MenuTheme => Pick("Appearance", "Vzhled");
    public static string ThemeAuto => Pick("Automatic (Windows)", "Automaticky (podle Windows)");
    public static string ThemeLight => Pick("Light", "Světlý");
    public static string ThemeDark => Pick("Dark", "Tmavý");
    public static string MenuQuit => Pick("Quit", "Ukončit");
    public static string SendToAdded => Pick("Added to Send to", "Přidáno do Odeslat do");
    public static string SendToAddedHint => Pick("Right-click a file → Send to › Phone", "Pravé tlačítko na soubor → Odeslat do › Telefon");
    public static string SendToRemoved => Pick("Removed from Send to", "Odebráno z Odeslat do");
    public static string SendToRemovedHint => Pick("The Explorer entry is gone", "Položka v Průzkumníku zmizela");
    public static string SendToLinkName => Pick("Phone (PhoneDrop)", "Telefon (PhoneDrop)");
    public static string SendToDescription => Pick("Send to the phone with PhoneDrop", "Odeslat do telefonu přes PhoneDrop");

    // Párování
    public static string PairTitle => Pick("Pair phone", "Spárovat telefon");
    public static string PairIntro => Pick("Open PhoneDrop on the phone, pick it from the list here and type the PIN it shows.",
        "V telefonu otevři PhoneDrop, vyber ho tady ze seznamu a opiš PIN, který ukazuje.");
    public static string PairFound => Pick("PHONES ON THIS NETWORK", "TELEFONY V SÍTI");
    public static string PairSearchAgain => Pick("Search again", "Hledat znovu");
    public static string PairSearching => Pick("Searching…", "Hledám…");
    public static string PairSearchingNetwork => Pick("Looking for phones on the network…", "Hledám telefony v síti…");
    public static string PairNoneInList => Pick("No phone on the network\nIs PhoneDrop running on it?", "Žádný telefon v síti\nBěží v něm PhoneDrop?");
    public static string PairFoundOne => Pick("Found one phone – enter the PIN.", "Našel jsem jeden telefon – zadej PIN.");
    public static string PairFoundMany(int n) => Pick($"Phones found: {n}", $"Nalezeno telefonů: {n}");
    public static string PairNothing => Pick("Nothing found – type the address the phone app shows.", "Nic nenalezeno – zadej adresu, kterou ukazuje appka v telefonu.");
    public static string PairSearchFailed(string msg) => Pick("Search failed: ", "Hledání selhalo: ") + msg;
    public static string PairAddress => Pick("ADDRESS", "ADRESA");
    public static string PairNeedAddress => Pick("Enter the phone's address.", "Zadej adresu telefonu.");
    public static string PairPinDigits => Pick("The PIN has 6 digits.", "PIN má 6 číslic.");
    public static string Pairing => Pick("Pairing…", "Páruji…");
    public static string PairButton => Pick("Pair", "Spárovat");
    public static string Cancel => Pick("Cancel", "Zrušit");
    public static string PhoneNotResponding(string msg) => Pick("Phone not responding: ", "Telefon neodpovídá: ") + msg;

    // Přenos
    public static string NotPaired => Pick("No phone is paired.", "Telefon není spárovaný.");
    public static string SearchingNetwork => Pick("Searching the network…", "Hledám v síti…");
    public static string PhoneNotFound(string addr) => Pick("Not found on the network. Is receiving turned on?",
        "Nenalezen v síti. Je v něm zapnutý příjem?");
    public static string Progress(string done, string total, string speed) => Pick($"{done} of {total}{speed}", $"{done} z {total}{speed}");
    public static string SavedTo(string path) => Pick("Saved to ", "Uloženo do ") + path;
    public static string ConfirmInstall => Pick("Confirm the install on the phone…", "Potvrď instalaci v telefonu…");
    public static string AllowInstall => Pick("On the phone, allow PhoneDrop to install apps and confirm…", "V telefonu povol PhoneDropu instalaci aplikací a potvrď…");
    public static string Installed => Pick("Installed", "Nainstalováno");
    public static string InstallAborted => Pick("Install cancelled on the phone", "Instalace zrušena v telefonu");
    public static string InstallUnknown => Pick("Sent, install result unknown", "Odesláno, výsledek instalace neznámý");
    public static string InstallFailed(string why) => Pick("Install failed: ", "Instalace selhala: ") + why;
    public static string InstallPending => Pick("Sent, the install is waiting for confirmation on the phone", "Odesláno, instalace čeká na potvrzení v telefonu");

    // Aktualizace
    public static string MenuCheckUpdates => Pick("Check for updates", "Zkontrolovat aktualizace");
    public static string MenuUpdateTo(string v) => Pick($"Update to {v}", $"Aktualizovat na {v}");
    public static string UpdateAvailable(string v) => Pick($"Version {v} is available", $"Je dostupná verze {v}");
    public static string UpdateAvailableHint => Pick("⋯ → Update, or click the notification", "⋯ → Aktualizovat, nebo klikni na oznámení");
    public static string UpdateBalloon => Pick("Click to update. PhoneDrop restarts on its own.", "Klikni pro aktualizaci. PhoneDrop se sám restartuje.");
    public static string UpToDate(string v) => Pick($"You have the latest version ({v})", $"Máš nejnovější verzi ({v})");
    public static string UpdateCheckFailed => Pick("Couldn't check for updates", "Aktualizace se nepodařilo ověřit");
    public static string Updating => Pick("Downloading the update…", "Stahuji aktualizaci…");
    public static string UpdatedTo(string v) => Pick($"Updated to {v}", $"Aktualizováno na {v}");
    public static string UpdateNoChecksum => Pick("The release has no SHA256SUMS.txt, update skipped.", "Vydání nemá SHA256SUMS.txt, aktualizace přeskočena.");
    public static string UpdateBadChecksum => Pick("The download doesn't match its checksum, update skipped.", "Stažený soubor nesedí s kontrolním součtem, aktualizace přeskočena.");
    public static string UpdateFailed(string why) => Pick("Update failed: ", "Aktualizace selhala: ") + why;

    // Zpětná vazba
    // Z telefonu do počítače
    public static string ReceivingFrom(string phone) => Pick($"From {phone}", $"Z telefonu {phone}");
    public static string ReceivedTitle => Pick("Received from the phone", "Přijato z telefonu");
    public static string ReceivedIn(string name, string folder) => Pick($"{name} → {folder}", $"{name} → {folder}");
    public static string ReceivedBalloon(string name) => Pick($"{name} – click to show it", $"{name} – kliknutím ho ukážeš");
    public static string ReceiveFailed(string name) => Pick($"Couldn't receive {name}", $"Nepodařilo se přijmout {name}");
    public static string ReceiveIncomplete => Pick("The transfer was interrupted", "Přenos se přerušil");
    public static string MenuOpenReceived => Pick("Open files from the phone", "Otevřít soubory z telefonu");
    public static string MenuReceiveFolder => Pick("Folder for files from the phone…", "Složka pro soubory z telefonu…");
    public static string ReceiveFolderPick => Pick("Where should files from the phone go?", "Kam ukládat soubory z telefonu?");
    public static string ReceiveFolderSet => Pick("Files from the phone go to", "Soubory z telefonu se ukládají do");
    public static string MenuFeedback => Pick("Send feedback…", "Napsat zpětnou vazbu…");
    public static string FeedbackTitle => Pick("Send feedback", "Zpětná vazba");
    public static string FeedbackIntro => Pick("Found a bug or have an idea? It goes straight to the author.", "Našel jsi chybu nebo máš nápad? Dorazí rovnou autorovi.");
    public static string KindBug => Pick("Bug", "Chyba");
    public static string KindIdea => Pick("Idea", "Nápad");
    public static string KindQuestion => Pick("Question", "Otázka");
    public static string KindOther => Pick("Other", "Jiné");
    public static string FeedbackMessage => Pick("MESSAGE", "ZPRÁVA");
    public static string FeedbackPlaceholder => Pick("What happened, or what would you like PhoneDrop to do?", "Co se stalo, nebo co bys od PhoneDropu chtěl?");
    public static string FeedbackEmail => Pick("E-MAIL (OPTIONAL, ONLY FOR A REPLY)", "E-MAIL (NEPOVINNÝ, JEN PRO ODPOVĚĎ)");
    public static string FeedbackEmailPlaceholder => Pick("you@example.com", "ty@example.cz");
    public static string FeedbackPrivacy => Pick("Sent: your text, the app version and the Windows version. Your IP address isn't stored.",
        "Odešle se tvůj text, verze aplikace a verze Windows. IP adresa se neukládá.");
    public static string FeedbackSend => Pick("Send", "Odeslat");
    public static string FeedbackTooShort => Pick("Please write a few words first.", "Napiš prosím aspoň pár slov.");
    public static string FeedbackBadEmail => Pick("That e-mail doesn't look right.", "Ten e-mail nevypadá správně.");
    public static string FeedbackSending => Pick("Sending…", "Odesílám…");
    public static string FeedbackOffline => Pick("Sending failed. Check your internet connection.", "Odeslání se nepovedlo. Zkontroluj připojení k internetu.");
    public static string FeedbackTooMany => Pick("Too many messages in a short time. Try again in a few minutes.", "Moc zpráv za krátkou dobu. Zkus to za pár minut.");
    public static string FeedbackFailed(int code) => Pick($"Sending failed ({code}). Please try again later.", $"Odeslání se nepovedlo ({code}). Zkus to prosím později.");
    public static string FeedbackThanks => Pick("Thank you!", "Díky!");
    public static string FeedbackThanksDetail => Pick("Your message has been sent.", "Zpráva je odeslaná.");

    // Chyby od telefonu (kódy z PROTOCOL.md)
    public static string WrongPin => Pick("Wrong PIN.", "Špatný PIN.");
    public static string PairingClosed => Pick("Open PhoneDrop on the phone – pairing only works while it's open.",
        "Otevři v telefonu PhoneDrop – párovat jde, jen když je appka otevřená.");
    public static string PairingLocked(long seconds) => Pick($"Too many wrong PINs. The phone shows a new one; try again in {seconds} s.",
        $"Moc špatných PINů. Telefon ukazuje nový, zkus to znovu za {seconds} s.");
    public static string UnknownComputer => Pick("The phone doesn't know this PC – pair it again.", "Telefon tento počítač nezná – spáruj ho znovu.");
    public static string NoSpace(long need, long free) => Pick($"Not enough space on the phone: need {need} MB, {free} MB free",
        $"V telefonu není místo: potřeba {need} MB, volno {free} MB");
    public static string InvalidResponse(int code) => Pick($"Invalid response ({code}).", $"Neplatná odpověď ({code}).");
    public static string PhoneError(int code) => Pick($"Error {code}.", $"Chyba {code}.");

    // Příkazová řádka
    public static string CliHelp => Pick("""
        PhoneDrop – send files to your phone

          PhoneDrop.exe [files…]                opens the window (and sends the files)
          PhoneDrop.exe --discover [service]    lists phones on the network (mDNS)
          PhoneDrop.exe --pair <ip:port> <PIN>  pairs a phone (the PIN is in the phone app)
          PhoneDrop.exe --send <files…>         sends files, APKs open the installer
          PhoneDrop.exe --status                checks the connection to the paired phone
          PhoneDrop.exe --sendto on|off         adds/removes "Send to › Phone (PhoneDrop)"
          PhoneDrop.exe --lang auto|en|cs       sets the language
          PhoneDrop.exe --theme auto|light|dark sets the appearance
          PhoneDrop.exe --update                checks for a new version and installs it
        """, """
        PhoneDrop – odeslání souborů do telefonu

          PhoneDrop.exe [soubory…]              otevře okno (a soubory rovnou pošle)
          PhoneDrop.exe --discover [služba]     vypíše telefony v síti (mDNS)
          PhoneDrop.exe --pair <ip:port> <PIN>  spáruje telefon (PIN je v appce v telefonu)
          PhoneDrop.exe --send <soubory…>       pošle soubory, APK nabídne k instalaci
          PhoneDrop.exe --status                ověří spojení se spárovaným telefonem
          PhoneDrop.exe --sendto on|off         přidá/odebere „Odeslat do › Telefon (PhoneDrop)“
          PhoneDrop.exe --lang auto|en|cs       nastaví jazyk
          PhoneDrop.exe --theme auto|light|dark nastaví vzhled
          PhoneDrop.exe --update                zkontroluje a nainstaluje novou verzi
        """);
    public static string CliError => Pick("Error: ", "Chyba: ");
    public static string CliNothing => Pick("Nothing found.", "Nic nenalezeno.");
    public static string CliPaired(string name, string addr) => Pick($"Paired with {name} ({addr}).", $"Spárováno s {name} ({addr}).");
    public static string CliStatus(string name, string addr) => Pick($"OK: {name} at {addr}", $"OK: {name} na {addr}");
    public static string CliSendToOn => Pick("Send to › Phone (PhoneDrop): added", "Odeslat do › Telefon (PhoneDrop): přidáno");
    public static string CliSendToOff => Pick("Removed.", "Odebráno.");
    public static string CliTheme(string name) => Pick("Appearance: ", "Vzhled: ") + name;
    public static string CliLanguage(string name) => Pick("Language: ", "Jazyk: ") + name;
}
