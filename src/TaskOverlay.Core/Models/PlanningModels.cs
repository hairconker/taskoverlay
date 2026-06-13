namespace TaskOverlay.Core.Models;

public enum PlanningMode
{
    TaskList,
    TimeBlock
}

public enum PlanningItemKind
{
    ExistingTask,
    ProposedTask,
    Adjustment
}

public sealed class PlanningRequest
{
    public PlanningMode Mode { get; set; } = PlanningMode.TaskList;
    public DateOnly TargetDate { get; set; } = DateOnly.FromDateTime(DateTime.Today.AddDays(1));
    public List<PlanningTimeWindow> TimeWindows { get; set; } = [];
    public int MaxItems { get; set; } = 8;
    public string? GoalSummary { get; set; }
}

public sealed class PlanningTimeWindow
{
    public TimeOnly Start { get; set; }
    public TimeOnly End { get; set; }

    public int DurationMinutes => Math.Max(0, (int)(End - Start).TotalMinutes);
    public string Summary => $"{Start:HH\\:mm}-{End:HH\\:mm}";
}

public sealed class PlanningReview
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public PlanningMode Mode { get; set; }
    public DateOnly TargetDate { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public string Summary { get; set; } = string.Empty;
    public List<PlanningItem> Items { get; set; } = [];
    public List<string> Warnings { get; set; } = [];
}

public sealed class PlanningItem
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public PlanningItemKind Kind { get; set; }
    public long? TaskId { get; set; }
    public long? GoalId { get; set; }
    public string? GoalTitle { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Notes { get; set; }
    public TaskPriority Priority { get; set; } = TaskPriority.Normal;
    public DateTime? DueAt { get; set; }
    public DateTime? ReminderAt { get; set; }
    public string? TimeBlock { get; set; }
    public string Reason { get; set; } = string.Empty;
    public List<Tag> Tags { get; set; } = [];
    public List<PlanningItem> Children { get; set; } = [];
}
