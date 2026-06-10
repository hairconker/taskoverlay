namespace TaskOverlay.Core.Models;

public sealed class DailyCompletion
{
    public long Id { get; set; }
    public long TaskId { get; set; }
    public DateOnly CompletedOn { get; set; }
    public DateTime CompletedAt { get; set; } = DateTime.Now;
}
