using System.Net.Http;
using System.Text.Json;
using System.Xml.Linq;

namespace Audiomatic.Services;

public record PodcastInfo(string Name, string Author, string FeedUrl, string ArtworkUrl);

public record PodcastEpisode(string Title, string Published, string Duration, string AudioUrl, string Description);

public static class PodcastService
{
    private static readonly HttpClient Http = new();
    private static readonly string PodcastsPath =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Audiomatic", "podcasts.json");

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    /// <summary>
    /// Search podcasts via iTunes Search API.
    /// </summary>
    public static async Task<List<PodcastInfo>> SearchAsync(string query, int limit = 20)
    {
        if (string.IsNullOrWhiteSpace(query)) return [];

        var url = $"https://itunes.apple.com/search?term={Uri.EscapeDataString(query)}&media=podcast&limit={limit}";
        var json = await Http.GetStringAsync(url);
        var doc = JsonDocument.Parse(json);

        var results = new List<PodcastInfo>();
        foreach (var item in doc.RootElement.GetProperty("results").EnumerateArray())
        {
            var name = item.TryGetProperty("collectionName", out var n) ? n.GetString() ?? "" : "";
            var author = item.TryGetProperty("artistName", out var a) ? a.GetString() ?? "" : "";
            var feedUrl = item.TryGetProperty("feedUrl", out var f) ? f.GetString() ?? "" : "";
            var artwork = item.TryGetProperty("artworkUrl100", out var art) ? art.GetString() ?? "" : "";

            if (!string.IsNullOrEmpty(feedUrl))
                results.Add(new PodcastInfo(name, author, feedUrl, artwork));
        }
        return results;
    }

    /// <summary>
    /// Fetch episodes from a podcast RSS feed.
    /// </summary>
    public static async Task<List<PodcastEpisode>> FetchEpisodesAsync(string feedUrl, int limit = 50)
    {
        var xml = await Http.GetStringAsync(feedUrl);
        var doc = XDocument.Parse(xml);
        XNamespace itunes = "http://www.itunes.com/dtds/podcast-1.0.dtd";

        var episodes = new List<PodcastEpisode>();
        var items = doc.Descendants("item");

        foreach (var item in items.Take(limit))
        {
            var title = item.Element("title")?.Value ?? "";
            var pubDate = item.Element("pubDate")?.Value ?? "";
            var duration = item.Element(itunes + "duration")?.Value ?? "";
            var enclosure = item.Element("enclosure");
            var audioUrl = enclosure?.Attribute("url")?.Value ?? "";
            var description = item.Element("description")?.Value ?? "";

            // Clean up published date
            if (DateTime.TryParse(pubDate, out var dt))
                pubDate = dt.ToString("dd MMM yyyy");

            if (!string.IsNullOrEmpty(audioUrl))
                episodes.Add(new PodcastEpisode(title, pubDate, duration, audioUrl, description));
        }
        return episodes;
    }

    // -- Episode progress tracking --

    private static readonly string ProgressPath =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Audiomatic", "podcast_progress.json");

    /// <summary>Load saved progress (audioUrl → position in seconds).</summary>
    public static Dictionary<string, double> LoadProgress()
    {
        try
        {
            if (File.Exists(ProgressPath))
            {
                var json = File.ReadAllText(ProgressPath);
                return JsonSerializer.Deserialize<Dictionary<string, double>>(json, JsonOpts) ?? [];
            }
        }
        catch { }
        return [];
    }

    public static void SaveProgress(Dictionary<string, double> progress)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(ProgressPath)!);
            var json = JsonSerializer.Serialize(progress, JsonOpts);
            File.WriteAllText(ProgressPath, json);
        }
        catch { }
    }

    // -- Read/unread episode tracking --

    private static readonly string ReadPath =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Audiomatic", "podcast_read.json");

    public static HashSet<string> LoadReadEpisodes()
    {
        try
        {
            if (File.Exists(ReadPath))
            {
                var json = File.ReadAllText(ReadPath);
                var list = JsonSerializer.Deserialize<List<string>>(json, JsonOpts);
                return list != null ? new HashSet<string>(list) : [];
            }
        }
        catch { }
        return [];
    }

    public static void SaveReadEpisodes(HashSet<string> readUrls)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(ReadPath)!);
            var json = JsonSerializer.Serialize(readUrls.ToList(), JsonOpts);
            File.WriteAllText(ReadPath, json);
        }
        catch { }
    }

    // -- Episode downloads --

    private static readonly string DownloadsDir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Audiomatic", "podcasts");

    /// <summary>
    /// Deterministic local file path for a given episode audio URL.
    /// </summary>
    /// <summary>
    /// Supported download extensions — formats NAudio can play natively.
    /// </summary>
    private static readonly HashSet<string> SupportedDownloadExtensions =
        new(StringComparer.OrdinalIgnoreCase) { ".mp3", ".wav", ".flac", ".aac", ".ogg", ".m4a", ".aiff" };

    public static string GetDownloadPath(string audioUrl)
    {
        // Use a hash of the URL to avoid filesystem issues with long/special-char URLs
        var hash = Convert.ToHexString(
            System.Security.Cryptography.SHA256.HashData(
                System.Text.Encoding.UTF8.GetBytes(audioUrl)))[..24];
        // Keep original extension only if NAudio supports it, otherwise default to .mp3
        var ext = Path.GetExtension(new Uri(audioUrl).AbsolutePath);
        if (string.IsNullOrEmpty(ext) || !SupportedDownloadExtensions.Contains(ext))
            ext = ".mp3";
        return Path.Combine(DownloadsDir, hash + ext);
    }

    public static bool IsDownloaded(string audioUrl)
    {
        var path = GetDownloadPath(audioUrl);
        return File.Exists(path);
    }

    public static async Task DownloadEpisodeAsync(string audioUrl,
        IProgress<double>? progress = null, CancellationToken ct = default)
    {
        Directory.CreateDirectory(DownloadsDir);
        var destPath = GetDownloadPath(audioUrl);

        // If already downloaded, skip
        if (File.Exists(destPath)) return;

        var tmpPath = destPath + ".tmp";

        try
        {
            using var response = await Http.GetAsync(audioUrl, HttpCompletionOption.ResponseHeadersRead, ct);
            response.EnsureSuccessStatusCode();

            var totalBytes = response.Content.Headers.ContentLength ?? -1;
            long downloaded = 0;

            // Explicit using blocks so streams are closed before File.Move
            using (var contentStream = await response.Content.ReadAsStreamAsync(ct))
            using (var fileStream = new FileStream(tmpPath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true))
            {
                var buffer = new byte[81920];
                int bytesRead;
                while ((bytesRead = await contentStream.ReadAsync(buffer, ct)) > 0)
                {
                    await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead), ct);
                    downloaded += bytesRead;
                    if (totalBytes > 0)
                        progress?.Report((double)downloaded / totalBytes);
                }
            }
            // Streams are now closed — safe to rename
            File.Move(tmpPath, destPath, overwrite: true);
            progress?.Report(1.0);
        }
        catch
        {
            // Clean up partial .tmp file on any failure
            try { if (File.Exists(tmpPath)) File.Delete(tmpPath); } catch { }
            throw;
        }
    }

    public static void DeleteDownload(string audioUrl)
    {
        var path = GetDownloadPath(audioUrl);
        try { if (File.Exists(path)) File.Delete(path); } catch { }
    }

    public static long GetDownloadsSizeBytes()
    {
        if (!Directory.Exists(DownloadsDir)) return 0;
        return new DirectoryInfo(DownloadsDir)
            .EnumerateFiles()
            .Where(f => !f.Name.EndsWith(".tmp"))
            .Sum(f => f.Length);
    }

    /// <summary>
    /// Load saved podcast subscriptions.
    /// </summary>
    public static List<PodcastInfo> LoadSubscriptions()
    {
        try
        {
            if (File.Exists(PodcastsPath))
            {
                var json = File.ReadAllText(PodcastsPath);
                return JsonSerializer.Deserialize<List<PodcastInfo>>(json, JsonOpts) ?? [];
            }
        }
        catch { }
        return [];
    }

    /// <summary>
    /// Save podcast subscriptions.
    /// </summary>
    public static void SaveSubscriptions(List<PodcastInfo> podcasts)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(PodcastsPath)!);
            var json = JsonSerializer.Serialize(podcasts, JsonOpts);
            File.WriteAllText(PodcastsPath, json);
        }
        catch { }
    }
}
