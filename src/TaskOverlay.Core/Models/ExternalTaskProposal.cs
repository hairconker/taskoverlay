namespace TaskOverlay.Core.Models;

public sealed class ExternalTaskProposal
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Title { get; set; } = string.Empty;
    public string? Notes { get; set; }
    public TaskPriority Priority { get; set; } = TaskPriority.Normal;
    public DateTime? DueAt { get; set; }
    public DateTime? ReminderAt { get; set; }
    public bool IsDaily { get; set; }
    public RecurrenceRule? Recurrence { get; set; }
    public List<Tag> Tags { get; set; } = [];
    public string Source { get; set; } = "external";
    public DateTime CreatedAt { get; set; } = DateTime.Now;

    public string TagSummary => string.Join(", ", Tags.Select(t => t.Name));

    public string ScheduleSummary => IsDaily
        ? "每天"
        : Recurrence?.Kind switch
        {
            RecurrenceKind.Daily => $"每 {Math.Max(1, Recurrence.Interval)} 天",
            RecurrenceKind.Weekly => $"每 {Math.Max(1, Recurrence.Interval)} 周",
            RecurrenceKind.Monthly => $"每 {Math.Max(1, Recurrence.Interval)} 月",
            RecurrenceKind.CustomDays => $"每隔 {Math.Max(1, Recurrence.Interval)} 天",
            _ => string.Empty
        };
}
