namespace TaskOverlay.Core.Models;

public sealed class Tag
{
    public long Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Color { get; set; } = "#6B7280";
}
