using TaskOverlay.Core.Models;

namespace TaskOverlay.Core.Services;

public static class TaskOccurrenceRules
{
    public static bool IsRecurring(TaskItem task)
        => task.IsDaily || task.Recurrence is { Kind: not RecurrenceKind.None };

    public static bool OccursOn(TaskItem task, DateOnly date)
    {
        if (task.IsDaily)
        {
            return true;
        }

        var rule = task.Recurrence;
        if (rule is null || rule.Kind == RecurrenceKind.None)
        {
            return task.DueAt is not null && DateOnly.FromDateTime(task.DueAt.Value) == date;
        }

        var anchor = DateOnly.FromDateTime(task.DueAt ?? task.CreatedAt);
        if (date < anchor)
        {
            return false;
        }

        var interval = Math.Max(1, rule.Interval);
        return rule.Kind switch
        {
            RecurrenceKind.Daily => (date.DayNumber - anchor.DayNumber) % interval == 0,
            RecurrenceKind.Weekly => date.DayOfWeek == (rule.DayOfWeek ?? anchor.DayOfWeek) &&
                                     (date.DayNumber - anchor.DayNumber) / 7 % interval == 0,
            RecurrenceKind.Monthly => date.Day == (rule.DayOfMonth ?? anchor.Day) &&
                                      MonthsBetween(anchor, date) % interval == 0,
            RecurrenceKind.CustomDays => (date.DayNumber - anchor.DayNumber) % interval == 0,
            _ => false
        };
    }

    public static bool HasActiveOccurrenceBetween(TaskItem task, DateOnly start, DateOnly endExclusive)
    {
        for (var date = start; date < endExclusive; date = date.AddDays(1))
        {
            if (OccursOn(task, date))
            {
                return true;
            }
        }

        return false;
    }

    private static int MonthsBetween(DateOnly start, DateOnly end)
        => (end.Year - start.Year) * 12 + end.Month - start.Month;
}
