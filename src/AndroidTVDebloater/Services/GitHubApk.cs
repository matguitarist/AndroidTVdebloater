using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;

namespace AndroidTVDebloater.Services;

/// <summary>
/// Downloads the .apk of a project's latest GitHub release, checked against the SHA-256 that
/// GitHub publishes for the file. A file without a checksum is refused.
/// </summary>
public static class GitHubApk
{
    /// <summary>The verified .apk, in a temporary folder the caller deletes.</summary>
    public static async Task<string> DownloadLatestAsync(string repo, CancellationToken ct = default)
    {
        using var http = UpdateService.NewClient(TimeSpan.FromMinutes(10));
        http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        using var doc = JsonDocument.Parse(await http.GetStringAsync($"https://api.github.com/repos/{repo}/releases/latest", ct));
        var asset = doc.RootElement.GetProperty("assets").EnumerateArray()
            .Cast<JsonElement?>()
            .FirstOrDefault(a => a!.Value.GetProperty("name").GetString()?.EndsWith(".apk", StringComparison.OrdinalIgnoreCase) == true)
            ?? throw new InvalidDataException("no .apk in the latest release");
        var digest = asset.TryGetProperty("digest", out var d) ? d.GetString() ?? "" : "";
        if (!digest.StartsWith("sha256:")) throw new InvalidDataException("no checksum");

        var folder = Directory.CreateTempSubdirectory("unbloatmytv-").FullName;
        var path = Path.Combine(folder, Path.GetFileName(asset.GetProperty("name").GetString()!));
        try
        {
            await using (var dst = File.Create(path))
            await using (var src = await http.GetStreamAsync(asset.GetProperty("browser_download_url").GetString(), ct))
                await src.CopyToAsync(dst, ct);
            string hash;
            await using (var f = File.OpenRead(path))
                hash = Convert.ToHexStringLower(await SHA256.HashDataAsync(f, ct));
            if (hash != digest["sha256:".Length..].ToLowerInvariant()) throw new InvalidDataException("checksum");
            return path;
        }
        catch
        {
            Directory.Delete(folder, true);
            throw;
        }
    }
}
