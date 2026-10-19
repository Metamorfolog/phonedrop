using System.Runtime.InteropServices;

namespace PhoneDrop;

/// <summary>
/// Soubory z telefonu (Sdílet › PhoneDrop). Telefon je server, takže se počítač pořád ptá dlouhým dotazem
/// (<c>GET /outbox?wait=25</c>) – nic se neotevírá ve firewallu. Co telefon nabídne, stáhne do Stažených
/// (nebo vybrané složky) a potvrdí, aby to telefon smazal z fronty.
/// </summary>
public sealed class Receiver(Config config)
{
    public event Action<OutboxItem>? Started;
    public event Action<OutboxItem, long>? Progress;
    public event Action<OutboxItem, string>? Finished;
    public event Action<OutboxItem, string>? Failed;
    /// <summary>Telefon odpověděl (true) / neodpovídá (false) – zdarma k hlídání spojení.</summary>
    public event Action<bool>? Reachable;

    public bool Receiving { get; private set; }

    readonly Dictionary<string, int> failures = [];
    /// <summary>Uložené, ale telefon to ještě nepotvrdil (výpadek po stažení) – podruhé se nestahuje.</summary>
    readonly HashSet<string> saved = [];

    /// <summary>Běží, dokud se aplikace nezavře. Volat z UI vlákna: události chodí tam.</summary>
    public async Task RunAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            if (!config.IsPaired) { await Delay(5, ct); continue; }
            var link = new LanLink(config.Address!, config.Token!);
            try
            {
                var items = await link.OutboxAsync(25, ct);
                Reachable?.Invoke(true);
                foreach (var item in items)
                {
                    if (failures.GetValueOrDefault(item.Id) >= 3) continue;
                    await ReceiveAsync(link, item, ct);
                }
                if (items.Count > 0 && items.All(i => failures.GetValueOrDefault(i.Id) >= 3)) await Delay(30, ct);
            }
            catch (NotSupportedException)
            {
                // Telefon má starou verzi PhoneDropu bez posílání do počítače.
                Reachable?.Invoke(true);
                await Delay(600, ct);
            }
            catch (PhoneException)
            {
                await Delay(60, ct); // zrušené párování – řeší hlídání telefonu v okně
            }
            catch (Exception e) when (Sender.IsUnreachable(e, ct))
            {
                Reachable?.Invoke(false);
                await Delay(5, ct);
            }
            catch (OperationCanceledException) { return; }
        }
    }

    async Task ReceiveAsync(LanLink link, OutboxItem item, CancellationToken ct)
    {
        if (!saved.Contains(item.Id))
        {
            Receiving = true;
            Started?.Invoke(item);
            var folder = config.ReceiveFolder;
            var part = Path.Combine(folder, $".{item.Id}.phonedrop-part");
            try
            {
                Directory.CreateDirectory(folder);
                if (File.Exists(part)) File.Delete(part);
                long last = 0;
                var progress = new Progress<long>(done =>
                {
                    if (done - last < 256 * 1024 && done != item.Size) return;
                    last = done;
                    Progress?.Invoke(item, done);
                });
                await link.DownloadAsync(item, part, progress, ct);
                var target = UniquePath(folder, item.Name);
                File.Move(part, target);
                saved.Add(item.Id);
                Receiving = false;
                Finished?.Invoke(item, target);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or PhoneException or HttpRequestException
                                          || (e is OperationCanceledException && !ct.IsCancellationRequested))
            {
                Receiving = false;
                try { File.Delete(part); } catch (Exception) { }
                failures[item.Id] = failures.GetValueOrDefault(item.Id) + 1;
                Failed?.Invoke(item, e is OperationCanceledException ? L.ReceiveIncomplete : e.Message);
                if (e is HttpRequestException or OperationCanceledException) throw new HttpRequestException(e.Message, e);
                return;
            }
        }
        await link.DoneAsync(item, ct);
        saved.Remove(item.Id);
    }

    static Task Delay(int seconds, CancellationToken ct) =>
        Task.Delay(TimeSpan.FromSeconds(seconds), ct).ContinueWith(_ => { }, TaskScheduler.Default);

    /// <summary>Bezpečný název (bez cesty a zakázaných znaků) a volné jméno: „foto (1).jpg“.</summary>
    public static string UniquePath(string folder, string name)
    {
        var clean = new string(Path.GetFileName(name.Replace('/', '\\').Split('\\').Last())
            .Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c).ToArray()).Trim().TrimEnd('.');
        if (clean.Length == 0) clean = "file";
        var stem = Path.GetFileNameWithoutExtension(clean);
        var ext = Path.GetExtension(clean);
        string[] reserved = ["CON", "PRN", "AUX", "NUL", "COM1", "COM2", "COM3", "COM4", "LPT1", "LPT2", "LPT3"];
        if (reserved.Contains(stem.ToUpperInvariant())) stem = "_" + stem;
        if (stem.Length > 180) stem = stem[..180];
        var path = Path.Combine(folder, stem + ext);
        for (int i = 1; File.Exists(path) || Directory.Exists(path); i++)
            path = Path.Combine(folder, $"{stem} ({i}){ext}");
        return path;
    }

    /// <summary>Složka Stažené (i když ji uživatel přesunul jinam).</summary>
    public static string Downloads
    {
        get
        {
            var id = new Guid("374DE290-123F-4565-9164-39C4925E467B");
            if (SHGetKnownFolderPath(ref id, 0, IntPtr.Zero, out var path) == 0 && !string.IsNullOrEmpty(path)) return path;
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
        }
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    static extern int SHGetKnownFolderPath(ref Guid id, uint flags, IntPtr token, out string path);
}
