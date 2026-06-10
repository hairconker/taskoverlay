namespace TaskOverlay.Core.Models;

public sealed class RecurrenceRule
{
    public long Id { get; set; }
    public long TaskId { get; set; }
    public RecurrenceKind Kind { get; set; }
    public int Interval { get; set; } = 1;
    public DayOfWeek? DayOfWeek { get; set; }
    public int? DayOfMonth { get; set; }
}
