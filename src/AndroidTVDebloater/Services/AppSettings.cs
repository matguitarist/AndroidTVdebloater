using System.Text.Json;

namespace AndroidTVDebloater.Services;

/// <summary>The few preferences kept between runs, in settings.json next to the backups.</summary>
public sealed class AppSettings
{
    public string? Language { get; set; }
    public bool CheckForUpdates { get; set; } = true;
    /// <summary>A version the user chose to skip; newer ones are still offered.</summary>
    public string? SkippedVersion { get; set; }

    public static AppSettings Current { get; } = Load();

    private static string FilePath => Path.Combine(AdbService.DataDir, "settings.json");

    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
    };

    private static AppSettings Load()
    {
        try
        {
            return File.Exists(FilePath)
                ? JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath), Json) ?? new()
                : new();
        }
        catch { return new(); }
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(AdbService.DataDir);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, Json));
        }
        catch { /* the choice just won't be remembered */ }
    }
}
