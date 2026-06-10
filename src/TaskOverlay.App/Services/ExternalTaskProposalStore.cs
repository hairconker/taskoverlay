using System.IO;
using System.Text.Json;
using TaskOverlay.Core.Models;
using TaskOverlay.Core.Services;

namespace TaskOverlay.App.Services;

public sealed class ExternalTaskProposalStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private readonly string _path;
    private readonly string _tempPath;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public ExternalTaskProposalStore(string directory)
    {
        _path = Path.Combine(directory, "proposals.json");
        _tempPath = Path.Combine(directory, "proposals.tmp.json");
    }

    public event EventHandler? ProposalsChanged;

    public async Task<IReadOnlyList<ExternalTaskProposal>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            return (await ReadAsync(cancellationToken))
                .OrderByDescending(p => p.CreatedAt)
                .ToList();
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<ExternalTaskProposal> AddAsync(ExternalTaskProposal proposal, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(proposal.Title))
        {
            throw new ArgumentException("任务标题不能为空。", nameof(proposal));
        }

        proposal.Id = proposal.Id == Guid.Empty ? Guid.NewGuid() : proposal.Id;
        proposal.Title = proposal.Title.Trim();
        proposal.Notes = string.IsNullOrWhiteSpace(proposal.Notes) ? null : proposal.Notes.Trim();
        proposal.Source = string.IsNullOrWhiteSpace(proposal.Source) ? "external" : proposal.Source.Trim();
        proposal.Tags = TaskTagRules.Normalize(proposal.Tags);
        proposal.CreatedAt = proposal.CreatedAt == default ? DateTime.Now : proposal.CreatedAt;

        await _gate.WaitAsync(cancellationToken);
        try
        {
            var proposals = await ReadAsync(cancellationToken);
            proposals.Add(proposal);
            await WriteAsync(proposals, cancellationToken);
        }
        finally
        {
            _gate.Release();
        }

        ProposalsChanged?.Invoke(this, EventArgs.Empty);
        return proposal;
    }

    public async Task<TaskItem?> ConfirmAsync(Guid id, TaskApplicationService tasks, CancellationToken cancellationToken = default)
    {
        ExternalTaskProposal? proposal;
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var proposals = await ReadAsync(cancellationToken);
            proposal = proposals.FirstOrDefault(p => p.Id == id);
            if (proposal is null)
            {
                return null;
            }

            var saved = await tasks.SaveTaskAsync(new TaskItem
            {
                Title = proposal.Title,
                Notes = proposal.Notes,
                Priority = proposal.Priority,
                DueAt = proposal.DueAt,
                ReminderAt = proposal.ReminderAt,
                IsDaily = proposal.IsDaily,
                Recurrence = proposal.Recurrence,
                Tags = proposal.Tags.Select(t => new Tag { Name = t.Name, Color = t.Color }).ToList()
            }, cancellationToken);
            proposals.Remove(proposal);
            await WriteAsync(proposals, cancellationToken);
            ProposalsChanged?.Invoke(this, EventArgs.Empty);
            return saved;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<bool> RejectAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var removed = false;
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var proposals = await ReadAsync(cancellationToken);
            removed = proposals.RemoveAll(p => p.Id == id) > 0;
            if (removed)
            {
                await WriteAsync(proposals, cancellationToken);
            }
        }
        finally
        {
            _gate.Release();
        }

        if (removed)
        {
            ProposalsChanged?.Invoke(this, EventArgs.Empty);
        }

        return removed;
    }

    private async Task<List<ExternalTaskProposal>> ReadAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_path))
        {
            return [];
        }

        var json = await File.ReadAllTextAsync(_path, cancellationToken);
        return JsonSerializer.Deserialize<List<ExternalTaskProposal>>(json, JsonOptions) ?? [];
    }

    private async Task WriteAsync(List<ExternalTaskProposal> proposals, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        await File.WriteAllTextAsync(_tempPath, JsonSerializer.Serialize(proposals, JsonOptions), cancellationToken);
        File.Move(_tempPath, _path, overwrite: true);
    }
}
