using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;

namespace PhoneDrop;

/// <summary>Nová verze z GitHub Releases.</summary>
public sealed record UpdateInfo(Version Version, string Tag, string ExeName, string ExeUrl, string? SumsUrl);

/// <summary>
/// Aktualizace aplikace pro Windows: zjistí poslední vydání na GitHubu, stáhne exe, ověří SHA-256 podle
/// SHA256SUMS.txt z téhož vydání a vymění běžící exe (běžící soubor jde přejmenovat, ne přepsat).
/// Adresu jde pro testy přesměrovat proměnnou PHONEDROP_UPDATE_URL.
/// </summary>
public static class Updater
{
    const string DefaultUrl = "https://api.github.com/repos/Metamorfolog/phonedrop/releases/latest";
    static readonly HttpClient Http = new() { Timeout = TimeSpan.FromMinutes(10) };

    static Updater()
    {
        Http.DefaultRequestHeaders.UserAgent.ParseAdd("PhoneDrop/" + Current.ToString(3));
    }

    static string Url => Environment.GetEnvironmentVariable("PHONEDROP_UPDATE_URL") is { Length: > 0 } u ? u : DefaultUrl;

    /// <summary>Plné vydání s .NET uvnitř (jinak malé, které potřebuje nainstalovaný .NET).</summary>
#if SELF_CONTAINED
    public const bool IsSelfContained = true;
#else
    public const bool IsSelfContained = false;
#endif

    public static Version Current
    {
        get
        {
            var v = typeof(Updater).Assembly.GetName().Version ?? new Version(0, 0, 0);
            return new Version(v.Major, v.Minor, Math.Max(0, v.Build));
        }
    }

    /// <summary>Vrátí novější vydání, nebo null, když už je tohle nejnovější. Výjimka = nejde ověřit.</summary>
    public static async Task<UpdateInfo?> CheckAsync(CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, Url);
        req.Headers.Accept.ParseAdd("application/vnd.github+json");
        using var res = await Http.SendAsync(req, ct);
        // 404 = žádné vydání nebo soukromé repo; „nejnovější verze“ by tu byla lež, takže chyba.
        if (!res.IsSuccessStatusCode) throw new HttpRequestException($"HTTP {(int)res.StatusCode}");
        using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync(ct));
        var root = doc.RootElement;
        var tag = root.GetProperty("tag_name").GetString() ?? "";
        if (!Version.TryParse(tag.TrimStart('v', 'V'), out var version)) return null;
        version = new Version(version.Major, version.Minor, Math.Max(0, version.Build));
        if (version <= Current) return null;

        // Vydání má dvě exe: plné (…-win-x64.exe, .NET uvnitř) a malé (…-small.exe, potřebuje .NET).
        // Aktualizuje se na stejnou variantu, jakou uživatel má; když chybí, vezme se jakékoli exe.
        string? exeName = null, exeUrl = null, sumsUrl = null;
        foreach (var a in root.GetProperty("assets").EnumerateArray())
        {
            var name = a.GetProperty("name").GetString() ?? "";
            var url = a.GetProperty("browser_download_url").GetString() ?? "";
            if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            {
                bool small = name.EndsWith("-small.exe", StringComparison.OrdinalIgnoreCase);
                if (exeUrl == null || small == !IsSelfContained) (exeName, exeUrl) = (name, url);
            }
            else if (name.Equals("SHA256SUMS.txt", StringComparison.OrdinalIgnoreCase)) sumsUrl = url;
        }
        return exeUrl == null ? null : new UpdateInfo(version, tag, exeName!, exeUrl, sumsUrl);
    }

    /// <summary>Stáhne nové exe do dočasné složky a ověří kontrolní součet. Vrací cestu ke staženému souboru.</summary>
    public static async Task<string> DownloadAsync(UpdateInfo update, IProgress<double>? progress, CancellationToken ct)
    {
        var dir = Path.Combine(Path.GetTempPath(), "PhoneDrop-update");
        Directory.CreateDirectory(dir);
        var file = Path.Combine(dir, update.ExeName);

        using (var res = await Http.GetAsync(update.ExeUrl, HttpCompletionOption.ResponseHeadersRead, ct))
        {
            res.EnsureSuccessStatusCode();
            long total = res.Content.Headers.ContentLength ?? 0;
            await using var src = await res.Content.ReadAsStreamAsync(ct);
            await using var dst = File.Create(file);
            var buf = new byte[1 << 16];
            long done = 0;
            int n;
            while ((n = await src.ReadAsync(buf, ct)) > 0)
            {
                await dst.WriteAsync(buf.AsMemory(0, n), ct);
                done += n;
                if (total > 0) progress?.Report((double)done / total);
            }
        }

        if (update.SumsUrl == null) throw new PhoneException(L.UpdateNoChecksum);
        var sums = await Http.GetStringAsync(update.SumsUrl, ct);
        var expected = sums.Split('\n')
            .Select(l => l.Trim().Split(' ', 2, StringSplitOptions.RemoveEmptyEntries))
            .Where(p => p.Length == 2 && p[1].TrimStart('*', ' ') == update.ExeName)
            .Select(p => p[0])
            .FirstOrDefault();
        await using (var fs = File.OpenRead(file))
        {
            var actual = Convert.ToHexString(await SHA256.HashDataAsync(fs, ct));
            if (expected == null || !actual.Equals(expected, StringComparison.OrdinalIgnoreCase))
            {
                fs.Close();
                File.Delete(file);
                throw new PhoneException(L.UpdateBadChecksum);
            }
        }
        return file;
    }

    /// <summary>Vymění běžící exe za nové (staré zůstane jako .old a smaže se při dalším startu).</summary>
    public static void Install(string newExe)
    {
        var current = Environment.ProcessPath ?? throw new PhoneException(L.UpdateFailed("ProcessPath"));
        var old = current + ".old";
        if (File.Exists(old)) File.Delete(old);
        File.Move(current, old);
        try
        {
            File.Copy(newExe, current);
        }
        catch
        {
            File.Move(old, current); // vrátit původní, ať appka zůstane funkční
            throw;
        }
    }

    /// <summary>Spustí novou verzi; ta počká, až tahle skončí, a teprve pak převezme jedinou instanci.</summary>
    public static void Restart()
    {
        Process.Start(new ProcessStartInfo(Environment.ProcessPath!, $"--after-update {Environment.ProcessId}") { UseShellExecute = false });
    }

    /// <summary>Úklid po aktualizaci (staré exe).</summary>
    public static void CleanUp()
    {
        try
        {
            var old = Environment.ProcessPath + ".old";
            if (File.Exists(old)) File.Delete(old);
        }
        catch (Exception) { /* staré exe ještě drží končící proces – smaže se příště */ }
    }
}
