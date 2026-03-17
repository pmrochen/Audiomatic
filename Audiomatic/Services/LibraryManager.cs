using System.Security.Cryptography;
using Audiomatic.Models;
using Microsoft.Data.Sqlite;

namespace Audiomatic.Services;

public static class LibraryManager
{
    private static readonly string DbDir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Audiomatic");
    private static readonly string DbPath = Path.Combine(DbDir, "library.db");

    public static readonly string[] AudioExtensions =
        [".mp3", ".flac", ".wav", ".ogg", ".aac", ".wma", ".m4a", ".opus", ".aiff", ".ape"];

    private static string ConnectionString => $"Data Source={DbPath}";

    /// <summary>Enables foreign keys on the given connection. Call after every conn.Open().</summary>
    private static void EnablePragmas(SqliteConnection conn)
    {
        using var pragma = conn.CreateCommand();
        pragma.CommandText = "PRAGMA foreign_keys = ON;";
        pragma.ExecuteNonQuery();
    }

    public static void Initialize()
    {
        Directory.CreateDirectory(DbDir);
        using var conn = new SqliteConnection(ConnectionString);
        conn.Open();
        EnablePragmas(conn);

        using var walCmd = conn.CreateCommand();
        walCmd.CommandText = "PRAGMA journal_mode = WAL;";
        walCmd.ExecuteNonQuery();

        conn.CreateFunction("lower", (string? s) => s?.ToLowerInvariant());

        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS folders (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                path TEXT NOT NULL UNIQUE,
                enabled INTEGER NOT NULL DEFAULT 1,
                last_scan INTEGER NOT NULL DEFAULT 0
            );

            CREATE TABLE IF NOT EXISTS tracks (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                path TEXT NOT NULL UNIQUE,
                title TEXT NOT NULL DEFAULT '',
                artist TEXT NOT NULL DEFAULT '',
                album TEXT NOT NULL DEFAULT '',
                duration_ms INTEGER NOT NULL DEFAULT 0,
                track_number INTEGER NOT NULL DEFAULT 0,
                year INTEGER NOT NULL DEFAULT 0,
                genre TEXT NOT NULL DEFAULT '',
                folder_id INTEGER NOT NULL,
                hash TEXT NOT NULL DEFAULT '',
                last_modified INTEGER NOT NULL DEFAULT 0,
                created_at INTEGER NOT NULL DEFAULT 0,
                FOREIGN KEY (folder_id) REFERENCES folders(id) ON DELETE CASCADE
            );

            CREATE TABLE IF NOT EXISTS playlists (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                name TEXT NOT NULL,
                created_at INTEGER NOT NULL DEFAULT 0
            );

            CREATE TABLE IF NOT EXISTS playlist_tracks (
                playlist_id INTEGER NOT NULL,
                track_id INTEGER NOT NULL,
                position INTEGER NOT NULL DEFAULT 0,
                PRIMARY KEY (playlist_id, track_id),
                FOREIGN KEY (playlist_id) REFERENCES playlists(id) ON DELETE CASCADE,
                FOREIGN KEY (track_id) REFERENCES tracks(id) ON DELETE CASCADE
            );

            CREATE TABLE IF NOT EXISTS favorites (
                track_id INTEGER PRIMARY KEY,
                added_at INTEGER NOT NULL DEFAULT 0,
                FOREIGN KEY (track_id) REFERENCES tracks(id) ON DELETE CASCADE
            );

            CREATE INDEX IF NOT EXISTS idx_tracks_folder ON tracks(folder_id);
            CREATE INDEX IF NOT EXISTS idx_tracks_artist ON tracks(artist);
            CREATE INDEX IF NOT EXISTS idx_tracks_album ON tracks(album);
            """;
        cmd.ExecuteNonQuery();

        // Migration: add bpm column if missing
        using var colCheck = conn.CreateCommand();
        colCheck.CommandText = "PRAGMA table_info(tracks);";
        bool hasBpm = false;
        using (var reader = colCheck.ExecuteReader())
        {
            while (reader.Read())
                if (reader.GetString(1) == "bpm") hasBpm = true;
        }
        if (!hasBpm)
        {
            using var alter = conn.CreateCommand();
            alter.CommandText = "ALTER TABLE tracks ADD COLUMN bpm INTEGER NOT NULL DEFAULT 0;";
            alter.ExecuteNonQuery();
        }
    }

    // ── Folder management ────────────────────────────────────

    public static long AddFolder(string path)
    {
        using var conn = new SqliteConnection(ConnectionString);
        conn.Open();
        EnablePragmas(conn);
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "INSERT OR IGNORE INTO folders (path) VALUES (@path); SELECT id FROM folders WHERE path = @path;";
        cmd.Parameters.AddWithValue("@path", path);
        return (long)cmd.ExecuteScalar()!;
    }

    public static void RemoveFolder(long folderId)
    {
        using var conn = new SqliteConnection(ConnectionString);
        conn.Open();
        EnablePragmas(conn);
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM tracks WHERE folder_id = @id; DELETE FROM folders WHERE id = @id;";
        cmd.Parameters.AddWithValue("@id", folderId);
        cmd.ExecuteNonQuery();
    }

    public static List<FolderInfo> GetFolders()
    {
        using var conn = new SqliteConnection(ConnectionString);
        conn.Open();
        EnablePragmas(conn);
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT id, path, enabled, last_scan FROM folders ORDER BY path;";
        var folders = new List<FolderInfo>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            folders.Add(new FolderInfo
            {
                Id = reader.GetInt64(0),
                Path = reader.GetString(1),
                Enabled = reader.GetInt64(2) == 1,
                LastScan = reader.GetInt64(3)
            });
        }
        return folders;
    }

    // ── Scanning ─────────────────────────────────────────────

    public static async Task<int> ScanFolderAsync(long folderId, string folderPath,
        IProgress<(int scanned, int total)>? progress = null,
        CancellationToken ct = default)
    {
        var files = new List<string>();
        try
        {
            foreach (var ext in AudioExtensions)
            {
                files.AddRange(Directory.EnumerateFiles(folderPath, $"*{ext}", SearchOption.AllDirectories));
            }
        }
        catch { return 0; }

        int added = 0;
        int scanned = 0;

        using var conn = new SqliteConnection(ConnectionString);
        await conn.OpenAsync(ct);
        EnablePragmas(conn);

        // Get existing paths for this folder
        var existingPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "SELECT path FROM tracks WHERE folder_id = @fid;";
            cmd.Parameters.AddWithValue("@fid", folderId);
            using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
                existingPaths.Add(reader.GetString(0));
        }

        using var transaction = conn.BeginTransaction();

        foreach (var filePath in files)
        {
            ct.ThrowIfCancellationRequested();
            scanned++;

            if (existingPaths.Contains(filePath))
            {
                existingPaths.Remove(filePath); // still exists
                continue;
            }

            try
            {
                var track = await Task.Run(() => ReadMetadata(filePath, folderId), ct);
                InsertTrack(conn, track, transaction);
                added++;
            }
            catch { }

            if (scanned % 50 == 0)
                progress?.Report((scanned, files.Count));
        }

        // Remove tracks whose files no longer exist
        foreach (var removed in existingPaths)
        {
            using var delCmd = conn.CreateCommand();
            delCmd.CommandText = "DELETE FROM tracks WHERE path = @path;";
            delCmd.Parameters.AddWithValue("@path", removed);
            delCmd.Transaction = transaction;
            await delCmd.ExecuteNonQueryAsync(ct);
        }

        // Update last_scan
        using (var updCmd = conn.CreateCommand())
        {
            updCmd.CommandText = "UPDATE folders SET last_scan = @ts WHERE id = @id;";
            updCmd.Parameters.AddWithValue("@ts", DateTimeOffset.UtcNow.ToUnixTimeSeconds());
            updCmd.Parameters.AddWithValue("@id", folderId);
            updCmd.Transaction = transaction;
            await updCmd.ExecuteNonQueryAsync(ct);
        }

        transaction.Commit();
        progress?.Report((files.Count, files.Count));
        return added;
    }

    private static TrackInfo ReadMetadata(string filePath, long folderId)
    {
        using var tagFile = TagLib.File.Create(filePath);
        var fi = new FileInfo(filePath);

        return new TrackInfo
        {
            Path = filePath,
            Title = string.IsNullOrWhiteSpace(tagFile.Tag.Title)
                ? System.IO.Path.GetFileNameWithoutExtension(filePath)
                : tagFile.Tag.Title.Trim(),
            Artist = tagFile.Tag.FirstPerformer?.Trim() ?? "",
            Album = tagFile.Tag.Album?.Trim() ?? "",
            DurationMs = (int)tagFile.Properties.Duration.TotalMilliseconds,
            TrackNumber = (int)tagFile.Tag.Track,
            Year = (int)tagFile.Tag.Year,
            Genre = tagFile.Tag.FirstGenre ?? "",
            Bpm = (int)tagFile.Tag.BeatsPerMinute,
            FolderId = folderId,
            Hash = ComputeFileHash(filePath),
            LastModified = new DateTimeOffset(fi.LastWriteTimeUtc).ToUnixTimeSeconds(),
            CreatedAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
        };
    }

    private static void InsertTrack(SqliteConnection conn, TrackInfo t, SqliteTransaction transaction)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = transaction;
        cmd.CommandText = """
            INSERT OR REPLACE INTO tracks
            (path, title, artist, album, duration_ms, track_number, year, genre, bpm, folder_id, hash, last_modified, created_at)
            VALUES (@path, @title, @artist, @album, @dur, @tn, @year, @genre, @bpm, @fid, @hash, @lm, @ca);
            """;
        cmd.Parameters.AddWithValue("@path", t.Path);
        cmd.Parameters.AddWithValue("@title", t.Title);
        cmd.Parameters.AddWithValue("@artist", t.Artist);
        cmd.Parameters.AddWithValue("@album", t.Album);
        cmd.Parameters.AddWithValue("@dur", t.DurationMs);
        cmd.Parameters.AddWithValue("@tn", t.TrackNumber);
        cmd.Parameters.AddWithValue("@year", t.Year);
        cmd.Parameters.AddWithValue("@genre", t.Genre);
        cmd.Parameters.AddWithValue("@bpm", t.Bpm);
        cmd.Parameters.AddWithValue("@fid", t.FolderId);
        cmd.Parameters.AddWithValue("@hash", t.Hash);
        cmd.Parameters.AddWithValue("@lm", t.LastModified);
        cmd.Parameters.AddWithValue("@ca", t.CreatedAt);
        cmd.ExecuteNonQuery();
    }

    private static string ComputeFileHash(string path)
    {
        using var stream = File.OpenRead(path);
        // Read first 64KB for fast hashing
        var buffer = new byte[65536];
        int read = stream.Read(buffer, 0, buffer.Length);
        var hash = SHA256.HashData(buffer.AsSpan(0, read));
        return Convert.ToHexString(hash)[..16];
    }

    // ── Track queries ────────────────────────────────────────

    public static List<TrackInfo> GetAllTracks()
    {
        using var conn = new SqliteConnection(ConnectionString);
        conn.Open();
        EnablePragmas(conn);
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT t.id, t.path, t.title, t.artist, t.album, t.duration_ms,
                   t.track_number, t.year, t.genre, t.bpm, t.folder_id, t.hash, t.last_modified, t.created_at,
                   CASE WHEN f.track_id IS NOT NULL THEN 1 ELSE 0 END as is_fav
            FROM tracks t
            LEFT JOIN favorites f ON f.track_id = t.id
            ORDER BY t.artist, t.album, t.track_number, t.title;
            """;
        return ReadTracks(cmd);
    }

    public static List<TrackInfo> SearchTracks(string query)
    {
        if (string.IsNullOrWhiteSpace(query)) return GetAllTracks();

        using var conn = new SqliteConnection(ConnectionString);
        conn.Open();
        EnablePragmas(conn);
        using var cmd = conn.CreateCommand();
        var q = $"%{query.Trim()}%";
        cmd.CommandText = """
            SELECT t.id, t.path, t.title, t.artist, t.album, t.duration_ms,
                   t.track_number, t.year, t.genre, t.bpm, t.folder_id, t.hash, t.last_modified, t.created_at,
                   CASE WHEN f.track_id IS NOT NULL THEN 1 ELSE 0 END as is_fav
            FROM tracks t
            LEFT JOIN favorites f ON f.track_id = t.id
            WHERE t.title LIKE @q OR t.artist LIKE @q OR t.album LIKE @q
            ORDER BY t.artist, t.album, t.track_number, t.title;
            """;
        cmd.Parameters.AddWithValue("@q", q);
        return ReadTracks(cmd);
    }

    public static int GetTrackCount()
    {
        using var conn = new SqliteConnection(ConnectionString);
        conn.Open();
        EnablePragmas(conn);
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM tracks;";
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    private static List<TrackInfo> ReadTracks(SqliteCommand cmd)
    {
        var tracks = new List<TrackInfo>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            tracks.Add(new TrackInfo
            {
                Id = reader.GetInt64(0),
                Path = reader.GetString(1),
                Title = reader.GetString(2),
                Artist = reader.GetString(3),
                Album = reader.GetString(4),
                DurationMs = reader.GetInt32(5),
                TrackNumber = reader.GetInt32(6),
                Year = reader.GetInt32(7),
                Genre = reader.GetString(8),
                Bpm = reader.GetInt32(9),
                FolderId = reader.GetInt64(10),
                Hash = reader.GetString(11),
                LastModified = reader.GetInt64(12),
                CreatedAt = reader.GetInt64(13),
                IsFavorite = reader.GetInt64(14) == 1
            });
        }
        return tracks;
    }

    // ── Favorites ────────────────────────────────────────────

    public static void ToggleFavorite(long trackId)
    {
        using var conn = new SqliteConnection(ConnectionString);
        conn.Open();
        EnablePragmas(conn);
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO favorites (track_id, added_at) VALUES (@id, @ts)
            ON CONFLICT(track_id) DO DELETE;
            """;
        cmd.Parameters.AddWithValue("@id", trackId);
        cmd.Parameters.AddWithValue("@ts", DateTimeOffset.UtcNow.ToUnixTimeSeconds());
        cmd.ExecuteNonQuery();
    }

    public static List<TrackInfo> GetFavorites()
    {
        using var conn = new SqliteConnection(ConnectionString);
        conn.Open();
        EnablePragmas(conn);
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT t.id, t.path, t.title, t.artist, t.album, t.duration_ms,
                   t.track_number, t.year, t.genre, t.bpm, t.folder_id, t.hash, t.last_modified, t.created_at,
                   1 as is_fav
            FROM tracks t
            INNER JOIN favorites f ON f.track_id = t.id
            ORDER BY f.added_at DESC;
            """;
        return ReadTracks(cmd);
    }

    // ── Metadata editing ─────────────────────────────────────

    public static void UpdateTrackMetadata(long trackId, string title, string artist, string album)
    {
        using var conn = new SqliteConnection(ConnectionString);
        conn.Open();
        EnablePragmas(conn);
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE tracks SET title = @title, artist = @artist, album = @album WHERE id = @id;";
        cmd.Parameters.AddWithValue("@title", title);
        cmd.Parameters.AddWithValue("@artist", artist);
        cmd.Parameters.AddWithValue("@album", album);
        cmd.Parameters.AddWithValue("@id", trackId);
        cmd.ExecuteNonQuery();
    }

    public static void UpdateTrackBpm(long trackId, int bpm)
    {
        using var conn = new SqliteConnection(ConnectionString);
        conn.Open();
        EnablePragmas(conn);
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE tracks SET bpm = @bpm WHERE id = @id;";
        cmd.Parameters.AddWithValue("@bpm", bpm);
        cmd.Parameters.AddWithValue("@id", trackId);
        cmd.ExecuteNonQuery();
    }

    // ── Playlist management ──────────────────────────────────

    public static long CreatePlaylist(string name)
    {
        using var conn = new SqliteConnection(ConnectionString);
        conn.Open();
        EnablePragmas(conn);
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "INSERT INTO playlists (name, created_at) VALUES (@name, @ts); SELECT last_insert_rowid();";
        cmd.Parameters.AddWithValue("@name", name);
        cmd.Parameters.AddWithValue("@ts", DateTimeOffset.UtcNow.ToUnixTimeSeconds());
        return (long)cmd.ExecuteScalar()!;
    }

    public static void DeletePlaylist(long playlistId)
    {
        using var conn = new SqliteConnection(ConnectionString);
        conn.Open();
        EnablePragmas(conn);
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM playlist_tracks WHERE playlist_id = @id; DELETE FROM playlists WHERE id = @id;";
        cmd.Parameters.AddWithValue("@id", playlistId);
        cmd.ExecuteNonQuery();
    }

    public static void RenamePlaylist(long playlistId, string newName)
    {
        using var conn = new SqliteConnection(ConnectionString);
        conn.Open();
        EnablePragmas(conn);
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE playlists SET name = @name WHERE id = @id;";
        cmd.Parameters.AddWithValue("@name", newName);
        cmd.Parameters.AddWithValue("@id", playlistId);
        cmd.ExecuteNonQuery();
    }

    public static List<PlaylistInfo> GetPlaylists()
    {
        using var conn = new SqliteConnection(ConnectionString);
        conn.Open();
        EnablePragmas(conn);
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT id, name, created_at FROM playlists ORDER BY name;";
        var playlists = new List<PlaylistInfo>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            playlists.Add(new PlaylistInfo
            {
                Id = reader.GetInt64(0),
                Name = reader.GetString(1),
                CreatedAt = reader.GetInt64(2)
            });
        }
        return playlists;
    }

    public static void AddTrackToPlaylist(long playlistId, long trackId)
    {
        using var conn = new SqliteConnection(ConnectionString);
        conn.Open();
        EnablePragmas(conn);
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT OR IGNORE INTO playlist_tracks (playlist_id, track_id, position)
            VALUES (@pid, @tid, (SELECT COALESCE(MAX(position), 0) + 1 FROM playlist_tracks WHERE playlist_id = @pid));
            """;
        cmd.Parameters.AddWithValue("@pid", playlistId);
        cmd.Parameters.AddWithValue("@tid", trackId);
        cmd.ExecuteNonQuery();
    }

    public static void RemoveTrackFromPlaylist(long playlistId, long trackId)
    {
        using var conn = new SqliteConnection(ConnectionString);
        conn.Open();
        EnablePragmas(conn);
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM playlist_tracks WHERE playlist_id = @pid AND track_id = @tid;";
        cmd.Parameters.AddWithValue("@pid", playlistId);
        cmd.Parameters.AddWithValue("@tid", trackId);
        cmd.ExecuteNonQuery();
    }

    public static List<TrackInfo> GetPlaylistTracks(long playlistId)
    {
        using var conn = new SqliteConnection(ConnectionString);
        conn.Open();
        EnablePragmas(conn);
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT t.id, t.path, t.title, t.artist, t.album, t.duration_ms,
                   t.track_number, t.year, t.genre, t.bpm, t.folder_id, t.hash, t.last_modified, t.created_at,
                   CASE WHEN f.track_id IS NOT NULL THEN 1 ELSE 0 END as is_fav
            FROM tracks t
            INNER JOIN playlist_tracks pt ON pt.track_id = t.id
            LEFT JOIN favorites f ON f.track_id = t.id
            WHERE pt.playlist_id = @pid
            ORDER BY pt.position;
            """;
        cmd.Parameters.AddWithValue("@pid", playlistId);
        return ReadTracks(cmd);
    }

    public static void MoveTrackInPlaylist(long playlistId, int fromPosition, int toPosition)
    {
        using var conn = new SqliteConnection(ConnectionString);
        conn.Open();
        EnablePragmas(conn);
        using var tx = conn.BeginTransaction();

        // Get ordered track IDs
        using var selectCmd = conn.CreateCommand();
        selectCmd.CommandText = "SELECT track_id FROM playlist_tracks WHERE playlist_id = @pid ORDER BY position;";
        selectCmd.Parameters.AddWithValue("@pid", playlistId);
        var trackIds = new List<long>();
        using (var reader = selectCmd.ExecuteReader())
        {
            while (reader.Read())
                trackIds.Add(reader.GetInt64(0));
        }

        if (fromPosition < 0 || fromPosition >= trackIds.Count) return;
        toPosition = Math.Clamp(toPosition, 0, trackIds.Count - 1);
        if (fromPosition == toPosition) return;

        var trackId = trackIds[fromPosition];
        trackIds.RemoveAt(fromPosition);
        trackIds.Insert(toPosition, trackId);

        // Rewrite all positions
        using var updateCmd = conn.CreateCommand();
        updateCmd.CommandText = "UPDATE playlist_tracks SET position = @pos WHERE playlist_id = @pid AND track_id = @tid;";
        var posParam = updateCmd.Parameters.Add("@pos", SqliteType.Integer);
        updateCmd.Parameters.AddWithValue("@pid", playlistId);
        var tidParam = updateCmd.Parameters.Add("@tid", SqliteType.Integer);
        updateCmd.Prepare();

        for (int i = 0; i < trackIds.Count; i++)
        {
            posParam.Value = i + 1;
            tidParam.Value = trackIds[i];
            updateCmd.ExecuteNonQuery();
        }

        tx.Commit();
    }

    // ── Reset ────────────────────────────────────────────────

    public static void ResetLibrary()
    {
        using var conn = new SqliteConnection(ConnectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            DELETE FROM playlist_tracks;
            DELETE FROM playlists;
            DELETE FROM favorites;
            DELETE FROM tracks;
            DELETE FROM folders;
            """;
        cmd.ExecuteNonQuery();
    }

    // ── Full rescan ──────────────────────────────────────────

    public static async Task<int> ScanAllFoldersAsync(
        IProgress<(int scanned, int total)>? progress = null,
        CancellationToken ct = default)
    {
        var folders = GetFolders().Where(f => f.Enabled).ToList();
        int totalAdded = 0;
        foreach (var folder in folders)
        {
            ct.ThrowIfCancellationRequested();
            totalAdded += await ScanFolderAsync(folder.Id, folder.Path, progress, ct);
        }
        return totalAdded;
    }
}
