using System.Text.Json;

namespace PhoneDrop;

/// <summary>Spárovaný telefon – uložený v %APPDATA%\PhoneDrop\config.json.</summary>
public sealed class Config
{
    public string? DeviceId { get; set; }
    public string? DeviceName { get; set; }
    /// <summary>Poslední známá adresa v LAN (host:port). Po změně IP se telefon dohledá přes mDNS podle DeviceId.</summary>
    public string? Address { get; set; }
    public string? Token { get; set; }
    /// <summary>Sdílený klíč z párování QR kódem (base64url) – pro šifrovaný přenos přes relay v dalších verzích.</summary>
    public string? Key { get; set; }
    public bool TopMost { get; set; }
    /// <summary>"auto" (podle Windows) nebo kód jazyka – viz <see cref="L.Languages"/>.</summary>
    public string Language { get; set; } = "auto";
    /// <summary>Vzhled: "auto" (podle Windows), "light" nebo "dark".</summary>
    public string Appearance { get; set; } = "auto";
    /// <summary>Kam se ukládají soubory z telefonu; prázdné = složka Stažené.</summary>
    public string? ReceiveDir { get; set; }

    /// <summary>Složka pro soubory z telefonu (vlastní, jinak Stažené).</summary>
    public string ReceiveFolder => !string.IsNullOrEmpty(ReceiveDir) && Directory.Exists(ReceiveDir) ? ReceiveDir : Receiver.Downloads;

    public bool IsPaired => !string.IsNullOrEmpty(Token) && !string.IsNullOrEmpty(Address);

    public static string Dir => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PhoneDrop");
    static string FilePath => Path.Combine(Dir, "config.json");

    public static Config Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize(File.ReadAllText(FilePath), ConfigJson.Default.Config) ?? new Config();
        }
        catch (Exception) { }
        return new Config();
    }

    public void Save()
    {
        Directory.CreateDirectory(Dir);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(this, ConfigJson.Default.Config));
    }
}

/// <summary>Serializace bez reflexe – funguje i v ořezaném self-contained exe.</summary>
[System.Text.Json.Serialization.JsonSourceGenerationOptions(WriteIndented = true)]
[System.Text.Json.Serialization.JsonSerializable(typeof(Config))]
internal sealed partial class ConfigJson : System.Text.Json.Serialization.JsonSerializerContext;
