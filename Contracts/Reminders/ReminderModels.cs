using NOAH.Contracts.Common;
using NOAH.Contracts.Enums;

namespace NOAH.Contracts.Reminders;

public sealed record ReminderDto(
    Guid Id,
    string Title,
    string? Message,
    ReminderTriggerTypeDto TriggerType,
    ReminderStatusDto Status,
    bool ShouldNotify,
    DateTimeOffset? TriggerAtUtc,
    GeoCoordinateDto? TriggerLocation,
    double? TriggerRadiusMeters,
    DateTimeOffset? LastTriggeredAtUtc,
    Guid? TaskItemId,
    Guid? NoteId,
    Guid? SavedLocationId,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? UpdatedAtUtc);

public sealed record CreateReminderRequest(
    string Title,
    string? Message,
    ReminderTriggerTypeDto TriggerType,
    bool ShouldNotify,
    DateTimeOffset? TriggerAtUtc,
    GeoCoordinateDto? TriggerLocation,
    double? TriggerRadiusMeters,
    Guid? TaskItemId,
    Guid? NoteId,
    Guid? SavedLocationId);

public sealed record UpdateReminderRequest(
    string Title,
    string? Message,
    ReminderTriggerTypeDto TriggerType,
    ReminderStatusDto Status,
    bool ShouldNotify,
    DateTimeOffset? TriggerAtUtc,
    GeoCoordinateDto? TriggerLocation,
    double? TriggerRadiusMeters,
    Guid? TaskItemId,
    Guid? NoteId,
    Guid? SavedLocationId);
