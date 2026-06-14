using TaskOverlay.Core.Models;

namespace TaskOverlay.Core.Services;

public static class TaskAcceptanceRules
{
    private static readonly string[] AcceptanceKeywords =
    [
        "验收",
        "交付",
        "检查",
        "检测",
        "测试",
        "uat",
        "ccd",
        "今日计划",
        "效果图"
    ];

    public static bool IsTodayAcceptanceCandidate(TaskItem task, DateOnly today)
    {
        if (task.IsCompleted)
        {
            return false;
        }

        return TaskOccurrenceRules.OccursOn(task, today) && HasAcceptanceSignal(task);
    }

    private static bool HasAcceptanceSignal(TaskItem task)
    {
        if (task.Priority >= TaskPriority.High)
        {
            return true;
        }

        return ContainsKeyword(task.Title) ||
               ContainsKeyword(task.Notes) ||
               task.Tags.Any(tag => ContainsKeyword(tag.Name));
    }

    private static bool ContainsKeyword(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        return AcceptanceKeywords.Any(keyword => value.Contains(keyword, StringComparison.OrdinalIgnoreCase));
    }
}
