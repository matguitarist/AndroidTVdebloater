using System.Diagnostics;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace AndroidTVDebloater.Services;

public sealed record UpdateInfo(Version Version, string Notes, string DownloadUrl, long Size, string Sha256, string PageUrl);

/// <summary>
/// Sparkle-style updates from GitHub Releases. The new exe is checked against the SHA-256 that
/// GitHub publishes for the file, then swapped in: Windows and Linux let a running program be
/// renamed, so the old one becomes .old (deleted on the next start) and the new one takes its name.
/// </summary>
public static partial class UpdateService
{
    private const string Repo = "matguitarist/UnbloatMyTV";
    /// <summary>The release file for this system. The Mac app is a separate Swift app with its own updater.</summary>
    private static string AssetName =>
        OperatingSystem.IsWindows() ? "UnbloatMyTV.exe"
        : AppImagePath is not null ? "UnbloatMyTV-x86_64.AppImage"
        : "UnbloatMyTV-linux-x64";

    /// <summary>Releases before 1.5 used the old name, "Android TV Debloater".</summary>
    private static string LegacyAssetName => AssetName.Replace("UnbloatMyTV", "AndroidTVDebloater");

    /// <summary>Set by the AppImage runtime: the .AppImage file this copy was started from.</summary>
    private static string? AppImagePath =>
        OperatingSystem.IsLinux() && Environment.GetEnvironmentVariable("APPIMAGE") is { Length: > 0 } p ? p : null;
    public const string ReleasesPage = $"https://github.com/{Repo}/releases/latest";

    public static Version Current => Normalize(typeof(UpdateService).Assembly.GetName().Version ?? new Version(1, 0, 0));

    /// <summary>1.2 and 1.2.0.0 both become 1.2.0, so they compare equal.</summary>
    private static Version Normalize(Version v) => new(v.Major, Math.Max(v.Minor, 0), Math.Max(v.Build, 0));

    internal static HttpClient NewClient(TimeSpan timeout)
    {
        var http = new HttpClient { Timeout = timeout };
        http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("UnbloatMyTV", Current.ToString()));
        // CI only: shared build machines hit GitHub's anonymous rate limit, so tests use their token.
        if (Environment.GetEnvironmentVariable("GITHUB_TOKEN") is { Length: > 0 } token)
            http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return http;
    }

    /// <summary>The latest release if it is newer than this copy (or <paramref name="than"/>), otherwise null.</summary>
    public static async Task<UpdateInfo?> CheckAsync(Version? than = null, CancellationToken ct = default)
    {
        using var http = NewClient(TimeSpan.FromSeconds(20));
        http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        using var doc = JsonDocument.Parse(await http.GetStringAsync($"https://api.github.com/repos/{Repo}/releases/latest", ct));
        var root = doc.RootElement;

        var tag = root.GetProperty("tag_name").GetString() ?? "";
        if (!Version.TryParse(tag.TrimStart('v', 'V'), out var parsed)) return null;
        var version = Normalize(parsed);
        if (version <= (than ?? Current)) return null;

        var assets = root.GetProperty("assets").EnumerateArray().ToList();
        JsonElement? asset = null;
        foreach (var name in new[] { AssetName, LegacyAssetName })
            asset ??= assets.Cast<JsonElement?>().FirstOrDefault(a => a!.Value.GetProperty("name").GetString() == name);
        if (asset is not { } found) return null;

        var digest = found.TryGetProperty("digest", out var d) ? d.GetString() ?? "" : "";
        if (!digest.StartsWith("sha256:")) return null; // no checksum, no update
        return new UpdateInfo(
            version,
            CleanNotes(root.TryGetProperty("body", out var b) ? b.GetString() : null),
            found.GetProperty("browser_download_url").GetString()!,
            found.GetProperty("size").GetInt64(),
            digest["sha256:".Length..].ToLowerInvariant(),
            root.GetProperty("html_url").GetString() ?? ReleasesPage);
    }

    /// <summary>Release notes are Markdown; show them as plain text.</summary>
    private static string CleanNotes(string? md)
    {
        if (string.IsNullOrWhiteSpace(md)) return "";
        var text = md.Replace("\r", "");
        text = HeadingRegex().Replace(text, "");
        text = BulletRegex().Replace(text, "• ");
        text = text.Replace("**", "").Replace("`", "");
        text = LinkRegex().Replace(text, "$1");
        return text.Trim();
    }

    [GeneratedRegex(@"^#+\s*", RegexOptions.Multiline)] private static partial Regex HeadingRegex();
    [GeneratedRegex(@"^\s*[-*]\s+", RegexOptions.Multiline)] private static partial Regex BulletRegex();
    [GeneratedRegex(@"\[([^\]]+)\]\([^)]+\)")] private static partial Regex LinkRegex();

    /// <summary>The file to replace: the .AppImage itself (the program inside runs from a read-only
    /// mount), otherwise the running program.</summary>
    private static string? ExePath => AppImagePath ?? Environment.ProcessPath;

    /// <summary>
    /// Only the published Windows or Linux build can replace itself, and only where it is allowed to write.
    /// Elsewhere (a dev build, Program Files…) the release page is opened instead.
    /// </summary>
    public static bool CanInstallInPlace
    {
        get
        {
            var exe = ExePath;
            if (!(OperatingSystem.IsWindows() || OperatingSystem.IsLinux()) || exe is null) return false;
            // A single-file exe has no assembly path; a dev build (dotnet run) does.
#pragma warning disable IL3000 // empty Location is exactly what we are testing for
            if (typeof(UpdateService).Assembly.Location.Length > 0) return false;
#pragma warning restore IL3000
            try
            {
                var probe = exe + ".write-test";
                File.WriteAllText(probe, "");
                File.Delete(probe);
                return true;
            }
            catch { return false; }
        }
    }

    /// <summary>Download, verify and swap in the new exe, then start it. The caller then exits.</summary>
    public static async Task InstallAsync(UpdateInfo update, IProgress<double>? progress, CancellationToken ct = default)
    {
        var exe = ExePath!;
        var download = exe + ".download";
        var old = exe + ".old";

        using (var http = NewClient(TimeSpan.FromMinutes(10)))
        using (var resp = await http.GetAsync(update.DownloadUrl, HttpCompletionOption.ResponseHeadersRead, ct))
        {
            resp.EnsureSuccessStatusCode();
            await using var src = await resp.Content.ReadAsStreamAsync(ct);
            await using var dst = File.Create(download);
            var buffer = new byte[81920];
            long read = 0;
            int n;
            while ((n = await src.ReadAsync(buffer, ct)) > 0)
            {
                await dst.WriteAsync(buffer.AsMemory(0, n), ct);
                read += n;
                progress?.Report(Math.Min(0.99, (double)read / update.Size));
            }
        }

        string hash;
        await using (var f = File.OpenRead(download))
            hash = Convert.ToHexStringLower(await SHA256.HashDataAsync(f, ct));
        if (hash != update.Sha256)
        {
            File.Delete(download);
            throw new InvalidDataException("checksum");
        }

        if (File.Exists(old)) File.Delete(old);
        File.Move(exe, old);
        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(download, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
                                           UnixFileMode.GroupRead | UnixFileMode.GroupExecute | UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
        try { File.Move(download, exe); }
        catch
        {
            File.Move(old, exe);
            throw;
        }
        progress?.Report(1);
        Process.Start(new ProcessStartInfo(exe) { UseShellExecute = false });
    }

    /// <summary>Remove what an earlier update left behind. The old process may still be closing.</summary>
    public static void CleanUpAfterUpdate()
    {
        var exe = ExePath;
        if (!(OperatingSystem.IsWindows() || OperatingSystem.IsLinux()) || exe is null) return;
        _ = Task.Run(async () =>
        {
            for (var i = 0; i < 10; i++)
            {
                try
                {
                    if (File.Exists(exe + ".old")) File.Delete(exe + ".old");
                    if (File.Exists(exe + ".download")) File.Delete(exe + ".download");
                    return;
                }
                catch { await Task.Delay(1000); }
            }
        });
    }

    public static void OpenInBrowser(string url)
    {
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
        catch { }
    }
}
