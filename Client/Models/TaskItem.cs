using NOAH.Contracts.Enums;

namespace Client.Models;

public class TaskItem
{
    public Guid Id { get; set; }

    public string Title { get; set; } = string.Empty;

    public string? Description { get; set; }

    public TaskItemStatusDto Status { get; set; } = TaskItemStatusDto.Open;

    public TaskPriorityDto Priority { get; set; } = TaskPriorityDto.Normal;

    public DateTimeOffset? DueAtUtc { get; set; }

    public DateOnly? PlannedFor { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; }

    public DateTimeOffset? UpdatedAtUtc { get; set; }

    public bool IsComplete => Status == TaskItemStatusDto.Completed;

    public string DueDisplay => DueAtUtc.HasValue
        ? DueAtUtc.Value.ToLocalTime().ToString("HH:mm")
        : PlannedFor?.ToString("dd MMM") ?? "Open";

    public bool IsRelevantForDate(DateOnly date)
    {
        if (PlannedFor == date)
        {
            return true;
        }

        return DueAtUtc.HasValue && DateOnly.FromDateTime(DueAtUtc.Value.LocalDateTime) == date;
    }
}
