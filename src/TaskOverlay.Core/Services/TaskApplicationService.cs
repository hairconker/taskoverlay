using TaskOverlay.Core.Models;

namespace TaskOverlay.Core.Services;

public sealed class TaskApplicationService(ITaskRepository repository)
{
    public event EventHandler? TasksChanged;

    public Task<IReadOnlyList<TaskItem>> GetTasksAsync(TaskFilter filter, string? search = null, CancellationToken cancellationToken = default)
        => repository.GetTasksAsync(filter, search, cancellationToken);

    public Task<IReadOnlyList<TaskItem>> GetTasksForDateAsync(DateOnly date, CancellationToken cancellationToken = default)
        => repository.GetTasksForDateAsync(date, cancellationToken);

    public async Task MarkReminderDeliveredAsync(long taskId, CancellationToken cancellationToken = default)
    {
        await repository.MarkReminderDeliveredAsync(taskId, cancellationToken);
        OnTasksChanged();
    }

    public async Task<TaskItem> AddTaskAsync(string title, DateTime? dueAt = null, bool isDaily = false, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            throw new ArgumentException("任务标题不能为空。", nameof(title));
        }

        var saved = await repository.SaveTaskAsync(new TaskItem
        {
            Title = title.Trim(),
            DueAt = dueAt,
            IsDaily = isDaily,
            CreatedAt = DateTime.Now,
            UpdatedAt = DateTime.Now
        }, cancellationToken);
        OnTasksChanged();
        return saved;
    }

    public async Task<TaskItem> SaveTaskAsync(TaskItem task, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(task.Title))
        {
            throw new ArgumentException("任务标题不能为空。", nameof(task));
        }

        task.Title = task.Title.Trim();
        task.Tags = TaskTagRules.Normalize(task.Tags);
        TaskReminderRules.NormalizeOffset(task);
        if (TaskOccurrenceRules.IsRecurring(task))
        {
            task.IsCompleted = false;
            task.CompletedAt = null;
        }

        task.UpdatedAt = DateTime.Now;
        var saved = await repository.SaveTaskAsync(task, cancellationToken);
        OnTasksChanged();
        return saved;
    }

    public async Task DeleteTaskAsync(long taskId, CancellationToken cancellationToken = default)
    {
        await repository.DeleteTaskAsync(taskId, cancellationToken);
        OnTasksChanged();
    }

    public async Task SetCompletedAsync(long taskId, bool completed, CancellationToken cancellationToken = default)
    {
        await repository.SetCompletedAsync(taskId, completed, cancellationToken);
        OnTasksChanged();
    }

    public async Task SetOccurrenceCompletedAsync(long taskId, DateOnly date, bool completed, CancellationToken cancellationToken = default)
    {
        await repository.SetOccurrenceCompletedAsync(taskId, date, completed, cancellationToken);
        OnTasksChanged();
    }

    public void NotifyTasksChanged() => OnTasksChanged();

    private void OnTasksChanged() => TasksChanged?.Invoke(this, EventArgs.Empty);
}
