using System.Diagnostics;
using System.IO.Compression;

namespace AndroidTVDebloater.Services;

public sealed record AdbResult(int ExitCode, string Output, string Error)
{
    public bool Ok => ExitCode == 0;
    public string All => (Output + "\n" + Error).Trim();
}

/// <summary>
/// Owns a private copy of Google's platform-tools (downloaded on first run) and runs adb.
/// </summary>
public sealed class AdbService
{
    public static string DataDir { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AndroidTVDebloater");

    public static string BackupDir => Path.Combine(DataDir, "backups");

    public string ToolsDir => Path.Combine(DataDir, "platform-tools");
    public string AdbPath => Path.Combine(ToolsDir, OperatingSystem.IsWindows() ? "adb.exe" : "adb");
    public bool IsInstalled => File.Exists(AdbPath);

    private bool _serverStarted;

    private static string DownloadUrl
    {
        get
        {
            var os = OperatingSystem.IsWindows() ? "windows" : OperatingSystem.IsMacOS() ? "darwin" : "linux";
            return $"https://dl.google.com/android/repository/platform-tools-latest-{os}.zip";
        }
    }

    /// <summary>Download and unpack platform-tools from Google if they are not there yet.</summary>
    public async Task EnsureInstalledAsync(IProgress<double>? progress, CancellationToken ct = default)
    {
        if (IsInstalled) { progress?.Report(1); return; }

        Directory.CreateDirectory(DataDir);
        var zip = Path.Combine(DataDir, "platform-tools.zip");
        var tmp = Path.Combine(DataDir, "platform-tools.tmp");

        using (var http = new HttpClient { Timeout = TimeSpan.FromMinutes(10) })
        using (var resp = await http.GetAsync(DownloadUrl, HttpCompletionOption.ResponseHeadersRead, ct))
        {
            resp.EnsureSuccessStatusCode();
            var total = resp.Content.Headers.ContentLength ?? -1;
            await using var src = await resp.Content.ReadAsStreamAsync(ct);
            await using var dst = File.Create(zip);
            var buffer = new byte[81920];
            long read = 0;
            int n;
            while ((n = await src.ReadAsync(buffer, ct)) > 0)
            {
                await dst.WriteAsync(buffer.AsMemory(0, n), ct);
                read += n;
                if (total > 0) progress?.Report(Math.Min(0.99, (double)read / total));
            }
        }

        if (Directory.Exists(tmp)) Directory.Delete(tmp, true);
        ZipFile.ExtractToDirectory(zip, tmp);
        if (Directory.Exists(ToolsDir)) Directory.Delete(ToolsDir, true);
        Directory.Move(Path.Combine(tmp, "platform-tools"), ToolsDir);
        Directory.Delete(tmp, true);
        File.Delete(zip);

        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(AdbPath, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);

        progress?.Report(1);
    }

    /// <summary>
    /// Start the adb server without redirecting its output: the server outlives the call and
    /// would otherwise hold our pipes open forever.
    /// </summary>
    private async Task StartServerAsync()
    {
        if (_serverStarted) return;
        var psi = new ProcessStartInfo(AdbPath) { UseShellExecute = false, CreateNoWindow = true };
        psi.ArgumentList.Add("start-server");
        using var p = Process.Start(psi)!;
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        try { await p.WaitForExitAsync(cts.Token); } catch (OperationCanceledException) { }
        _serverStarted = true;
    }

    public async Task<AdbResult> RunAsync(IEnumerable<string> args, int timeoutMs = 20000, CancellationToken ct = default)
    {
        await StartServerAsync();
        var psi = new ProcessStartInfo(AdbPath)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var a in args) psi.ArgumentList.Add(a);

        using var p = Process.Start(psi)!;
        var stdout = p.StandardOutput.ReadToEndAsync(ct);
        var stderr = p.StandardError.ReadToEndAsync(ct);
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(timeoutMs);
        try
        {
            await p.WaitForExitAsync(cts.Token);
        }
        catch (OperationCanceledException)
        {
            try { p.Kill(true); } catch { }
            return new AdbResult(-1, "", "timeout");
        }
        return new AdbResult(p.ExitCode, (await stdout).Replace("\r", ""), (await stderr).Replace("\r", ""));
    }

    public Task<AdbResult> ShellAsync(string serial, string command, int timeoutMs = 20000) =>
        RunAsync(["-s", serial, "shell", command], timeoutMs);

    /// <summary>State of one device in "adb devices": device, unauthorized, offline, or null if absent.</summary>
    public async Task<string?> GetStateAsync(string serial)
    {
        var r = await RunAsync(["devices"], 10000);
        foreach (var line in r.Output.Split('\n'))
        {
            var parts = line.Split(['\t', ' '], StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 2 && parts[0] == serial) return parts[1];
        }
        return null;
    }

    public void KillServer()
    {
        if (!IsInstalled || !_serverStarted) return;
        try
        {
            var psi = new ProcessStartInfo(AdbPath) { UseShellExecute = false, CreateNoWindow = true };
            psi.ArgumentList.Add("kill-server");
            using var p = Process.Start(psi);
            p?.WaitForExit(3000);
        }
        catch { }
    }
}
