using System.Text.Json;

namespace Audiomatic;

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
    int? WindowX = null,
    int? WindowY = null);

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

    public static string LoadTheme() => Load().Theme ?? "system";

    public static void SaveTheme(string theme)
    {
        var current = Load();
        Save(current with { Theme = theme });
    }

    private static AppSettings CreateDefault() => new(
        Backdrop: new BackdropSettings(),
        Volume: 1.0,
        ShuffleEnabled: false,
        RepeatMode: "none",
        SortBy: "title",
        SortAscending: true,
        Language: "fr");
}
