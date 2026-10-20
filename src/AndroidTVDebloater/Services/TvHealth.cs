using System.Globalization;
using System.Text.RegularExpressions;
using AndroidTVDebloater.Models;

namespace AndroidTVDebloater.Services;

public sealed record ProcessUse(string Name, long Mb);
public sealed record CrashLoop(string Package, int Restarts);

/// <summary>A read-only snapshot of how the TV is doing.</summary>
public sealed class HealthReport
{
    public long MemTotalMb { get; init; }
    public long MemAvailableMb { get; init; }
    public long StorageTotalMb { get; init; }
    public long StorageFreeMb { get; init; }
    public double Load { get; init; }
    public int Cores { get; init; }
    public double UptimeHours { get; init; }
    public List<ProcessUse> TopMemory { get; init; } = [];
    public List<CrashLoop> Crashing { get; init; } = [];
}

public static partial class TvHealth
{
    /// <summary>Restarts in the current log above which an app counts as crash-looping.</summary>
    private const int CrashThreshold = 3;

    public static async Task<HealthReport> ReadAsync(AdbService adb, string serial)
    {
        var basics = await adb.ShellAsync(serial,
            "grep -E 'MemTotal|MemAvailable' /proc/meminfo; echo load $(cat /proc/loadavg); " +
            "echo cpus $(cat /sys/devices/system/cpu/present); echo up $(cat /proc/uptime)");
        long Kb(string key) => Regex.Match(basics.Output, key + @":\s+(\d+)") is { Success: true } m ? long.Parse(m.Groups[1].Value) : 0;
        var load = Regex.Match(basics.Output, @"load ([\d.]+)");
        var cpus = Regex.Match(basics.Output, @"cpus (\d+)-(\d+)");
        var up = Regex.Match(basics.Output, @"up ([\d.]+)");

        var storage = await TvDevice.ReadStorageAsync(adb, serial);

        // "193,472K: com.example.app (pid 4813 / activities)"
        var mem = await adb.ShellAsync(serial, "dumpsys meminfo | sed -n '/Total PSS by process/,/^$/p' | head -12", 30000);
        var top = ProcessLine().Matches(mem.Output)
            .Select(m => new ProcessUse(m.Groups[2].Value, long.Parse(m.Groups[1].Value.Replace(",", "")) / 1024))
            .Take(8).ToList();

        // Android logs every restart of a process that died: many restarts means a crash loop.
        var restarts = await adb.ShellAsync(serial, "logcat -d | grep -E 'Start proc [0-9]+:[^ ]+ for restart'", 30000);
        var crashing = RestartLine().Matches(restarts.Output)
            .GroupBy(m => m.Groups[1].Value)
            .Select(g => new CrashLoop(g.Key, g.Count()))
            .Where(c => c.Restarts >= CrashThreshold)
            .OrderByDescending(c => c.Restarts).ToList();

        return new HealthReport
        {
            MemTotalMb = Kb("MemTotal") / 1024,
            MemAvailableMb = Kb("MemAvailable") / 1024,
            StorageTotalMb = storage.TotalMb,
            StorageFreeMb = storage.FreeMb,
            Load = load.Success ? double.Parse(load.Groups[1].Value, CultureInfo.InvariantCulture) : 0,
            Cores = cpus.Success ? int.Parse(cpus.Groups[2].Value) - int.Parse(cpus.Groups[1].Value) + 1 : 1,
            UptimeHours = up.Success ? double.Parse(up.Groups[1].Value, CultureInfo.InvariantCulture) / 3600 : 0,
            TopMemory = top,
            Crashing = crashing,
        };
    }

    [GeneratedRegex(@"^\s*([\d,]+)K: ([^\s(]+)", RegexOptions.Multiline)] private static partial Regex ProcessLine();
    [GeneratedRegex(@"Start proc \d+:([^/\s]+)/\S+ for restart")] private static partial Regex RestartLine();
}
