using TaskOverlay.Core.Models;

namespace TaskOverlay.Core.Services;

public interface IGoalRepository
{
    Task InitializeAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Goal>> GetGoalsAsync(GoalStatus? status = null, CancellationToken cancellationToken = default);
    Task<Goal?> GetGoalAsync(long goalId, CancellationToken cancellationToken = default);
    Task<Goal> SaveGoalAsync(Goal goal, CancellationToken cancellationToken = default);
    Task<bool> DeleteGoalAsync(long goalId, CancellationToken cancellationToken = default);
}
