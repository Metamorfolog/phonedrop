using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;

namespace PhoneDrop;

/// <summary>Výsledek nahrání: soubor uložený v telefonu, nebo APK čekající na potvrzení instalace.</summary>
public sealed record SendResult(string Kind, string? Path, int Session, bool CanInstall);

public sealed class PhoneException(string message) : Exception(message);

/// <summary>
/// Cesta k telefonu. Dnes jen <see cref="LanLink"/> (přímé HTTP v místní síti);
/// relay přes server bude další implementace se stejným protokolem (viz PROTOCOL.md).
/// </summary>
public interface IPhoneLink
{
    string Describe { get; }
    Task PingAsync(CancellationToken ct);
    Task<SendResult> SendAsync(string file, IProgress<long> progress, CancellationToken ct);
    Task<string> InstallStateAsync(int session, CancellationToken ct);
}

public sealed class LanLink(string address, string token) : IPhoneLink
{
    static readonly HttpClient Http = new() { Timeout = Timeout.InfiniteTimeSpan };

    public string Address { get; } = address;
    public string Describe => Address;
    Uri Url(string pathAndQuery) => new($"http://{Address}{pathAndQuery}");

    /// <summary>Spáruje počítač s telefonem pomocí PINu z telefonu; vrátí token a identitu telefonu.</summary>
    public static async Task<(string Token, string Id, string Name)> PairAsync(string address, string pin, CancellationToken ct)
    {
        var url = new Uri($"http://{address}/pair?pin={Uri.EscapeDataString(pin)}&client={Uri.EscapeDataString(Environment.MachineName)}");
        using var cts = Short(ct);
        using var res = await Http.PostAsync(url, null, cts.Token);
        var json = await ReadJson(res, cts.Token);
        Check(res, json);
        return (json.GetProperty("token").GetString()!, json.GetProperty("id").GetString()!, json.GetProperty("name").GetString()!);
    }

    /// <summary>Ověří, že na adrese běží PhoneDrop; vrátí jeho ID a název (bez párování).</summary>
    public static async Task<(string Id, string Name)> InfoAsync(string address, CancellationToken ct)
    {
        using var cts = Short(ct, 3);
        using var res = await Http.GetAsync(new Uri($"http://{address}/info"), cts.Token);
        var json = await ReadJson(res, cts.Token);
        Check(res, json);
        return (json.GetProperty("id").GetString()!, json.GetProperty("name").GetString()!);
    }

    public async Task PingAsync(CancellationToken ct)
    {
        using var cts = Short(ct, 4);
        using var req = Request(HttpMethod.Get, "/ping");
        using var res = await Http.SendAsync(req, cts.Token);
        Check(res, await ReadJson(res, cts.Token));
    }

    public async Task<SendResult> SendAsync(string file, IProgress<long> progress, CancellationToken ct)
    {
        using var req = Request(HttpMethod.Put, "/upload?name=" + Uri.EscapeDataString(System.IO.Path.GetFileName(file)));
        req.Content = new ProgressContent(file, progress);
        // Telefon nejdřív zkontroluje token a volné místo; tělo se pošle až po "100 Continue".
        req.Headers.ExpectContinue = true;
        using var res = await Http.SendAsync(req, ct);
        var json = await ReadJson(res, ct);
        Check(res, json);
        return new SendResult(
            json.GetProperty("kind").GetString()!,
            json.TryGetProperty("path", out var p) ? p.GetString() : null,
            json.TryGetProperty("session", out var s) ? s.GetInt32() : -1,
            !json.TryGetProperty("canInstall", out var c) || c.GetBoolean());
    }

    public async Task<string> InstallStateAsync(int session, CancellationToken ct)
    {
        using var cts = Short(ct, 4);
        using var req = Request(HttpMethod.Get, "/install?session=" + session);
        using var res = await Http.SendAsync(req, cts.Token);
        var json = await ReadJson(res, cts.Token);
        Check(res, json);
        return json.GetProperty("state").GetString()!;
    }

    HttpRequestMessage Request(HttpMethod method, string path)
    {
        var req = new HttpRequestMessage(method, Url(path));
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return req;
    }

    static CancellationTokenSource Short(CancellationToken ct, int seconds = 10)
    {
        var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(seconds));
        return cts;
    }

    static async Task<JsonElement> ReadJson(HttpResponseMessage res, CancellationToken ct)
    {
        var text = await res.Content.ReadAsStringAsync(ct);
        try { return JsonDocument.Parse(text).RootElement; }
        catch (JsonException) { throw new PhoneException(L.InvalidResponse((int)res.StatusCode)); }
    }

    static void Check(HttpResponseMessage res, JsonElement json)
    {
        if (res.StatusCode == HttpStatusCode.Unauthorized) throw new PhoneException(L.UnknownComputer);
        if (!res.IsSuccessStatusCode || !json.TryGetProperty("ok", out var ok) || !ok.GetBoolean())
        {
            // Telefon posílá strojový kód (překládá se tady, do jazyka počítače) a anglický text jako zálohu.
            var code = json.TryGetProperty("code", out var c) ? c.GetString() : null;
            if (code == "no_space")
                throw new PhoneException(L.NoSpace(json.GetProperty("needMb").GetInt64(), json.GetProperty("freeMb").GetInt64()));
            if (code == "bad_pin") throw new PhoneException(L.WrongPin);
            if (code == "pairing_closed") throw new PhoneException(L.PairingClosed);
            if (code == "pairing_locked")
                throw new PhoneException(L.PairingLocked(json.TryGetProperty("retryAfter", out var ra) ? ra.GetInt64() : 60));
            if (code == "unpaired") throw new PhoneException(L.UnknownComputer);
            var msg = json.TryGetProperty("error", out var e) ? e.GetString() : null;
            throw new PhoneException(msg ?? L.PhoneError((int)res.StatusCode));
        }
    }

    /// <summary>Tělo PUT se známou délkou (telefon potřebuje Content-Length) a hlášením průběhu.</summary>
    sealed class ProgressContent(string file, IProgress<long> progress) : HttpContent
    {
        readonly long length = new FileInfo(file).Length;

        protected override async Task SerializeToStreamAsync(Stream stream, TransportContext? context, CancellationToken ct)
        {
            await using var fs = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 16, useAsync: true);
            var buf = new byte[1 << 16];
            long done = 0;
            int n;
            while ((n = await fs.ReadAsync(buf, ct)) > 0)
            {
                await stream.WriteAsync(buf.AsMemory(0, n), ct);
                done += n;
                progress.Report(done);
            }
        }

        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) =>
            SerializeToStreamAsync(stream, context, CancellationToken.None);

        protected override bool TryComputeLength(out long len)
        {
            len = length;
            return true;
        }
    }
}
