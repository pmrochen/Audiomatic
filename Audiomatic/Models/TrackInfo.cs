namespace Audiomatic.Models;

public sealed class TrackInfo
{
    public long Id { get; set; }
    public string Path { get; set; } = "";
    public string Title { get; set; } = "";
    public string Artist { get; set; } = "";
    public string Album { get; set; } = "";
    public int DurationMs { get; set; }
    public int TrackNumber { get; set; }
    public int Year { get; set; }
    public string Genre { get; set; } = "";
    public long FolderId { get; set; }
    public string Hash { get; set; } = "";
    public long LastModified { get; set; }
    public long CreatedAt { get; set; }
    public bool IsFavorite { get; set; }
    public int Bpm { get; set; }

    public string DurationFormatted
    {
        get
        {
            var ts = TimeSpan.FromMilliseconds(DurationMs);
            return ts.TotalHours >= 1
                ? $"{(int)ts.TotalHours}:{ts.Minutes:D2}:{ts.Seconds:D2}"
                : $"{(int)ts.TotalMinutes}:{ts.Seconds:D2}";
        }
    }

    public override string ToString() => $"{Artist} - {Title}";
}
