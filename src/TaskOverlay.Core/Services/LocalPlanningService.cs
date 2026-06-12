using TaskOverlay.Core.Models;

namespace TaskOverlay.Core.Services;

public sealed class LocalPlanningService(TaskApplicationService tasks, GoalApplicationService? goals = null)
{
    public async Task<PlanningReview> BuildTomorrowPlanAsync(
        PlanningRequest request,
        CancellationToken cancellationToken = default)
    {
        var targetDate = request.TargetDate == default
            ? DateOnly.FromDateTime(DateTime.Today.AddDays(1))
            : request.TargetDate;
        var targetStart = targetDate.ToDateTime(TimeOnly.MinValue);
        var targetDefaultDue = targetDate.ToDateTime(new TimeOnly(18, 0));

        var today = await tasks.GetTasksAsync(TaskFilter.Today, cancellationToken: cancellationToken);
        var tomorrow = await tasks.GetTasksAsync(TaskFilter.Tomorrow, cancellationToken: cancellationToken);
        var overdue = await tasks.GetTasksAsync(TaskFilter.Overdue, cancellationToken: cancellationToken);
        var all = await tasks.GetTasksAsync(TaskFilter.All, cancellationToken: cancellationToken);
        var activeGoals = goals is null
            ? []
            : (await goals.GetGoalsAsync(GoalStatus.Active, cancellationToken)).ToList();

        var selected = SelectCandidateTasks(today, tomorrow, overdue, request.MaxItems);
        var review = new PlanningReview
        {
            Mode = request.Mode,
            TargetDate = targetDate,
            Summary = BuildSummary(request, today.Count, tomorrow.Count, overdue.Count, selected.Count)
        };

        foreach (var item in BuildExistingTaskItems(selected, targetDefaultDue))
        {
            review.Items.Add(item);
        }

        foreach (var item in BuildCarryOverProposalItems(today, tomorrow, targetDefaultDue, request.MaxItems - review.Items.Count))
        {
            review.Items.Add(item);
        }

        foreach (var goalItem in BuildGoalProposalItems(activeGoals, targetDefaultDue, request.MaxItems - review.Items.Count))
        {
            review.Items.Add(goalItem);
        }

        if (!string.IsNullOrWhiteSpace(request.GoalSummary) && review.Items.Count < request.MaxItems)
        {
            review.Items.Add(new PlanningItem
            {
                Kind = PlanningItemKind.ProposedTask,
                Title = "推进长期目标",
                Notes = request.GoalSummary.Trim(),
                Priority = TaskPriority.High,
                DueAt = targetDefaultDue,
                Reason = "来自本次规划输入的目标摘要。",
                Tags = [new Tag { Name = "目标" }, new Tag { Name = "规划" }]
            });
        }

        if (request.Mode == PlanningMode.TimeBlock)
        {
            ApplyTimeBlocks(review, request.TimeWindows, targetStart);
        }

        if (overdue.Count > 0)
        {
            review.Warnings.Add($"有 {overdue.Count} 个过期任务，建议优先处理或重新安排。");
        }

        if (activeGoals.Count > 0)
        {
            review.Warnings.Add($"已读取 {activeGoals.Count} 个进行中的长期目标。");
        }

        if (review.Items.Count == 0 && all.Count == 0)
        {
            review.Items.Add(new PlanningItem
            {
                Kind = PlanningItemKind.ProposedTask,
                Title = "规划明天的三个重点",
                Priority = TaskPriority.Normal,
                DueAt = targetDefaultDue,
                Reason = "当前没有可用任务，先建立明日重点。",
                Tags = [new Tag { Name = "规划" }]
            });
        }

        return review;
    }

    private static List<TaskItem> SelectCandidateTasks(
        IReadOnlyList<TaskItem> today,
        IReadOnlyList<TaskItem> tomorrow,
        IReadOnlyList<TaskItem> overdue,
        int maxItems)
    {
        return overdue
            .Concat(tomorrow)
            .Concat(today)
            .Where(task => !task.IsCompleted)
            .GroupBy(task => task.Id)
            .Select(group => group.First())
            .OrderByDescending(task => task.Priority)
            .ThenBy(task => task.DueAt ?? DateTime.MaxValue)
            .ThenBy(task => task.SortOrder)
            .ThenBy(task => task.CreatedAt)
            .Take(Math.Max(1, maxItems))
            .ToList();
    }

    private static IEnumerable<PlanningItem> BuildExistingTaskItems(IEnumerable<TaskItem> tasks, DateTime targetDefaultDue)
    {
        foreach (var task in tasks)
        {
            yield return new PlanningItem
            {
                Kind = PlanningItemKind.ExistingTask,
                TaskId = task.Id,
                Title = task.Title,
                Notes = task.Notes,
                Priority = task.Priority,
                DueAt = task.DueAt ?? targetDefaultDue,
                ReminderAt = task.ReminderAt,
                Reason = BuildReason(task),
                Tags = task.Tags.Select(tag => new Tag { Name = tag.Name, Color = tag.Color }).ToList()
            };
        }
    }

    private static IEnumerable<PlanningItem> BuildCarryOverProposalItems(
        IReadOnlyList<TaskItem> today,
        IReadOnlyList<TaskItem> tomorrow,
        DateTime targetDefaultDue,
        int remainingSlots)
    {
        if (remainingSlots <= 0)
        {
            yield break;
        }

        var tomorrowIds = tomorrow.Select(task => task.Id).ToHashSet();
        foreach (var task in today.Where(task => !task.IsCompleted && !tomorrowIds.Contains(task.Id)).Take(remainingSlots))
        {
            yield return new PlanningItem
            {
                Kind = PlanningItemKind.Adjustment,
                TaskId = task.Id,
                Title = $"建议明天继续：{task.Title}",
                Notes = task.Notes,
                Priority = task.Priority,
                DueAt = targetDefaultDue,
                Reason = "今天仍未完成，建议明天继续安排；需确认后才修改原任务。",
                Tags = task.Tags.Select(tag => new Tag { Name = tag.Name, Color = tag.Color }).ToList()
            };
        }
    }

    private static IEnumerable<PlanningItem> BuildGoalProposalItems(
        IReadOnlyList<Goal> goals,
        DateTime targetDefaultDue,
        int remainingSlots)
    {
        if (remainingSlots <= 0)
        {
            yield break;
        }

        foreach (var goal in goals
                     .OrderByDescending(goal => goal.Priority)
                     .ThenBy(goal => goal.TimeHorizon)
                     .ThenBy(goal => goal.CreatedAt)
                     .Take(remainingSlots))
        {
            var milestone = goal.Milestones
                .Where(item => item.Status != MilestoneStatus.Completed)
                .OrderBy(item => item.TargetDate ?? DateOnly.MaxValue)
                .FirstOrDefault();
            var title = milestone is null
                ? $"推进目标：{goal.Title}"
                : $"推进目标：{goal.Title} / {milestone.Title}";
            var notes = new List<string>();
            if (!string.IsNullOrWhiteSpace(goal.Description))
            {
                notes.Add(goal.Description);
            }
            if (goal.DailyBudgetMinutes is not null)
            {
                notes.Add($"建议投入：{goal.DailyBudgetMinutes} 分钟");
            }
            if (milestone?.TargetDate is not null)
            {
                notes.Add($"阶段目标日期：{milestone.TargetDate:yyyy-MM-dd}");
            }

            yield return new PlanningItem
            {
                Kind = PlanningItemKind.ProposedTask,
                Title = title,
                Notes = notes.Count == 0 ? null : string.Join(Environment.NewLine, notes),
                Priority = goal.Priority,
                DueAt = targetDefaultDue,
                Reason = "来自进行中的长期目标库。",
                Tags = goal.Tags.Select(tag => new Tag { Name = tag.Name, Color = tag.Color }).ToList()
            };
        }
    }

    private static string BuildReason(TaskItem task)
    {
        if (task.DueAt is not null && task.DueAt.Value < DateTime.Now)
        {
            return "任务已过期，优先进入明日计划。";
        }

        if (task.DueAt is not null && DateOnly.FromDateTime(task.DueAt.Value) == DateOnly.FromDateTime(DateTime.Today.AddDays(1)))
        {
            return "任务已安排在明天。";
        }

        return task.Priority >= TaskPriority.High
            ? "高优先级任务，建议保留在明日计划中。"
            : "来自当前待处理任务列表。";
    }

    private static string BuildSummary(
        PlanningRequest request,
        int todayCount,
        int tomorrowCount,
        int overdueCount,
        int selectedCount)
    {
        var mode = request.Mode == PlanningMode.TimeBlock ? "时间块模式" : "任务列表模式";
        return $"{mode}：读取今天 {todayCount} 项、明天 {tomorrowCount} 项、过期 {overdueCount} 项，生成 {selectedCount} 项核心建议。";
    }

    private static void ApplyTimeBlocks(PlanningReview review, IReadOnlyList<PlanningTimeWindow> windows, DateTime targetStart)
    {
        var usableWindows = windows
            .Where(window => window.DurationMinutes > 0)
            .OrderBy(window => window.Start)
            .ToList();

        if (usableWindows.Count == 0)
        {
            usableWindows =
            [
                new PlanningTimeWindow { Start = new TimeOnly(9, 0), End = new TimeOnly(10, 30) },
                new PlanningTimeWindow { Start = new TimeOnly(14, 0), End = new TimeOnly(15, 30) },
                new PlanningTimeWindow { Start = new TimeOnly(20, 0), End = new TimeOnly(20, 45) }
            ];
            review.Warnings.Add("未提供可用时间段，已使用默认时间块。");
        }

        for (var index = 0; index < review.Items.Count; index++)
        {
            var window = usableWindows[index % usableWindows.Count];
            var item = review.Items[index];
            item.TimeBlock = window.Summary;
            item.DueAt ??= targetStart.Date.Add(window.End.ToTimeSpan());

            if (ShouldSplit(item, window))
            {
                item.Children = SplitItem(item, window);
            }
        }
    }

    private static bool ShouldSplit(PlanningItem item, PlanningTimeWindow window)
        => window.DurationMinutes >= 50 &&
           item.Children.Count == 0 &&
           item.Title.Length >= 6;

    private static List<PlanningItem> SplitItem(PlanningItem parent, PlanningTimeWindow window)
    {
        var midpoint = window.Start.AddMinutes(Math.Max(20, window.DurationMinutes / 2));
        return
        [
            new PlanningItem
            {
                Kind = parent.Kind,
                TaskId = parent.TaskId,
                Title = $"准备：{parent.Title}",
                Priority = parent.Priority,
                TimeBlock = $"{window.Start:HH\\:mm}-{midpoint:HH\\:mm}",
                Reason = "从父规划项拆出的准备步骤，确认前不修改正式任务。",
                Tags = parent.Tags.Select(tag => new Tag { Name = tag.Name, Color = tag.Color }).ToList()
            },
            new PlanningItem
            {
                Kind = parent.Kind,
                TaskId = parent.TaskId,
                Title = $"推进：{parent.Title}",
                Priority = parent.Priority,
                TimeBlock = $"{midpoint:HH\\:mm}-{window.End:HH\\:mm}",
                Reason = "从父规划项拆出的执行步骤，保留在父项下展示。",
                Tags = parent.Tags.Select(tag => new Tag { Name = tag.Name, Color = tag.Color }).ToList()
            }
        ];
    }
}
