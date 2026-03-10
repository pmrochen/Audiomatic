namespace Audiomatic.Models;

public sealed class PlaylistInfo
{
    public long Id { get; set; }
    public string Name { get; set; } = "";
    public long CreatedAt { get; set; }
    public List<long> TrackIds { get; set; } = [];
}
