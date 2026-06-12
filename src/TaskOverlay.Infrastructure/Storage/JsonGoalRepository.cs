using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using TaskOverlay.Core.Models;
using TaskOverlay.Core.Services;

namespace TaskOverlay.Infrastructure.Storage;

public sealed class JsonGoalRepository(string directory) : IGoalRepository
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    private readonly string _path = Path.Combine(directory, "goals.json");
    private readonly string _backupPath = Path.Combine(directory, "goals.bak.json");
    private readonly string _tempPath = Path.Combine(directory, "goals.tmp.json");
    private readonly SemaphoreSlim _gate = new(1, 1);

    public Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        if (!File.Exists(_path))
        {
            return SaveStateAsync(new GoalState(), cancellationToken);
        }
        return Task.CompletedTask;
    }

    public async Task<IReadOnlyList<Goal>> GetGoalsAsync(GoalStatus? status = null, CancellationToken cancellationToken = default)
    {
        var state = await LoadStateAsync(cancellationToken);
        var query = state.Goals.AsEnumerable();
        if (status is not null)
        {
            query = query.Where(goal => goal.Status == status);
        }

        return query
            .OrderBy(goal => goal.Status)
            .ThenByDescending(goal => goal.Priority)
            .ThenBy(goal => goal.TimeHorizon)
            .ThenBy(goal => goal.CreatedAt)
            .ToList();
    }

    public async Task<Goal?> GetGoalAsync(long goalId, CancellationToken cancellationToken = default)
    {
        var state = await LoadStateAsync(cancellationToken);
        return state.Goals.FirstOrDefault(goal => goal.Id == goalId);
    }

    public async Task<Goal> SaveGoalAsync(Goal goal, CancellationToken cancellationToken = default)
    {
        var state = await LoadStateAsync(cancellationToken);
        goal.Tags = TaskTagRules.Normalize(goal.Tags);
        goal.UpdatedAt = DateTime.Now;
        if (goal.Id == 0)
        {
            goal.Id = state.NextGoalId++;
            goal.CreatedAt = DateTime.Now;
            state.Goals.Add(goal);
        }
        else
        {
            var index = state.Goals.FindIndex(item => item.Id == goal.Id);
            if (index >= 0)
            {
                goal.CreatedAt = state.Goals[index].CreatedAt;
                state.Goals[index] = goal;
            }
            else
            {
                state.Goals.Add(goal);
            }
        }

        NormalizeNestedIds(state, goal);
        await SaveStateAsync(state, cancellationToken);
        return goal;
    }

    public async Task<bool> DeleteGoalAsync(long goalId, CancellationToken cancellationToken = default)
    {
        var state = await LoadStateAsync(cancellationToken);
        var removed = state.Goals.RemoveAll(goal => goal.Id == goalId) > 0;
        if (removed)
        {
            await SaveStateAsync(state, cancellationToken);
        }
        return removed;
    }

    private void NormalizeNestedIds(GoalState state, Goal goal)
    {
        foreach (var milestone in goal.Milestones)
        {
            milestone.GoalId = goal.Id;
            if (milestone.Id == 0)
            {
                milestone.Id = state.NextMilestoneId++;
                milestone.CreatedAt = DateTime.Now;
            }
            milestone.UpdatedAt = DateTime.Now;
        }

        foreach (var link in goal.TaskLinks)
        {
            link.GoalId = goal.Id;
            if (link.Id == 0)
            {
                link.Id = state.NextTaskLinkId++;
                link.CreatedAt = DateTime.Now;
            }
        }
    }

    private async Task<GoalState> LoadStateAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            if (!File.Exists(_path))
            {
                return new GoalState();
            }

            try
            {
                return await ReadStateFileAsync(_path, cancellationToken);
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

    private async Task SaveStateAsync(GoalState state, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            var json = JsonSerializer.Serialize(state, JsonOptions);
            await File.WriteAllTextAsync(_tempPath, json, cancellationToken);
            if (File.Exists(_path))
            {
                File.Replace(_tempPath, _path, _backupPath, ignoreMetadataErrors: true);
            }
            else
            {
                File.Move(_tempPath, _path);
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

    private static async Task<GoalState> ReadStateFileAsync(string path, CancellationToken cancellationToken)
    {
        var json = await File.ReadAllTextAsync(path, cancellationToken);
        var state = JsonSerializer.Deserialize<GoalState>(json, JsonOptions)
                    ?? throw new JsonException($"目标数据文件无效：{path}");
        state.NextGoalId = Math.Max(state.NextGoalId, state.Goals.Select(g => g.Id).DefaultIfEmpty().Max() + 1);
        state.NextMilestoneId = Math.Max(state.NextMilestoneId, state.Goals.SelectMany(g => g.Milestones).Select(m => m.Id).DefaultIfEmpty().Max() + 1);
        state.NextTaskLinkId = Math.Max(state.NextTaskLinkId, state.Goals.SelectMany(g => g.TaskLinks).Select(l => l.Id).DefaultIfEmpty().Max() + 1);
        return state;
    }

    private sealed class GoalState
    {
        public long NextGoalId { get; set; } = 1;
        public long NextMilestoneId { get; set; } = 1;
        public long NextTaskLinkId { get; set; } = 1;
        public List<Goal> Goals { get; set; } = [];
    }
}
