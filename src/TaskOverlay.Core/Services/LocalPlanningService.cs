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

        var review = new PlanningReview
        {
            Mode = request.Mode,
            TargetDate = targetDate,
            Summary = BuildSummary(request, today.Count, tomorrow.Count, overdue.Count, 0)
        };

        var selected = SelectCandidateTasks(today, tomorrow, overdue, Math.Max(request.MaxItems * 2, request.MaxItems));
        var planningItems = new List<PlanningItem>();
        planningItems.AddRange(BuildExistingTaskItems(selected, targetDefaultDue));
        var selectedTaskIds = selected.Select(task => task.Id).ToHashSet();
        planningItems.AddRange(BuildCarryOverProposalItems(
            today,
            tomorrow,
            selectedTaskIds,
            targetDefaultDue,
            Math.Max(request.MaxItems, 1)));
        planningItems.AddRange(BuildGoalProposalItems(activeGoals, targetDefaultDue, Math.Max(request.MaxItems, 1)));

        if (!string.IsNullOrWhiteSpace(request.GoalSummary) &&
            !HasEquivalentTemplate(planningItems, request.GoalSummary))
        {
            planningItems.Add(BuildGoalSummaryItem(request.GoalSummary, targetDefaultDue));
        }

        foreach (var item in planningItems
                     .OrderByDescending(ScorePlanningItem)
                     .ThenBy(item => item.DueAt ?? DateTime.MaxValue)
                     .ThenBy(item => item.Title)
                     .Take(Math.Max(1, request.MaxItems)))
        {
            review.Items.Add(item);
        }

        review.Summary = BuildSummary(request, today.Count, tomorrow.Count, overdue.Count, review.Items.Count);

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
        IReadOnlySet<long> selectedTaskIds,
        DateTime targetDefaultDue,
        int remainingSlots)
    {
        if (remainingSlots <= 0)
        {
            yield break;
        }

        var tomorrowIds = tomorrow.Select(task => task.Id).ToHashSet();
        foreach (var task in today
                     .Where(task => !task.IsCompleted &&
                                    !tomorrowIds.Contains(task.Id) &&
                                    !selectedTaskIds.Contains(task.Id))
                     .Take(remainingSlots))
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
            var title = BuildGoalPlanningTitle(goal, milestone);
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

            var item = new PlanningItem
            {
                Kind = PlanningItemKind.ProposedTask,
                GoalId = goal.Id,
                GoalTitle = goal.Title,
                Title = title,
                Notes = notes.Count == 0 ? null : string.Join(Environment.NewLine, notes),
                Priority = goal.Priority,
                DueAt = targetDefaultDue,
                Reason = "来自进行中的长期目标库。",
                Tags = goal.Tags.Select(tag => new Tag { Name = tag.Name, Color = tag.Color }).ToList()
            };
            item.Children = BuildTemplateChildren(item, BuildTemplateSource(goal, milestone));
            yield return item;
        }
    }

    private static PlanningItem BuildGoalSummaryItem(string goalSummary, DateTime targetDefaultDue)
    {
        var item = new PlanningItem
        {
            Kind = PlanningItemKind.ProposedTask,
            Title = BuildTemplateTitle(goalSummary) ?? "推进长期目标",
            Notes = goalSummary.Trim(),
            Priority = InferPriority(goalSummary),
            DueAt = targetDefaultDue,
            Reason = "来自本次规划输入的目标摘要。",
            Tags = [new Tag { Name = "目标" }, new Tag { Name = "规划" }]
        };
        item.Children = BuildTemplateChildren(item, goalSummary);
        return item;
    }

    private static string BuildGoalPlanningTitle(Goal goal, GoalMilestone? milestone)
    {
        var source = BuildTemplateSource(goal, milestone);
        if (BuildTemplateTitle(source) is { } templateTitle)
        {
            return templateTitle;
        }

        return milestone is null
            ? $"推进目标：{goal.Title}"
            : $"推进目标：{goal.Title} / {milestone.Title}";
    }

    private static string BuildTemplateSource(Goal goal, GoalMilestone? milestone)
        => string.Join(Environment.NewLine, new[]
        {
            goal.Title,
            goal.Description,
            milestone?.Title
        }.Where(text => !string.IsNullOrWhiteSpace(text)));

    private static string? BuildTemplateTitle(string source)
    {
        var normalized = source.ToLowerInvariant();
        if (normalized.Contains("ccd", StringComparison.OrdinalIgnoreCase) ||
            normalized.Contains("相机", StringComparison.Ordinal) ||
            normalized.Contains("效果图", StringComparison.Ordinal))
        {
            return "完成 CCD 效果图拍摄与相机效果检查";
        }

        if ((normalized.Contains("游戏", StringComparison.Ordinal) && normalized.Contains("脚本", StringComparison.Ordinal)) ||
            normalized.Contains("人手操控", StringComparison.Ordinal) ||
            normalized.Contains("自动化", StringComparison.Ordinal))
        {
            return "推进游戏脚本自动化：定义并验证人手操控模拟 MVP";
        }

        return null;
    }

    private static bool HasEquivalentTemplate(IEnumerable<PlanningItem> existingItems, string source)
    {
        var title = BuildTemplateTitle(source);
        return title is not null &&
               existingItems.Any(item => string.Equals(item.Title, title, StringComparison.OrdinalIgnoreCase));
    }

    private static TaskPriority InferPriority(string source)
    {
        var normalized = source.ToLowerInvariant();
        if (normalized.Contains("明天", StringComparison.Ordinal) ||
            normalized.Contains("答应", StringComparison.Ordinal) ||
            normalized.Contains("交付", StringComparison.Ordinal) ||
            normalized.Contains("urgent", StringComparison.OrdinalIgnoreCase))
        {
            return TaskPriority.Urgent;
        }

        return TaskPriority.High;
    }

    private static List<PlanningItem> BuildTemplateChildren(PlanningItem parent, string source)
    {
        var normalized = source.ToLowerInvariant();
        if (normalized.Contains("ccd", StringComparison.OrdinalIgnoreCase) ||
            normalized.Contains("相机", StringComparison.Ordinal) ||
            normalized.Contains("效果图", StringComparison.Ordinal))
        {
            return BuildChildren(parent,
            [
                "确认同学要看的相机效果点和交付格式",
                "准备 CCD、镜头、连接线、电源、存储和拍摄环境",
                "拍摄样张并记录光线、距离、参数和异常",
                "检查清晰度、噪点、色彩、暗角和坏点",
                "导出效果图并把结论反馈给同学"
            ], "CCD 拍摄检查模板。");
        }

        if ((normalized.Contains("游戏", StringComparison.Ordinal) && normalized.Contains("脚本", StringComparison.Ordinal)) ||
            normalized.Contains("人手操控", StringComparison.Ordinal) ||
            normalized.Contains("自动化", StringComparison.Ordinal))
        {
            return BuildChildren(parent,
            [
                "限定明天只做一个可验证场景，写清输入、退出和失败条件",
                "搭建输入动作序列原型：移动、点击、按键和等待",
                "加入人手化节奏：随机延迟、微小偏移和动作间隔",
                "记录运行日志和截图，验证是否稳定复现",
                "列出下一步：视觉识别、状态判断和安全停止"
            ], "游戏脚本自动化模板；只做合规自用原型，不绕过反作弊。");
        }

        return [];
    }

    private static List<PlanningItem> BuildChildren(PlanningItem parent, IReadOnlyList<string> titles, string reason)
        => titles
            .Select(title => new PlanningItem
            {
                Kind = parent.Kind,
                TaskId = parent.TaskId,
                GoalId = parent.GoalId,
                GoalTitle = parent.GoalTitle,
                Title = title,
                Priority = parent.Priority,
                Reason = reason,
                Tags = parent.Tags.Select(tag => new Tag { Name = tag.Name, Color = tag.Color }).ToList()
            })
            .ToList();

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

    private static int ScorePlanningItem(PlanningItem item)
    {
        var score = item.Priority switch
        {
            TaskPriority.Urgent => 400,
            TaskPriority.High => 300,
            TaskPriority.Normal => 200,
            TaskPriority.Low => 100,
            _ => 0
        };

        if (item.GoalId is not null)
        {
            score += 80;
        }

        if (item.Kind == PlanningItemKind.ProposedTask)
        {
            score += 30;
        }

        if (item.Children.Count > 0)
        {
            score += 20;
        }

        if (item.DueAt is not null && item.DueAt.Value < DateTime.Now)
        {
            score -= Math.Min(80, Math.Max(0, (DateTime.Now.Date - item.DueAt.Value.Date).Days));
        }

        return score;
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

            if (item.Children.Count > 0)
            {
                ApplyChildTimeBlocks(item, window);
            }
        }
    }

    private static bool ShouldSplit(PlanningItem item, PlanningTimeWindow window)
        => window.DurationMinutes >= 50 &&
           item.Children.Count == 0 &&
           item.Title.Length >= 6;

    private static void ApplyChildTimeBlocks(PlanningItem parent, PlanningTimeWindow window)
    {
        if (parent.Children.Count == 0)
        {
            return;
        }

        var slice = Math.Max(10, window.DurationMinutes / parent.Children.Count);
        var current = window.Start;
        for (var index = 0; index < parent.Children.Count; index++)
        {
            var child = parent.Children[index];
            var end = index == parent.Children.Count - 1
                ? window.End
                : current.AddMinutes(slice);
            child.TimeBlock = $"{current:HH\\:mm}-{end:HH\\:mm}";
            current = end;
        }
    }

    private static List<PlanningItem> SplitItem(PlanningItem parent, PlanningTimeWindow window)
    {
        var midpoint = window.Start.AddMinutes(Math.Max(20, window.DurationMinutes / 2));
        return
        [
            new PlanningItem
            {
                Kind = parent.Kind,
                TaskId = parent.TaskId,
                GoalId = parent.GoalId,
                GoalTitle = parent.GoalTitle,
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
                GoalId = parent.GoalId,
                GoalTitle = parent.GoalTitle,
                Title = $"推进：{parent.Title}",
                Priority = parent.Priority,
                TimeBlock = $"{midpoint:HH\\:mm}-{window.End:HH\\:mm}",
                Reason = "从父规划项拆出的执行步骤，保留在父项下展示。",
                Tags = parent.Tags.Select(tag => new Tag { Name = tag.Name, Color = tag.Color }).ToList()
            }
        ];
    }
}
