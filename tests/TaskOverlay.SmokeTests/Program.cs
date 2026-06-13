using TaskOverlay.Core.Models;
using TaskOverlay.Core.Services;
using TaskOverlay.Infrastructure.Storage;
using System.Text.Json;
using TaskOverlay.App.Services;

var testDir = Path.Combine(Path.GetTempPath(), "TaskOverlay.SmokeTests", Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(testDir);

using (var primaryInstance = new SingleInstanceService())
{
    if (!primaryInstance.IsPrimaryInstance)
    {
        throw new InvalidOperationException("Expected the first single-instance lock owner to become primary.");
    }

    using var duplicateInstance = new SingleInstanceService();
    if (duplicateInstance.IsPrimaryInstance)
    {
        throw new InvalidOperationException("Duplicate app instance unexpectedly acquired the single-instance lock.");
    }
}

using (var reacquiredInstance = new SingleInstanceService())
{
    if (!reacquiredInstance.IsPrimaryInstance)
    {
        throw new InvalidOperationException("Single-instance lock could not be reacquired after disposal.");
    }
}

var localSettingsDir = Path.Combine(testDir, "settings");
var localSettings = new LocalSettingsStore(localSettingsDir);
localSettings.Load();
localSettings.Save(new AppSettings { Hotkey = "Ctrl+Shift+Y", OverlayOpacity = 0.1 });
localSettings.Save(new AppSettings { Hotkey = "Alt+Shift+Y" });
File.WriteAllText(localSettings.SettingsFilePath, "{ invalid settings");
var recoveredSettings = new LocalSettingsStore(localSettingsDir).Load();
if (recoveredSettings.Hotkey != "Ctrl+Shift+Y" || recoveredSettings.OverlayOpacity != 0.25)
{
    throw new InvalidOperationException("Local settings were not recovered and normalized from the backup.");
}

File.WriteAllText(localSettings.SettingsFilePath, "{ invalid primary settings");
File.WriteAllText(Path.Combine(localSettingsDir, "settings.bak.json"), "{ invalid backup settings");
var defaultSettings = new LocalSettingsStore(localSettingsDir).Load();
if (defaultSettings.Hotkey != AppSettings.DefaultHotkey || defaultSettings.StorageBackend != TaskStorageBackend.Json)
{
    throw new InvalidOperationException("Invalid local settings did not fall back to safe defaults.");
}

using (JsonDocument.Parse(File.ReadAllText(localSettings.SettingsFilePath)))
{
}

var settings = new AppSettings();
if (settings.StorageBackend != TaskStorageBackend.Json)
{
    throw new InvalidOperationException("Local JSON must be the default storage backend.");
}

var repository = new JsonTaskRepository(() => settings, testDir);
var tasks = new TaskApplicationService(repository);

await repository.InitializeAsync();
if ((await tasks.GetTasksAsync(TaskFilter.All)).Count != 0)
{
    throw new InvalidOperationException("New JSON repository unexpectedly created sample tasks.");
}

var proposalStore = new ExternalTaskProposalStore(testDir);
var goalRepository = new JsonGoalRepository(testDir);
await goalRepository.InitializeAsync();
var goals = new GoalApplicationService(goalRepository);
var savedGoal = await goals.SaveGoalAsync(new Goal
{
    Title = "AI Agent 工程能力",
    Description = "长期提升工程化能力",
    Priority = TaskPriority.High,
    TimeHorizon = GoalTimeHorizon.LongTerm,
    DailyBudgetMinutes = 90,
    Tags = [new Tag { Name = "AI" }],
    Milestones =
    [
        new GoalMilestone
        {
            Title = "完成 TaskOverlay 明日规划 V2",
            TargetDate = DateOnly.FromDateTime(DateTime.Today.AddDays(7))
        }
    ]
});
if (savedGoal.Id == 0 || savedGoal.Milestones.Count != 1 || savedGoal.Milestones[0].GoalId != savedGoal.Id)
{
    throw new InvalidOperationException("Goal library did not persist nested milestones.");
}

var proposed = await proposalStore.AddAsync(new ExternalTaskProposal
{
    Title = "AI 提案确认测试",
    Notes = "来自冒烟测试",
    Priority = TaskPriority.High,
    GoalId = savedGoal.Id,
    GoalTitle = savedGoal.Title,
    Tags = [new Tag { Name = "AI" }]
});
if ((await proposalStore.GetAllAsync()).Single().Id != proposed.Id)
{
    throw new InvalidOperationException("External task proposal was not persisted.");
}

var confirmedProposal = await proposalStore.ConfirmAsync(proposed.Id, tasks, goals);
if (confirmedProposal is null ||
    (await tasks.GetTasksAsync(TaskFilter.All)).All(t => t.Id != confirmedProposal.Id) ||
    (await proposalStore.GetAllAsync()).Count != 0)
{
    throw new InvalidOperationException("Confirmed proposal was not converted into a task.");
}
var linkedGoal = await goals.GetGoalAsync(savedGoal.Id);
if (linkedGoal is null ||
    linkedGoal.TaskLinks.All(link => link.TaskId != confirmedProposal.Id || link.ProposalId != proposed.Id))
{
    throw new InvalidOperationException("Confirmed goal proposal was not linked back to the goal.");
}

var rejectedProposal = await proposalStore.AddAsync(new ExternalTaskProposal { Title = "AI 提案拒绝测试" });
if (!await proposalStore.RejectAsync(rejectedProposal.Id) || (await proposalStore.GetAllAsync()).Count != 0)
{
    throw new InvalidOperationException("Rejected proposal was not removed.");
}

await tasks.AddTaskAsync("添加任务冒烟测试", DateTime.Today.AddHours(20));
var undated = await tasks.AddTaskAsync("无截止时间冒烟测试");
var daily = await tasks.AddTaskAsync("每日任务冒烟测试", DateTime.Today, isDaily: true);

var today = await tasks.GetTasksAsync(TaskFilter.Today);
if (today.Count < 3)
{
    throw new InvalidOperationException($"Expected at least 3 tasks, found {today.Count}.");
}

if (today.All(t => t.Id != undated.Id))
{
    throw new InvalidOperationException("Undated task was not returned by today's active filter.");
}

var first = today.Single(t => t.Title == "添加任务冒烟测试");
await tasks.SetCompletedAsync(first.Id, true);
var completed = await tasks.GetTasksAsync(TaskFilter.Completed);
if (completed.All(t => t.Id != first.Id))
{
    throw new InvalidOperationException("Completed task was not returned by the completed filter.");
}

await tasks.SetOccurrenceCompletedAsync(daily.Id, DateOnly.FromDateTime(DateTime.Today), true);
today = await tasks.GetTasksAsync(TaskFilter.Today);
if (today.Any(t => t.Id == daily.Id))
{
    throw new InvalidOperationException("Completed daily task was still returned by today's active filter.");
}

completed = await tasks.GetTasksAsync(TaskFilter.Completed);
if (completed.All(t => t.Id != daily.Id))
{
    throw new InvalidOperationException("Completed daily task was not returned by the completed filter.");
}

var tomorrow = await tasks.GetTasksForDateAsync(DateOnly.FromDateTime(DateTime.Today.AddDays(1)));
if (tomorrow.Single(t => t.Id == daily.Id).IsCompleted)
{
    throw new InvalidOperationException("Daily task did not reset on the next day.");
}

await tasks.SetOccurrenceCompletedAsync(daily.Id, DateOnly.FromDateTime(DateTime.Today), false);
today = await tasks.GetTasksAsync(TaskFilter.Today);
if (today.All(t => t.Id != daily.Id))
{
    throw new InvalidOperationException("Restored daily task was not returned by today's active filter.");
}

var reminder = await repository.SaveTaskAsync(new TaskItem
{
    Title = "提醒冒烟测试",
    ReminderAt = DateTime.Now.AddMinutes(-1)
});
var dueReminders = await repository.GetDueRemindersAsync(DateTime.Now);
if (dueReminders.All(t => t.Id != reminder.Id))
{
    throw new InvalidOperationException("Due reminder was not returned before delivery.");
}

await tasks.MarkReminderDeliveredAsync(reminder.Id);
dueReminders = await repository.GetDueRemindersAsync(DateTime.Now);
if (dueReminders.Any(t => t.Id == reminder.Id))
{
    throw new InvalidOperationException("Delivered reminder was returned again.");
}

var recurringReminderAt = DateTime.Now.AddMinutes(-1);
var recurringReminder = await repository.SaveTaskAsync(new TaskItem
{
    Title = "重复提醒冒烟测试",
    DueAt = recurringReminderAt.AddHours(1),
    ReminderAt = recurringReminderAt,
    Recurrence = new RecurrenceRule
    {
        Kind = RecurrenceKind.Daily,
        Interval = 1
    }
});
await tasks.MarkReminderDeliveredAsync(recurringReminder.Id);
var rescheduledReminder = (await tasks.GetTasksAsync(TaskFilter.All)).Single(t => t.Id == recurringReminder.Id);
if (rescheduledReminder.ReminderOffsetMinutes != -60 ||
    rescheduledReminder.ReminderAt is null ||
    Math.Abs((rescheduledReminder.ReminderAt.Value - recurringReminderAt.AddDays(1)).TotalSeconds) > 1)
{
    throw new InvalidOperationException("Recurring reminder was not rescheduled with its original offset.");
}

var secondCycle = TaskReminderRules.GetNextReminderAt(rescheduledReminder, rescheduledReminder.ReminderAt.Value.AddSeconds(1));
if (secondCycle is null ||
    Math.Abs((secondCycle.Value - recurringReminderAt.AddDays(2)).TotalSeconds) > 1)
{
    throw new InvalidOperationException("Recurring reminder did not retain its offset for the second cycle.");
}

var staleReminderAt = DateTime.Now.AddDays(-3).AddMinutes(-1);
var staleReminder = await repository.SaveTaskAsync(new TaskItem
{
    Title = "过期重复提醒冒烟测试",
    DueAt = staleReminderAt.AddHours(1),
    ReminderAt = staleReminderAt,
    Recurrence = new RecurrenceRule
    {
        Kind = RecurrenceKind.Daily,
        Interval = 1
    }
});
await tasks.MarkReminderDeliveredAsync(staleReminder.Id);
var skippedReminder = (await tasks.GetTasksAsync(TaskFilter.All)).Single(t => t.Id == staleReminder.Id).ReminderAt;
if (skippedReminder is null || skippedReminder <= DateTime.Now || skippedReminder > DateTime.Now.AddDays(2))
{
    throw new InvalidOperationException("Stale recurring reminder did not skip directly to a future cycle.");
}

var monthlyReminderTask = new TaskItem
{
    DueAt = new DateTime(2026, 1, 15, 10, 0, 0),
    ReminderAt = new DateTime(2026, 1, 15, 9, 0, 0),
    Recurrence = new RecurrenceRule
    {
        Kind = RecurrenceKind.Monthly,
        Interval = 1,
        DayOfMonth = 15
    }
};
TaskReminderRules.NormalizeOffset(monthlyReminderTask);
var nextMonthlyReminder = TaskReminderRules.GetNextReminderAt(monthlyReminderTask, new DateTime(2026, 1, 15, 9, 1, 0));
if (nextMonthlyReminder != new DateTime(2026, 2, 15, 9, 0, 0))
{
    throw new InvalidOperationException("Monthly recurring reminder was not scheduled for the next month.");
}

var afterDueReminderTask = new TaskItem
{
    DueAt = new DateTime(2026, 1, 15, 10, 0, 0),
    ReminderAt = new DateTime(2026, 1, 15, 11, 0, 0),
    Recurrence = new RecurrenceRule
    {
        Kind = RecurrenceKind.Monthly,
        Interval = 1,
        DayOfMonth = 15
    }
};
TaskReminderRules.NormalizeOffset(afterDueReminderTask);
var currentCycleAfterDueReminder = TaskReminderRules.GetNextReminderAt(afterDueReminderTask, new DateTime(2026, 1, 15, 10, 30, 0));
if (currentCycleAfterDueReminder != new DateTime(2026, 1, 15, 11, 0, 0))
{
    throw new InvalidOperationException("Reminder after due time skipped the current recurring cycle.");
}

await repository.SaveTaskAsync(new TaskItem
{
    Title = "搜索标题验证",
    Notes = "search-notes-token"
});
var titleMatches = await tasks.GetTasksAsync(TaskFilter.All, "标题验证");
if (titleMatches.Count != 1 || titleMatches[0].Title != "搜索标题验证")
{
    throw new InvalidOperationException("Title search did not return the expected task.");
}

var noteMatches = await tasks.GetTasksAsync(TaskFilter.All, "search-notes-token");
if (noteMatches.Count != 1 || noteMatches[0].Title != "搜索标题验证")
{
    throw new InvalidOperationException("Notes search did not return the expected task.");
}

var detailsDueAt = DateTime.Today.AddDays(2).AddHours(15).AddMinutes(30);
var detailsReminderAt = detailsDueAt.AddHours(-1);
var details = await tasks.SaveTaskAsync(new TaskItem
{
    Title = "  详情字段验证  ",
    Notes = "initial notes",
    Priority = TaskPriority.Urgent,
    DueAt = detailsDueAt,
    ReminderAt = detailsReminderAt
});
var savedDetails = (await tasks.GetTasksAsync(TaskFilter.All)).Single(t => t.Id == details.Id);
if (savedDetails.Title != "详情字段验证" ||
    savedDetails.Notes != "initial notes" ||
    savedDetails.Priority != TaskPriority.Urgent ||
    savedDetails.DueAt != detailsDueAt ||
    savedDetails.ReminderAt != detailsReminderAt)
{
    throw new InvalidOperationException("Task detail fields were not persisted on create.");
}

savedDetails.Notes = "updated notes";
savedDetails.Priority = TaskPriority.Low;
savedDetails.DueAt = detailsDueAt.AddDays(1);
savedDetails.ReminderAt = null;
var changeEvents = 0;
tasks.TasksChanged += (_, _) => changeEvents++;
await tasks.SaveTaskAsync(savedDetails);
if (changeEvents != 1)
{
    throw new InvalidOperationException($"Expected one task change event after update, found {changeEvents}.");
}

savedDetails = (await tasks.GetTasksAsync(TaskFilter.All)).Single(t => t.Id == details.Id);
if (savedDetails.Notes != "updated notes" ||
    savedDetails.Priority != TaskPriority.Low ||
    savedDetails.DueAt != detailsDueAt.AddDays(1) ||
    savedDetails.ReminderAt is not null)
{
    throw new InvalidOperationException("Task detail fields were not persisted on update.");
}

var weekly = await tasks.SaveTaskAsync(new TaskItem
{
    Title = "每周任务验证",
    DueAt = DateTime.Today.AddHours(9),
    Recurrence = new RecurrenceRule
    {
        Kind = RecurrenceKind.Weekly,
        Interval = 1,
        DayOfWeek = DateTime.Today.DayOfWeek
    }
});
today = await tasks.GetTasksAsync(TaskFilter.Today);
if (today.All(t => t.Id != weekly.Id))
{
    throw new InvalidOperationException("Weekly task was not returned on its occurrence date.");
}

await tasks.SetOccurrenceCompletedAsync(weekly.Id, DateOnly.FromDateTime(DateTime.Today), true);
today = await tasks.GetTasksAsync(TaskFilter.Today);
if (today.Any(t => t.Id == weekly.Id))
{
    throw new InvalidOperationException("Completed weekly occurrence was still returned by today's active filter.");
}

var nextWeek = await tasks.GetTasksForDateAsync(DateOnly.FromDateTime(DateTime.Today.AddDays(7)));
if (nextWeek.Single(t => t.Id == weekly.Id).IsCompleted)
{
    throw new InvalidOperationException("Weekly task did not reset for its next occurrence.");
}

var monthlyAnchor = new DateTime(2026, 1, 15, 10, 0, 0);
var monthly = await tasks.SaveTaskAsync(new TaskItem
{
    Title = "每月任务验证",
    DueAt = monthlyAnchor,
    Recurrence = new RecurrenceRule
    {
        Kind = RecurrenceKind.Monthly,
        Interval = 1,
        DayOfMonth = 15
    }
});
var february = await tasks.GetTasksForDateAsync(new DateOnly(2026, 2, 15));
if (february.All(t => t.Id != monthly.Id))
{
    throw new InvalidOperationException("Monthly task was not returned on its configured day.");
}

var customAnchor = new DateTime(2026, 1, 1, 8, 0, 0);
var custom = await tasks.SaveTaskAsync(new TaskItem
{
    Title = "间隔天数验证",
    DueAt = customAnchor,
    Recurrence = new RecurrenceRule
    {
        Kind = RecurrenceKind.CustomDays,
        Interval = 3
    }
});
var customMatch = await tasks.GetTasksForDateAsync(new DateOnly(2026, 1, 4));
var customMiss = await tasks.GetTasksForDateAsync(new DateOnly(2026, 1, 3));
if (customMatch.All(t => t.Id != custom.Id) || customMiss.Any(t => t.Id == custom.Id))
{
    throw new InvalidOperationException("Custom day interval recurrence did not match expected dates.");
}

var everyTwoDays = await tasks.SaveTaskAsync(new TaskItem
{
    Title = "每两天验证",
    DueAt = customAnchor,
    Recurrence = new RecurrenceRule
    {
        Kind = RecurrenceKind.Daily,
        Interval = 2
    }
});
var persistedRecurrence = (await tasks.GetTasksAsync(TaskFilter.All)).Single(t => t.Id == everyTwoDays.Id).Recurrence;
if (persistedRecurrence?.Kind != RecurrenceKind.Daily || persistedRecurrence.Interval != 2)
{
    throw new InvalidOperationException("Recurring task rule was not persisted.");
}

var tagged = await tasks.SaveTaskAsync(new TaskItem
{
    Title = "标签验证",
    Tags =
    [
        new Tag { Name = " 工作 " },
        new Tag { Name = "work" },
        new Tag { Name = "工作" },
        new Tag { Name = "紧急" }
    ]
});
var savedTags = (await tasks.GetTasksAsync(TaskFilter.All)).Single(t => t.Id == tagged.Id).Tags;
if (savedTags.Count != 3 ||
    savedTags.All(t => t.Name != "工作") ||
    savedTags.All(t => t.Name != "紧急") ||
    savedTags.All(t => t.Name != "work"))
{
    throw new InvalidOperationException("Task tags were not normalized and persisted.");
}

var tagMatches = await tasks.GetTasksAsync(TaskFilter.All, "紧急");
if (tagMatches.All(t => t.Id != tagged.Id))
{
    throw new InvalidOperationException("Tag search did not return the expected task.");
}

var dataPath = Path.Combine(testDir, "tasks.json");
File.WriteAllText(dataPath, "{ invalid json");
var recoveredTasks = await tasks.GetTasksAsync(TaskFilter.All);
if (recoveredTasks.All(t => t.Id != undated.Id))
{
    throw new InvalidOperationException("Repository did not recover tasks from the JSON backup.");
}

await tasks.AddTaskAsync("备份恢复后保存验证");
using (JsonDocument.Parse(File.ReadAllText(dataPath)))
{
}

var transferRepository = (ITaskDataTransferRepository)repository;
if (transferRepository.DataFilePath != dataPath)
{
    throw new InvalidOperationException("Data transfer repository did not expose the expected JSON path.");
}

var manualExportPath = Path.Combine(testDir, "manual-export.json");
await transferRepository.ExportAsync(manualExportPath);
var exportedCount = (await tasks.GetTasksAsync(TaskFilter.All)).Count;
var addedAfterExport = await tasks.AddTaskAsync("导入替换验证");
if ((await tasks.GetTasksAsync(TaskFilter.All)).All(t => t.Id != addedAfterExport.Id))
{
    throw new InvalidOperationException("Task added after export was not persisted.");
}

await transferRepository.ImportAsync(manualExportPath);
var importedTasks = await tasks.GetTasksAsync(TaskFilter.All);
if (importedTasks.Count != exportedCount || importedTasks.Any(t => t.Id == addedAfterExport.Id))
{
    throw new InvalidOperationException("Manual import did not replace current JSON data with the exported snapshot.");
}

var externalChangeEvents = 0;
tasks.TasksChanged += (_, _) => externalChangeEvents++;
tasks.NotifyTasksChanged();
if (externalChangeEvents != 1)
{
    throw new InvalidOperationException("External task change notification was not broadcast.");
}

var invalidImportPath = Path.Combine(testDir, "invalid-import.json");
File.WriteAllText(invalidImportPath, "{ invalid import");
try
{
    await transferRepository.ImportAsync(invalidImportPath);
    throw new InvalidOperationException("Invalid JSON import was accepted.");
}
catch (JsonException)
{
}

if ((await tasks.GetTasksAsync(TaskFilter.All)).Count != exportedCount)
{
    throw new InvalidOperationException("Rejected import changed current JSON data.");
}

var planning = new LocalPlanningService(tasks, goals);
var taskListPlan = await planning.BuildTomorrowPlanAsync(new PlanningRequest
{
    Mode = PlanningMode.TaskList,
    GoalSummary = "推进 TaskOverlay AI Planning Assistant",
    MaxItems = 20
});
if (taskListPlan.Mode != PlanningMode.TaskList ||
    taskListPlan.TargetDate != DateOnly.FromDateTime(DateTime.Today.AddDays(1)) ||
    taskListPlan.Items.Count == 0 ||
    taskListPlan.Items.All(item => !item.Title.Contains("AI Agent 工程能力", StringComparison.Ordinal)))
{
    throw new InvalidOperationException("Task-list planning did not use active goals.");
}
var goalPlanningItem = taskListPlan.Items.First(item => item.Title.Contains("AI Agent 工程能力", StringComparison.Ordinal));
if (goalPlanningItem.GoalId != savedGoal.Id || goalPlanningItem.GoalTitle != savedGoal.Title)
{
    throw new InvalidOperationException("Goal-derived planning item did not preserve its goal source.");
}

var timeBlockPlan = await planning.BuildTomorrowPlanAsync(new PlanningRequest
{
    Mode = PlanningMode.TimeBlock,
    TimeWindows = [new PlanningTimeWindow { Start = new TimeOnly(8, 0), End = new TimeOnly(9, 0) }],
    MaxItems = 4
});
if (timeBlockPlan.Items.Count == 0 ||
    timeBlockPlan.Items[0].TimeBlock != "08:00-09:00" ||
    timeBlockPlan.Items[0].Children.Count == 0)
{
    throw new InvalidOperationException("Time-block planning did not preserve hierarchy under the parent planning item.");
}

Console.WriteLine($"PASS: JSON tasks, proposals, planning, completion, reminders, search, details, events, recurrence, tags, backup recovery, transfer, settings recovery and single-instance locking. Count={today.Count}");
