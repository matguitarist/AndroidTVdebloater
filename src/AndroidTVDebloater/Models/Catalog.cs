using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using AndroidTVDebloater.Localization;
using CommunityToolkit.Mvvm.ComponentModel;

namespace AndroidTVDebloater.Models;

public sealed partial class TweakGroup : ObservableObject
{
    public required string Id { get; init; }
    public required LocText TitleText { get; init; }
    public required LocText DescriptionText { get; init; }
    public List<Tweak> Tweaks { get; } = [];

    public string Title => TitleText.ToString();
    public string Description => DescriptionText.ToString();
    public string CountText => Loc.Format("Choose_GroupCount", Tweaks.Count(t => t.IsSelected), Tweaks.Count);

    /// <summary>Keep <see cref="CountText"/> up to date once the list is filled.</summary>
    public void WatchSelection()
    {
        foreach (var t in Tweaks)
            t.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(Tweak.IsSelected)) OnPropertyChanged(nameof(CountText));
            };
    }

    public void RefreshTexts()
    {
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(Description));
        OnPropertyChanged(nameof(CountText));
        foreach (var t in Tweaks) t.RefreshTexts();
    }
}

/// <summary>Builds the list of choices for one TV from catalog.json plus the settings below.</summary>
public static class Catalog
{
    public const string BasicScreensaver = "com.android.dreams.basic/com.android.dreams.basic.Colors";

    private sealed class Text { public string En { get; set; } = ""; public string Fr { get; set; } = ""; }
    private sealed class GroupDef { public string Id { get; set; } = ""; public Text Title { get; set; } = new(); public Text Description { get; set; } = new(); }
    private sealed class PackageDef
    {
        public string? Id { get; set; }
        public string? Prefix { get; set; }
        public string Group { get; set; } = "";
        public bool Recommended { get; set; }
        public bool RequiresSetupComplete { get; set; }
        public Text Name { get; set; } = new();
        public Text Description { get; set; } = new();
        public Text? Warning { get; set; }
    }
    private sealed class CatalogFile { public List<GroupDef> Groups { get; set; } = []; public List<PackageDef> Packages { get; set; } = []; }

    private static readonly Lazy<CatalogFile> File = new(() =>
    {
        using var s = Assembly.GetExecutingAssembly().GetManifestResourceStream("catalog.json")!;
        return JsonSerializer.Deserialize<CatalogFile>(s, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
        })!;
    });

    private static LocText L(Text t) => new(t.En, t.Fr);
    private static LocText L(string en, string fr) => new(en, fr);

    public static IEnumerable<string> AllKnownPackages =>
        File.Value.Packages.Where(p => p.Id is not null).Select(p => p.Id!);

    public static List<TweakGroup> Build(TvDevice dev)
    {
        var groups = File.Value.Groups.Select(g => new TweakGroup
        {
            Id = g.Id, TitleText = L(g.Title), DescriptionText = L(g.Description),
        }).ToDictionary(g => g.Id);

        var used = new HashSet<string>();

        // Apps from the catalog, only those installed, enabled and not protected on this TV.
        foreach (var def in File.Value.Packages)
        {
            if (def.RequiresSetupComplete && !dev.SetupComplete) continue;
            var matches = def.Id is not null
                ? (dev.Enabled.Contains(def.Id) ? [def.Id] : Array.Empty<string>())
                : dev.Enabled.Where(p => p.StartsWith(def.Prefix!)).OrderBy(p => p).ToArray();

            foreach (var pkg in matches)
            {
                if (dev.IsProtected(pkg) || !used.Add(pkg)) continue;
                groups[def.Group].Tweaks.Add(new PackageTweak
                {
                    Id = "pkg:" + pkg, GroupId = def.Group, Package = pkg, Recommended = def.Recommended,
                    TitleText = L(def.Name), DescriptionText = L(def.Description),
                    WarningText = def.Warning is null ? null : L(def.Warning),
                });
            }
        }

        // Apps installed on top of the system that the catalog doesn't know.
        foreach (var pkg in dev.ThirdParty.Where(dev.Enabled.Contains).OrderBy(p => p))
        {
            if (used.Contains(pkg) || dev.IsProtected(pkg)) continue;
            groups["other"].Tweaks.Add(new PackageTweak
            {
                Id = "pkg:" + pkg, GroupId = "other", Package = pkg, Recommended = false,
                TitleText = L(FriendlyName(pkg), FriendlyName(pkg)),
                DescriptionText = L("Installed app.", "Application installée."),
            });
        }

        AddSettings(dev, groups);

        var list = groups.Values.Where(g => g.Tweaks.Count > 0).ToList();
        foreach (var t in list.SelectMany(g => g.Tweaks))
        {
            t.SelectedOption ??= t.Options?.FirstOrDefault();
            t.IsSelected = t.Recommended;
        }
        foreach (var g in list) g.WatchSelection();
        return list;
    }

    private static void AddSettings(TvDevice dev, Dictionary<string, TweakGroup> g)
    {
        g["performance"].Tweaks.Add(new SettingsTweak
        {
            Id = "animations", GroupId = "performance", Recommended = true,
            TitleText = L("Faster animations", "Animations plus rapides"),
            DescriptionText = L("Menus open and close quicker. Nothing else changes.",
                                "Les menus s'ouvrent et se ferment plus vite. Rien d'autre ne change."),
            Options =
            [
                new(L("Twice as fast (recommended)", "Deux fois plus rapides (recommandé)"), "0.5"),
                new(L("Off, instant", "Désactivées, instantané"), "0"),
            ],
            Writes =
            [
                new("global", "animator_duration_scale", "{option}"),
                new("global", "transition_animation_scale", "{option}"),
                new("global", "window_animation_scale", "{option}"),
            ],
        });

        if (dev.IsInstalled("com.android.dreams.basic") && dev.ScreensaverComponent != BasicScreensaver)
            g["performance"].Tweaks.Add(new SettingsTweak
            {
                Id = "screensaver", GroupId = "performance", Recommended = true,
                TitleText = L("Simple screensaver", "Écran de veille simple"),
                DescriptionText = L("Slowly changing colours instead of a screensaver that downloads photos and suggestions. Still protects the screen.",
                                    "Des couleurs qui changent lentement au lieu d'un écran de veille qui télécharge photos et suggestions. Protège toujours l'écran."),
                Writes = [new("secure", "screensaver_components", BasicScreensaver)],
            });

        g["performance"].Tweaks.Add(new BackgroundLimitTweak
        {
            Id = "bglimit", GroupId = "performance", Recommended = false,
            TitleText = L("Keep fewer apps in memory", "Garder moins d'applications en mémoire"),
            DescriptionText = L("Limits how many closed apps Android keeps in the background, leaving more memory for what you watch.",
                                "Limite le nombre d'applications fermées qu'Android garde en arrière-plan, pour laisser plus de mémoire à ce que vous regardez."),
            WarningText = L("Apps you go back to may take a second longer to open.",
                            "Les applications que vous rouvrez peuvent mettre une seconde de plus à s'ouvrir."),
            Options =
            [
                new(L("16 apps", "16 applications"), "16"),
                new(L("24 apps", "24 applications"), "24"),
            ],
        });

        g["performance"].Tweaks.Add(new SettingsTweak
        {
            Id = "sleep", GroupId = "performance", Recommended = false,
            TitleText = L("Standby when nobody is watching", "Veille quand personne ne regarde"),
            DescriptionText = L("Puts the device to sleep after a long time without any remote button pressed, to save power.",
                                "Met l'appareil en veille après un long moment sans appui sur la télécommande, pour économiser l'énergie."),
            WarningText = L("A long film without touching the remote counts as idle.",
                            "Un long film sans toucher la télécommande compte comme une inactivité."),
            Options =
            [
                new(L("After 4 hours", "Après 4 heures"), "14400000"),
                new(L("After 2 hours", "Après 2 heures"), "7200000"),
                new(L("After 8 hours", "Après 8 heures"), "28800000"),
            ],
            Writes = [new("secure", "sleep_timeout", "{option}")],
        });

        g["privacy"].Tweaks.Add(new SettingsTweak
        {
            Id = "wifiscan", GroupId = "privacy", Recommended = true,
            TitleText = L("Stop background Wi-Fi scanning", "Arrêter l'analyse Wi-Fi en arrière-plan"),
            DescriptionText = L("Stops the TV scanning for networks all the time and nagging about available Wi-Fi. Your connection is not affected.",
                                "Le téléviseur arrête de chercher des réseaux en permanence et de signaler les Wi-Fi disponibles. Votre connexion n'est pas touchée."),
            Writes =
            [
                new("global", "wifi_scan_always_enabled", "0"),
                new("global", "wifi_wakeup_enabled", "0"),
                new("global", "wifi_networks_available_notification_on", "0"),
            ],
        });

        g["privacy"].Tweaks.Add(new SettingsTweak
        {
            Id = "crashreports", GroupId = "privacy", Recommended = true,
            TitleText = L("Stop sending crash reports", "Arrêter l'envoi de rapports de plantage"),
            DescriptionText = L("Apps that crash no longer offer to send a report to Google.",
                                "Les applications qui plantent ne proposent plus d'envoyer un rapport à Google."),
            Writes = [new("global", "send_action_app_error", "0"), new("secure", "send_action_app_error", "0")],
        });

        if (dev.Sdk >= 28)
            g["privacy"].Tweaks.Add(new PrivateDnsTweak
            {
                Id = "dns", GroupId = "privacy", Recommended = false,
                TitleText = L("Block ads and trackers (DNS)", "Bloquer publicités et traqueurs (DNS)"),
                DescriptionText = L("Uses a filtering DNS service for the whole TV, so ad and tracking servers can't be reached. Tested right away: if the internet stops working, it is undone automatically.",
                                    "Utilise un service DNS filtrant pour tout le téléviseur : les serveurs de publicité et de pistage deviennent injoignables. Testé immédiatement : si internet ne fonctionne plus, c'est annulé automatiquement."),
                WarningText = L("If an app stops loading something later, undo this first from the Restore page.",
                                "Si une application n'affiche plus quelque chose plus tard, annulez d'abord ceci depuis la page Restaurer."),
                Options =
                [
                    new(L("AdGuard DNS: ads and trackers (recommended)", "AdGuard DNS : publicités et traqueurs (recommandé)"), "dns.adguard-dns.com"),
                    new(L("AdGuard Family: also adult content", "AdGuard Famille : aussi le contenu adulte"), "family.adguard-dns.com"),
                    new(L("Mullvad: ads and trackers", "Mullvad : publicités et traqueurs"), "adblock.dns.mullvad.net"),
                ],
            });

        g["privacy"].Tweaks.Add(new SettingsTweak
        {
            Id = "location", GroupId = "privacy", Recommended = false,
            TitleText = L("Turn off location", "Désactiver la localisation"),
            DescriptionText = L("A TV doesn't need to know where it is. Google network location is turned off.",
                                "Un téléviseur n'a pas besoin de savoir où il est. La localisation réseau de Google est désactivée."),
            WarningText = L("Weather or regional apps may ask for your city instead.",
                            "Les applications météo ou régionales pourront vous demander votre ville."),
            Writes = [new("secure", "location_mode", "0")],
        });

        if (dev.Network == NetworkKind.Ethernet)
            g["privacy"].Tweaks.Add(new WifiOffTweak
            {
                Id = "wifioff", GroupId = "privacy", Recommended = false,
                TitleText = L("Turn Wi-Fi off (this TV uses a cable)", "Désactiver le Wi-Fi (ce téléviseur utilise un câble)"),
                DescriptionText = L("This TV is connected by Ethernet cable, so the Wi-Fi radio is doing nothing.",
                                    "Ce téléviseur est connecté par câble Ethernet, la puce Wi-Fi ne sert donc à rien."),
                WarningText = L("If you unplug the cable one day, turn Wi-Fi back on in the TV settings first.",
                                "Si vous débranchez le câble un jour, réactivez d'abord le Wi-Fi dans les réglages du téléviseur."),
            });
    }

    private static string FriendlyName(string pkg)
    {
        var parts = pkg.Split('.');
        var last = parts.Length > 1 && parts[^1] is "android" or "tv" or "app" or "androidtv" ? parts[^2] : parts[^1];
        return last.Length == 0 ? pkg : char.ToUpperInvariant(last[0]) + last[1..];
    }
}
