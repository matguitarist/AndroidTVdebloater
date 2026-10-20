using System.Collections.Concurrent;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace AndroidTVDebloater.Services;

/// <summary>Finds devices on the local network that accept adb connections on port 5555.</summary>
public static class NetworkScanner
{
    public const int AdbPort = 5555;

    public static async Task<List<string>> ScanAsync(IProgress<double>? progress, CancellationToken ct = default)
    {
        var targets = new List<IPAddress>();
        foreach (var subnet in LocalSubnets().Take(4))
            for (var i = 1; i < 255; i++)
            {
                var ip = new IPAddress([subnet[0], subnet[1], subnet[2], (byte)i]);
                if (!targets.Contains(ip)) targets.Add(ip);
            }

        var found = new ConcurrentBag<IPAddress>();
        var done = 0;
        await Parallel.ForEachAsync(targets,
            new ParallelOptions { MaxDegreeOfParallelism = 64, CancellationToken = ct },
            async (ip, token) =>
            {
                if (await IsPortOpenAsync(ip, AdbPort, 700, token)) found.Add(ip);
                progress?.Report((double)Interlocked.Increment(ref done) / targets.Count);
            });

        return found
            .OrderBy(ip => BitConverter.ToUInt32(ip.GetAddressBytes().Reverse().ToArray()))
            .Select(ip => ip.ToString())
            .ToList();
    }

    public static async Task<bool> IsPortOpenAsync(IPAddress ip, int port, int timeoutMs, CancellationToken ct = default)
    {
        using var client = new TcpClient();
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(timeoutMs);
        try
        {
            await client.ConnectAsync(ip, port, cts.Token);
            return true;
        }
        catch { return false; }
    }

    /// <summary>The /24 prefixes of this PC's private IPv4 addresses (home networks).</summary>
    private static IEnumerable<byte[]> LocalSubnets()
    {
        var seen = new HashSet<string>();
        foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (ni.OperationalStatus != OperationalStatus.Up) continue;
            if (ni.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel) continue;
            foreach (var ua in ni.GetIPProperties().UnicastAddresses)
            {
                if (ua.Address.AddressFamily != AddressFamily.InterNetwork) continue;
                var b = ua.Address.GetAddressBytes();
                var isPrivate = b[0] == 10 || (b[0] == 172 && b[1] >= 16 && b[1] <= 31) || (b[0] == 192 && b[1] == 168);
                if (!isPrivate) continue;
                if (seen.Add($"{b[0]}.{b[1]}.{b[2]}")) yield return b;
            }
        }
    }
}
