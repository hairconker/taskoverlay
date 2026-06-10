using TaskOverlay.Core.Models;

namespace TaskOverlay.Core.Services;

public static class TaskReminderRules
{
    private const int MaxSearchDays = 366 * 20;

    public static void NormalizeOffset(TaskItem task)
    {
        if (!TaskOccurrenceRules.IsRecurring(task) ||
            task.ReminderAt is null ||
            task.DueAt is null ||
            task.ReminderOffsetMinutes is not null)
        {
            return;
        }

        task.ReminderOffsetMinutes = ToWholeMinutes(task.ReminderAt.Value - task.DueAt.Value);
    }

    public static DateTime? GetNextReminderAt(TaskItem task, DateTime now)
    {
        if (!TaskOccurrenceRules.IsRecurring(task) || task.ReminderAt is null)
        {
            return null;
        }

        var offset = task.DueAt is null
            ? 0
            : task.ReminderOffsetMinutes ??
              ToWholeMinutes(task.ReminderAt.Value - task.DueAt.Value);
        var start = DateOnly.FromDateTime(now.AddMinutes(-offset)).AddDays(-1);
        for (var dayOffset = 0; dayOffset < MaxSearchDays; dayOffset++)
        {
            var occurrenceDate = start.AddDays(dayOffset);
            if (!TaskOccurrenceRules.OccursOn(task, occurrenceDate))
            {
                continue;
            }

            var candidate = BuildReminderAt(task, occurrenceDate);
            if (candidate > now)
            {
                return candidate;
            }
        }

        return null;
    }

    private static DateTime BuildReminderAt(TaskItem task, DateOnly occurrenceDate)
    {
        if (task.DueAt is null)
        {
            return occurrenceDate.ToDateTime(TimeOnly.FromDateTime(task.ReminderAt!.Value));
        }

        var dueAt = occurrenceDate.ToDateTime(TimeOnly.FromDateTime(task.DueAt.Value));
        var offset = task.ReminderOffsetMinutes ??
                     ToWholeMinutes(task.ReminderAt!.Value - task.DueAt.Value);
        return dueAt.AddMinutes(offset);
    }

    private static int ToWholeMinutes(TimeSpan value)
        => checked((int)Math.Round(value.TotalMinutes, MidpointRounding.AwayFromZero));
}
