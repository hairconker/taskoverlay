using TaskOverlay.Core.Models;

namespace TaskOverlay.Core.Services;

public sealed class GoalApplicationService(IGoalRepository repository)
{
    public event EventHandler? GoalsChanged;

    public Task InitializeAsync(CancellationToken cancellationToken = default)
        => repository.InitializeAsync(cancellationToken);

    public Task<IReadOnlyList<Goal>> GetGoalsAsync(GoalStatus? status = null, CancellationToken cancellationToken = default)
        => repository.GetGoalsAsync(status, cancellationToken);

    public Task<Goal?> GetGoalAsync(long goalId, CancellationToken cancellationToken = default)
        => repository.GetGoalAsync(goalId, cancellationToken);

    public async Task<Goal> SaveGoalAsync(Goal goal, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(goal.Title))
        {
            throw new ArgumentException("目标标题不能为空。", nameof(goal));
        }

        goal.Title = goal.Title.Trim();
        goal.Description = string.IsNullOrWhiteSpace(goal.Description) ? null : goal.Description.Trim();
        goal.Tags = TaskTagRules.Normalize(goal.Tags);
        goal.DailyBudgetMinutes = goal.DailyBudgetMinutes is > 0 ? goal.DailyBudgetMinutes : null;
        foreach (var milestone in goal.Milestones)
        {
            milestone.Title = milestone.Title.Trim();
        }
        goal.Milestones = goal.Milestones.Where(m => !string.IsNullOrWhiteSpace(m.Title)).ToList();

        var saved = await repository.SaveGoalAsync(goal, cancellationToken);
        GoalsChanged?.Invoke(this, EventArgs.Empty);
        return saved;
    }

    public async Task<Goal?> LinkTaskAsync(
        long goalId,
        long taskId,
        Guid? proposalId = null,
        string? note = null,
        CancellationToken cancellationToken = default)
    {
        var goal = await repository.GetGoalAsync(goalId, cancellationToken);
        if (goal is null)
        {
            return null;
        }

        var alreadyLinked = goal.TaskLinks.Any(link =>
            link.TaskId == taskId ||
            (proposalId is not null && link.ProposalId == proposalId));
        if (!alreadyLinked)
        {
            goal.TaskLinks.Add(new GoalTaskLink
            {
                GoalId = goal.Id,
                TaskId = taskId,
                ProposalId = proposalId,
                Note = string.IsNullOrWhiteSpace(note) ? null : note.Trim()
            });
        }

        var saved = await SaveGoalAsync(goal, cancellationToken);
        return saved;
    }

    public async Task<bool> DeleteGoalAsync(long goalId, CancellationToken cancellationToken = default)
    {
        var deleted = await repository.DeleteGoalAsync(goalId, cancellationToken);
        if (deleted)
        {
            GoalsChanged?.Invoke(this, EventArgs.Empty);
        }
        return deleted;
    }
}
