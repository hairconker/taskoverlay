namespace TaskOverlay.Core.Models;

public enum GoalStatus
{
    Active,
    Paused,
    Completed
}

public enum GoalTimeHorizon
{
    LongTerm,
    ThisMonth,
    ThisWeek
}

public enum MilestoneStatus
{
    NotStarted,
    InProgress,
    Completed
}

public sealed class Goal
{
    public long Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public TaskPriority Priority { get; set; } = TaskPriority.Normal;
    public GoalStatus Status { get; set; } = GoalStatus.Active;
    public GoalTimeHorizon TimeHorizon { get; set; } = GoalTimeHorizon.LongTerm;
    public int? DailyBudgetMinutes { get; set; }
    public List<Tag> Tags { get; set; } = [];
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public DateTime UpdatedAt { get; set; } = DateTime.Now;
    public List<GoalMilestone> Milestones { get; set; } = [];
    public List<GoalTaskLink> TaskLinks { get; set; } = [];

    public string TagSummary => string.Join(", ", Tags.Select(t => t.Name));
}

public sealed class GoalMilestone
{
    public long Id { get; set; }
    public long GoalId { get; set; }
    public string Title { get; set; } = string.Empty;
    public DateOnly? TargetDate { get; set; }
    public MilestoneStatus Status { get; set; } = MilestoneStatus.NotStarted;
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public DateTime UpdatedAt { get; set; } = DateTime.Now;
}

public sealed class GoalTaskLink
{
    public long Id { get; set; }
    public long GoalId { get; set; }
    public long? TaskId { get; set; }
    public Guid? ProposalId { get; set; }
    public string? Note { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.Now;
}
