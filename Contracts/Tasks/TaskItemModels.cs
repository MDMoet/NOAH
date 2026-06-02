using NOAH.Contracts.Enums;

// ReSharper disable once CheckNamespace
namespace NOAH.Contracts.Tasks;

public sealed record TaskItemDto(
    Guid Id,
    string Title,
    string? Description,
    TaskItemStatusDto Status,
    TaskPriorityDto Priority,
    DateTimeOffset? DueAtUtc,
    DateOnly? PlannedFor,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? UpdatedAtUtc);

public sealed record CreateTaskItemRequest(
    string Title,
    string? Description,
    TaskPriorityDto Priority,
    DateTimeOffset? DueAtUtc,
    DateOnly? PlannedFor);

public sealed record UpdateTaskItemRequest(
    string Title,
    string? Description,
    TaskItemStatusDto Status,
    TaskPriorityDto Priority,
    DateTimeOffset? DueAtUtc,
    DateOnly? PlannedFor);
