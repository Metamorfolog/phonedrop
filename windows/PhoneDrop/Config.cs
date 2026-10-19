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
    public bool TopMost { get; set; }
    /// <summary>"auto" (podle Windows) nebo kód jazyka – viz <see cref="L.Languages"/>.</summary>
    public string Language { get; set; } = "auto";
    /// <summary>Vzhled: "auto" (podle Windows), "light" nebo "dark".</summary>
    public string Appearance { get; set; } = "auto";

    public bool IsPaired => !string.IsNullOrEmpty(Token) && !string.IsNullOrEmpty(Address);

    public static string Dir => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PhoneDrop");
    static string FilePath => Path.Combine(Dir, "config.json");
    static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    public static Config Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<Config>(File.ReadAllText(FilePath)) ?? new Config();
        }
        catch (Exception) { }
        return new Config();
    }

    public void Save()
    {
        Directory.CreateDirectory(Dir);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(this, Json));
    }
}
