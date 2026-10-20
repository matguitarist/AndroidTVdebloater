using System.ComponentModel;
using System.Globalization;
using System.Text.Json;
using Avalonia.Data;
using Avalonia.Markup.Xaml;

namespace AndroidTVDebloater.Localization;

/// <summary>Current UI language and string lookup. Views bind through <see cref="T"/>.</summary>
public sealed class Loc : INotifyPropertyChanged
{
    public static Loc Instance { get; } = new();

    public event PropertyChangedEventHandler? PropertyChanged;
    public event Action? LanguageChanged;

    private string _language;

    private Loc()
    {
        _language = LoadSavedLanguage()
                    ?? (CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "fr" ? "fr" : "en");
    }

    public string Language
    {
        get => _language;
        set
        {
            if (value == _language || (value != "en" && value != "fr")) return;
            _language = value;
            SaveLanguage(value);
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Language)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsFrench)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsEnglish)));
            // "Item[]" refreshes every indexer binding, i.e. every {l:T} in the UI.
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item[]"));
            LanguageChanged?.Invoke();
        }
    }

    public bool IsFrench { get => _language == "fr"; set { if (value) Language = "fr"; } }
    public bool IsEnglish { get => _language == "en"; set { if (value) Language = "en"; } }

    public string this[string key] => Get(key);

    public static string Get(string key)
    {
        var table = Instance._language == "fr" ? Strings.Fr : Strings.En;
        if (table.TryGetValue(key, out var s)) return s;
        return Strings.En.TryGetValue(key, out var en) ? en : key;
    }

    public static string Format(string key, params object[] args) =>
        string.Format(CultureInfo.CurrentCulture, Get(key), args);

    private static string SettingsPath =>
        Path.Combine(Services.AdbService.DataDir, "settings.json");

    private static string? LoadSavedLanguage()
    {
        try
        {
            if (!File.Exists(SettingsPath)) return null;
            using var doc = JsonDocument.Parse(File.ReadAllText(SettingsPath));
            return doc.RootElement.TryGetProperty("language", out var l) ? l.GetString() : null;
        }
        catch { return null; }
    }

    private static void SaveLanguage(string lang)
    {
        try
        {
            Directory.CreateDirectory(Services.AdbService.DataDir);
            File.WriteAllText(SettingsPath, JsonSerializer.Serialize(new { language = lang }));
        }
        catch { /* the choice just won't be remembered */ }
    }
}

/// <summary>Text in both languages, for catalog entries.</summary>
public sealed record LocText(string En, string Fr)
{
    public override string ToString() => Loc.Instance.Language == "fr" && Fr.Length > 0 ? Fr : En;
}

/// <summary>XAML: Text="{l:T Welcome_Title}"</summary>
public sealed class T : MarkupExtension
{
    public T() { }
    public T(string key) => Key = key;
    public string Key { get; set; } = "";

    public override object ProvideValue(IServiceProvider serviceProvider) =>
        new Binding($"[{Key}]") { Source = Loc.Instance, Mode = BindingMode.OneWay };
}
