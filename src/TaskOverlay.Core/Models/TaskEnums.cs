namespace TaskOverlay.Core.Models;

public enum TaskPriority
{
    Low = 0,
    Normal = 1,
    High = 2,
    Urgent = 3
}

public enum TaskFilter
{
    Today,
    TodayAcceptance,
    Tomorrow,
    ThisWeek,
    Overdue,
    Completed,
    All
}

public enum RecurrenceKind
{
    None,
    Daily,
    Weekly,
    Monthly,
    CustomDays
}

public enum TaskStorageBackend
{
    Json,
    MySql
}
