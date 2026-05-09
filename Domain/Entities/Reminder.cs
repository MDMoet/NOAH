using NOAH.Domain.Common;
using NOAH.Domain.Enums;
using NOAH.Domain.ValueObjects;

namespace NOAH.Domain.Entities;

public sealed class Reminder : TrackedEntity
{
    public string Title { get; set; } = string.Empty;

    public string? Message { get; set; }

    public ReminderTriggerType TriggerType { get; set; } = ReminderTriggerType.Time;

    public ReminderStatus Status { get; set; } = ReminderStatus.Scheduled;

    public bool ShouldNotify { get; set; } = true;

    public DateTimeOffset? TriggerAtUtc { get; set; }

    public GeoCoordinate? TriggerLocation { get; set; }

    public double? TriggerRadiusMeters { get; set; }

    public DateTimeOffset? LastTriggeredAtUtc { get; set; }

    public Guid? TaskItemId { get; set; }

    public Guid? NoteId { get; set; }

    public Guid? SavedLocationId { get; set; }
}
