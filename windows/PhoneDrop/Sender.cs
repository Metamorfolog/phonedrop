namespace PhoneDrop;

/// <summary>Výsledek jednoho souboru pro uživatele.</summary>
public sealed record Outcome(bool Ok, string Title, string Detail)
{
    public override string ToString() => $"{(Ok ? "✓" : "✗")} {Title}: {Detail}";
}

/// <summary>Odeslání souborů na spárovaný telefon – sdílené oknem i příkazovou řádkou.</summary>
public sealed class Sender(Config config)
{
    /// <summary>Probíhající krok: nadpis (obvykle název souboru), detail a průběh 0–1 (null = neurčitý).</summary>
    public event Action<string, string, double?>? Status;

    void Report(string title, string detail, double? progress = null) => Status?.Invoke(title, detail, progress);

    /// <summary>Spojení s telefonem; když na uložené adrese neodpovídá, dohledá ho přes mDNS podle ID.</summary>
    public async Task<IPhoneLink> ConnectAsync(CancellationToken ct)
    {
        if (!config.IsPaired) throw new PhoneException("Telefon není spárovaný.");
        var link = new LanLink(config.Address!, config.Token!);
        try
        {
            await link.PingAsync(ct);
            return link;
        }
        catch (Exception e) when (IsUnreachable(e, ct)) { }

        Report(config.DeviceName ?? "Telefon", "Hledám v síti…");
        var phones = await Mdns.DiscoverAsync(ct: ct);
        var match = phones.FirstOrDefault(p => p.Id == config.DeviceId);
        if (match != null && match.Address != config.Address)
        {
            config.Address = match.Address;
            config.Save();
            link = new LanLink(match.Address, config.Token!);
            await link.PingAsync(ct);
            return link;
        }
        throw new PhoneException($"Telefon nenalezen ({config.Address}). Běží v něm PhoneDrop a jste ve stejné síti?");
    }

    public static bool IsUnreachable(Exception e, CancellationToken ct) =>
        e is HttpRequestException || (e is OperationCanceledException && !ct.IsCancellationRequested);

    /// <summary>Pošle soubor; u APK počká, až ho uživatel v telefonu potvrdí.</summary>
    public async Task<Outcome> SendFileAsync(IPhoneLink link, string file, CancellationToken ct)
    {
        var name = Path.GetFileName(file);
        long total = Math.Max(1, new FileInfo(file).Length);
        var started = DateTime.UtcNow;
        var progress = new Progress<long>(done =>
        {
            var secs = (DateTime.UtcNow - started).TotalSeconds;
            var speed = secs > 0.5 ? $"  ·  {done / secs / 1048576:0.0} MB/s" : "";
            Report(name, $"{Size(done)} z {Size(total)}{speed}", (double)done / total);
        });
        Report(name, "Připojuji…", 0);
        var result = await link.SendAsync(file, progress, ct);

        if (result.Kind != "apk") return new Outcome(true, name, "Uloženo do " + result.Path);

        Report(name, result.CanInstall
            ? "Potvrď instalaci v telefonu…"
            : "V telefonu povol PhoneDropu instalaci aplikací a potvrď…");
        var deadline = DateTime.UtcNow.AddMinutes(5);
        while (DateTime.UtcNow < deadline)
        {
            await Task.Delay(1000, ct);
            string state;
            try { state = await link.InstallStateAsync(result.Session, ct); }
            catch (Exception e) when (IsUnreachable(e, ct)) { continue; } // telefon může instalací chvíli „zamrznout“
            switch (state)
            {
                case "installed": return new Outcome(true, name, "Nainstalováno");
                case "aborted": return new Outcome(false, name, "Instalace zrušena v telefonu");
                case "unknown": return new Outcome(true, name, "Odesláno, výsledek instalace neznámý");
            }
            if (state.StartsWith("failed")) return new Outcome(false, name, "Instalace selhala: " + state[6..].TrimStart(':', ' '));
        }
        return new Outcome(true, name, "Odesláno, instalace čeká na potvrzení v telefonu");
    }

    public static string Size(long bytes) => bytes switch
    {
        < 1024 => $"{bytes} B",
        < 1048576 => $"{bytes / 1024.0:0.#} kB",
        < 1073741824 => $"{bytes / 1048576.0:0.#} MB",
        _ => $"{bytes / 1073741824.0:0.##} GB",
    };
}
