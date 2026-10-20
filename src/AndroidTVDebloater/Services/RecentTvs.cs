using System.Text.Json;

namespace AndroidTVDebloater.Services;

public sealed record RecentTv(string Address, string Name, DateTime LastUsed);

/// <summary>TVs this PC connected to before, so they can be reached again in one click.</summary>
public static class RecentTvs
{
    private const int Max = 5;
    private static string FilePath => Path.Combine(AdbService.DataDir, "recent.json");

    public static List<RecentTv> Load()
    {
        try
        {
            return File.Exists(FilePath)
                ? JsonSerializer.Deserialize<List<RecentTv>>(File.ReadAllText(FilePath)) ?? []
                : [];
        }
        catch { return []; }
    }

    public static void Remember(string address, string name)
    {
        try
        {
            var list = Load().Where(t => t.Address != address).ToList();
            list.Insert(0, new RecentTv(address, name, DateTime.Now));
            Directory.CreateDirectory(AdbService.DataDir);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(list.Take(Max)));
        }
        catch { /* only a convenience */ }
    }
}
