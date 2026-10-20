using System.Security.Cryptography;
using AndroidTVDebloater.Services;

// Pretends to be version 0.9, "updates" to the latest release, then checks the running exe
// was really replaced and the old one kept aside, exactly as the app does it.
// Usage (Windows, published single-file): UpdateSwapTest.exe
var exe = Environment.ProcessPath!;
Console.WriteLine($"exe: {exe}");
Console.WriteLine($"can install in place: {UpdateService.CanInstallInPlace}");
if (!UpdateService.CanInstallInPlace) return Fail("expected to be able to install in place");

var update = await UpdateService.CheckAsync(new Version(0, 9, 0));
if (update is null) return Fail("no release found");
Console.WriteLine($"latest: {update.Version}, {update.Size} bytes, sha256 {update.Sha256}");

await UpdateService.InstallAsync(update, new Progress<double>(_ => { }));

string hash;
await using (var f = File.OpenRead(exe)) hash = Convert.ToHexStringLower(await SHA256.HashDataAsync(f));
if (hash != update.Sha256) return Fail($"exe not replaced (sha256 {hash})");
if (!File.Exists(exe + ".old")) return Fail("old exe not kept aside");
if (File.Exists(exe + ".download")) return Fail("download left behind");
Console.WriteLine("swap ok");
return 0;

static int Fail(string why)
{
    Console.Error.WriteLine("FAIL: " + why);
    return 1;
}
