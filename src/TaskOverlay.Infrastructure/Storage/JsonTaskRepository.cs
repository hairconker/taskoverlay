using System.IO;
using System.Text.Json;
using TaskOverlay.Core.Models;
using TaskOverlay.Core.Services;

namespace TaskOverlay.Infrastructure.Storage;

public sealed class JsonTaskRepository(Func<AppSettings> settingsProvider, string? dataDirectory = null) : ITaskRepository, ITaskDataTransferRepository
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private readonly bool _usesDefaultDataDirectory = dataDirectory is null;
    private readonly string _dataPath = Path.Combine(dataDirectory ?? ResolveDefaultDataDirectory(), "tasks.json");
    private readonly string _backupPath = Path.Combine(dataDirectory ?? ResolveDefaultDataDirectory(), "tasks.bak.json");
    private readonly string _tempPath = Path.Combine(dataDirectory ?? ResolveDefaultDataDirectory(), "tasks.tmp.json");
    private readonly SemaphoreSlim _gate = new(1, 1);

    public string DataFilePath => _dataPath;

    public Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_dataPath)!);
        if (!File.Exists(_dataPath))
        {
            if (_usesDefaultDataDirectory && TryMigrateLegacyData())
            {
                return Task.CompletedTask;
            }

            return SaveStateAsync(new RepositoryState { Settings = settingsProvider() }, cancellationToken);
        }

        return Task.CompletedTask;
    }

    public async Task<IReadOnlyList<TaskItem>> GetTasksAsync(TaskFilter filter, string? search, CancellationToken cancellationToken = default)
    {
        var state = await LoadStateAsync(cancellationToken);
        var now = DateTime.Now;
        var today = DateOnly.FromDateTime(now);
        ApplyOccurrenceCompletionStatus(state, today);
        var query = state.Tasks.AsEnumerable();

        query = filter switch
        {
            TaskFilter.Today => query.Where(t => !t.IsCompleted && (IsUndatedSingleTask(t) || TaskOccurrenceRules.OccursOn(t, today))),
            TaskFilter.Tomorrow => query.Where(t => !t.IsCompleted && TaskOccurrenceRules.OccursOn(t, today.AddDays(1))),
            TaskFilter.ThisWeek => query.Where(t => !t.IsCompleted && TaskOccurrenceRules.HasActiveOccurrenceBetween(t, today, today.AddDays(7))),
            TaskFilter.Overdue => query.Where(t => !TaskOccurrenceRules.IsRecurring(t) && !t.IsCompleted && t.DueAt is not null && t.DueAt.Value < now),
            TaskFilter.Completed => query.Where(t => t.IsCompleted),
            _ => query
        };

        if (!string.IsNullOrWhiteSpace(search))
        {
            query = query.Where(t => t.Title.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                                     (t.Notes?.Contains(search, StringComparison.OrdinalIgnoreCase) ?? false) ||
                                     t.Tags.Any(tag => tag.Name.Contains(search, StringComparison.OrdinalIgnoreCase)));
        }

        return query.OrderBy(t => t.IsCompleted)
            .ThenBy(t => t.SortOrder)
            .ThenBy(t => t.DueAt ?? DateTime.MaxValue)
            .ThenByDescending(t => t.CreatedAt)
            .ToList();
    }

    public async Task<IReadOnlyList<TaskItem>> GetTasksForDateAsync(DateOnly date, CancellationToken cancellationToken = default)
    {
        var state = await LoadStateAsync(cancellationToken);
        ApplyOccurrenceCompletionStatus(state, date);
        return state.Tasks
            .Where(t => TaskOccurrenceRules.OccursOn(t, date))
            .OrderBy(t => t.IsCompleted)
            .ThenBy(t => t.SortOrder)
            .ThenBy(t => t.DueAt ?? DateTime.MaxValue)
            .ToList();
    }

    public async Task<IReadOnlyList<TaskItem>> GetDueRemindersAsync(DateTime now, CancellationToken cancellationToken = default)
    {
        var state = await LoadStateAsync(cancellationToken);
        ApplyOccurrenceCompletionStatus(state, DateOnly.FromDateTime(now));
        return state.Tasks
            .Where(t => !t.IsCompleted && t.ReminderAt is not null && t.ReminderAt.Value <= now)
            .OrderBy(t => t.ReminderAt)
            .ToList();
    }

    public async Task MarkReminderDeliveredAsync(long taskId, CancellationToken cancellationToken = default)
    {
        var state = await LoadStateAsync(cancellationToken);
        var task = state.Tasks.FirstOrDefault(t => t.Id == taskId);
        if (task is null || task.ReminderAt is null)
        {
            return;
        }

        TaskReminderRules.NormalizeOffset(task);
        task.ReminderAt = TaskReminderRules.GetNextReminderAt(task, DateTime.Now);
        task.UpdatedAt = DateTime.Now;
        await SaveStateAsync(state, cancellationToken);
    }

    public async Task<TaskItem> SaveTaskAsync(TaskItem task, CancellationToken cancellationToken = default)
    {
        var state = await LoadStateAsync(cancellationToken);
        task.Tags = TaskTagRules.Normalize(task.Tags);
        TaskReminderRules.NormalizeOffset(task);
        if (TaskOccurrenceRules.IsRecurring(task))
        {
            task.IsCompleted = false;
            task.CompletedAt = null;
        }

        if (task.Id == 0)
        {
            task.Id = state.NextTaskId++;
            task.CreatedAt = DateTime.Now;
            state.Tasks.Add(task);
        }
        else
        {
            var index = state.Tasks.FindIndex(t => t.Id == task.Id);
            if (index >= 0)
            {
                state.Tasks[index] = task;
            }
            else
            {
                state.Tasks.Add(task);
            }
        }

        if (task.Recurrence is not null)
        {
            task.Recurrence.TaskId = task.Id;
        }

        task.UpdatedAt = DateTime.Now;
        await SaveStateAsync(state, cancellationToken);
        return task;
    }

    public async Task DeleteTaskAsync(long taskId, CancellationToken cancellationToken = default)
    {
        var state = await LoadStateAsync(cancellationToken);
        state.Tasks.RemoveAll(t => t.Id == taskId);
        state.DailyCompletions.RemoveAll(c => c.TaskId == taskId);
        await SaveStateAsync(state, cancellationToken);
    }

    public async Task SetCompletedAsync(long taskId, bool completed, CancellationToken cancellationToken = default)
    {
        var state = await LoadStateAsync(cancellationToken);
        var task = state.Tasks.FirstOrDefault(t => t.Id == taskId);
        if (task is not null)
        {
            if (TaskOccurrenceRules.IsRecurring(task))
            {
                UpdateOccurrenceCompletion(state, task, DateOnly.FromDateTime(DateTime.Today), completed);
            }
            else
            {
                task.IsCompleted = completed;
                task.CompletedAt = completed ? DateTime.Now : null;
                task.UpdatedAt = DateTime.Now;
            }

            await SaveStateAsync(state, cancellationToken);
        }
    }

    public async Task SetOccurrenceCompletedAsync(long taskId, DateOnly date, bool completed, CancellationToken cancellationToken = default)
    {
        var state = await LoadStateAsync(cancellationToken);
        var task = state.Tasks.FirstOrDefault(t => t.Id == taskId);
        if (task is null || !TaskOccurrenceRules.IsRecurring(task))
        {
            return;
        }

        UpdateOccurrenceCompletion(state, task, date, completed);
        await SaveStateAsync(state, cancellationToken);
    }

    private static void UpdateOccurrenceCompletion(RepositoryState state, TaskItem task, DateOnly date, bool completed)
    {
        task.IsCompleted = false;
        task.CompletedAt = null;
        task.UpdatedAt = DateTime.Now;
        var existing = state.DailyCompletions.FirstOrDefault(c => c.TaskId == task.Id && c.CompletedOn == date);
        if (completed && existing is null)
        {
            state.DailyCompletions.Add(new DailyCompletion
            {
                Id = state.NextCompletionId++,
                TaskId = task.Id,
                CompletedOn = date,
                CompletedAt = DateTime.Now
            });
        }
        else if (!completed && existing is not null)
        {
            state.DailyCompletions.Remove(existing);
        }
    }

    public async Task<AppSettings> GetSettingsAsync(CancellationToken cancellationToken = default)
    {
        var state = await LoadStateAsync(cancellationToken);
        return state.Settings;
    }

    public async Task SaveSettingsAsync(AppSettings settings, CancellationToken cancellationToken = default)
    {
        var state = await LoadStateAsync(cancellationToken);
        state.Settings = settings;
        await SaveStateAsync(state, cancellationToken);
    }

    public async Task ExportAsync(string destinationPath, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(destinationPath))
        {
            throw new ArgumentException("导出路径不能为空。", nameof(destinationPath));
        }

        var state = await LoadStateAsync(cancellationToken);
        var fullPath = Path.GetFullPath(destinationPath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        var json = JsonSerializer.Serialize(state, JsonOptions);
        await File.WriteAllTextAsync(fullPath, json, cancellationToken);
    }

    public async Task ImportAsync(string sourcePath, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(sourcePath))
        {
            throw new ArgumentException("导入路径不能为空。", nameof(sourcePath));
        }

        var state = await ReadStateFileAsync(Path.GetFullPath(sourcePath), cancellationToken);
        ValidateImportedState(state);
        await SaveStateAsync(state, cancellationToken);
    }

    private async Task<RepositoryState> LoadStateAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_dataPath)!);
            if (!File.Exists(_dataPath))
            {
                return new RepositoryState { Settings = settingsProvider() };
            }

            try
            {
                return await ReadStateFileAsync(_dataPath, cancellationToken);
            }
            catch (Exception ex) when ((ex is JsonException or IOException) && File.Exists(_backupPath))
            {
                return await ReadStateFileAsync(_backupPath, cancellationToken);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task SaveStateAsync(RepositoryState state, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_dataPath)!);
            var json = JsonSerializer.Serialize(state, JsonOptions);
            await File.WriteAllTextAsync(_tempPath, json, cancellationToken);
            if (File.Exists(_dataPath))
            {
                File.Replace(_tempPath, _dataPath, _backupPath, ignoreMetadataErrors: true);
            }
            else
            {
                File.Move(_tempPath, _dataPath);
            }
        }
        finally
        {
            if (File.Exists(_tempPath))
            {
                File.Delete(_tempPath);
            }

            _gate.Release();
        }
    }

    private static async Task<RepositoryState> ReadStateFileAsync(string path, CancellationToken cancellationToken)
    {
        var json = await File.ReadAllTextAsync(path, cancellationToken);
        return JsonSerializer.Deserialize<RepositoryState>(json, JsonOptions)
               ?? throw new JsonException($"任务数据文件无效：{path}");
    }

    private static void ValidateImportedState(RepositoryState state)
    {
        if (state.NextTaskId < 1 || state.NextCompletionId < 1)
        {
            throw new InvalidDataException("导入文件中的编号状态无效。");
        }

        if (state.Tasks.Any(t => t.Id <= 0 || string.IsNullOrWhiteSpace(t.Title)) ||
            state.Tasks.GroupBy(t => t.Id).Any(g => g.Count() > 1))
        {
            throw new InvalidDataException("导入文件中存在无效或重复的任务。");
        }

        if (state.DailyCompletions.Any(c => c.Id <= 0 || c.TaskId <= 0) ||
            state.DailyCompletions.GroupBy(c => new { c.TaskId, c.CompletedOn }).Any(g => g.Count() > 1))
        {
            throw new InvalidDataException("导入文件中存在无效或重复的完成记录。");
        }

        var taskIds = state.Tasks.Select(t => t.Id).ToHashSet();
        if (state.DailyCompletions.Any(c => !taskIds.Contains(c.TaskId)))
        {
            throw new InvalidDataException("导入文件中的完成记录引用了不存在的任务。");
        }

        foreach (var task in state.Tasks)
        {
            task.Title = task.Title.Trim();
            task.Tags = TaskTagRules.Normalize(task.Tags);
            TaskReminderRules.NormalizeOffset(task);
        }

        state.NextTaskId = Math.Max(state.NextTaskId, state.Tasks.Select(t => t.Id).DefaultIfEmpty().Max() + 1);
        state.NextCompletionId = Math.Max(state.NextCompletionId, state.DailyCompletions.Select(c => c.Id).DefaultIfEmpty().Max() + 1);
    }

    private static void ApplyOccurrenceCompletionStatus(RepositoryState state, DateOnly date)
    {
        foreach (var task in state.Tasks.Where(TaskOccurrenceRules.IsRecurring))
        {
            var completion = state.DailyCompletions.FirstOrDefault(c => c.TaskId == task.Id && c.CompletedOn == date);
            task.IsCompleted = completion is not null;
            task.CompletedAt = completion?.CompletedAt;
        }
    }

    private static bool IsUndatedSingleTask(TaskItem task)
        => !TaskOccurrenceRules.IsRecurring(task) && task.DueAt is null;

    private bool TryMigrateLegacyData()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        if (string.IsNullOrWhiteSpace(appData))
        {
            return false;
        }

        var legacyDirectory = Path.Combine(appData, "TaskOverlay", "data");
        var legacyPath = Path.Combine(legacyDirectory, "tasks.json");
        if (string.Equals(Path.GetFullPath(legacyPath), Path.GetFullPath(_dataPath), StringComparison.OrdinalIgnoreCase) ||
            !File.Exists(legacyPath))
        {
            return false;
        }

        CopyIfExists(Path.Combine(legacyDirectory, "tasks.bak.json"), _backupPath);
        File.Copy(legacyPath, _dataPath, overwrite: false);
        return true;
    }

    private static string ResolveDefaultDataDirectory()
    {
        var overrideDirectory = Environment.GetEnvironmentVariable("TASKOVERLAY_DATA_DIR");
        if (!string.IsNullOrWhiteSpace(overrideDirectory))
        {
            return Path.GetFullPath(overrideDirectory);
        }

        return Path.Combine(AppContext.BaseDirectory, "data");
    }

    private static void CopyIfExists(string sourcePath, string destinationPath)
    {
        if (!File.Exists(sourcePath) || File.Exists(destinationPath))
        {
            return;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
        File.Copy(sourcePath, destinationPath, overwrite: false);
    }

    private sealed class RepositoryState
    {
        public long NextTaskId { get; set; } = 1;
        public long NextCompletionId { get; set; } = 1;
        public List<TaskItem> Tasks { get; set; } = [];
        public List<DailyCompletion> DailyCompletions { get; set; } = [];
        public AppSettings Settings { get; set; } = new();
    }
}
