namespace PhoneDrop;

/// <summary>Příkazová řádka – pro skripty a testy (výstup do konzole, ze které byl spuštěn).</summary>
static class Cli
{
    const string Help = """
        PhoneDrop – odeslání souborů do telefonu

          PhoneDrop.exe [soubory…]              otevře okno (a soubory rovnou pošle)
          PhoneDrop.exe --discover [služba]     vypíše telefony v síti (mDNS)
          PhoneDrop.exe --pair <ip:port> <PIN>  spáruje telefon (PIN je v appce v telefonu)
          PhoneDrop.exe --send <soubory…>       pošle soubory, APK nabídne k instalaci
          PhoneDrop.exe --status                ověří spojení se spárovaným telefonem
          PhoneDrop.exe --sendto on|off         přidá/odebere „Odeslat do › Telefon (PhoneDrop)“
        """;

    public static int Run(string[] args)
    {
        if (!Console.IsOutputRedirected) Program.AttachConsole(-1);
        Console.OutputEncoding = System.Text.Encoding.UTF8;
        Console.WriteLine();
        try
        {
            return RunAsync(args).GetAwaiter().GetResult();
        }
        catch (Exception e)
        {
            Console.Error.WriteLine("Chyba: " + e.Message);
            return 1;
        }
    }

    static async Task<int> RunAsync(string[] args)
    {
        var config = Config.Load();
        var ct = CancellationToken.None;
        switch (args[0])
        {
            case "--discover":
            {
                var service = args.Length > 1 ? args[1] : Mdns.PhoneDropService;
                var found = await Mdns.DiscoverAsync(service, 3000, ct);
                foreach (var p in found) Console.WriteLine($"{p.Instance}\t{p.Address}\t{p.Id}");
                if (found.Count == 0) Console.WriteLine("Nic nenalezeno.");
                return found.Count > 0 ? 0 : 2;
            }
            case "--pair" when args.Length >= 3:
            {
                var (token, id, name) = await LanLink.PairAsync(args[1], args[2], ct);
                config.Address = args[1];
                config.Token = token;
                config.DeviceId = id;
                config.DeviceName = name;
                config.Save();
                Console.WriteLine($"Spárováno s {name} ({args[1]}).");
                return 0;
            }
            case "--status":
            {
                var link = await new Sender(config).ConnectAsync(ct);
                Console.WriteLine($"OK: {config.DeviceName} na {link.Describe}");
                return 0;
            }
            case "--send" when args.Length >= 2:
            {
                var sender = new Sender(config);
                string last = "";
                sender.Status += (title, detail, _) =>
                {
                    var text = $"{title}: {detail}";
                    if (text == last) return;
                    last = text;
                    if (!Console.IsOutputRedirected) Console.Write("\r" + text.PadRight(70));
                };
                var link = await sender.ConnectAsync(ct);
                int failed = 0;
                foreach (var file in args.Skip(1))
                {
                    try
                    {
                        var result = await sender.SendFileAsync(link, Path.GetFullPath(file), ct);
                        Console.WriteLine((Console.IsOutputRedirected ? "" : "\r") + result.ToString().PadRight(70));
                        if (!result.Ok) failed++;
                    }
                    catch (Exception e) when (e is PhoneException or IOException or HttpRequestException)
                    {
                        failed++;
                        Console.WriteLine($"\r{Path.GetFileName(file)}: {e.Message}".PadRight(70));
                    }
                }
                return failed == 0 ? 0 : 1;
            }
            case "--sendto" when args.Length >= 2:
                if (args[1] == "off") SendTo.Uninstall(); else SendTo.Install();
                Console.WriteLine(SendTo.Installed ? "Odeslat do › Telefon (PhoneDrop): přidáno" : "Odebráno.");
                return 0;
            default:
                Console.WriteLine(Help);
                return args[0] is "--help" or "-h" ? 0 : 1;
        }
    }
}
