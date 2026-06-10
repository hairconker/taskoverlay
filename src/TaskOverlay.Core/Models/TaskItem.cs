using System.Text.Json.Serialization;
using TaskOverlay.Core.Services;

namespace TaskOverlay.Core.Models;

public sealed class TaskItem
{
    public long Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Notes { get; set; }
    public TaskPriority Priority { get; set; } = TaskPriority.Normal;
    public DateTime? DueAt { get; set; }
    public DateTime? ReminderAt { get; set; }
    public int? ReminderOffsetMinutes { get; set; }
    public bool IsCompleted { get; set; }
    public DateTime? CompletedAt { get; set; }
    public bool IsDaily { get; set; }
    public int SortOrder { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public DateTime UpdatedAt { get; set; } = DateTime.Now;
    public RecurrenceRule? Recurrence { get; set; }
    public List<Tag> Tags { get; set; } = [];

    [JsonIgnore]
    public bool IsRecurring => TaskOccurrenceRules.IsRecurring(this);

    [JsonIgnore]
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

    [JsonIgnore]
    public string TagSummary => string.Join(", ", Tags.Select(t => t.Name));
}
