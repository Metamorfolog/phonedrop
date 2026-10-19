using System.Security.Cryptography;
using System.Text;

namespace PhoneDrop;

/// <summary>
/// Párování QR kódem. Kód je odkaz <c>https://phonedrop.apexpiksel.cz/pair#1.&lt;c&gt;.&lt;k&gt;</c>:
/// <c>c</c> je jednorázové ID žádosti, <c>k</c> tajný klíč sdílený jen přes obrazovku (po síti nikdy nejde).
/// Telefon po naskenování 2 minuty čeká; počítač obchází telefony v síti a žádost „vyzvedne“ s důkazem
/// HMAC(k, c). Klíč si obě strany nechají – poslouží k šifrování (relay) v dalších verzích.
/// </summary>
public sealed class QrPairing
{
    public const string BaseUrl = "https://phonedrop.apexpiksel.cz/pair#";
    static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(2);

    readonly byte[] claim = RandomNumberGenerator.GetBytes(16);
    readonly byte[] key = RandomNumberGenerator.GetBytes(32);
    readonly DateTime created = DateTime.UtcNow;

    public string Url { get; }
    public bool[,] Matrix { get; }
    public string Key => B64(key);
    public bool Expired => DateTime.UtcNow - created > Lifetime - TimeSpan.FromSeconds(5);

    public QrPairing()
    {
        Url = BaseUrl + "1." + B64(claim) + "." + B64(key);
        Matrix = QrCode.Encode(Url);
    }

    string ClaimId => B64(claim);
    string Proof => Convert.ToHexStringLower(HMACSHA256.HashData(key, Encoding.UTF8.GetBytes("phonedrop-pair:" + ClaimId)));

    /// <summary>Zkusí žádost vyzvednout na daných adresách; vrátí spárovaný telefon, nebo null (zatím nikdo nenaskenoval).</summary>
    public async Task<PairedPhone?> TryClaimAsync(IEnumerable<string> addresses, CancellationToken ct)
    {
        foreach (var address in addresses.Distinct())
        {
            try
            {
                var r = await LanLink.ClaimAsync(address, ClaimId, Proof, ct);
                if (r != null) return r with { Address = address };
            }
            catch (Exception e) when (Sender.IsUnreachable(e, ct) || e is PhoneException) { }
        }
        return null;
    }

    static string B64(byte[] b) => Convert.ToBase64String(b).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}

public sealed record PairedPhone(string Token, string Id, string Name, string Address);
