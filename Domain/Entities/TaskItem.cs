using NOAH.Domain.Common;
using NOAH.Domain.Enums;

namespace NOAH.Domain.Entities;

public sealed class TaskItem : TrackedEntity
{
    public string Title { get; set; } = string.Empty;

    public string? Description { get; set; }

    public TaskItemStatus Status { get; set; } = TaskItemStatus.Open;

    public TaskPriority Priority { get; set; } = TaskPriority.Normal;

    public DateTimeOffset? DueAtUtc { get; set; }

    public DateOnly? PlannedFor { get; set; }
}
