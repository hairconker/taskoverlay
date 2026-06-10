using TaskOverlay.Core.Models;

namespace TaskOverlay.Core.Services;

public interface ITaskRepository
{
    Task InitializeAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<TaskItem>> GetTasksAsync(TaskFilter filter, string? search, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<TaskItem>> GetTasksForDateAsync(DateOnly date, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<TaskItem>> GetDueRemindersAsync(DateTime now, CancellationToken cancellationToken = default);
    Task MarkReminderDeliveredAsync(long taskId, CancellationToken cancellationToken = default);
    Task<TaskItem> SaveTaskAsync(TaskItem task, CancellationToken cancellationToken = default);
    Task DeleteTaskAsync(long taskId, CancellationToken cancellationToken = default);
    Task SetCompletedAsync(long taskId, bool completed, CancellationToken cancellationToken = default);
    Task SetOccurrenceCompletedAsync(long taskId, DateOnly date, bool completed, CancellationToken cancellationToken = default);
    Task<AppSettings> GetSettingsAsync(CancellationToken cancellationToken = default);
    Task SaveSettingsAsync(AppSettings settings, CancellationToken cancellationToken = default);
}
