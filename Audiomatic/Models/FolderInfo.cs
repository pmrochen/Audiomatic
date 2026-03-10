namespace Audiomatic.Models;

public sealed class FolderInfo
{
    public long Id { get; set; }
    public string Path { get; set; } = "";
    public bool Enabled { get; set; } = true;
    public long LastScan { get; set; }
}
