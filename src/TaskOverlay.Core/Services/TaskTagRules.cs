using TaskOverlay.Core.Models;

namespace TaskOverlay.Core.Services;

public static class TaskTagRules
{
    public static List<Tag> Normalize(IEnumerable<Tag>? tags)
    {
        return tags?
            .Where(t => !string.IsNullOrWhiteSpace(t.Name))
            .Select(t => new Tag
            {
                Id = t.Id,
                Name = t.Name.Trim(),
                Color = string.IsNullOrWhiteSpace(t.Color) ? "#6B7280" : t.Color.Trim()
            })
            .GroupBy(t => t.Name, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .ToList() ?? [];
    }
}
