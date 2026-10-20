using AndroidTVDebloater.Localization;
using System.Globalization;
using System.Text.Json;
using AndroidTVDebloater.Services;

namespace AndroidTVDebloater.Models;

public sealed class SavedChoice
{
    /// <summary>Tweak id: "pkg:&lt;package&gt;" for apps, the setting id otherwise.</summary>
    public string Id { get; set; } = "";
    /// <summary>Value of the chosen option, for choices that have options.</summary>
    public string? Option { get; set; }
}

/// <summary>
/// A set of ticked choices, to do the same on another TV. The last run's set is kept
/// automatically; others can be saved to a file (same format in the Mac app).
/// </summary>
public sealed class ChoiceSet
{
    /// <summary>The TV the choices were made on.</summary>
    public string Name { get; set; } = "";
    /// <summary>ISO 8601, kept as text so other apps read it easily.</summary>
    public string Created { get; set; } = "";
    public List<SavedChoice> Choices { get; set; } = [];

    public string CreatedText =>
        DateTime.TryParse(Created, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var d)
            ? d.ToLocalTime().ToString("g", Loc.Culture) : Created;

    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
    };

    private static string LastPath => Path.Combine(AdbService.DataDir, "last-choices.json");

    public static ChoiceSet From(string tvName, IEnumerable<Tweak> selected) => new()
    {
        Name = tvName,
        Created = DateTimeOffset.Now.ToString("yyyy-MM-dd'T'HH:mm:sszzz", CultureInfo.InvariantCulture),
        Choices = selected.Select(t => new SavedChoice { Id = t.Id, Option = t.SelectedOption?.Value }).ToList(),
    };

    /// <summary>Tick exactly these choices. Returns how many exist on this TV.</summary>
    public int ApplyTo(IEnumerable<Tweak> all)
    {
        var wanted = Choices.GroupBy(c => c.Id).ToDictionary(g => g.Key, g => g.First());
        var found = 0;
        foreach (var t in all)
        {
            t.IsSelected = wanted.TryGetValue(t.Id, out var c);
            if (!t.IsSelected) continue;
            found++;
            if (c!.Option is { } value && t.Options?.FirstOrDefault(o => o.Value == value) is { } option)
                t.SelectedOption = option;
        }
        return found;
    }

    public void Save(Stream s) => JsonSerializer.Serialize(s, this, Json);

    public static ChoiceSet? Load(Stream s)
    {
        try { return JsonSerializer.Deserialize<ChoiceSet>(s, Json) is { Choices.Count: > 0 } c ? c : null; }
        catch { return null; }
    }

    public void SaveAsLast()
    {
        try
        {
            Directory.CreateDirectory(AdbService.DataDir);
            using var f = File.Create(LastPath);
            Save(f);
        }
        catch { /* only a convenience */ }
    }

    public static ChoiceSet? LoadLast()
    {
        try
        {
            if (!File.Exists(LastPath)) return null;
            using var f = File.OpenRead(LastPath);
            return Load(f);
        }
        catch { return null; }
    }
}
