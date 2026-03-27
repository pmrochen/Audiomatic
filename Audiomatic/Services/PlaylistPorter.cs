using Audiomatic.Models;

namespace Audiomatic.Services;

/// <summary>
/// Import and export playlists in M3U/M3U8 and PLS formats.
/// </summary>
public static class PlaylistPorter
{
    // ── Export ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Exports a playlist to a file.
    /// Format is inferred from the extension: .pls → PLS, anything else → M3U8.
    /// </summary>
    public static void Export(long playlistId, string filePath)
    {
        var ext = Path.GetExtension(filePath).ToLowerInvariant();
        if (ext == ".pls")
            ExportPls(playlistId, filePath);
        else
            ExportM3U(playlistId, filePath);
    }

    private static void ExportM3U(long playlistId, string filePath)
    {
        var tracks = LibraryManager.GetPlaylistTracks(playlistId);
        using var w = new StreamWriter(filePath, append: false, System.Text.Encoding.UTF8);
        w.WriteLine("#EXTM3U");
        foreach (var t in tracks)
        {
            var secs = t.DurationMs / 1000;
            var display = string.IsNullOrWhiteSpace(t.Artist) ? t.Title : $"{t.Artist} - {t.Title}";
            w.WriteLine($"#EXTINF:{secs},{display}");
            w.WriteLine(t.Path);
        }
    }

    private static void ExportPls(long playlistId, string filePath)
    {
        var tracks = LibraryManager.GetPlaylistTracks(playlistId);
        using var w = new StreamWriter(filePath, append: false, System.Text.Encoding.UTF8);
        w.WriteLine("[playlist]");
        for (int i = 0; i < tracks.Count; i++)
        {
            var t = tracks[i];
            var n = i + 1;
            var secs = t.DurationMs > 0 ? t.DurationMs / 1000 : -1;
            var display = string.IsNullOrWhiteSpace(t.Artist) ? t.Title : $"{t.Artist} - {t.Title}";
            w.WriteLine($"File{n}={t.Path}");
            w.WriteLine($"Title{n}={display}");
            w.WriteLine($"Length{n}={secs}");
        }
        w.WriteLine($"NumberOfEntries={tracks.Count}");
        w.WriteLine("Version=2");
    }

    // ── Import ────────────────────────────────────────────────────────────────

    public record ImportResult(long PlaylistId, string PlaylistName, int Imported, int Total);

    /// <summary>
    /// Parses a playlist file (M3U/M3U8 or PLS) and creates a new playlist in the
    /// library with tracks that could be matched by path.
    /// Returns an <see cref="ImportResult"/> with the new playlist id and match counts.
    /// </summary>
    public static ImportResult Import(string filePath)
    {
        var ext = Path.GetExtension(filePath).ToLowerInvariant();
        var rawPaths = ext == ".pls" ? ParsePls(filePath) : ParseM3U(filePath);

        // Resolve relative paths relative to the playlist file's directory
        var playlistDir = Path.GetDirectoryName(filePath) ?? "";
        var absolutePaths = rawPaths
            .Select(p => Path.IsPathRooted(p) ? p : Path.GetFullPath(Path.Combine(playlistDir, p)))
            .ToList();

        var lookup = LibraryManager.GetTracksByPaths(absolutePaths);

        var playlistName = Path.GetFileNameWithoutExtension(filePath);
        var playlistId = LibraryManager.CreatePlaylist(playlistName);

        int imported = 0;
        foreach (var path in absolutePaths)
        {
            var key = path.Replace('/', '\\').TrimEnd();
            if (lookup.TryGetValue(key, out var track))
            {
                LibraryManager.AddTrackToPlaylist(playlistId, track.Id);
                imported++;
            }
        }

        return new ImportResult(playlistId, playlistName, imported, absolutePaths.Count);
    }

    // ── Parsers ───────────────────────────────────────────────────────────────

    private static List<string> ParseM3U(string filePath)
    {
        var paths = new List<string>();
        foreach (var line in File.ReadLines(filePath))
        {
            var l = line.Trim();
            if (string.IsNullOrEmpty(l) || l.StartsWith('#'))
                continue;
            paths.Add(l);
        }
        return paths;
    }

    private static List<string> ParsePls(string filePath)
    {
        var paths = new List<string>();
        foreach (var line in File.ReadLines(filePath))
        {
            var l = line.Trim();
            // Lines of the form "FileN=path"
            if (l.StartsWith("File", StringComparison.OrdinalIgnoreCase))
            {
                var eq = l.IndexOf('=');
                if (eq >= 0)
                {
                    var prefix = l[..eq];
                    // Verify it's "FileN" where N is a number
                    if (prefix.Length > 4 && int.TryParse(prefix[4..], out _))
                        paths.Add(l[(eq + 1)..].Trim());
                }
            }
        }
        return paths;
    }
}
