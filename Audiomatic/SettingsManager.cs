using System.Text.Json;

namespace Audiomatic;

public record RadioStation(string Url, string Name);

public record BackdropSettings(
    string Type = "acrylic",
    double TintOpacity = 1.0,
    double LuminosityOpacity = 0.0,
    string TintColor = "#000000",
    string FallbackColor = "#1E1E1E",
    string Kind = "Base");

public record AppSettings(
    BackdropSettings Backdrop,
    double Volume,
    bool ShuffleEnabled,
    string RepeatMode,  // "none", "all", "one"
    string SortBy,      // "title", "artist", "album", "duration", "added"
    bool SortAscending,
    string Language,     // "fr", "en"
    string Theme = "system",  // "system", "light", "dark"
    int VisualizerFps = 30,
    string VisualizerMode = "classic",  // "classic", "bars", "circle", "wave"
    string VisualizerColor = "",        // hex string, empty = system accent
    bool VisualizerGlow = true,
    bool VisualizerDarkBg = false,
    int? WindowX = null,
    int? WindowY = null,
    bool EqEnabled = true,
    string EqPreset = "Flat",
    float[]? EqBands = null,       // 10-band gains in dB (-12 to +12)
    float EqPreamp = 0f,
    string AccentColor = "",       // hex string, empty = system accent
    int DurationDataVersion = 0,
    int? OverlayX = null,
    int? OverlayY = null,
    int[]? TabOrder = null);

public static class SettingsManager
{
    private static readonly string SettingsDir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Audiomatic");
    private static readonly string SettingsPath = Path.Combine(SettingsDir, "settings.json");

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(SettingsPath))
            {
                var json = File.ReadAllText(SettingsPath);
                return JsonSerializer.Deserialize<AppSettings>(json, JsonOpts)
                    ?? CreateDefault();
            }
        }
        catch { }
        return CreateDefault();
    }

    public static void Save(AppSettings settings)
    {
        try
        {
            Directory.CreateDirectory(SettingsDir);
            var json = JsonSerializer.Serialize(settings, JsonOpts);
            File.WriteAllText(SettingsPath, json);
        }
        catch { }
    }

    public static BackdropSettings LoadBackdrop() => Load().Backdrop;

    public static void SaveBackdrop(BackdropSettings backdrop)
    {
        var current = Load();
        Save(current with { Backdrop = backdrop });
    }

    public static string LoadLanguage() => Load().Language ?? "en";

    public static void SaveLanguage(string language)
    {
        var current = Load();
        Save(current with { Language = language });
        Strings.Language = language;
    }

    public static string LoadTheme() => Load().Theme ?? "system";

    public static void SaveTheme(string theme)
    {
        var current = Load();
        Save(current with { Theme = theme });
    }

    public static int[] LoadTabOrder()
    {
        var order = Load().TabOrder;
        // Validate: must contain exactly 0..10 once each
        if (order is { Length: 11 } && Enumerable.Range(0, 11).All(order.Contains))
            return order;
        return [0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10];
    }

    public static void SaveTabOrder(int[] order)
    {
        var current = Load();
        Save(current with { TabOrder = order });
    }

    // -- Radio stations persistence --

    private static readonly string RadioPath = Path.Combine(SettingsDir, "radio_stations.json");

    public static List<RadioStation> LoadRadioStations()
    {
        try
        {
            if (File.Exists(RadioPath))
            {
                var json = File.ReadAllText(RadioPath);
                return JsonSerializer.Deserialize<List<RadioStation>>(json, JsonOpts) ?? [];
            }
        }
        catch { }
        return [];
    }

    public static void SaveRadioStations(List<RadioStation> stations)
    {
        try
        {
            Directory.CreateDirectory(SettingsDir);
            var json = JsonSerializer.Serialize(stations, JsonOpts);
            File.WriteAllText(RadioPath, json);
        }
        catch { }
    }

    private static AppSettings CreateDefault() => new(
        Backdrop: new BackdropSettings(),
        Volume: 1.0,
        ShuffleEnabled: false,
        RepeatMode: "none",
        SortBy: "title",
        SortAscending: true,
        Language: "en");
}
