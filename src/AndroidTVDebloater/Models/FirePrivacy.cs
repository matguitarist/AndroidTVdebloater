using AndroidTVDebloater.Services;

namespace AndroidTVDebloater.Models;

/// <summary>
/// Amazon's tracking switches on Fire TV. They are kept in the Amazon account and synced back,
/// so the app only reads them and sends the user to the TV's own menu.
/// </summary>
public static class FirePrivacy
{
    /// <summary>Secure setting (1 = on) and the string key of its name in the TV's menu.</summary>
    public static readonly (string Key, string Label)[] Switches =
    [
        ("advertisingIdApp:interestBasedAds:value", "Fire_IbAds"),
        ("settingsApp:deviceUsageData:value", "Fire_DeviceUsage"),
        ("settingsApp:appUsageData:value", "Fire_AppUsage"),
    ];

    /// <summary>The labels of the switches that are on.</summary>
    public static async Task<List<string>> ReadOnAsync(AdbService adb, string serial)
    {
        var on = new List<string>();
        foreach (var (key, label) in Switches)
        {
            var r = await adb.ShellAsync(serial, $"settings get secure {key}");
            if (r.Output.Trim() == "1") on.Add(label);
        }
        return on;
    }

    /// <summary>Opens Settings → Preferences on the TV, where Privacy Settings is.</summary>
    public static Task OpenOnTvAsync(AdbService adb, string serial) =>
        adb.ShellAsync(serial, "am start -a com.amazon.device.settings.action.PREFERENCES");
}
